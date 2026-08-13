using UnityEngine;

/// <summary>
/// Global prefab library for enemy status presentation. Every enemy resolves the
/// same Resources asset, while spawning its own local visual instance.
/// </summary>
[CreateAssetMenu(
    fileName = "EnemyStatusVfxConfig",
    menuName = "Vampire Hunt/VFX/Enemy Status VFX Config",
    order = 10)]
public sealed class EnemyStatusVfxConfig : ScriptableObject
{
    private const string DefaultResourcePath = "GameVFX/EnemyStatusVfxConfig";
    private static EnemyStatusVfxConfig cachedDefault;

    [Header("状态特效预制体 / Status VFX Prefabs")]
    [Tooltip("所有普通敌人共用的持续燃烧表现预制体。留空时使用代码生成的兜底粒子。")]
    [SerializeField] private GameObject burningPrefab;
    [Tooltip("所有普通敌人共用的持续冻结表现预制体。留空时使用代码生成的兜底粒子。")]
    [SerializeField] private GameObject frozenPrefab;

    [Header("默认挂点偏移 / Default Local Offsets")]
    [SerializeField] private Vector3 burningLocalOffset = new(0f, 0.9f, 0f);
    [SerializeField] private Vector3 frozenLocalOffset = new(0f, 0.9f, 0f);

    public GameObject BurningPrefab => burningPrefab;
    public GameObject FrozenPrefab => frozenPrefab;
    public Vector3 BurningLocalOffset => burningLocalOffset;
    public Vector3 FrozenLocalOffset => frozenLocalOffset;

    public static EnemyStatusVfxConfig LoadDefault()
    {
        cachedDefault ??= Resources.Load<EnemyStatusVfxConfig>(DefaultResourcePath);
        return cachedDefault;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => cachedDefault = null;
}
