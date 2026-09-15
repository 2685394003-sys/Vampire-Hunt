using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Boss.Encounter;
using VampireHunt.Spawning;

namespace VampireHunt.Diagnostics
{
    /// <summary>
    /// 运行时统计监控插件（调试/数值验证用）。
    ///
    /// 统计四项「每分钟」指标，直接对接现有战斗结算链路，不修改任何核心文件或预制体：
    ///   1. 每分钟击杀数        —— 订阅本地玩家的 ServerCombatResolutionHost（role = Source，record.WasKilled）
    ///   2. 每分钟获取猩红值     —— 轮询本地玩家 CoreStatsHandler 的 StatKeys.Scarlet，累加正向增量
    ///   3. 每分钟造成输出伤害   —— 订阅本地玩家的 ServerCombatResolutionHost（role = Source，record.AppliedDamage）
    ///   4. 每分钟获取魔币值     —— 轮询本地玩家 CoreStatsHandler 的 StatKeys.Coin（商店购买用金币），累加正向增量（商店消费为负增量，不计入累计获取量）；面板另显当前余额
    ///
    /// 用法：
    ///   - 在编辑器 / Development Build 中自动生成（无需手动挂预制体）。
    ///   - 按 F3 开关显示面板；按 F4 把当前汇总打印到 Console（方便复制进数值表）。
    ///   - 面板内点击「导出CSV (本局全量)」按钮：把本局从游戏开始到按下按钮的逐秒监控数据
    ///     （击杀/猩红/输出，含累计与每秒增量）以及每分钟汇总，写入
    ///     Assets/VampireHunt/Scripts/Diagnostics/StatsExport_*.csv。
    ///   - 额外监控「血契选择」：订阅本地玩家 PactNetworkState.PactChanged，记录第几次、游戏内时间点、
    ///     选了哪个血契（id + 名称 + 选完后层数）；CSV 另含「各血契生效段效率」段，直接对比选不同血契后的击杀/输出效率。
    ///   - 额外监控「副契选择」：订阅场景级 EnemyAffixRunState.AffixesChanged（副契=敌群缩放轴，由玩家在满槽抽卡时选择），
    ///     用快照 diff 检出新增/叠加的副契，记录第几次、游戏内时间点、选了哪个副契（id + 名称 + 层数）；
        ///     CSV 另含「各副契生效段效率」段，对比选副契前后击杀/输出效率（副契会强化敌人，通常使 kills_per_min 下降）。
        ///   - 额外监听「每阶段 Boss 战时间」：每帧轮询 BossEncounterDirector（服务器权威，整局一个实例、3 阶段共用），
        ///     记录每阶段从进入 Battle（玩家开始对该阶段 Boss 输出 / DPS 阶段）到清空（PhaseTransition 进入下一场，或最终 Defeated 通关）的用时；
        ///     计时轴与逐秒样本/血契/副契一致（游戏内秒，菜单暂停冻结），CSV 第 4c 段输出 stage/起止秒/用时/结果。
        ///   - 联机时每位玩家各自统计自身；纯客户端（非 Host/Server）收不到结算事件，属已知限制。
    ///     副契状态为场景级服务器权威，Host/单机模式可正常接收；纯客户端（非 Host）收不到副契快照，属已知限制。
    ///   - 对「菜单暂停」感知：打开血契/商店菜单时（MenuPauseController 冻结模拟），本插件不推进本局计时与滚动窗口，
    ///     导出的每分钟速率仅统计纯游戏内时间，不会被暂停思考/阅读菜单的时间稀释。
    ///   - 刷怪倍率滑条：面板顶部滑条实时乘到 EnemySpawnDirector 刷怪速率上（0.1 ~ 20 倍），并把每次调整（时间点+倍率）写入 CSV 第 6 段。
    ///   - 武器切换记录：每帧轮询玩家 CombatAbilityHost.ActiveWeaponId，切换时记录（时间点+武器），写入 CSV 第 5 段。
    ///   - 导出文件命名：日期+时间精确到分钟（yyyyMMdd_HHmm）。
    /// </summary>
    public sealed class RuntimeStatsMonitor : MonoBehaviour, IServerCombatResolutionListener
    {
        [Header("显示")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F3;
        [SerializeField] private KeyCode dumpKey = KeyCode.F4;
        [SerializeField] private bool startVisible = true;

        private const float BucketSeconds = 1f;
        private const int WindowSeconds = 60;
        private const int BucketCount = WindowSeconds;

        // 滚动窗口：每秒一桶，共 60 桶（= 最近 60 秒）
        private readonly float[] m_DamageBuckets = new float[BucketCount];
        private readonly int[] m_KillBuckets = new int[BucketCount];
        private readonly float[] m_ScarletBuckets = new float[BucketCount];
        private readonly float[] m_CoinBuckets = new float[BucketCount];
        // 对 Boss 输出（格挡条下降量）滚动窗口：Boss 伤害不走结算路由，需单独采样统计
        private readonly float[] m_BossDamageBuckets = new float[BucketCount];
        private int m_BucketHead;
        private float m_BucketTimer;

        // 累计（会话开始起）
        private float m_TotalDamage;
        private int m_TotalKills;
        private float m_TotalScarlet;
        private float m_TotalCoin;
        // 对 Boss 累计输出（由格挡条下降量反推；不含破防后打本体血的部分）
        private float m_TotalBossDamage;
        // 本局「游戏内」已流逝时间（秒），暂停期间不推进 → 速率统计不含菜单暂停时间
        private float m_SessionElapsed;
        private bool m_SessionStarted;

        // 运行时引用
        private ServerCombatResolutionHost m_ResolutionHost;
        private CoreStatsHandler m_CoreStats;
        private NetworkObject m_PlayerObject;
        private float m_LastScarlet;
        private float m_LastCoin;
        private bool m_Wired;

        private bool m_Visible;

        // 本局历史（游戏开始起逐秒采样，用于 CSV 全量导出）
        private readonly List<SessionSample> m_History = new List<SessionSample>();
        private int m_LastSnapshotSecond = -1;
        private int m_SnapKills;
        private float m_SnapScarlet;
        private float m_SnapDamage;
        private float m_SnapCoin;
        private float m_SnapBossDamage;
        private string m_LastExportMsg = "";
        private Vector2 m_Scroll; // 监控信息区滚动状态，避免信息过多把按钮顶出可视区

        // 血契选择记录（玩家每次确认选择/叠加血契）
        private PactNetworkState m_PactState;
        private PactCatalogAsset m_PactCatalog;
        private readonly List<PactChoice> m_Choices = new List<PactChoice>();
        private readonly Dictionary<uint, int> m_PactBaseline = new Dictionary<uint, int>();
        private int m_SelectionCount;

        // 副契选择记录（副契=敌群缩放轴，场景级 EnemyAffixRunState，由玩家满槽抽卡时选择）
        private EnemyAffixRunState m_AffixState;
        private EnemyAffixCatalogAsset m_AffixCatalog;
        private readonly List<AffixChoice> m_AffixChoices = new List<AffixChoice>();
        private readonly Dictionary<uint, int> m_AffixBaseline = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> m_AffixLast = new Dictionary<uint, int>();
        private readonly List<EnemyAffixStackNetworkState> m_AffixCaptureBuf = new List<EnemyAffixStackNetworkState>();
        private int m_AffixSelectionCount;

        // Boss 阶段战时间监听（每阶段 Boss 战 = 一场 boss 遭遇；整局共 3 场，对应 aggregate 的 StageNumber 1/2/3）
        // 计时轴与监控其它指标一致，使用游戏内秒 m_SessionElapsed（菜单暂停期间冻结，不计入），与逐秒样本/血契/副契同一时间轴。
        private BossEncounterDirector m_BossDirector;
        private int m_BossObservedStage;          // 当前正在计时战时间的阶段号（0=尚未开始）
        private bool m_BossStageBattleActive;     // 该阶段是否已进入 Battle（战时间计时中）
        private float m_BossStageStartTime;       // 该阶段战开始时间（游戏内秒）
        private readonly List<BossStageFight> m_BossStageFights = new List<BossStageFight>();

        // Boss 格挡条采样（对 Boss 输出 = 格挡条下降量；上升=回盾/重载，忽略）
        private float m_BossGuardLast;            // 上一帧格挡条值
        private bool m_BossGuardPrimed;           // 是否已建立基线（首次采样/换阶段时需重置基线）
        private int m_BossGuardStage;             // 建立基线时的阶段号（换阶段时格挡条会重载，需重置基线）
        private float m_BossGuardCurrent;         // 当前格挡条值（面板/CSV 显示用）
        private float m_BossGuardMax;             // 当前阶段格挡条上限

        // 武器切换记录（每次玩家切换主武器；timeSec=游戏内秒，与血契/副契同一时间轴）
        private CombatAbilityHost m_AbilityHost;
        private uint m_LastWeaponId;
        private uint m_InitialWeaponId;   // 开局时手上那把武器（用于 CSV「各武器生效段效率」段 0 的命名）
        private bool m_WeaponInitialized;
        private readonly List<WeaponSwitch> m_WeaponSwitches = new List<WeaponSwitch>();

        // 刷怪倍率调整记录（监控面板滑条实时调；multiplier=调整后倍率，1=默认）
        private EnemySpawnDirector m_SpawnDirector;
        private float m_SpawnRateMultiplier = 1f;
        private readonly List<SpawnRateChange> m_SpawnRateChanges = new List<SpawnRateChange>();

        // 缓存的粗体样式（默认 GUISkin 没有 boldLabel 字段，运行时构造一次复用）
        private static GUIStyle s_BoldLabel;

        // 武器 id → 显示名（与速查表 6.1 一致；新增武器时需在此补充）
        private static readonly Dictionary<uint, string> s_WeaponNames = new Dictionary<uint, string>
        {
            { 1, "扇形剑气" },
            { 110, "狙击枪" },
            { 120, "自动步枪" },
            { 130, "导弹" },
            { 140, "激光" },
            { 150, "火焰喷射器" },
        };

        // 单次血契选择：selectionIndex=第几次；timeSec=游戏开始后秒；stacksAfter=选完后层数
        private struct PactChoice
        {
            public int selectionIndex;
            public int timeSec;
            public uint pactId;
            public string pactName;
            public int stacksAfter;
        }

        // 单次副契选择：selectionIndex=第几次；timeSec=游戏开始后秒；affixId/affixName=副契；stacksAfter=选完后层数
        private struct AffixChoice
        {
            public int selectionIndex;
            public int timeSec;
            public uint affixId;
            public string affixName;
            public int stacksAfter;
        }

        // 单场 Boss 阶段战：stageNumber=第几场（1/2/3）；startSec/endSec=游戏内起止秒；durationSec=用时；result=结果
        private struct BossStageFight
        {
            public int stageNumber;
            public int startSec;
            public int endSec;
            public float durationSec;
            public string result;   // "defeated"=阶段清空进入下一场 / "final"=最终击败(通关) / "aborted"=异常中断(如 boss 物体卸载) / "ongoing"=导出时仍在进行
        }

        // 单次武器切换：timeSec=游戏开始后秒；weaponId=切换到的武器id；weaponName=武器名
        private struct WeaponSwitch
        {
            public int timeSec;
            public uint weaponId;
            public string weaponName;
        }

        // 单次刷怪倍率调整：timeSec=游戏开始后秒；multiplier=调整后倍率（1=默认不变）
        private struct SpawnRateChange
        {
            public int timeSec;
            public float multiplier;
        }

        // 逐秒样本：second = 游戏开始后经过的整秒；*Cum = 累计值；*Delta = 该秒增量
        // bossGuard = 该秒末 Boss 格挡条当前值（木桩测试看剩余量）；bossDmg* = 对 Boss 输出（格挡条下降量）
        private struct SessionSample
        {
            public int second;
            public int killsCum;
            public float scarletCum;
            public float damageCum;
            public float coinCum;
            public int killDelta;
            public float scarletDelta;
            public float damageDelta;
            public float coinDelta;
            public float bossGuard;
            public float bossDmgCum;
            public float bossDmgDelta;
        }

        #region IServerCombatResolutionListener

        // 决议监听器优先级：0 即可，不影响其他 Pact 监听器顺序
        public int Priority => 0;

        public void OnCombatResolved(in CombatResolutionRecord record, CombatParticipantRole role)
        {
            // 只统计「玩家作为伤害来源」的结算（打敌人）
            if ((role & CombatParticipantRole.Source) == 0) return;

            if (record.AppliedDamage > 0f)
            {
                m_TotalDamage += record.AppliedDamage;
                m_DamageBuckets[m_BucketHead] += record.AppliedDamage;
            }

            if (record.WasKilled)
            {
                m_TotalKills += 1;
                m_KillBuckets[m_BucketHead] += 1;
            }
        }

        #endregion

        private void Awake()
        {
            m_Visible = startVisible;
        }

        private void Update()
        {
            // 延迟绑定本地玩家（玩家物体可能晚于监控生成）
            if (!m_Wired || m_PlayerObject == null || !m_PlayerObject.IsSpawned)
            {
                TryWire();
            }
            // 副契状态是场景级对象，可能晚于玩家物体出现，每帧尝试绑定一次（已绑定则直接返回）
            TryWireAffix();
            // Boss 阶段战时间监听：找到 BossEncounterDirector（服务器权威，整局一个实例，3 阶段共用同一物体），
            // 每帧轮询其 State / StageNumber，记录每阶段从进入 Battle 到清空（PhaseTransition 或 Defeated）的用时。
            TryWireBoss();
            PollBoss();
            // 刷怪倍率滑条对应的 EnemySpawnDirector 绑定 + 当前主武器轮询（武器切换 / 刷怪倍率调整记录）
            TryWireSpawnDirector();
            PollWeapon();

            // 滚动窗口推进：用真实时间，但「菜单暂停」期间冻结（不推进计时与滚动桶），
            // 这样导出的每分钟速率不会被暂停思考/阅读菜单的时间稀释。
            bool paused = MenuPauseController.IsPaused;
            float gameDelta = paused ? 0f : Time.unscaledDeltaTime;
            if (gameDelta > 0f)
            {
                m_SessionElapsed += gameDelta;
                m_BucketTimer += gameDelta;
                while (m_BucketTimer >= BucketSeconds)
                {
                    m_BucketTimer -= BucketSeconds;
                    AdvanceBucket();
                }
            }

            if (Input.GetKeyDown(toggleKey)) m_Visible = !m_Visible;
            if (Input.GetKeyDown(dumpKey)) DumpSummary();
        }

        private void TryWire()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;
            NetworkClient localClient = nm.LocalClient;
            if (localClient == null || localClient.PlayerObject == null) return;

            NetworkObject player = localClient.PlayerObject;
            var host = player.GetComponent<ServerCombatResolutionHost>();
            var stats = player.GetComponent<CoreStatsHandler>();
            var pactState = player.GetComponent<PactNetworkState>();
            if (host == null || stats == null || pactState == null) return;

            // 玩家物体更换时先解绑旧的（结算 + 血契）
            if (m_Wired && (m_ResolutionHost != null && m_ResolutionHost != host ||
                            m_PactState != null && m_PactState != pactState))
            {
                if (m_ResolutionHost != null) m_ResolutionHost.UnregisterCombatResolutionListener(this);
                if (m_PactState != null) m_PactState.PactChanged -= OnPactChanged;
                m_AbilityHost = null;
                m_WeaponInitialized = false;
                m_Wired = false;
            }

            if (!m_Wired && host.RegisterCombatResolutionListener(this))
            {
                m_ResolutionHost = host;
                m_CoreStats = stats;
                m_PactState = pactState;
                m_PlayerObject = player;
                m_AbilityHost = player.GetComponent<CombatAbilityHost>();
                m_WeaponInitialized = false;
                m_LastScarlet = stats.GetCurrentValue(StatKeys.Scarlet);
                m_LastCoin = stats.GetCurrentValue(StatKeys.Coin);
                if (m_PactCatalog == null)
                {
                    var catalogs = Resources.FindObjectsOfTypeAll<PactCatalogAsset>();
                    m_PactCatalog = catalogs.Length > 0 ? catalogs[0] : null;
                }
                // 以当前已拥有血契为基线，避免把开局预设/网络恢复算成"选择"
                m_PactBaseline.Clear();
                var snap = new List<PactStackNetworkState>();
                pactState.Capture(snap);
                for (int i = 0; i < snap.Count; i++) m_PactBaseline[snap[i].PactId] = snap[i].Stacks;
                pactState.PactChanged += OnPactChanged;
                m_Wired = true;
                if (!m_SessionStarted) { m_SessionStarted = true; m_SessionElapsed = 0f; }
            }
        }

        // 绑定场景级副契状态（EnemyAffixRunState）。它不在玩家物体上，用 FindAnyObjectByType 取；
        // 副契选择通过 AffixesChanged 事件 + 快照 diff 检测新增/叠加，不直接改核心代码。
        private void TryWireAffix()
        {
            if (m_AffixState != null) return;
            var state = FindAnyObjectByType<EnemyAffixRunState>();
            if (state == null) return;

            m_AffixState = state;
            m_AffixCatalog = state.CatalogAsset;
            // 订阅前先抓基线，避免把开局已存在的副契算成"选择"
            m_AffixBaseline.Clear();
            m_AffixLast.Clear();
            state.Capture(m_AffixCaptureBuf);
            for (int i = 0; i < m_AffixCaptureBuf.Count; i++)
            {
                m_AffixBaseline[m_AffixCaptureBuf[i].AffixId] = m_AffixCaptureBuf[i].Stacks;
                m_AffixLast[m_AffixCaptureBuf[i].AffixId] = m_AffixCaptureBuf[i].Stacks;
            }
            state.AffixesChanged += OnAffixesChanged;
        }

        // 副契快照变化（每次选择/叠加副契都会触发）。与基线 & 上一快照 diff，检出"新增或叠加"记为一次选择。
        private void OnAffixesChanged()
        {
            if (m_AffixState == null) return;
            m_AffixState.Capture(m_AffixCaptureBuf);
            for (int i = 0; i < m_AffixCaptureBuf.Count; i++)
            {
                uint affixId = m_AffixCaptureBuf[i].AffixId;
                int stacks = m_AffixCaptureBuf[i].Stacks;
                int last = m_AffixLast.TryGetValue(affixId, out int lv) ? lv : 0;
                int baseStacks = m_AffixBaseline.TryGetValue(affixId, out int bv) ? bv : 0;
                // 仅当"比上一快照增多"且"高于基线"时记为一次真实选择（忽略开局预设/重复刷新）
                if (stacks > last && stacks > baseStacks)
                {
                    int sec = m_SessionStarted ? (int)m_SessionElapsed : 0;
                    string name = (m_AffixCatalog != null && m_AffixCatalog.TryGetAsset(affixId, out var asset))
                        ? asset.DisplayName
                        : $"Affix {affixId}";
                    m_AffixSelectionCount += 1;
                    m_AffixChoices.Add(new AffixChoice
                    {
                        selectionIndex = m_AffixSelectionCount,
                        timeSec = sec,
                        affixId = affixId,
                        affixName = name,
                        stacksAfter = stacks,
                    });
                    Debug.Log($"[StatsMonitor] 选择副契 #{m_AffixSelectionCount}: {name} (id={affixId}, 层={stacks}) @ {sec}s");
                }
                m_AffixLast[affixId] = stacks;
            }
        }

        // 绑定 EnemySpawnDirector（场景组件，服务器权威刷怪入口）。可能晚于监控生成，每帧尝试绑定；
        // 首次绑定时把监控面板当前滑条倍率同步进去，避免滑条在 director 生成前调了却未生效。
        private void TryWireSpawnDirector()
        {
            if (m_SpawnDirector != null) return;
            m_SpawnDirector = FindAnyObjectByType<EnemySpawnDirector>();
            if (m_SpawnDirector != null)
            {
                m_SpawnDirector.runtimeSpawnRateMultiplier = m_SpawnRateMultiplier;
            }
        }

        // 每帧轮询当前主武器 id，检测到变化则记录一次「武器切换」（与血契/副契同一游戏内时间轴）。
        private void PollWeapon()
        {
            if (!m_Wired || m_AbilityHost == null) return;
            uint current = m_AbilityHost.ActiveWeaponId;
            if (!m_WeaponInitialized)
            {
                m_LastWeaponId = current;
                m_InitialWeaponId = current;
                m_WeaponInitialized = true;
                return;
            }
            if (current == m_LastWeaponId) return;
            m_LastWeaponId = current;
            int sec = m_SessionStarted ? (int)m_SessionElapsed : 0;
            string name = GetWeaponName(current);
            m_WeaponSwitches.Add(new WeaponSwitch { timeSec = sec, weaponId = current, weaponName = name });
            Debug.Log($"[StatsMonitor] 切换武器: {name} (id={current}) @ {sec}s");
        }

        // 滑条调整刷怪倍率：立即同步到 EnemySpawnDirector 并记录一次调整（供 CSV 分析）。
        private void ApplySpawnRateMultiplier(float multiplier)
        {
            multiplier = Mathf.Clamp(multiplier, 0.1f, 20f);
            if (m_SpawnDirector != null) m_SpawnDirector.runtimeSpawnRateMultiplier = multiplier;
            int sec = m_SessionStarted ? (int)m_SessionElapsed : 0;
            m_SpawnRateChanges.Add(new SpawnRateChange { timeSec = sec, multiplier = multiplier });
            Debug.Log($"[StatsMonitor] 刷怪倍率调整为 x{multiplier:F1} @ {sec}s");
        }

        // 武器 id → 显示名（未知 id 回退到数字）
        private static string GetWeaponName(uint weaponId)
        {
            return s_WeaponNames.TryGetValue(weaponId, out string name) ? name : $"武器 {weaponId}";
        }

        // 绑定 BossEncounterDirector（服务器权威；整局一个实例，3 阶段共用同一物体）。可能晚于监控生成，每帧尝试绑定。
        private void TryWireBoss()
        {
            if (m_BossDirector != null) return;
            var director = FindAnyObjectByType<BossEncounterDirector>();
            if (director == null || !director.IsSpawned) return;
            m_BossDirector = director;
        }

        // 每帧轮询 Boss 状态机，记录每阶段战时间（进入 Battle 开始，清空/击败结束）。
        // 计时使用 m_SessionElapsed（游戏内秒），与监控其它段同一时间轴；菜单暂停期间该值冻结，故暂停不计入 Boss 战时间。
        private void PollBoss()
        {
            if (m_BossDirector == null) return;
            if (!m_BossDirector.IsSpawned)
            {
                // boss 物体已卸载：若正在计时则记为异常中断
                if (m_BossStageBattleActive) FinalizeBossStage("aborted");
                m_BossDirector = null;
                m_BossGuardPrimed = false;
                m_BossGuardCurrent = 0f;
                return;
            }

            BossEncounterState state = m_BossDirector.State;
            int stage = m_BossDirector.StageNumber;

            // 采样格挡条 → 反推本帧玩家对 Boss 的输出（必须在下面的状态判断 return 之前执行）
            SampleBossGuard(stage);

            if (!m_BossStageBattleActive)
            {
                // 等待该阶段进入 Battle —— Battle 即玩家开始对该阶段 Boss 输出（DPS 阶段），作为战时间起点
                if (state == BossEncounterState.Battle)
                {
                    m_BossStageBattleActive = true;
                    m_BossObservedStage = stage;
                    m_BossStageStartTime = m_SessionStarted ? m_SessionElapsed : 0f;
                    Debug.Log($"[StatsMonitor] 阶段 {stage} Boss战开始 @ {m_BossStageStartTime:F1}s");
                }
                return;
            }

            // 已在计时：阶段号前进（PhaseTransition 后载入下一阶段）或 Boss 被击败 → 本阶段战结束。
            // 注意：非最终阶段的清空以 PhaseTransition 开始为界（此时 StageNumber 仍是本阶段），最终阶段以 Defeated 为界。
            if (stage != m_BossObservedStage || state == BossEncounterState.Defeated ||
                state == BossEncounterState.PhaseTransition)
            {
                FinalizeBossStage(state == BossEncounterState.Defeated ? "final" : "defeated");
            }
        }

        // 每帧采样 Boss 格挡条：把「下降量」计为玩家对 Boss 的有效输出。
        // 只认下降、不认上升 —— 格挡条上升有 3 种来源（脱战回盾 GuardRegenPerSecond / 换阶段重载 LoadStage /
        // 玩家死亡重置 ResetToRoaming），都不是玩家伤害，若按差值直接累加会出现负伤害。
        // 换阶段时格挡条整条重载，必须重置基线，否则会把"上限变化"误算成巨额伤害。
        private void SampleBossGuard(int stage)
        {
            if (m_BossDirector == null) return;
            float guard = m_BossDirector.GuardHealth;
            m_BossGuardMax = m_BossDirector.MaxGuardHealth;

            if (!m_BossGuardPrimed || stage != m_BossGuardStage)
            {
                // 首次采样 / 换阶段：建立新基线，本帧不计伤害
                m_BossGuardLast = guard;
                m_BossGuardPrimed = true;
                m_BossGuardStage = stage;
                m_BossGuardCurrent = guard;
                return;
            }

            float loss = m_BossGuardLast - guard;
            if (loss > 0f)
            {
                m_TotalBossDamage += loss;
                m_BossDamageBuckets[m_BucketHead] += loss;
            }
            m_BossGuardLast = guard;
            m_BossGuardCurrent = guard;
        }

        private void FinalizeBossStage(string result)
        {
            float endSec = m_SessionStarted ? m_SessionElapsed : 0f;
            float dur = endSec - m_BossStageStartTime;
            if (dur < 0f) dur = 0f;
            m_BossStageFights.Add(new BossStageFight
            {
                stageNumber = m_BossObservedStage,
                startSec = (int)m_BossStageStartTime,
                endSec = (int)endSec,
                durationSec = dur,
                result = result,
            });
            Debug.Log($"[StatsMonitor] 阶段 {m_BossObservedStage} Boss战结束: 开始 {m_BossStageStartTime:F1}s / 结束 {endSec:F1}s / 用时 {dur:F1}s / 结果 {result}");
            m_BossStageBattleActive = false;
            m_BossObservedStage = 0;
        }

        private void AdvanceBucket()
        {
            m_BucketHead = (m_BucketHead + 1) % BucketCount;
            m_DamageBuckets[m_BucketHead] = 0f;
            m_KillBuckets[m_BucketHead] = 0;
            m_ScarletBuckets[m_BucketHead] = 0f;
            m_CoinBuckets[m_BucketHead] = 0f;      // 修复：原先漏清魔币桶，导致"每分钟魔币"实际是累计魔币
            m_BossDamageBuckets[m_BucketHead] = 0f;

            // 在桶切换时采样猩红增量（只计正向流入 = 获取量）
            if (m_Wired && m_CoreStats != null)
            {
                float current = m_CoreStats.GetCurrentValue(StatKeys.Scarlet);
                float delta = current - m_LastScarlet;
                if (delta > 0f)
                {
                    m_TotalScarlet += delta;
                    m_ScarletBuckets[m_BucketHead] += delta;
                }
                m_LastScarlet = current;
            }

            // 在桶切换时采样魔币增量（StatKeys.Coin，商店购买用金币；只计正向流入 = 获取量；商店消费为负增量，不计入累计）
            if (m_Wired && m_CoreStats != null)
            {
                float current = m_CoreStats.GetCurrentValue(StatKeys.Coin);
                float delta = current - m_LastCoin;
                if (delta > 0f)
                {
                    m_TotalCoin += delta;
                    m_CoinBuckets[m_BucketHead] += delta;
                }
                m_LastCoin = current;
            }

            // 记录本秒样本到本局历史（用于 CSV 全量导出：游戏开始 → 按下按钮）
            if (m_Wired && m_SessionStarted)
            {
                int sec = (int)m_SessionElapsed;
                if (sec != m_LastSnapshotSecond && sec >= 0)
                {
                    m_History.Add(new SessionSample
                    {
                        second = sec,
                        killsCum = m_TotalKills,
                        scarletCum = m_TotalScarlet,
                        damageCum = m_TotalDamage,
                        coinCum = m_TotalCoin,
                        killDelta = m_TotalKills - m_SnapKills,
                        scarletDelta = m_TotalScarlet - m_SnapScarlet,
                        damageDelta = m_TotalDamage - m_SnapDamage,
                        coinDelta = m_TotalCoin - m_SnapCoin,
                        bossGuard = m_BossGuardCurrent,
                        bossDmgCum = m_TotalBossDamage,
                        bossDmgDelta = m_TotalBossDamage - m_SnapBossDamage,
                    });
                    m_SnapKills = m_TotalKills;
                    m_SnapScarlet = m_TotalScarlet;
                    m_SnapDamage = m_TotalDamage;
                    m_SnapCoin = m_TotalCoin;
                    m_SnapBossDamage = m_TotalBossDamage;
                    m_LastSnapshotSecond = sec;
                }
            }
        }

        // 玩家确认选择/叠加血契时触发（来自 PactNetworkState.PactChanged）
        private void OnPactChanged(PactStackNetworkState value, int previousStacks)
        {
            if (value.PactId == 0 || value.Stacks <= 0) return;
            // 仅在"比基线增多"时记为一次选择（忽略开局预设与重复刷新）
            if (m_PactBaseline.TryGetValue(value.PactId, out int baseStacks))
            {
                if (value.Stacks <= baseStacks) return;
            }
            m_PactBaseline[value.PactId] = value.Stacks;

            int sec = m_SessionStarted ? (int)m_SessionElapsed : 0;
            string name = (m_PactCatalog != null && m_PactCatalog.TryGetAsset(value.PactId, out var asset))
                ? asset.DisplayName
                : $"Pact {value.PactId}";
            m_SelectionCount += 1;
            m_Choices.Add(new PactChoice
            {
                selectionIndex = m_SelectionCount,
                timeSec = sec,
                pactId = value.PactId,
                pactName = name,
                stacksAfter = value.Stacks,
            });
            Debug.Log($"[StatsMonitor] 选择血契 #{m_SelectionCount}: {name} (id={value.PactId}, 层={value.Stacks}) @ {sec}s");
        }

        private void OnDestroy()
        {
            if (m_Wired && m_ResolutionHost != null)
            {
                m_ResolutionHost.UnregisterCombatResolutionListener(this);
            }
            if (m_PactState != null)
            {
                m_PactState.PactChanged -= OnPactChanged;
            }
            if (m_AffixState != null)
            {
                m_AffixState.AffixesChanged -= OnAffixesChanged;
            }
            m_Wired = false;
        }

        #region 指标计算

        private float RollingDamage => Sum(m_DamageBuckets);   // 最近 60s 总伤害 = 每分钟伤害
        private int RollingKills => Sum(m_KillBuckets);        // 最近 60s 击杀数 = 每分钟击杀
        private float RollingScarlet => Sum(m_ScarletBuckets); // 最近 60s 获取猩红 = 每分钟获取
        private float RollingCoin => Sum(m_CoinBuckets);     // 最近 60s 获取魔币 = 每分钟获取
        private float RollingBossDamage => Sum(m_BossDamageBuckets); // 最近 60s 对 Boss 输出 = 每分钟对 Boss 输出（格挡条下降量）

        private double ElapsedMinutes => m_SessionStarted ? m_SessionElapsed / 60d : 0d;

        private static float Sum(float[] arr)
        {
            float s = 0f;
            for (int i = 0; i < arr.Length; i++) s += arr[i];
            return s;
        }

        private static int Sum(int[] arr)
        {
            int s = 0;
            for (int i = 0; i < arr.Length; i++) s += arr[i];
            return s;
        }

        #endregion

        #region 显示

        private void OnGUI()
        {
            if (!m_Visible) return;
            if (s_BoldLabel == null) s_BoldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            const int w = 320;
            // 窗口高度自适应屏幕，但封顶 560，避免在小屏上把按钮顶出可视区
            int h = Mathf.Min(Screen.height - 40, 560);
            int x = Screen.width - w - 12;
            int y = 12;

            GUILayout.BeginArea(new Rect(x, y, w, h), GUI.skin.box);
            GUILayout.Label("运行时统计监控 (F3 开关 / F4 保存摘要)", s_BoldLabel);

            // —— 顶部固定操作区：导出按钮永远在可视区，不会被下面的信息流顶下去 ——
            if (GUILayout.Button("导出CSV (本局全量)"))
            {
                ExportCsv();
            }
            if (!string.IsNullOrEmpty(m_LastExportMsg))
            {
                GUILayout.Label(m_LastExportMsg);
            }

            // —— 刷怪倍率滑条（实时乘到 EnemySpawnDirector 刷怪速率上；范围 0.1 ~ 20 倍）——
            GUILayout.BeginHorizontal();
            GUILayout.Label($"刷怪倍率 x{m_SpawnRateMultiplier:F1}", GUILayout.Width(130));
            float rawMult = GUILayout.HorizontalSlider(m_SpawnRateMultiplier, 0.1f, 20f);
            GUILayout.EndHorizontal();
            float quantizedMult = Mathf.Round(rawMult * 10f) / 10f;
            if (!Mathf.Approximately(quantizedMult, m_SpawnRateMultiplier))
            {
                m_SpawnRateMultiplier = quantizedMult;
                ApplySpawnRateMultiplier(quantizedMult);
            }

            // —— 时间驱动曲线实时读数（只读）：刷怪速率与怪物血量随本局探索时长增长，二者相乘=每分涌入血量 ——
            if (m_SpawnDirector != null)
            {
                float rateRamp = m_SpawnDirector.CurrentTimeRampMultiplier;
                float hpRamp = m_SpawnDirector.CurrentHealthRampMultiplier;
                GUILayout.Label($"时间曲线: 刷怪 x{rateRamp:F2} | 怪血 x{hpRamp:F2} | 涌入 x{rateRamp * hpRamp:F2}/分");
            }

            if (!m_Wired)
            {
                GUILayout.Label("等待本地玩家接入…");
                GUILayout.EndArea();
                return;
            }

            // —— 信息区放入滚动视图：信息再多也可滚动查看，导出按钮固定在上方 ——
            m_Scroll = GUILayout.BeginScrollView(m_Scroll, false, true);

            double min = ElapsedMinutes;
            float avgKills = min > 0d ? m_TotalKills / (float)min : 0f;
            float avgScarlet = min > 0d ? m_TotalScarlet / (float)min : 0f;
            float avgDamage = min > 0d ? m_TotalDamage / (float)min : 0f;
            float avgCoin = min > 0d ? m_TotalCoin / (float)min : 0f;

            GUILayout.Label($"已运行: {min:F1} 分");
            if (MenuPauseController.IsPaused)
            {
                GUILayout.Label("⏸ 已暂停（菜单打开，计时冻结）", s_BoldLabel);
            }
            GUILayout.Label($"击杀  总 {m_TotalKills}  | 滚动 {RollingKills}/分 | 均值 {avgKills:F1}/分");
            GUILayout.Label($"猩红  总 {m_TotalScarlet:F0} | 滚动 {RollingScarlet:F0}/分 | 均值 {avgScarlet:F0}/分");
            GUILayout.Label($"输出  总 {m_TotalDamage:F0} | 滚动 {RollingDamage:F0}/分 | 均值 {avgDamage:F0}/分");
            // 魔币：显示获取速率 + 当前余额（余额=StatKeys.Coin，商店消费会让余额下降，但 m_TotalCoin 只计获取量）
            float coinBal = m_CoreStats != null ? m_CoreStats.GetCurrentValue(StatKeys.Coin) : 0f;
            GUILayout.Label($"魔币  余额 {coinBal:F0} | 获取 总 {m_TotalCoin:F0} | 滚动 {RollingCoin:F0}/分 | 均值 {avgCoin:F0}/分");
            if (m_TotalKills > 0)
            {
                GUILayout.Label($"效率  猩红/杀 {m_TotalScarlet / m_TotalKills:F2} | 伤/杀 {m_TotalDamage / m_TotalKills:F1}");
            }
            if (m_Choices.Count > 0)
            {
                var last = m_Choices[m_Choices.Count - 1];
                GUILayout.Label($"血契选择 {m_Choices.Count} 次 | 最近: {last.pactName} @{last.timeSec}s");
            }
            if (m_AffixChoices.Count > 0)
            {
                var last = m_AffixChoices[m_AffixChoices.Count - 1];
                GUILayout.Label($"副契选择 {m_AffixChoices.Count} 次 | 最近: {last.affixName} @{last.timeSec}s");
            }
            // Boss 阶段战时间（整局 3 场；仅统计进入 Battle 后的 DPS 用时）
            if (m_BossStageFights.Count > 0)
            {
                float total = 0f;
                for (int i = 0; i < m_BossStageFights.Count; i++) total += m_BossStageFights[i].durationSec;
                var last = m_BossStageFights[m_BossStageFights.Count - 1];
                GUILayout.Label($"Boss战 {m_BossStageFights.Count}/3 场 | 累计 {total:F1}s | 最近阶段{last.stageNumber} 用时 {last.durationSec:F1}s ({last.result})");
            }
            else if (m_BossStageBattleActive)
            {
                GUILayout.Label($"Boss战 进行中: 阶段 {m_BossObservedStage} 已 {m_SessionElapsed - m_BossStageStartTime:F1}s…");
            }
            // 对 Boss 输出（格挡条）：木桩测试主读数 —— bossDmg 是"格挡条下降量"，破防进入 Battle 后不再统计
            if (m_BossDirector != null && m_BossGuardPrimed)
            {
                float avgBoss = min > 0d ? m_TotalBossDamage / (float)min : 0f;
                GUILayout.Label($"Boss格挡 {m_BossGuardCurrent:F0}/{m_BossGuardMax:F0} | 对Boss输出 滚动 {RollingBossDamage:F0}/分 | 累计 {m_TotalBossDamage:F0} | 均值 {avgBoss:F0}/分");
            }

            // 当前武器 + 武器切换 / 刷怪倍率调整记录
            if (m_AbilityHost != null)
            {
                GUILayout.Label($"当前武器: {GetWeaponName(m_AbilityHost.ActiveWeaponId)} (id={m_AbilityHost.ActiveWeaponId})");
            }
            if (m_WeaponSwitches.Count > 0)
            {
                var lastW = m_WeaponSwitches[m_WeaponSwitches.Count - 1];
                GUILayout.Label($"武器切换 {m_WeaponSwitches.Count} 次 | 最近: {lastW.weaponName} @{lastW.timeSec}s");
            }
            if (m_SpawnRateChanges.Count > 0)
            {
                var lastR = m_SpawnRateChanges[m_SpawnRateChanges.Count - 1];
                GUILayout.Label($"刷怪倍率调整 {m_SpawnRateChanges.Count} 次 | 最近: x{lastR.multiplier:F1} @{lastR.timeSec}s");
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // F4：把当前关键摘要「打印到控制台」+「保存为 .txt 文件」（之前只打印控制台，没有保存功能）
        private void DumpSummary()
        {
            if (!m_Wired) return;
            double min = ElapsedMinutes;
            float coinBal = m_CoreStats != null ? m_CoreStats.GetCurrentValue(StatKeys.Coin) : 0f;
            string line = $"[StatsMonitor] t={min:F1}min | " +
                          $"kill={m_TotalKills}({RollingKills}/min,{m_TotalKills / Math.Max(min, 1e-3):F1}avg) | " +
                          $"scarlet={m_TotalScarlet:F0}({RollingScarlet:F0}/min) | " +
                          $"coin={m_TotalCoin:F0}(bal={coinBal:F0},{RollingCoin:F0}/min) | " +
                          $"bossDmg={m_TotalBossDamage:F0}({RollingBossDamage:F0}/min,guard={m_BossGuardCurrent:F0}/{m_BossGuardMax:F0}) | " +
                          $"dmg={m_TotalDamage:F0}({RollingDamage:F0}/min) | " +
                          $"scarlet/kill={(m_TotalKills > 0 ? m_TotalScarlet / m_TotalKills : 0):F2}";
            Debug.Log(line);
            try
            {
                string dir = ResolveExportDir();
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string path = dir + "/StatsSummary_" + stamp + ".txt";
                File.WriteAllText(path, line, new UTF8Encoding(true));
                Debug.Log($"[StatsMonitor] 摘要已保存: {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StatsMonitor] F4 摘要保存失败: {e.Message}");
            }
        }

        // 导出/F4保存共用的目录解析：
        // 编辑器下写进项目 Assets（与既有的 6 份 CSV 工作流一致，Project 窗口直接可见）；
        // 打包后的 exe 里 Application.dataPath 指向只读的 _Data 目录，故构建版改写到始终可写的 persistentDataPath。
        private string ResolveExportDir()
        {
            return Application.isEditor
                ? Application.dataPath + "/VampireHunt/Scripts/Diagnostics"
                : Application.persistentDataPath + "/VampireHuntStats";
        }

        private void ExportCsv()
        {
            if (!m_Wired)
            {
                m_LastExportMsg = "未接入玩家，无法导出";
                return;
            }
            try
            {
                string dir = ResolveExportDir();
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string path = dir + "/StatsExport_" + stamp + ".csv";

                var sb = new StringBuilder();
                // 第 1 段：逐秒原始数据（从游戏开始到导出时刻）
                sb.AppendLine("# 逐秒监控 (t_sec=游戏开始后整秒; *_cum=累计; *_delta=该秒增量; boss_guard=该秒末Boss格挡条当前值; boss_dmg_*=对Boss输出,由格挡条下降量反推)");
                sb.AppendLine("t_sec,kills_cum,scarlet_cum,damage_cum,coin_cum,kills_delta,scarlet_delta,damage_delta,coin_delta,boss_guard,boss_dmg_cum,boss_dmg_delta");
                for (int i = 0; i < m_History.Count; i++)
                {
                    var s = m_History[i];
                    sb.AppendLine($"{s.second},{s.killsCum},{s.scarletCum:F0},{s.damageCum:F0},{s.coinCum:F0},{s.killDelta},{s.scarletDelta:F0},{s.damageDelta:F0},{s.coinDelta:F0},{s.bossGuard:F0},{s.bossDmgCum:F0},{s.bossDmgDelta:F0}");
                }

                // 第 2 段：每分钟汇总（直接对应原 KPI：每分钟击杀/猩红/输出/魔币）
                sb.AppendLine();
                sb.AppendLine("# 每分钟汇总 (minute=第几分钟; *_in_min=该分钟增量; boss_dmg_in_min=该分钟对Boss输出/格挡条下降量)");
                sb.AppendLine("minute,kills_in_min,scarlet_in_min,damage_in_min,coin_in_min,boss_dmg_in_min,kills_cum,scarlet_cum,damage_cum,coin_cum,boss_dmg_cum");
                var mk = new Dictionary<int, int>();
                var ms = new Dictionary<int, float>();
                var md = new Dictionary<int, float>();
                var mc = new Dictionary<int, float>();
                var mb = new Dictionary<int, float>();
                var mkc = new Dictionary<int, int>();
                var msc = new Dictionary<int, float>();
                var mdc = new Dictionary<int, float>();
                var mcc = new Dictionary<int, float>();
                var mbc = new Dictionary<int, float>();
                foreach (var s in m_History)
                {
                    int m = s.second / 60;
                    if (!mk.ContainsKey(m)) { mk[m] = 0; ms[m] = 0f; md[m] = 0f; mc[m] = 0f; mb[m] = 0f; }
                    mk[m] += s.killDelta;
                    ms[m] += s.scarletDelta;
                    md[m] += s.damageDelta;
                    mc[m] += s.coinDelta;
                    mb[m] += s.bossDmgDelta;
                    mkc[m] = s.killsCum;
                    msc[m] = s.scarletCum;
                    mdc[m] = s.damageCum;
                    mcc[m] = s.coinCum;
                    mbc[m] = s.bossDmgCum;
                }
                foreach (int m in mk.Keys.OrderBy(k => k))
                {
                    sb.AppendLine($"{m},{mk[m]},{ms[m]:F0},{md[m]:F0},{mc[m]:F0},{mb[m]:F0},{mkc[m]},{msc[m]:F0},{mdc[m]:F0},{mcc[m]:F0},{mbc[m]:F0}");
                }

                // 第 3 段：血契选择记录（第几次 / 游戏内时间点 / 选了哪个）
                sb.AppendLine();
                sb.AppendLine("# 血契选择记录 (selection=第几次; t_sec=游戏开始后秒; stacks_after=选完后层数)");
                sb.AppendLine("selection,t_sec,pact_id,pact_name,stacks_after");
                for (int i = 0; i < m_Choices.Count; i++)
                {
                    var c = m_Choices[i];
                    sb.AppendLine($"{c.selectionIndex},{c.timeSec},{c.pactId},\"{c.pactName}\",{c.stacksAfter}");
                }

                // 第 4 段：各血契生效段效率（段0=开局到首血契；段i=第i次选择后到下次选择前；末段到导出时刻）
                // 直接对比"选了不同血契"后的击杀/猩红/输出效率，用于判断哪类血契影响击杀效率
                sb.AppendLine();
                sb.AppendLine("# 各血契生效段效率 (segment: 0=开局, 1=首血契生效后, 2=第2次血契生效后 …; 末段到导出时刻)");
                sb.AppendLine("segment,pact_name,t_start,t_end,seconds,kills,scarlet,damage,kills_per_min,scarlet_per_min,damage_per_min");
                var bounds = new List<int> { 0 };
                for (int i = 0; i < m_Choices.Count; i++) bounds.Add(m_Choices[i].timeSec);
                int endT = m_History.Count > 0 ? m_History[m_History.Count - 1].second : 0;
                for (int s = 0; s < bounds.Count; s++)
                {
                    int startT = bounds[s];
                    int endSeg = (s + 1 < bounds.Count) ? bounds[s + 1] : endT;
                    if (endSeg <= startT) endSeg = startT + 1; // 防 0 时长段
                    string segName = s == 0 ? "(开局)" : m_Choices[s - 1].pactName;
                    int k = 0; float sc = 0f; float d = 0f;
                    for (int i = 0; i < m_History.Count; i++)
                    {
                        var h = m_History[i];
                        if (h.second >= startT && h.second < endSeg)
                        {
                            k += h.killDelta;
                            sc += h.scarletDelta;
                            d += h.damageDelta;
                        }
                    }
                    int dur = endSeg - startT;
                    float kpm = k * 60f / dur;
                    float spm = sc * 60f / dur;
                    float dpm = d * 60f / dur;
                    sb.AppendLine($"{s},\"{segName}\",{startT},{endSeg},{dur},{k},{sc:F0},{d:F0},{kpm:F1},{spm:F0},{dpm:F0}");
                }

                // 第 3b 段：副契选择记录（第几次 / 游戏内时间点 / 选了哪个副契）
                sb.AppendLine();
                sb.AppendLine("# 副契选择记录 (selection=第几次; t_sec=游戏开始后秒; stacks_after=选完后层数)");
                sb.AppendLine("selection,t_sec,affix_id,affix_name,stacks_after");
                for (int i = 0; i < m_AffixChoices.Count; i++)
                {
                    var c = m_AffixChoices[i];
                    sb.AppendLine($"{c.selectionIndex},{c.timeSec},{c.affixId},\"{c.affixName}\",{c.stacksAfter}");
                }

                // 第 4b 段：各副契生效段效率（段0=开局；段i=第i次副契选择后到下次选择前；末段到导出时刻）
                // 副契=敌群缩放轴，强化敌人，通常使 kills_per_min / damage_per_min 下降；横向对比可看选哪类副契压力变化
                sb.AppendLine();
                sb.AppendLine("# 各副契生效段效率 (segment: 0=开局, 1=首副契生效后, 2=第2次副契生效后 …; 末段到导出时刻)");
                sb.AppendLine("segment,affix_name,t_start,t_end,seconds,kills,scarlet,damage,kills_per_min,scarlet_per_min,damage_per_min");
                var abounds = new List<int> { 0 };
                for (int i = 0; i < m_AffixChoices.Count; i++) abounds.Add(m_AffixChoices[i].timeSec);
                int aendT = m_History.Count > 0 ? m_History[m_History.Count - 1].second : 0;
                for (int s = 0; s < abounds.Count; s++)
                {
                    int startT = abounds[s];
                    int endSeg = (s + 1 < abounds.Count) ? abounds[s + 1] : aendT;
                    if (endSeg <= startT) endSeg = startT + 1;
                    string segName = s == 0 ? "(开局)" : m_AffixChoices[s - 1].affixName;
                    int k = 0; float sc = 0f; float d = 0f;
                    for (int i = 0; i < m_History.Count; i++)
                    {
                        var h = m_History[i];
                        if (h.second >= startT && h.second < endSeg)
                        {
                            k += h.killDelta;
                            sc += h.scarletDelta;
                            d += h.damageDelta;
                        }
                    }
                    int dur = endSeg - startT;
                    float kpm = k * 60f / dur;
                    float spm = sc * 60f / dur;
                    float dpm = d * 60f / dur;
                    sb.AppendLine($"{s},\"{segName}\",{startT},{endSeg},{dur},{k},{sc:F0},{d:F0},{kpm:F1},{spm:F0},{dpm:F0}");
                }

                // 第 4c 段：每阶段 Boss 战时间（stage=第几场 1/2/3；start_sec/end_sec=游戏内起止秒；duration_sec=用时秒；result=结果）
                sb.AppendLine();
                sb.AppendLine("# 每阶段Boss战时间 (stage=第几场Boss战 1/2/3; start_sec/end_sec=游戏内起止秒; duration_sec=用时秒; result: defeated=阶段清空进入下一场 / final=最终击败通关 / aborted=异常中断 / ongoing=导出时仍在进行)");
                sb.AppendLine("stage,start_sec,end_sec,duration_sec,result");
                for (int i = 0; i < m_BossStageFights.Count; i++)
                {
                    var b = m_BossStageFights[i];
                    sb.AppendLine($"{b.stageNumber},{b.startSec},{b.endSec},{b.durationSec:F1},{b.result}");
                }
                // 若导出时仍有进行中的阶段战，追加一行（未结束，end_sec 记为当前游戏内秒）
                if (m_BossStageBattleActive)
                {
                    int curEnd = m_SessionStarted ? (int)m_SessionElapsed : 0;
                    float curDur = curEnd - m_BossStageStartTime;
                    if (curDur < 0f) curDur = 0f;
                    sb.AppendLine($"{m_BossObservedStage},{m_BossStageStartTime:F0},{curEnd},{curDur:F1},ongoing");
                }

                // 第 5 段：武器切换记录（第几次 / 游戏内时间点 / 切换成了哪把武器）
                sb.AppendLine();
                sb.AppendLine("# 武器切换记录 (switch=第几次; t_sec=游戏开始后秒; weapon_id=武器id; weapon_name=武器名)");
                sb.AppendLine("switch,t_sec,weapon_id,weapon_name");
                for (int i = 0; i < m_WeaponSwitches.Count; i++)
                {
                    var w = m_WeaponSwitches[i];
                    sb.AppendLine($"{i + 1},{w.timeSec},{w.weaponId},\"{w.weaponName}\"");
                }

                // 第 5b 段：各武器生效段效率（段0=开局武器；段i=第i次切换后到下次切换前；末段到导出时刻）
                // boss_dmg_per_min = 对 Boss 输出（格挡条下降量）每分钟 —— Boss 木桩测「对单 DPS」的主读数
                sb.AppendLine();
                sb.AppendLine("# 各武器生效段效率 (segment: 0=开局武器, 1=第1次切换后, 2=第2次切换后 …; 末段到导出时刻; boss_dmg=对Boss输出(格挡条下降量))");
                sb.AppendLine("segment,weapon_name,t_start,t_end,seconds,kills,damage,boss_dmg,kills_per_min,damage_per_min,boss_dmg_per_min");
                var wbounds = new List<int> { 0 };
                for (int i = 0; i < m_WeaponSwitches.Count; i++) wbounds.Add(m_WeaponSwitches[i].timeSec);
                int wendT = m_History.Count > 0 ? m_History[m_History.Count - 1].second : 0;
                for (int s = 0; s < wbounds.Count; s++)
                {
                    int startT = wbounds[s];
                    int endSeg = (s + 1 < wbounds.Count) ? wbounds[s + 1] : wendT;
                    if (endSeg <= startT) endSeg = startT + 1;
                    string segName = s == 0 ? GetWeaponName(m_InitialWeaponId) : m_WeaponSwitches[s - 1].weaponName;
                    int k = 0; float d = 0f; float bd = 0f;
                    for (int i = 0; i < m_History.Count; i++)
                    {
                        var h = m_History[i];
                        if (h.second >= startT && h.second < endSeg)
                        {
                            k += h.killDelta;
                            d += h.damageDelta;
                            bd += h.bossDmgDelta;
                        }
                    }
                    int dur = endSeg - startT;
                    sb.AppendLine($"{s},\"{segName}\",{startT},{endSeg},{dur},{k},{d:F0},{bd:F0},{k * 60f / dur:F1},{d * 60f / dur:F0},{bd * 60f / dur:F0}");
                }

                // 第 6 段：刷怪倍率调整记录（第几次 / 游戏内时间点 / 调整为多少倍）
                sb.AppendLine();
                sb.AppendLine("# 刷怪倍率调整记录 (adj=第几次; t_sec=游戏开始后秒; multiplier=调整后倍率, 1=默认)");
                sb.AppendLine("adj,t_sec,multiplier");
                for (int i = 0; i < m_SpawnRateChanges.Count; i++)
                {
                    var c = m_SpawnRateChanges[i];
                    sb.AppendLine($"{i + 1},{c.timeSec},{c.multiplier:F1}");
                }

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                m_LastExportMsg = "已导出: " + path;
                Debug.Log($"[StatsMonitor] 已导出 CSV: {path} (逐秒 {m_History.Count} 行 + 每分钟 {mk.Count} 行)");
            }
            catch (Exception e)
            {
                m_LastExportMsg = "导出失败: " + e.Message;
                Debug.LogError("[StatsMonitor] 导出 CSV 失败: " + e);
            }
        }

        #endregion

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 编辑器 / 开发构建中自动生成监控物体，不修改任何预制体或核心场景物体
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (FindAnyObjectByType<RuntimeStatsMonitor>() != null) return;
            var go = new GameObject("RuntimeStatsMonitor");
            go.AddComponent<RuntimeStatsMonitor>();
            DontDestroyOnLoad(go);
        }
#endif
    }
}
