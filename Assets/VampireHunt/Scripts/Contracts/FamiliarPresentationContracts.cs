namespace VampireHunt.Contracts
{
    /// <summary>单只使魔的只读视觉快照。仅描述姿态，不允许表现层反写玩法状态。</summary>
    public readonly struct FamiliarVisualPose
    {
        public int Index { get; }
        public Float3 Position { get; }
        public Float3 Facing { get; }
        public Float3 Scale { get; }

        public FamiliarVisualPose(int index, in Float3 position, in Float3 facing, in Float3 scale)
        {
            Index = index;
            Position = position;
            Facing = facing;
            Scale = scale;
        }
    }

    /// <summary>一次射击使魔开火的表现事件；伤害已由服务器结算，本事件只画枪口、曳光与命中闪光。</summary>
    public readonly struct GunnerFamiliarShotPresentationCue
    {
        public byte WeaponId { get; }
        public Float3 Origin { get; }
        public Float3 End { get; }
        public Float3 Direction { get; }
        public float TracerSpeed { get; }
        public float TracerScale { get; }
        public bool ShowImpactOnArrival { get; }

        public GunnerFamiliarShotPresentationCue(
            byte weaponId,
            in Float3 origin,
            in Float3 end,
            in Float3 direction,
            float tracerSpeed,
            float tracerScale,
            bool showImpactOnArrival)
        {
            WeaponId = weaponId;
            Origin = origin;
            End = end;
            Direction = direction;
            TracerSpeed = tracerSpeed;
            TracerScale = tracerScale;
            ShowImpactOnArrival = showImpactOnArrival;
        }
    }

    public interface IImpactFamiliarPresentationSink
    {
        void Rebuild(int count);
        void Clear();
        void ApplyPose(in FamiliarVisualPose pose);
    }

    public interface IGunnerFamiliarPresentationSink
    {
        void Rebuild(int count);
        void Clear();
        void ApplyPose(in FamiliarVisualPose pose);
        void PlayShot(in GunnerFamiliarShotPresentationCue cue);
    }

    /// <summary>
    /// 撞击使魔玩法侧的表现发布端口。控制器只发布只读结果；由 Netcode 适配器决定
    /// 是本地直送还是复制给所有客户端。
    /// </summary>
    public interface IImpactFamiliarPresentationPublisher
    {
        void SetVisualCount(int count);
        void PublishPose(in FamiliarVisualPose pose);
    }

    /// <summary>射击使魔玩法侧的表现发布端口，瞬时开火事件不参与伤害结算。</summary>
    public interface IGunnerFamiliarPresentationPublisher
    {
        void SetVisualCount(int count);
        void PublishPose(in FamiliarVisualPose pose);
        void PublishShot(in GunnerFamiliarShotPresentationCue cue);
    }
}
