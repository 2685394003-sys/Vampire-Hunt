using UnityEngine;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Player.Abilities.Familiar
{
    /// <summary>
    /// 单只射击僚机使魔的状态机（familiar state machine）：<br/>
    /// <b>Orbit</b>（环绕玩家）→ <b>Approach</b>（飞到距目标 <c>Weapon.Range</c> 的站位）
    /// → <b>Aim</b>（瞄准前摇）→ <b>Fire</b>（连发，每发向外抛一次开火请求）
    /// → <b>Recharge</b>（射击冷却）→ 回到 <b>Approach</b> 打下一套 ……
    /// 直到退出条件满足才 <b>Return</b>。<br/>
    /// 纯逻辑类（不继承 MonoBehaviour），由 <c>GunnerFamiliarController</c> 每帧驱动并结算伤害。
    /// </summary>
    /// <remarks>
    /// 五条硬性规则在此落地：
    /// 1. **不主动索敌** —— 只有控制器把「玩家命中过的敌人」登记进来（<see cref="SetPendingTarget"/>）才会起飞。
    /// 2. **射击流程不可打断** —— <c>Approach / Aim / Fire / Recharge</c> 全程算交战中（<see cref="IsEngaged"/>），
    ///    期间玩家命中新敌人只会写进候选缓存，<b>不影响当前这一套</b>。
    /// 3. **打完一套才用最新候选** —— 唯一的目标切换时机是 <c>Recharge</c> 结束的<b>决策点</b>
    ///    （<see cref="DecideAfterVolley"/>），此时才读最新的 PendingTarget。
    /// 4. **目标死亡立刻收手** —— 任何状态下 <c>LockedTarget == null</c> 都立即结束交战（这不算打断，是目标没了）。
    /// 5. **多只自然散开** —— 每只使魔按实例序号占据目标周围的不同方位角（<c>m_SlotAngle</c>），
    ///    并在交战中绕目标缓慢侧移（strafe），形成包围阵型而不是挤在同一点。
    /// 每只使魔实例各自持有一份候选缓存与状态，互不干扰。
    /// </remarks>
    /// <remarks>
    /// <b>待机队列</b>：实现 <see cref="IFamiliarOrbitMember"/> 并登记进 <see cref="FamiliarOrbitRegistry"/>，
    /// 因此<b>和别的种类（如撞击水滴 161）共用同一条环绕队列</b>，不会各自从 0° 开始重叠。
    /// 起飞交战时会自动让出槽位，归队时平滑滑回队列。<br/>
    /// ⚠️ 注意区分两个「槽位角」：<c>m_SlotAngle</c> 是<b>交战时绕敌人</b>的站位角（本类型内分配），
    /// 待机时绕玩家的槽位角由统一队列下发，两者互不相干。
    /// </remarks>
    public sealed class GunnerFamiliarBrain : IFamiliarOrbitMember
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
            /// <summary>接近：飞向距目标 <c>Weapon.Range</c> 的站位。</summary>
            Approach = 1,
            /// <summary>瞄准：到位后的前摇，锁定朝向，给玩家与视觉一个预告。</summary>
            Aim = 2,
            /// <summary>开火：按 <c>BurstCount</c> 连发，每发间隔 <c>BurstInterval</c>。</summary>
            Fire = 3,
            /// <summary>射击冷却：一套打完后的间隙，结束时是唯一的换目标决策点。</summary>
            Recharge = 4,
            /// <summary>返回：飞回环绕轨道（交战结束）。</summary>
            Return = 5
        }

        private readonly GunnerFamiliarDefinition m_Definition;
        private readonly int m_Index;
        private readonly int m_Count;
        /// <summary>队列归属者（玩家根物体）：同一 owner 下所有种类的使魔排进同一条队列。</summary>
        private readonly Transform m_Owner;

        private FamiliarState m_State = FamiliarState.Orbit;
        /// <summary>当前是否占用环绕队列的槽位（待机 / 返回 = 占用，交战 = 让出）。</summary>
        private bool m_OrbitQueued = true;
        /// <summary>交战时绕<b>敌人</b>的站位角（本类型内分配，与待机队列无关）。</summary>
        private float m_SlotAngle;
        private float m_StrafeAngle;
        private float m_StateTimer;
        private float m_Elapsed;
        private float m_BobPhase;
        private int m_VolleyCount;
        private int m_BurstRemaining;
        private float m_BurstTimer;
        private bool m_ShotPending;

        private Vector3 m_Position;
        private Vector3 m_Facing = Vector3.forward;
        private Vector3 m_OwnerPosition;

        // ── 本只使魔独立的候选目标缓存 ──
        private GameplayEntityId m_PendingTarget;
        private float m_PendingExpireAt;
        /// <summary>当前候选的优先级（<see cref="PriorityPassive"/> / <see cref="PriorityCommand"/>）。</summary>
        private int m_PendingPriority;

        // ── 当前交战锁定的目标 ──
        private MonoBehaviour m_LockedTarget;
        private GameplayEntityId m_LockedTargetId;

        /// <summary>
        /// 构造一只射击僚机的状态机。
        /// </summary>
        /// <param name="definition">配置数据。</param>
        /// <param name="index">本只使魔在<b>本类型内</b>的序号（用于交战时绕敌人散开）。</param>
        /// <param name="count">本类型使魔总数。</param>
        /// <param name="owner">队列归属者（玩家根物体）。留空则进无名队列（不推荐：不同玩家会混编）。</param>
        public GunnerFamiliarBrain(in GunnerFamiliarDefinition definition, int index, int count, Transform owner)
        {
            m_Definition = definition;
            m_Index = Mathf.Max(0, index);
            m_Count = Mathf.Max(1, count);
            m_Owner = owner;
            // 交战时均匀分布在目标四周：每只占据一个固定方位角（本类型内分配）。
            m_SlotAngle = 360f * m_Index / m_Count;
            // 登记进统一环绕队列：待机角度由队列统一分配，不再各自算 360°×index/count。
            FamiliarOrbitRegistry.Register(this);
        }

        // ── 对外只读状态 ──────────────────────────────────────

        /// <summary>当前状态。</summary>
        public FamiliarState State => m_State;
        /// <summary>当前世界坐标（由状态机每帧推进）。</summary>
        public Vector3 Position => m_Position;
        /// <summary>当前朝向：瞄准及开火时持续指向目标。</summary>
        public Vector3 Facing => m_Facing;
        /// <summary>
        /// 交战进行中（接近 / 瞄准 / 开火 / 冷却）：这四个状态<b>全程不可打断</b>。
        /// </summary>
        public bool IsEngaged =>
            m_State == FamiliarState.Approach || m_State == FamiliarState.Aim ||
            m_State == FamiliarState.Fire || m_State == FamiliarState.Recharge;
        /// <summary>本帧是否应该结算一次伤害（由控制器查询）。</summary>
        public bool HasPendingShot => m_ShotPending;
        /// <summary>正在交战的目标（未锁定时为 null）。</summary>
        public MonoBehaviour LockedTarget => m_LockedTarget;
        /// <summary>当前候选目标 id（未登记时为 <see cref="GameplayEntityId.None"/>）。</summary>
        public GameplayEntityId PendingTargetId => m_PendingTarget;
        /// <summary>候选目标是否仍在有效期内。</summary>
        public bool HasValidPending => !m_PendingTarget.IsNone && m_Elapsed <= m_PendingExpireAt;
        /// <summary>当前候选的优先级（调试用：0 = 被动命中，1 = 左键指令）。</summary>
        public int PendingPriority => m_PendingPriority;
        /// <summary>空闲且手上有有效候选 → 控制器应立刻为它解析目标并起飞。</summary>
        public bool WantsEngage => !IsEngaged && HasValidPending;
        /// <summary>本轮交战已打出的套数（调试用）。</summary>
        public int VolleyCount => m_VolleyCount;
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

        /// <summary>队列穿插用的种类标识：<b>射击僚机 = 2</b>（撞击水滴 = 1，将来新增种类继续往下取值）。</summary>
        public int OrbitKind => 2;

        /// <summary>
        /// 退出统一队列并将槽位让给同伴（brain 被重建、使魔被关闭、控制器销毁时必须调用，
        /// 否则队列里会留下一个永远没人站的空位）。
        /// </summary>
        public void ReleaseOrbitSlot() => FamiliarOrbitRegistry.Release(this);

        // ── 候选目标登记 ──────────────────────────────────────

        /// <summary>
        /// 登记候选目标（玩家攻击命中敌人时由控制器调用）。新命中的敌人会<b>覆盖</b>旧候选。
        /// 正在射击流程中的使魔只写缓存、绝不打断当前这一套。
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

        /// <summary>开始一轮交战（由控制器在解析出目标对象后调用）。会重置套数与连发计数。</summary>
        public void BeginEngagement(MonoBehaviour target, GameplayEntityId targetId)
        {
            if (target == null) return;

            m_LockedTarget = target;
            m_LockedTargetId = targetId;
            m_VolleyCount = 0;
            m_StrafeAngle = 0f;
            StartApproach();
        }

        /// <summary>强制结束本次交战并回到轨道（关闭使魔时用）。</summary>
        public void AbortEngagement()
        {
            m_LockedTarget = null;
            m_LockedTargetId = GameplayEntityId.None;
            m_ShotPending = false;
            m_VolleyCount = 0;
            m_BurstRemaining = 0;
            m_State = FamiliarState.Return;
            m_StateTimer = 0f;
        }

        /// <summary>
        /// 取走一次开火请求（controller 每帧在 <c>Tick</c> 之后调用）。
        /// 返回 true 表示本帧要打出一发，<c>origin</c> / <c>direction</c> 即枪口位置与朝向。
        /// </summary>
        public bool TryConsumeShot(out Vector3 origin, out Vector3 direction)
        {
            origin = m_Position + m_Facing * m_Definition.Weapon.muzzleForwardOffset;
            direction = m_Facing;
            if (!m_ShotPending) return false;
            m_ShotPending = false;
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
                case FamiliarState.Approach: TickApproach(deltaTime); break;
                case FamiliarState.Aim:      TickAim(deltaTime);      break;
                case FamiliarState.Fire:     TickFire(deltaTime);     break;
                case FamiliarState.Recharge: TickRecharge(deltaTime); break;
                case FamiliarState.Return:   TickReturn(deltaTime);   break;
                default:                     TickOrbit(deltaTime);    break;
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
        /// （<see cref="FamiliarState.Return"/>）占槽位，起飞交战（接近 / 瞄准 / 开火 / 冷却）就把位置让出来，
        /// 让还在待机的同伴重新均匀分布（队列不会留下空洞）。
        /// </summary>
        private void UpdateOrbitMembership()
        {
            bool wanted = m_State == FamiliarState.Orbit || m_State == FamiliarState.Return;
            if (wanted == m_OrbitQueued) return;
            m_OrbitQueued = wanted;
            FamiliarOrbitRegistry.SetOrbiting(this, wanted);
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

        /// <summary>飞向站位：距目标 <c>Weapon.Range</c>、按本实例 slot 角散开的一个点。</summary>
        private void TickApproach(float deltaTime)
        {
            m_StateTimer += deltaTime;
            if (m_LockedTarget == null) { FinishEngagement(); return; }

            HoldStation(deltaTime);

            // 水平距离进入容差带即视为到位（不比高度：高个子 Boss 的 transform.position
            // 与使魔飞行高度差很多，用三维距离会导致永远判不到「到位」）。
            float distance = HorizontalDistance(m_LockedTarget.transform.position, m_Position);
            float range = Mathf.Max(0.5f, m_Definition.Weapon.range);
            if (Mathf.Abs(distance - range) <= m_Definition.RangeTolerance)
            {
                m_State = FamiliarState.Aim;
                m_StateTimer = 0f;
                return;
            }

            // 追不上（目标跑远 / 被地形卡住）→ 兜底：到时间也直接开打，避免干追不打。
            if (m_StateTimer >= m_Definition.MaxApproachDuration) EnterAim();
        }

        /// <summary>瞄准前摇：维持站位、把朝向对准目标，走完进入开火。</summary>
        private void TickAim(float deltaTime)
        {
            m_StateTimer += deltaTime;
            if (m_LockedTarget == null) { FinishEngagement(); return; }

            HoldStation(deltaTime);
            AimAt(m_LockedTarget.transform.position, deltaTime);

            if (m_StateTimer >= m_Definition.AimDuration) StartVolley();
        }

        /// <summary>开火：按 BurstCount 连发，间隔 BurstInterval；打完一发就抛一次开火请求。</summary>
        private void TickFire(float deltaTime)
        {
            m_StateTimer += deltaTime;
            if (m_LockedTarget == null) { FinishEngagement(); return; }

            HoldStation(deltaTime);
            AimAt(m_LockedTarget.transform.position, deltaTime);

            m_BurstTimer -= deltaTime;
            if (m_BurstTimer > 0f) return;

            // 打出一发：写入请求，控制器本帧稍后会取走并结算。
            m_ShotPending = true;
            m_BurstRemaining--;
            m_BurstTimer = Mathf.Max(0f, m_Definition.Weapon.burstInterval);

            if (m_BurstRemaining <= 0)
            {
                m_VolleyCount++;
                m_State = FamiliarState.Recharge;
                m_StateTimer = 0f;
            }
        }

        /// <summary>射击冷却：维持站位继续瞄准，走完进入决策点。</summary>
        private void TickRecharge(float deltaTime)
        {
            m_StateTimer += deltaTime;
            if (m_LockedTarget == null) { FinishEngagement(); return; }

            HoldStation(deltaTime);
            AimAt(m_LockedTarget.transform.position, deltaTime);

            if (m_StateTimer < Mathf.Max(0f, m_Definition.Weapon.fireCooldown)) return;

            // ── 唯一的目标决策点：一整套打完了，这里才允许读最新候选 ──
            if (DecideAfterVolley()) StartApproach();
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

        private void StartApproach()
        {
            m_State = FamiliarState.Approach;
            m_StateTimer = 0f;
        }

        private void EnterAim()
        {
            m_State = FamiliarState.Aim;
            m_StateTimer = 0f;
        }

        /// <summary>开启一套射击：装填连发数并把首发间隔置 0（立刻打第一发）。</summary>
        private void StartVolley()
        {
            m_State = FamiliarState.Fire;
            m_StateTimer = 0f;
            m_BurstRemaining = Mathf.Max(1, m_Definition.Weapon.burstCount);
            m_BurstTimer = 0f;
        }

        /// <summary>
        /// 冷却结束的决策点：判断这一轮交战是否继续。返回 true = 继续打下一套（重新接近目标）。
        /// 只有在这里才会读取最新的 <see cref="PendingTargetId"/> —— 这就是「打完一套才换目标」。
        /// </summary>
        private bool DecideAfterVolley()
        {
            // ① 玩家改打了别的敌人 → 让位（下一帧控制器会用新候选重新起飞）。
            if (m_Definition.SwitchTargetOnNewCandidate && HasDifferentPending())
            {
                FinishEngagement();
                return false;
            }
            // ② 候选过期（玩家停手）→ 收手返回待机，不会无限追着旧目标打。
            if (m_Definition.StopWhenPendingExpired && !HasValidPending)
            {
                FinishEngagement();
                return false;
            }
            // ③ 单目标套数上限（0 = 不限）。
            if (m_Definition.MaxVolleysPerTarget > 0 && m_VolleyCount >= m_Definition.MaxVolleysPerTarget)
            {
                FinishEngagement();
                return false;
            }
            return true;
        }

        /// <summary>一轮交战结束：解除锁定，回到环绕轨道。</summary>
        private void FinishEngagement()
        {
            m_LockedTarget = null;
            m_LockedTargetId = GameplayEntityId.None;
            m_ShotPending = false;
            m_BurstRemaining = 0;
            m_VolleyCount = 0;
            m_State = FamiliarState.Return;
            m_StateTimer = 0f;
        }

        /// <summary>玩家是否命中了<b>另一只</b>敌人（当前锁定目标之外的新候选）。</summary>
        private bool HasDifferentPending()
        {
            if (!HasValidPending) return false;
            return m_PendingTarget.Value != m_LockedTargetId.Value;
        }

        // ── 运动 ──────────────────────────────────────────────

        /// <summary>
        /// 维持站位：朝「目标周围按 slot 角散开、距目标 Range 米」的点平滑移动，
        /// 同时让这个点绕目标缓慢旋转（strafe），使多只使魔形成包围阵型。
        /// </summary>
        private void HoldStation(float deltaTime)
        {
            m_StrafeAngle += m_Definition.StrafeSpeed * deltaTime;
            if (m_StrafeAngle > 360f) m_StrafeAngle -= 360f;
            else if (m_StrafeAngle < -360f) m_StrafeAngle += 360f;

            Vector3 station = ComputeStationPosition(m_LockedTarget.transform.position);
            float t = 1f - Mathf.Exp(-m_Definition.MoveLerp * deltaTime);
            m_Position = Vector3.Lerp(m_Position, station, t);
        }

        /// <summary>站位 = 目标位置 + 以目标为中心、按 slot 角 + strafe 偏移的方位 × 交战距离。</summary>
        private Vector3 ComputeStationPosition(Vector3 targetPosition)
        {
            float radians = (m_SlotAngle + m_StrafeAngle) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) *
                             Mathf.Max(0.5f, m_Definition.Weapon.range);
            // 高度统一用环绕高度：不跟着 Boss 的身高上下飘，弹道自然斜着打过去。
            return new Vector3(
                targetPosition.x + offset.x,
                m_OwnerPosition.y + m_Definition.OrbitHeight,
                targetPosition.z + offset.z);
        }

        private void AimAt(Vector3 targetPosition, float deltaTime)
        {
            Vector3 delta = targetPosition - m_Position;
            if (delta.sqrMagnitude <= 0.0001f) return;
            float t = 1f - Mathf.Exp(-m_Definition.AimLerp * deltaTime);
            m_Facing = Vector3.Slerp(m_Facing, delta.normalized, t).normalized;
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

        /// <summary>只比 XZ 平面的水平距离（忽略高度差，避免高个子 Boss 判不到位）。</summary>
        private static float HorizontalDistance(Vector3 from, Vector3 to)
        {
            float dx = from.x - to.x;
            float dz = from.z - to.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
