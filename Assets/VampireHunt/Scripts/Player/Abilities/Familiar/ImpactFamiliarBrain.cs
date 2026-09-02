using System.Collections.Generic;
using UnityEngine;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Player.Abilities.Familiar
{
    /// <summary>
    /// 单只撞击水滴使魔的状态机（familiar state machine）：<br/>
    /// <b>Orbit</b>（持续环绕玩家）→ <b>Dash</b>（朝目标冲刺，撞上后<b>锁定方向直穿出去</b>）
    /// → <b>Turn</b>（滑行掉头）→ 再次 <b>Dash</b> …… 来回穿插，直到退出条件满足才 <b>Return</b>。<br/>
    /// 纯逻辑类（不继承 MonoBehaviour），由 <c>ImpactFamiliarController</c> 每帧驱动并结算伤害。
    /// </summary>
    /// <remarks>
    /// 四条硬性规则在此落地：
    /// 1. **不主动索敌** —— 只有控制器把「玩家命中过的敌人」登记进来（<see cref="SetPendingTarget"/>）才会起飞。
    /// 2. **穿插循环** —— 撞上目标后<b>不停顿、不返回</b>，沿锁定方向穿出去 <c>OvershootDistance</c> 米，
    ///    再滑行掉头冲回来，如此反复（这是「来回穿插」而非「停在脚底」的关键）。
    /// 3. **退出条件** —— 目标死亡（<c>LockedTarget == null</c>）、玩家改变目标、候选过期、达到单目标穿插上限，
    ///    任一满足即结束交战返回轨道。
    /// 4. **无穿透上限 + 必定命中** —— 冲刺/掉头途中的球形查询<b>不做穿透计数，也不做「每敌一次」去重</b>，
    ///    路径上扫到的敌人一律结算；唯一的节流是 <c>HitCooldownPerTarget</c>（同一敌人的重复命中间隔）。
    /// 每只使魔实例各自持有一份候选缓存与命中冷却表，互不干扰。
    /// </remarks>
    /// <remarks>
    /// <b>待机队列</b>：实现 <see cref="IFamiliarOrbitMember"/> 并登记进 <see cref="FamiliarOrbitRegistry"/>，
    /// 因此<b>和别的种类（如射击僚机 162）共用同一条环绕队列</b>，不会各自从 0° 开始重叠。
    /// 起飞交战时会自动让出槽位，归队时平滑滑回队列。
    /// </remarks>
    public sealed class ImpactFamiliarBrain : IFamiliarOrbitMember
    {
        /// <summary>
        /// 候选目标的优先级（pending priority）：数值越大越优先。<br/>
        /// 高优先级（<see cref="PriorityCommand"/>）可以顶掉低优先级；低优先级<b>不能</b>顶掉仍然有效的高优先级。
        /// </summary>
        public const int PriorityPassive = 0;

        /// <summary>
        /// 玩家主动下达的指令（按住左键 → 指派鼠标附近的敌人）。<br/>
        /// 这是<u>血契「牵丝之契」</u>的核心：玩家指哪打哪，优先于「谁被打中就追谁」的被动索敌。
        /// </summary>
        public const int PriorityCommand = 1;

        public enum FamiliarState
        {
            /// <summary>待机：持续环绕玩家旋转。</summary>
            Orbit = 0,
            /// <summary>冲刺（单程）：朝目标飞，撞上后锁定方向直穿出去。</summary>
            Dash = 1,
            /// <summary>掉头：减速滑行并转向目标，走完立刻再次冲刺。</summary>
            Turn = 2,
            /// <summary>返回：飞回环绕轨道（交战结束）。</summary>
            Return = 3
        }

        private readonly ImpactFamiliarDefinition m_Definition;
        private readonly int m_Index;
        private readonly int m_Count;
        /// <summary>队列归属者（玩家根物体）：同一 owner 下所有种类的使魔排进同一条队列。</summary>
        private readonly Transform m_Owner;
        /// <summary>同一敌人的下次可命中时间（brain 本地时间），替代旧的「穿透去重」。</summary>
        private readonly Dictionary<GameplayEntityId, float> m_HitReadyAt = new Dictionary<GameplayEntityId, float>();

        private FamiliarState m_State = FamiliarState.Orbit;
        /// <summary>当前是否占用环绕队列的槽位（待机 / 返回 = 占用，交战 = 让出）。</summary>
        private bool m_OrbitQueued = true;
        private float m_StateTimer;
        private float m_Elapsed;
        private float m_BobPhase;
        private float m_DashDistance;
        private float m_PassDistance;
        private int m_PassCount;
        private bool m_HasReachedTarget;
        private Vector3 m_Position;
        private Vector3 m_Facing = Vector3.forward;
        private Vector3 m_OwnerPosition;

        // ── 本只使魔独立的候选目标缓存 ──
        private GameplayEntityId m_PendingTarget;
        private float m_PendingExpireAt;
        /// <summary>当前候选的优先级（<see cref="PriorityPassive"/> / <see cref="PriorityCommand"/>）。</summary>
        private int m_PendingPriority;

        // ── 当前穿插锁定的目标 ──
        private MonoBehaviour m_LockedTarget;
        private GameplayEntityId m_LockedTargetId;

        /// <summary>
        /// 构造一只水滴使魔的状态机。
        /// </summary>
        /// <param name="definition">配置数据。</param>
        /// <param name="index">本只使魔在<b>本类型内</b>的序号（备用：后续做交战散开时用）。</param>
        /// <param name="count">本类型使魔总数。</param>
        /// <param name="owner">队列归属者（玩家根物体）。留空则进无名队列（不推荐：不同玩家会混编）。</param>
        public ImpactFamiliarBrain(in ImpactFamiliarDefinition definition, int index, int count, Transform owner)
        {
            m_Definition = definition;
            m_Index = Mathf.Max(0, index);
            m_Count = Mathf.Max(1, count);
            m_Owner = owner;
            // 登记进统一环绕队列：角度由队列统一分配，不再各自算 360°×index/count。
            FamiliarOrbitRegistry.Register(this);
        }

        // ── 对外只读状态 ──────────────────────────────────────

        /// <summary>当前状态。</summary>
        public FamiliarState State => m_State;
        /// <summary>当前世界坐标（由状态机每帧推进）。</summary>
        public Vector3 Position => m_Position;
        /// <summary>当前朝向（冲刺时 = 飞行方向，用于水滴拉伸）。</summary>
        public Vector3 Facing => m_Facing;
        /// <summary>交战进行中（冲刺或掉头）：穿插循环期间为 true。</summary>
        public bool IsEngaged => m_State == FamiliarState.Dash || m_State == FamiliarState.Turn;
        /// <summary>
        /// 可以造成伤害的时间窗：交战期间<b>全程开放</b>（含掉头滑行段），
        /// 因此路径上 / 身体内接触到的敌人都会吃到伤害。
        /// </summary>
        public bool IsHitWindowOpen => IsEngaged;
        /// <summary>正在穿插的目标（未锁定时为 null）。</summary>
        public MonoBehaviour LockedTarget => m_LockedTarget;
        /// <summary>当前候选目标 id（未登记时为 <see cref="GameplayEntityId.None"/>）。</summary>
        public GameplayEntityId PendingTargetId => m_PendingTarget;
        /// <summary>候选目标是否仍在有效期内。</summary>
        public bool HasValidPending => !m_PendingTarget.IsNone && m_Elapsed <= m_PendingExpireAt;
        /// <summary>当前候选的优先级（调试用：0 = 被动命中，1 = 左键指令）。</summary>
        public int PendingPriority => m_PendingPriority;
        /// <summary>空闲且手上有有效候选 → 控制器应立刻为它解析目标并起飞。</summary>
        public bool WantsEngage => !IsEngaged && HasValidPending;
        /// <summary>本轮交战已完成的穿插单程数（调试用）。</summary>
        public int PassCount => m_PassCount;
        /// <summary>本只使魔在<b>本类型内</b>的序号（0 起，调试用）。</summary>
        public int Index => m_Index;
        /// <summary>本类型使魔总数（调试用）。</summary>
        public int TypeCount => m_Count;

        // ── 统一环绕队列（IFamiliarOrbitMember）──────────────
        // 注意：这几个值只是「期望值」，最终用哪个由 FamiliarOrbitRegistry 按聚合规则统一裁决。

        public Transform OrbitOwner => m_Owner;
        public float DesiredOrbitRadius => m_Definition.OrbitRadius;
        public float DesiredOrbitHeight => m_Definition.OrbitHeight;
        public float DesiredOrbitSpeed => m_Definition.OrbitSpeed;

        /// <summary>队列穿插用的种类标识：<b>撞击水滴 = 1</b>（射击僚机 = 2，将来新增种类继续往下取值）。</summary>
        public int OrbitKind => 1;

        /// <summary>
        /// 退出统一队列并将槽位让给同伴（brain 被重建、使魔被关闭、控制器销毁时必须调用，
        /// 否则队列里会留下一个永远没人站的空位）。
        /// </summary>
        public void ReleaseOrbitSlot() => FamiliarOrbitRegistry.Release(this);

        // ── 候选目标登记 ──────────────────────────────────────

        /// <summary>
        /// 登记候选目标（玩家攻击命中敌人时由控制器调用）。
        /// 新命中的敌人会<b>覆盖</b>旧候选；本次冲刺是否立刻让位给新目标由
        /// <see cref="ImpactFamiliarDefinition.SwitchTargetOnNewCandidate"/> 决定。
        /// </summary>
        /// <param name="targetId">目标实体 id。</param>
        /// <param name="lifetime">本条候选的有效期（秒）。</param>
        /// <param name="priority">
        /// 优先级（<see cref="PriorityPassive"/> / <see cref="PriorityCommand"/>）。<br/>
        /// <b>低于当前候选优先级的登记会被忽略</b>（只要当前候选还没过期）——
        /// 这就是「左键指令优先于被动索敌」：玩家指了目标之后，武器误伤到别的怪不会把使魔带走。
        /// </param>
        public void SetPendingTarget(GameplayEntityId targetId, float lifetime, int priority = PriorityPassive)
        {
            if (targetId.IsNone) return;
            // 低优先级不能顶掉仍然有效的高优先级候选。
            if (priority < m_PendingPriority && HasValidPending) return;

            m_PendingTarget = targetId;
            m_PendingPriority = priority;
            m_PendingExpireAt = m_Elapsed + Mathf.Max(0f, lifetime);
        }

        /// <summary>清空候选目标（玩家主动关闭使魔、或血契失效时调用）。</summary>
        public void ClearPendingTarget()
        {
            m_PendingTarget = GameplayEntityId.None;
            m_PendingExpireAt = 0f;
            m_PendingPriority = PriorityPassive;
        }

        // ── 起飞 / 打断 ───────────────────────────────────────

        /// <summary>开始一轮穿插交战（由控制器在解析出目标对象后调用）。会重置命中冷却表与穿插计数。</summary>
        public void BeginDash(MonoBehaviour target, GameplayEntityId targetId)
        {
            if (target == null) return;

            m_LockedTarget = target;
            m_LockedTargetId = targetId;
            m_HitReadyAt.Clear();
            m_PassCount = 0;
            StartDashSegment();
        }

        /// <summary>
        /// 命中节流：<b>不做穿透计数，也不做「每敌一次」去重</b>。
        /// 只要该敌人已过冷却（<c>HitCooldownPerTarget</c>）就能再次结算；冷却设为 0 则每帧都结算。
        /// 返回 true 表示本次可以结算伤害。
        /// </summary>
        public bool TryConsumeHit(GameplayEntityId targetId)
        {
            if (targetId.IsNone) return false;

            float cooldown = Mathf.Max(0f, m_Definition.HitCooldownPerTarget);
            if (cooldown <= 0f) return true;   // 无冷却：接触即结算（伤害极高，慎用）

            if (m_HitReadyAt.TryGetValue(targetId, out float readyAt) && m_Elapsed < readyAt) return false;
            m_HitReadyAt[targetId] = m_Elapsed + cooldown;
            return true;
        }

        // ── 每帧推进 ──────────────────────────────────────────

        /// <summary>推进状态机一帧。</summary>
        /// <param name="deltaTime">帧间隔（秒）。</param>
        /// <param name="ownerPosition">玩家当前位置（环绕中心）。</param>
        public void Tick(float deltaTime, Vector3 ownerPosition)
        {
            m_Elapsed += deltaTime;
            m_OwnerPosition = ownerPosition;
            m_BobPhase += deltaTime * m_Definition.BobFrequency;
            UpdateOrbitMembership();

            switch (m_State)
            {
                case FamiliarState.Dash:   TickDash(deltaTime);   break;
                case FamiliarState.Turn:   TickTurn(deltaTime);   break;
                case FamiliarState.Return: TickReturn(deltaTime); break;
                default:                   TickOrbit(deltaTime);  break;
            }
        }

        /// <summary>把使魔瞬移到玩家的环绕轨道上（生成、重置、玩家传送时用）。</summary>
        public void SnapToOrbit(Vector3 ownerPosition)
        {
            m_OwnerPosition = ownerPosition;
            // deltaTime = 0：只读队列、不推进整体相位（避免瞬移落位把队列转走一帧）。
            FamiliarOrbitSlot slot = FamiliarOrbitRegistry.GetSlot(this, 0f);
            m_Position = ComputeOrbitPosition(slot.Angle, slot.Radius, slot.Height);
            m_Facing = Vector3.forward;
        }

        /// <summary>
        /// 同步自己在统一队列里的占位：只有待机（<see cref="FamiliarState.Orbit"/>）与返回
        /// （<see cref="FamiliarState.Return"/>）占槽位，起飞交战就把位置让出来，
        /// 让还在待机的同伴重新均匀分布（队列不会留下空洞）。
        /// </summary>
        private void UpdateOrbitMembership()
        {
            bool wanted = m_State == FamiliarState.Orbit || m_State == FamiliarState.Return;
            if (wanted == m_OrbitQueued) return;
            m_OrbitQueued = wanted;
            FamiliarOrbitRegistry.SetOrbiting(this, wanted);
        }

        /// <summary>强制结束本次交战并回到轨道（关闭使魔时用）。</summary>
        public void AbortEngagement()
        {
            m_LockedTarget = null;
            m_LockedTargetId = GameplayEntityId.None;
            m_HitReadyAt.Clear();
            m_PassCount = 0;
            m_State = FamiliarState.Return;
            m_StateTimer = 0f;
        }

        // ── 各状态 ────────────────────────────────────────────

        private void TickOrbit(float deltaTime)
        {
            // 角度、半径、高度全部由统一队列给出：跨种类连续编号，不再和别的使魔叠在一起。
            FamiliarOrbitSlot slot = FamiliarOrbitRegistry.GetSlot(this, deltaTime);
            Vector3 target = ComputeOrbitPosition(slot.Angle, slot.Radius, slot.Height);
            float t = 1f - Mathf.Exp(-m_Definition.FollowLerp * deltaTime);
            m_Position = Vector3.Lerp(m_Position, target, t);
            FaceAlong(target - m_Position);
        }

        /// <summary>一个穿插单程：逼近目标 → 撞上 → 沿锁定方向直穿出去 → 掉头。</summary>
        private void TickDash(float deltaTime)
        {
            m_StateTimer += deltaTime;

            // ① 目标死亡或被销毁 → 立刻结束交战（不算打断）。
            if (m_LockedTarget == null)
            {
                FinishEngagement();
                return;
            }

            // ② 玩家改变目标 → 按配置决定是否立刻让位。
            if (m_Definition.SwitchTargetOnNewCandidate && HasDifferentPending())
            {
                FinishEngagement();
                return;
            }

            float step = m_Definition.DashSpeed * deltaTime;

            if (!m_HasReachedTarget)
            {
                // 逼近段：每帧朝目标修正方向。
                // 只取水平分量：Boss 这类高个子目标的 transform.position 高度与使魔飞行高度差很多，
                // 用三维距离会导致永远判定不了「撞上」，从而卡在目标脚边。
                Vector3 delta = m_LockedTarget.transform.position - m_Position;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance > 0.0001f) m_Facing = delta / distance;

                if (distance <= m_Definition.ArriveDistance) m_HasReachedTarget = true;
                else
                {
                    // 追不上 / 冲太远 → 放弃本单程，掉头（防止绕着跑不掉的敌人空转）。
                    if (m_StateTimer >= m_Definition.MaxDashDuration || m_DashDistance >= m_Definition.MaxDashDistance)
                    {
                        EnterTurn();
                        return;
                    }
                }
            }

            m_Position += m_Facing * step;
            m_DashDistance += step;

            if (m_HasReachedTarget)
            {
                // 穿越段：方向已锁定，径直穿出去，不再回头修正（否则会在目标身上抖动）。
                m_PassDistance += step;
                if (m_PassDistance >= m_Definition.OvershootDistance) EnterTurn();
            }
        }

        /// <summary>两个单程之间：以 TurnSpeedRatio 的速度继续滑行并把朝向转回目标，到点立刻再冲。</summary>
        private void TickTurn(float deltaTime)
        {
            m_StateTimer += deltaTime;

            // 目标死亡 → 结束交战。
            if (m_LockedTarget == null)
            {
                FinishEngagement();
                return;
            }
            // 玩家改变目标 → 让位（这里是自然的切换时机）。
            if (HasDifferentPending())
            {
                FinishEngagement();
                return;
            }
            // 候选过期 → 收手返回待机（玩家停手后不再无限追打旧目标）。
            if (m_Definition.StopWhenPendingExpired && !HasValidPending)
            {
                FinishEngagement();
                return;
            }
            // 单目标穿插次数上限（0 = 不限）。
            if (m_Definition.MaxPassesPerTarget > 0 && m_PassCount >= m_Definition.MaxPassesPerTarget)
            {
                FinishEngagement();
                return;
            }

            // 滑行 + 转向：位置沿当前朝向继续走，朝向插值转回目标，形成回摆弧线。
            Vector3 toTarget = m_LockedTarget.transform.position - m_Position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                float t = 1f - Mathf.Exp(-m_Definition.TurnLerp * deltaTime);
                m_Facing = Vector3.Slerp(m_Facing, toTarget.normalized, t).normalized;
            }
            m_Position += m_Facing * (m_Definition.DashSpeed * m_Definition.TurnSpeedRatio * deltaTime);

            if (m_StateTimer >= m_Definition.TurnDuration) StartDashSegment();
        }

        private void TickReturn(float deltaTime)
        {
            FamiliarOrbitSlot slot = FamiliarOrbitRegistry.GetSlot(this, deltaTime);
            Vector3 orbitPosition = ComputeOrbitPosition(slot.Angle, slot.Radius, slot.Height);
            Vector3 delta = orbitPosition - m_Position;
            float distance = delta.magnitude;

            if (distance <= m_Definition.ReturnArriveDistance)
            {
                m_State = FamiliarState.Orbit;
                m_StateTimer = 0f;
                return;
            }

            Vector3 direction = delta / Mathf.Max(0.0001f, distance);
            float step = m_Definition.ReturnSpeed * deltaTime;
            m_Position += direction * Mathf.Min(step, distance);
            m_Facing = direction;
        }

        // ── 状态切换 ──────────────────────────────────────────

        /// <summary>开启一个新的穿插单程（首程与掉头后的每一程都走这里）。</summary>
        private void StartDashSegment()
        {
            m_State = FamiliarState.Dash;
            m_StateTimer = 0f;
            m_DashDistance = 0f;
            m_PassDistance = 0f;
            m_HasReachedTarget = false;
        }

        /// <summary>结束本单程、进入掉头。</summary>
        private void EnterTurn()
        {
            m_PassCount++;
            m_State = FamiliarState.Turn;
            m_StateTimer = 0f;
        }

        /// <summary>
        /// 一轮穿插交战结束：解除锁定、清空命中冷却，回到环绕轨道。
        /// 此后若仍有有效候选，控制器会在下一帧为它重新解析目标并起飞。
        /// </summary>
        private void FinishEngagement()
        {
            m_LockedTarget = null;
            m_LockedTargetId = GameplayEntityId.None;
            m_HitReadyAt.Clear();
            m_PassCount = 0;
            m_State = FamiliarState.Return;
            m_StateTimer = 0f;
        }

        /// <summary>玩家是否命中了<b>另一只</b>敌人（当前锁定目标之外的新候选）。</summary>
        private bool HasDifferentPending()
        {
            if (!HasValidPending) return false;
            return m_PendingTarget.Value != m_LockedTargetId.Value;
        }

        // ── 工具 ──────────────────────────────────────────────

        /// <summary>
        /// 轨道坐标：角度 / 半径 / 高度由统一队列给出（<paramref name="angle"/> 已含队列整体相位），
        /// 上下浮动（bob）仍是每只使魔自己的相位，保留一点生动感。
        /// </summary>
        private Vector3 ComputeOrbitPosition(float angle, float radius, float height)
        {
            float radians = angle * Mathf.Deg2Rad;
            float bob = m_Definition.BobAmplitude > 0f
                ? Mathf.Sin(m_BobPhase * Mathf.PI * 2f) * m_Definition.BobAmplitude
                : 0f;
            return new Vector3(
                m_OwnerPosition.x + Mathf.Cos(radians) * radius,
                m_OwnerPosition.y + height + bob,
                m_OwnerPosition.z + Mathf.Sin(radians) * radius);
        }

        private void FaceAlong(Vector3 delta)
        {
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.0001f) m_Facing = delta.normalized;
        }
    }
}
