using System.Collections.Generic;
using UnityEngine;

namespace VampireHunt.Player.Abilities.Familiar
{
    /// <summary>
    /// 使魔环绕队列成员接口（orbit member interface）。<br/>
    /// 凡是「待机时围绕玩家转圈」的使魔都实现它，就能被 <see cref="FamiliarOrbitRegistry"/> 收编进
    /// <b>同一条</b>环绕队列 —— 撞击水滴(161)、射击僚机(162)，以及将来新增的无人机 / 魔符，都走这一套。
    /// </summary>
    /// <remarks>
    /// 实现类通常是纯逻辑的状态机（brain），例如 <c>ImpactFamiliarBrain</c> / <c>GunnerFamiliarBrain</c>。
    /// 它们只负责<b>上报自己期望的参数</b>，最终用哪个值由队列统一裁决。
    /// </remarks>
    public interface IFamiliarOrbitMember
    {
        /// <summary>
        /// 队列归属者（owner）：<b>同一个 owner 下的所有使魔，不论种类，都排进同一条队列</b>。<br/>
        /// 传玩家根物体（<c>transform.root</c>）。联机时每个玩家各有一条队列，互不干扰。
        /// </summary>
        Transform OrbitOwner { get; }

        /// <summary>本类型使魔<b>期望</b>的环绕半径（米）。实际半径由队列按 <see cref="FamiliarOrbitRegistry.RadiusMode"/> 统一取值。</summary>
        float DesiredOrbitRadius { get; }

        /// <summary>本类型使魔期望的环绕高度（米，相对玩家脚下）。</summary>
        float DesiredOrbitHeight { get; }

        /// <summary>本类型使魔期望的环绕角速度（度/秒，正负 = 旋转方向）。队列统一取<b>绝对值最大</b>的那个，保证队形不会被转散。</summary>
        float DesiredOrbitSpeed { get; }

        /// <summary>
        /// 使魔种类标识（kind id）：<b>只用于决定队列里的穿插顺序</b>，不代表强弱、不影响任何战斗数值。<br/>
        /// 同一种使魔返回同一个值，不同种返回不同的值（如 撞击水滴=1、射击僚机=2）。<br/>
        /// 队列会按它把不同种类<b>交错排列</b>（1·2·1·2），避免同种扎堆在相邻角度上。
        /// </summary>
        /// <remarks>将来新增第三种使魔时，给它一个没被占用的值即可，排队逻辑无需改动。</remarks>
        int OrbitKind { get; }
    }

    /// <summary>
    /// 一次环绕队列查询的结果（orbit slot snapshot）：使魔该站在圆周上的哪个角度、半径多少、多高。
    /// </summary>
    public readonly struct FamiliarOrbitSlot
    {
        /// <summary>全局槽位序号（0 起）。<b>-1 = 当前不在队列里</b>（正在交战，占位已释放）。</summary>
        public readonly int Index;
        /// <summary>队列当前总人数（只统计待机中的使魔，跨种类合计）。</summary>
        public readonly int Count;
        /// <summary>队列统一后的环绕半径（米）。</summary>
        public readonly float Radius;
        /// <summary>队列统一后的环绕高度（米）。</summary>
        public readonly float Height;
        /// <summary>最终站位角（度）= 队列整体相位 + 本槽位角（已做平滑，重排时不会瞬移）。</summary>
        public readonly float Angle;

        /// <summary>是否真的排在队列里（false = 交战中，返回值只是兜底快照）。</summary>
        public bool IsQueued => Index >= 0;

        public FamiliarOrbitSlot(int index, int count, float radius, float height, float angle)
        {
            Index = index;
            Count = count;
            Radius = radius;
            Height = height;
            Angle = angle;
        }

        public override string ToString() =>
            $"slot #{Index}/{(Count > 0 ? Count.ToString() : "-")} angle={Angle:F1}° r={Radius:F2} h={Height:F2}";
    }

    /// <summary>
    /// 使魔环绕队列登记表（familiar orbit registry）：<b>把一个玩家名下所有种类的使魔排成一条队列</b>。
    /// </summary>
    /// <remarks>
    /// <b>解决什么问题</b>：以前每种使魔各自按「本类型内的序号 / 本类型总数」分配角度
    /// （<c>360° × index / count</c>），于是 2 只水滴是 0°/180°、2 只射手也是 0°/180°，
    /// 两种使魔会<b>整叠在相同角度上</b>。现在改为跨类型统一编号：4 只使魔就是 0°/90°/180°/270°。
    /// </remarks>
    /// <remarks>
    /// <b>三条规则</b>：
    /// 1. **统一编号 + 跨种类穿插** —— 给待机中的使魔分配 0..N-1 号槽位，角度 = <c>360° / N × 槽位号</c>；
    ///    槽位先后<b>按种类交错</b>（水滴·射手·水滴·射手），同种尽量不相邻。
    /// 2. **统一参数** —— 半径 / 高度按聚合规则取一个公共值；角速度统一取绝对值最大的那个
    ///    （方向随之统一，否则各转各的队形会散）。只有一种使魔时，取到的就是它自己的值，行为不变。
    /// 3. **交战让位** —— 起飞打人的使魔<b>释放占位</b>，剩下的重新均匀分布（队列不留空洞）；
    ///    归队时重新拿一个槽位，并沿最短弧<b>平滑滑过去</b>（<c>SlotSettleLerp</c>），不会瞬移。
    /// </remarks>
    /// <remarks>
    /// <b>网络</b>：纯表现层的本地计算，按 owner 分组。服务器与客户端各自维护，不需要同步。
    /// </remarks>
    public static class FamiliarOrbitRegistry
    {
        /// <summary>多类型参数不一致时的取值规则（merge rule）。</summary>
        public enum AggregateMode
        {
            /// <summary>取最小（贴身紧凑）。</summary>
            Min = 0,
            /// <summary>取平均（折中，最不容易突兀）。</summary>
            Average = 1,
            /// <summary>取最大（宽松，不容易穿模）。</summary>
            Max = 2
        }

        /// <summary>环绕半径的聚合规则（默认取最大：保证队列在外圈，不会挤到玩家身上）。</summary>
        public static AggregateMode RadiusMode = AggregateMode.Max;
        /// <summary>环绕高度的聚合规则（默认取平均：不同体型的使魔都能接受的高度）。</summary>
        public static AggregateMode HeightMode = AggregateMode.Average;

        private const float MinRadius = 0.2f;
        private const float MinHeight = 0.1f;
        /// <summary>槽位重排时的平滑系数（越大越快归位到新槽位）。</summary>
        private const float SlotSettleLerp = 5f;
        private const int CleanupIntervalFrames = 120;

        /// <summary>队列中的一名成员（member entry）。用<b>弱引用</b>持有 brain：</summary>
        private sealed class Entry
        {
            private readonly System.WeakReference<IFamiliarOrbitMember> m_Member;
            /// <summary>注册序号（registration order）：槽位按它升序分配，先来的位置最稳定。</summary>
            public readonly long Order;
            /// <summary>是否在队列中占位（待机 = true，交战 = false）。</summary>
            public bool Orbiting = true;
            /// <summary>分配到的槽位号（-1 = 未占位）。</summary>
            public int SlotIndex = -1;
            /// <summary>当前实际槽位角（平滑值，用于避免重排瞬移）。</summary>
            public float SlotAngle;
            /// <summary>是否已初始化过槽位角（首次直接落位，不做插值）。</summary>
            public bool SlotInitialized;

            public Entry(IFamiliarOrbitMember member, long order)
            {
                m_Member = new System.WeakReference<IFamiliarOrbitMember>(member);
                Order = order;
            }

            /// <summary>取出成员；brain 已被 GC（控制器重建后没走 Release 的兜底）时返回 null。</summary>
            public IFamiliarOrbitMember GetMember() =>
                m_Member != null && m_Member.TryGetTarget(out IFamiliarOrbitMember member) ? member : null;
        }

        /// <summary>一个 owner（玩家）名下的整条队列（orbit ring）。</summary>
        private sealed class Ring
        {
            public readonly List<Entry> Entries = new List<Entry>();
            /// <summary>队列整体相位（度）：所有人共用，保证相对角度固定 = 队形不散。</summary>
            public float Phase;
            /// <summary>本帧是否已推进过相位（避免多只使魔各推一次导致转速翻倍）。</summary>
            public int LastFrame = -1;
            public int ActiveCount;
            public float Radius = MinRadius;
            public float Height = MinHeight;
            public float Speed;
            /// <summary>成员有增删或进出交战 → 需要重新分配槽位。</summary>
            public bool Dirty = true;
        }

        private static readonly Dictionary<Transform, Ring> s_Rings = new Dictionary<Transform, Ring>();
        private static long s_Order;
        private static int s_LastCleanupFrame = -1;

        // ── 注册 / 注销 ────────────────────────────────────────

        /// <summary>把一只使魔登记进它 owner 的队列（通常在 brain 构造时调用）。</summary>
        public static void Register(IFamiliarOrbitMember member)
        {
            if (member == null) return;

            Ring ring = GetOrCreateRing(member.OrbitOwner);
            // 重复注册保护：同一个 brain 只占一个位置。
            for (int i = 0; i < ring.Entries.Count; i++)
            {
                if (ring.Entries[i].GetMember() == member) return;
            }
            ring.Entries.Add(new Entry(member, s_Order++));
            ring.Dirty = true;
        }

        /// <summary>把一只使魔从队列里移除（brain 被重建、使魔被关闭、控制器销毁时调用）。</summary>
        public static void Release(IFamiliarOrbitMember member)
        {
            if (member == null) return;
            if (!s_Rings.TryGetValue(member.OrbitOwner, out Ring ring)) return;

            for (int i = ring.Entries.Count - 1; i >= 0; i--)
            {
                if (ring.Entries[i].GetMember() != member) continue;
                ring.Entries.RemoveAt(i);
                ring.Dirty = true;
            }
            if (ring.Entries.Count == 0) s_Rings.Remove(member.OrbitOwner);
        }

        /// <summary>
        /// 标记一只使魔是否在队列里占位：<b>起飞交战时传 false</b>（把位置让给还在待机的同伴，
        /// 队列不留空洞）；归队待机时传 true（重新拿一个槽位并平滑滑过去）。
        /// </summary>
        public static void SetOrbiting(IFamiliarOrbitMember member, bool orbiting)
        {
            if (member == null) return;
            if (!s_Rings.TryGetValue(member.OrbitOwner, out Ring ring)) return;

            for (int i = 0; i < ring.Entries.Count; i++)
            {
                Entry entry = ring.Entries[i];
                if (entry.GetMember() != member) continue;
                if (entry.Orbiting == orbiting) return;
                entry.Orbiting = orbiting;
                ring.Dirty = true;
                return;
            }
        }

        // ── 每帧查询 ──────────────────────────────────────────

        /// <summary>
        /// 查询一只使魔这一帧该站的槽位（slot）。由 brain 在待机 / 返回状态每帧调用。
        /// </summary>
        /// <param name="member">查询者。</param>
        /// <param name="deltaTime">帧间隔（秒）。<b>同帧只有第一次调用会推进队列相位</b>，传 0 表示只读不推进（瞬移落位时用）。</param>
        public static FamiliarOrbitSlot GetSlot(IFamiliarOrbitMember member, float deltaTime)
        {
            if (member == null) return new FamiliarOrbitSlot(-1, 0, MinRadius, MinHeight, 0f);

            Ring ring = GetOrCreateRing(member.OrbitOwner);
            if (ring.Dirty) Rebuild(ring);

            // 每帧只推进一次：谁先查谁触发，保证同一帧里所有使魔看到的是同一个相位。
            if (deltaTime > 0f && ring.LastFrame != Time.frameCount)
            {
                ring.LastFrame = Time.frameCount;
                Advance(ring, deltaTime);
            }

            Entry entry = FindEntry(ring, member);
            // 没注册（或槽位刚被释放）：给一个不占位的兜底角度，下一帧就会归位。
            if (entry == null)
                return new FamiliarOrbitSlot(-1, ring.ActiveCount, ring.Radius, ring.Height, ring.Phase);

            return new FamiliarOrbitSlot(entry.SlotIndex, ring.ActiveCount, ring.Radius, ring.Height,
                ring.Phase + entry.SlotAngle);
        }

        /// <summary>清空某个 owner 的队列（玩家退出对局、被销毁时调用，防止静态表残留）。</summary>
        public static void Clear(Transform owner)
        {
            if (owner == null) return;
            s_Rings.Remove(owner);
        }

        /// <summary>清空全部队列（切换场景 / 退出对局时调用）。</summary>
        public static void ClearAll() => s_Rings.Clear();

        /// <summary>调试用：某个 owner 当前队列里有多少只使魔在待机。</summary>
        public static int GetActiveCount(Transform owner) =>
            owner != null && s_Rings.TryGetValue(owner, out Ring ring) ? ring.ActiveCount : 0;

        // ── 内部实现 ──────────────────────────────────────────

        private static void Advance(Ring ring, float deltaTime)
        {
            ring.Phase += ring.Speed * deltaTime;
            if (ring.Phase > 360f || ring.Phase < -360f) ring.Phase = Mathf.Repeat(ring.Phase, 360f);
            if (ring.ActiveCount <= 0) return;

            // 槽位角平滑靠拢目标：用 LerpAngle 走最短弧，重排时不会绕大半圈。
            float step = 360f / ring.ActiveCount;
            float t = 1f - Mathf.Exp(-SlotSettleLerp * deltaTime);
            for (int i = 0; i < ring.Entries.Count; i++)
            {
                Entry entry = ring.Entries[i];
                if (entry.SlotIndex < 0) continue;
                entry.SlotAngle = Mathf.LerpAngle(entry.SlotAngle, step * entry.SlotIndex, t);
            }
        }

        /// <summary>重新分配槽位并重算统一参数（成员增删、进出交战时触发）。</summary>
        private static void Rebuild(Ring ring)
        {
            ring.Dirty = false;

            // ① 清掉已被 GC 的使魔（控制器重建 brain 却忘了 Release 时的兜底，靠弱引用兜住）。
            for (int i = ring.Entries.Count - 1; i >= 0; i--)
            {
                if (ring.Entries[i].GetMember() == null) ring.Entries.RemoveAt(i);
            }

            // ② 统计入列人数与公共参数（只算待机中的）。
            int active = 0;
            float radiusMin = float.MaxValue, radiusMax = MinRadius, radiusSum = 0f;
            float heightMin = float.MaxValue, heightMax = MinHeight, heightSum = 0f;
            float speed = 0f;

            for (int i = 0; i < ring.Entries.Count; i++)
            {
                IFamiliarOrbitMember member = ring.Entries[i].GetMember();
                if (member == null || !ring.Entries[i].Orbiting) continue;

                active++;
                float radius = Mathf.Max(MinRadius, member.DesiredOrbitRadius);
                float height = Mathf.Max(MinHeight, member.DesiredOrbitHeight);
                radiusSum += radius;
                heightSum += height;
                if (radius < radiusMin) radiusMin = radius;
                if (radius > radiusMax) radiusMax = radius;
                if (height < heightMin) heightMin = height;
                if (height > heightMax) heightMax = height;
                // 角速度取绝对值最大的那个：方向随之统一，队形才不会被转散。
                if (Mathf.Abs(member.DesiredOrbitSpeed) > Mathf.Abs(speed)) speed = member.DesiredOrbitSpeed;
            }

            ring.ActiveCount = active;

            if (active <= 0)
            {
                // 全员都在交战：参数沿用上一次（避免归队时队列半径/高度跳变），槽位全部释放。
                for (int i = 0; i < ring.Entries.Count; i++) ring.Entries[i].SlotIndex = -1;
                return;
            }

            ring.Radius = Merge(RadiusMode, radiusMin, radiusMax, radiusSum / active, MinRadius);
            ring.Height = Merge(HeightMode, heightMin, heightMax, heightSum / active, MinHeight);
            ring.Speed = speed;

            // ③ 交错分配槽位：不同种类穿插排列（1·2·1·2），同种尽量不相邻。
            InterleaveSlots(ring);
        }

        /// <summary>交错排序用的临时分桶（静态复用，避免每次重排都 new）。</summary>
        private static readonly List<int> s_Kinds = new List<int>();
        /// <summary>每个种类一桶；桶内保持注册顺序，保证同类之间的相对次序稳定。</summary>
        private static readonly List<List<Entry>> s_Buckets = new List<List<Entry>>();
        /// <summary>每个桶已取到第几只（游标）。</summary>
        private static readonly List<int> s_BucketCursor = new List<int>();

        /// <summary>
        /// <b>交错分配槽位</b>（interleave slots）：让不同种类的使魔在圆周上穿插排列，
        /// 避免出现「两只水滴挨在一起、两只射手挨在一起」的同种扎堆。
        /// </summary>
        /// <remarks>
        /// <b>算法</b>（贪心，O(种类数 × 总数)，数量级很小）：
        /// <list type="number">
        /// <item>按 <see cref="IFamiliarOrbitMember.OrbitKind"/> 把待机中的使魔分桶，桶内保持注册顺序。</item>
        /// <item>每轮从「<b>剩余最多</b>且<b>与上一位不同种类</b>」的桶里取一只，依次填 0..N-1 号槽位。</item>
        /// <item>若只剩与上一位同种的（数量悬殊到无法完全错开），兜底取任意还有剩余的桶 —— 此时必然出现同种相邻。</item>
        /// </list>
        /// 例：2 水滴 + 2 射手 → 水·射·水·射；2+2+2 → 1·2·3·1·2·3；3 水滴 + 1 射手 → 水·射·水·水
        /// （后者尾部必然相邻同种：4 个位子放 3 个同种，环形排列至少要有 2 处同种相邻，数学上无解）。
        /// </remarks>
        private static void InterleaveSlots(Ring ring)
        {
            s_Kinds.Clear();
            s_BucketCursor.Clear();
            for (int i = 0; i < s_Buckets.Count; i++) s_Buckets[i].Clear();

            // ① 分桶：同种类进同一个桶；未入列（交战中 / 已被 GC）的直接释放占位。
            for (int i = 0; i < ring.Entries.Count; i++)
            {
                Entry entry = ring.Entries[i];
                IFamiliarOrbitMember member = entry.GetMember();
                if (member == null || !entry.Orbiting)
                {
                    entry.SlotIndex = -1;
                    continue;
                }

                int kind = member.OrbitKind;
                int bucket = s_Kinds.IndexOf(kind);
                if (bucket < 0)
                {
                    bucket = s_Kinds.Count;
                    s_Kinds.Add(kind);
                    s_BucketCursor.Add(0);
                    if (bucket >= s_Buckets.Count) s_Buckets.Add(new List<Entry>());
                }
                s_Buckets[bucket].Add(entry);
            }

            // ② 贪心交错取桶。
            float step = 360f / ring.ActiveCount;
            int index = 0;
            int lastKind = int.MinValue;

            while (index < ring.ActiveCount)
            {
                int pick = -1;
                int pickRemaining = 0;
                for (int b = 0; b < s_Kinds.Count; b++)
                {
                    int remaining = s_Buckets[b].Count - s_BucketCursor[b];
                    if (remaining <= 0) continue;
                    if (s_Kinds[b] == lastKind) continue;   // 核心：跳过与上一位同种的桶

                    // 取剩余最多的；并列时取桶序靠前的（= 更早出现的种类），保证结果稳定可复现。
                    if (pick < 0 || remaining > pickRemaining)
                    {
                        pick = b;
                        pickRemaining = remaining;
                    }
                }

                // 兜底：只剩与上一位同种的了（数量悬殊，无法完全错开）。
                if (pick < 0)
                {
                    for (int b = 0; b < s_Kinds.Count; b++)
                    {
                        if (s_Buckets[b].Count - s_BucketCursor[b] > 0) { pick = b; break; }
                    }
                }
                if (pick < 0) break;

                Entry chosen = s_Buckets[pick][s_BucketCursor[pick]];
                s_BucketCursor[pick]++;
                lastKind = s_Kinds[pick];

                chosen.SlotIndex = index;
                // 首次入列直接落位，不做插值（否则新生成的使魔会从 0° 慢慢滑过去）。
                if (!chosen.SlotInitialized)
                {
                    chosen.SlotAngle = step * index;
                    chosen.SlotInitialized = true;
                }
                index++;
            }
        }

        private static float Merge(AggregateMode mode, float min, float max, float average, float fallback)
        {
            if (float.IsNaN(min) || float.IsNaN(max)) return fallback;
            switch (mode)
            {
                case AggregateMode.Min: return min;
                case AggregateMode.Max: return max;
                default: return average;
            }
        }

        private static Entry FindEntry(Ring ring, IFamiliarOrbitMember member)
        {
            for (int i = 0; i < ring.Entries.Count; i++)
            {
                if (ring.Entries[i].GetMember() == member) return ring.Entries[i];
            }
            return null;
        }

        private static Ring GetOrCreateRing(Transform owner)
        {
            if (owner != null && s_Rings.TryGetValue(owner, out Ring existing)) return existing;

            // owner 为 null（没传根物体）：全部塞进同一个无名队列，保证不会崩，只是不同玩家会混编。
            var ring = new Ring();
            s_Rings[owner] = ring;
            CleanupDestroyedOwners();
            return ring;
        }

        /// <summary>定期清掉 owner 已被销毁（玩家退出、场景切换）的队列，避免静态字典泄漏。</summary>
        private static void CleanupDestroyedOwners()
        {
            int frame = Time.frameCount;
            if (s_LastCleanupFrame >= 0 && frame - s_LastCleanupFrame < CleanupIntervalFrames) return;
            s_LastCleanupFrame = frame;
            if (s_Rings.Count == 0) return;

            var dead = ListPool<Transform>.Get();
            foreach (KeyValuePair<Transform, Ring> pair in s_Rings)
            {
                // Transform 是 UnityEngine.Object：被销毁后 == null 成立。
                if (pair.Key == null) dead.Add(pair.Key);
            }
            for (int i = 0; i < dead.Count; i++) s_Rings.Remove(dead[i]);
        }

        /// <summary>极简对象池：避免每次清理都 new 一个 List。</summary>
        private static class ListPool<T>
        {
            private static readonly List<T> s_Buffer = new List<T>();
            public static List<T> Get()
            {
                s_Buffer.Clear();
                return s_Buffer;
            }
        }
    }
}
