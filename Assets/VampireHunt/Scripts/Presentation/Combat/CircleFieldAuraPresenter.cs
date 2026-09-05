using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>只负责圆型领域的本地视觉，不参与施法、冷却、范围查询或伤害结算。</summary>
    [DisallowMultipleComponent]
    public sealed class CircleFieldAuraPresenter : MonoBehaviour
    {
        [Tooltip("实现 ICircleFieldAuraReadModel 的只读状态源。留空时自动从同物体查找。")]
        [SerializeField] private MonoBehaviour source;
        [Tooltip("领域视觉预制体（prefab，可选）：跟随玩家的常驻圆形表现。")]
        [SerializeField] private GameObject fieldVisualPrefab;
        [Tooltip("领域视觉预制体的基准直径（米）：实际按 直径 / 基准直径 缩放。")]
        [SerializeField, Min(0.01f)] private float visualBaseDiameter = 1f;
        [Tooltip("领域视觉相对玩家脚下的高度（米），避免与地面 z-fighting。")]
        [SerializeField, Min(0f)] private float visualHeightOffset = 0.05f;

        private GameObject m_Visual;
        private ICircleFieldAuraReadModel m_ReadModel;
        private CircleFieldAuraPresentationState m_State;

        private void Awake()
        {
            ResolveReadModel();
        }

        private void OnEnable()
        {
            ResolveReadModel();
            if (m_ReadModel == null) return;
            m_ReadModel.Changed -= HandleStateChanged;
            m_ReadModel.Changed += HandleStateChanged;
            HandleStateChanged(m_ReadModel.Current);
        }

        private void LateUpdate()
        {
            if (m_Visual == null || !m_State.IsActive) return;

            m_Visual.transform.position = transform.position + Vector3.up * visualHeightOffset;
            float scale = m_State.Radius * 2f / Mathf.Max(0.01f, visualBaseDiameter);
            m_Visual.transform.localScale = new Vector3(scale, m_Visual.transform.localScale.y, scale);
        }

        private void OnDisable()
        {
            if (m_ReadModel != null) m_ReadModel.Changed -= HandleStateChanged;
            DestroyVisual();
        }

        private void OnDestroy() => DestroyVisual();

        private void EnsureVisual()
        {
            if (fieldVisualPrefab == null || m_Visual != null) return;
            m_Visual = Instantiate(fieldVisualPrefab, transform.position, Quaternion.identity);
        }

        private void DestroyVisual()
        {
            if (m_Visual == null) return;
            Destroy(m_Visual);
            m_Visual = null;
        }

        private void HandleStateChanged(CircleFieldAuraPresentationState state)
        {
            m_State = state;
            if (!state.IsActive || state.Radius <= 0f)
            {
                DestroyVisual();
                return;
            }
            EnsureVisual();
        }

        private void ResolveReadModel()
        {
            m_ReadModel = source as ICircleFieldAuraReadModel;
            if (m_ReadModel != null) return;
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is ICircleFieldAuraReadModel readModel)) continue;
                source = behaviours[i];
                m_ReadModel = readModel;
                break;
            }
        }
    }
}
