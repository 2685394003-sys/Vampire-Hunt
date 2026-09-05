using System;

namespace VampireHunt.Contracts
{
    /// <summary>圆形领域的只读表现状态；不包含伤害、冷却或命中结果。</summary>
    public readonly struct CircleFieldAuraPresentationState
    {
        public bool IsActive { get; }
        public float Radius { get; }

        public CircleFieldAuraPresentationState(bool isActive, float radius)
        {
            IsActive = isActive;
            Radius = radius > 0f ? radius : 0f;
        }
    }

    /// <summary>玩法侧发布领域表现状态的端口。</summary>
    public interface ICircleFieldAuraStatePublisher
    {
        bool TryPublish(in CircleFieldAuraPresentationState state);
    }

    /// <summary>Presentation 层只读的领域状态模型，支持网络对象中途加入时恢复。</summary>
    public interface ICircleFieldAuraReadModel
    {
        CircleFieldAuraPresentationState Current { get; }
        event Action<CircleFieldAuraPresentationState> Changed;
    }
}
