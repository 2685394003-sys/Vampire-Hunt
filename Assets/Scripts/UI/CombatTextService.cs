using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;
using VampireHunt.Bootstrap;
using VampireHunt.Presentation.CombatText;
using VampireHunt.Presentation.Contracts;

/// <summary>
/// Client-only damage number presentation. The service owns one overlay canvas,
/// batches all animation updates and reuses a generously prewarmed object pool.
/// It creates itself on first use so gameplay scenes do not need extra setup.
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatTextService : MonoBehaviour,
    ICombatTextPool,
    IRuntimeGameplayEventStreamBinding,
    IRuntimeCameraBinding
{
    private const string CanvasName = "CombatTextCanvas";

    private static readonly Color32 NormalColor = new(240, 237, 230, 255);
    private static readonly Color32 NormalOutlineColor = new(8, 10, 14, 235);
    private static readonly Color32 CriticalColor = new(255, 211, 106, 255);
    private static readonly Color32 CriticalOutlineColor = new(184, 7, 20, 245);

    [Header("对象池 / Pool")]
    [SerializeField, Min(1)] private int prewarmCount = 128;
    [SerializeField, Min(1)] private int maxActiveCount = 256;

    [Header("合并 / Combining")]
    [SerializeField, Min(0f)] private float combineWindow = 0.12f;

    [Header("位置 / Placement")]
    [SerializeField, Min(0f)] private float worldHeightOffset = 1.1f;
    [SerializeField, Min(0f)] private float horizontalJitter = 14f;
    [SerializeField, Min(0f)] private float edgePadding = 48f;

    private readonly List<CombatTextView> activeViews = new(256);
    private readonly Dictionary<MergeKey, CombatTextView> mergeTargets = new(256);

    private static CombatTextService instance;
    private ObjectPool<CombatTextView> pool;
    private RectTransform canvasRect;
    private Camera viewCamera;
    private bool cameraBindingWasExplicit;
    private int createdViewCount;
    private uint spawnSequence;
    private bool initialized;
    private CombatTextPresenter presenter;

    public int ActiveCount => activeViews.Count;
    public int TotalPoolCount => pool?.CountAll ?? 0;
    public int InactivePoolCount => pool?.CountInactive ?? 0;
    int ICombatTextPool.Capacity => maxActiveCount;
    int ICombatTextPool.ActiveCount => activeViews.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void PrewarmRuntimeService()
    {
        if (!Application.isBatchMode)
        {
            EnsureInstance();
        }
    }

    /// <summary>
    /// Displays server-confirmed damage locally. In multiplayer every client
    /// receives the same call through the authoritative presentation event
    /// ingress. This method remains only as a serialized/API compatibility
    /// bridge while callers migrate to Bind(IGameplayEventStream).
    /// </summary>
    [Obsolete("Bind CombatTextPresenter to the authoritative gameplay event stream.")]
    public static void ShowDamage(
        int amount,
        bool critical,
        Vector3 worldPosition,
        int targetKey,
        ulong sourceKey)
    {
        if (amount <= 0 || Application.isBatchMode)
        {
            return;
        }

        CombatTextService service = EnsureInstance();
        service?.ShowInternal(amount, critical, worldPosition, targetKey, sourceKey);
    }

    /// <summary>
    /// Binds the Unity pool to the presentation event stream. The stream is
    /// the only supported path for new damage/healing text; it contains
    /// server-confirmed results and is safe to omit on a Dedicated Server.
    /// </summary>
    public void Bind(IGameplayEventStream events)
    {
        presenter?.Dispose();
        presenter = null;
        if (events == null || Application.isBatchMode)
        {
            return;
        }

        InitializeIfNeeded();
        if (enabled && pool != null)
        {
            presenter = new CombatTextPresenter(events, this);
        }
    }

    public void SetViewCamera(Camera camera)
    {
        viewCamera = camera;
    }

    public void BindCamera(Camera camera)
    {
        cameraBindingWasExplicit = true;
        SetViewCamera(camera);
    }

    bool ICombatTextPool.TryRent(out ICombatTextHandle handle)
    {
        InitializeIfNeeded();
        if (!enabled || pool == null || activeViews.Count >= maxActiveCount)
        {
            handle = null;
            return false;
        }

        CombatTextView view = pool.Get();
        activeViews.Add(view);
        handle = view;
        return true;
    }

    void ICombatTextPool.Return(ICombatTextHandle handle)
    {
        if (!(handle is CombatTextView view) || pool == null)
        {
            return;
        }

        int index = activeViews.IndexOf(view);
        if (index < 0)
        {
            return;
        }

        activeViews.RemoveAt(index);
        if (mergeTargets.TryGetValue(view.Key, out CombatTextView current) &&
            ReferenceEquals(current, view))
        {
            mergeTargets.Remove(view.Key);
        }

        pool.Release(view);
    }

    private static CombatTextService EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindFirstObjectByType<CombatTextService>(FindObjectsInactive.Include);
        if (instance != null)
        {
            instance.InitializeIfNeeded();
            return instance;
        }

        if (!Application.isPlaying || Application.isBatchMode)
        {
            return null;
        }

        GameObject root = new(
            CanvasName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        root.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue - 16;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        instance = root.AddComponent<CombatTextService>();
        DontDestroyOnLoad(root);
        instance.InitializeIfNeeded();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        // Dynamically-created compatibility instances are roots and persist.
        // An authored UI-scene instance remains owned by that explicit scene.
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        InitializeIfNeeded();
    }

    private void OnDestroy()
    {
        presenter?.Dispose();
        presenter = null;
        if (instance == this)
        {
            instance = null;
        }

        mergeTargets.Clear();
        activeViews.Clear();
        pool?.Clear();
    }

    private void InitializeIfNeeded()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        maxActiveCount = Mathf.Max(1, maxActiveCount);
        prewarmCount = Mathf.Clamp(prewarmCount, 1, maxActiveCount);
        canvasRect = transform as RectTransform;

        if (canvasRect == null)
        {
            Debug.LogError("[CombatText] CombatTextService requires a RectTransform.", this);
            enabled = false;
            return;
        }

        pool = new ObjectPool<CombatTextView>(
            CreateView,
            OnTakeFromPool,
            OnReturnToPool,
            DestroyView,
            true,
            prewarmCount,
            maxActiveCount);

        List<CombatTextView> prewarmed = new(prewarmCount);
        for (int i = 0; i < prewarmCount; i++)
        {
            prewarmed.Add(pool.Get());
        }
        for (int i = 0; i < prewarmed.Count; i++)
        {
            pool.Release(prewarmed[i]);
        }
    }

    private void ShowInternal(
        int amount,
        bool critical,
        Vector3 worldPosition,
        int targetKey,
        ulong sourceKey)
    {
        InitializeIfNeeded();
        if (!enabled || pool == null)
        {
            return;
        }

        float now = Time.unscaledTime;
        MergeKey key = new(targetKey, sourceKey, critical);
        if (mergeTargets.TryGetValue(key, out CombatTextView mergeTarget) &&
            mergeTarget.CanMerge(now, combineWindow))
        {
            mergeTarget.Merge(amount, now);
            mergeTarget.Transform.SetAsLastSibling();
            return;
        }

        if (activeViews.Count >= maxActiveCount)
        {
            RecycleOldestView();
        }

        CombatTextView view = pool.Get();
        int lane = (int)(spawnSequence++ % 3u) - 1;
        float jitter = UnityEngine.Random.Range(-horizontalJitter, horizontalJitter);
        view.Begin(
            key,
            amount,
            critical,
            worldPosition,
            now,
            lane * 22f + jitter,
            NormalColor,
            NormalOutlineColor,
            CriticalColor,
            CriticalOutlineColor);
        view.Transform.SetAsLastSibling();
        activeViews.Add(view);
        mergeTargets[key] = view;
    }

    private void LateUpdate()
    {
        if (activeViews.Count == 0)
        {
            return;
        }

        if (!cameraBindingWasExplicit && (viewCamera == null || !viewCamera.isActiveAndEnabled))
        {
            viewCamera = ResolveLegacyCamera();
        }

        float now = Time.unscaledTime;
        for (int i = activeViews.Count - 1; i >= 0; i--)
        {
            CombatTextView view = activeViews[i];
            if (!view.UpdatePresentation(
                    now,
                    viewCamera,
                    canvasRect,
                    worldHeightOffset,
                    edgePadding))
            {
                ReleaseActiveAt(i);
            }
        }
    }

    private CombatTextView CreateView()
    {
        createdViewCount++;
        GameObject viewObject = new(
            $"CombatText_{createdViewCount:000}",
            typeof(RectTransform),
            typeof(CanvasRenderer));
        viewObject.layer = LayerMask.NameToLayer("UI");
        viewObject.transform.SetParent(transform, false);

        RectTransform rectTransform = viewObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(260f, 96f);

        TextMeshProUGUI label = viewObject.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = false;

        Outline outline = viewObject.AddComponent<Outline>();
        outline.useGraphicAlpha = true;
        outline.effectDistance = new Vector2(1.6f, -1.6f);

        return new CombatTextView(this, viewObject, rectTransform, label, outline);
    }

    private static void OnTakeFromPool(CombatTextView view)
    {
        view.GameObject.SetActive(true);
    }

    private static void OnReturnToPool(CombatTextView view)
    {
        view.Reset();
        view.GameObject.SetActive(false);
    }

    private static void DestroyView(CombatTextView view)
    {
        if (view?.GameObject != null)
        {
            Destroy(view.GameObject);
        }
    }

    private void RecycleOldestView()
    {
        int oldestIndex = 0;
        float oldestTime = float.PositiveInfinity;
        bool foundNormal = false;

        for (int i = 0; i < activeViews.Count; i++)
        {
            CombatTextView candidate = activeViews[i];
            if (foundNormal && candidate.IsCritical)
            {
                continue;
            }
            if (!candidate.IsCritical && !foundNormal)
            {
                foundNormal = true;
                oldestTime = float.PositiveInfinity;
            }
            if (candidate.WindowStartedAt < oldestTime)
            {
                oldestTime = candidate.WindowStartedAt;
                oldestIndex = i;
            }
        }

        ReleaseActiveAt(oldestIndex);
    }

    private void ReleaseActiveAt(int index)
    {
        CombatTextView view = activeViews[index];
        int last = activeViews.Count - 1;
        activeViews[index] = activeViews[last];
        activeViews.RemoveAt(last);

        if (mergeTargets.TryGetValue(view.Key, out CombatTextView current) &&
            ReferenceEquals(current, view))
        {
            mergeTargets.Remove(view.Key);
        }

        pool.Release(view);
    }

    private void BeginFromPresentationCommand(CombatTextView view, CombatTextCommand command)
    {
        if (view == null || !activeViews.Contains(view))
        {
            return;
        }

        float now = Time.unscaledTime;
        MergeKey key = new MergeKey(
            command.TargetId.Value,
            command.SourceId.Value,
            command.IsCritical);
        int lane = (int)(spawnSequence++ % 3u) - 1;
        float jitter = UnityEngine.Random.Range(-horizontalJitter, horizontalJitter);
        view.Begin(
            key,
            command.Amount,
            command.IsCritical,
            new Vector3(command.Position.X, command.Position.Y, command.Position.Z),
            now,
            lane * 22f + jitter,
            NormalColor,
            NormalOutlineColor,
            CriticalColor,
            CriticalOutlineColor);
        view.Transform.SetAsLastSibling();
    }

    /// <summary>
    /// The old static entry point has no composition context. Keep its camera
    /// lookup isolated here until Bootstrap supplies SetViewCamera; no domain
    /// or network code is allowed to use this compatibility path.
    /// </summary>
    [Obsolete("Bind SetViewCamera from composition instead.")]
    private static Camera ResolveLegacyCamera()
    {
        return Camera.main;
    }

    private readonly struct MergeKey : IEquatable<MergeKey>
    {
        public readonly ulong TargetKey;
        public readonly ulong SourceKey;
        public readonly bool Critical;

        public MergeKey(int targetKey, ulong sourceKey, bool critical)
            : this(unchecked((ulong)(uint)targetKey), sourceKey, critical)
        {
        }

        public MergeKey(ulong targetKey, ulong sourceKey, bool critical)
        {
            TargetKey = targetKey;
            SourceKey = sourceKey;
            Critical = critical;
        }

        public bool Equals(MergeKey other) =>
            TargetKey == other.TargetKey &&
            SourceKey == other.SourceKey &&
            Critical == other.Critical;

        public override bool Equals(object obj) => obj is MergeKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(TargetKey, SourceKey, Critical);
    }

    private sealed class CombatTextView : ICombatTextHandle
    {
        private const float NormalLifetime = 0.68f;
        private const float CriticalLifetime = 0.86f;
        private const float FadeDuration = 0.2f;

        private readonly TextMeshProUGUI label;
        private readonly Outline outline;
        private readonly CombatTextService owner;

        private int amount;
        private Vector3 worldPosition;
        private float animationStartedAt;
        private float lifetime;
        private float horizontalOffset;

        public GameObject GameObject { get; }
        public RectTransform Transform { get; }
        public MergeKey Key { get; private set; }
        public float WindowStartedAt { get; private set; }
        public bool IsCritical { get; private set; }
        public bool IsActive => GameObject.activeSelf;

        public CombatTextView(
            CombatTextService owner,
            GameObject gameObject,
            RectTransform transform,
            TextMeshProUGUI text,
            Outline textOutline)
        {
            this.owner = owner;
            GameObject = gameObject;
            Transform = transform;
            label = text;
            outline = textOutline;
        }

        public void Show(CombatTextCommand command)
        {
            owner.BeginFromPresentationCommand(this, command);
        }

        public void Begin(
            MergeKey key,
            int initialAmount,
            bool critical,
            Vector3 position,
            float now,
            float xOffset,
            Color32 normalColor,
            Color32 normalOutline,
            Color32 criticalColor,
            Color32 criticalOutline)
        {
            Key = key;
            amount = initialAmount;
            IsCritical = critical;
            worldPosition = position;
            WindowStartedAt = now;
            animationStartedAt = now;
            horizontalOffset = xOffset;
            lifetime = critical ? CriticalLifetime : NormalLifetime;

            label.fontSize = critical ? 48f : 36f;
            label.color = critical ? criticalColor : normalColor;
            outline.effectColor = critical ? criticalOutline : normalOutline;
            outline.effectDistance = critical
                ? new Vector2(2.2f, -2.2f)
                : new Vector2(1.6f, -1.6f);
            UpdateNumber();
        }

        public bool CanMerge(float now, float window) =>
            GameObject.activeSelf && now - WindowStartedAt <= window;

        public void Merge(int additionalAmount, float now)
        {
            amount += additionalAmount;
            animationStartedAt = now;
            UpdateNumber();
        }

        public bool UpdatePresentation(
            float now,
            Camera camera,
            RectTransform parentRect,
            float heightOffset,
            float padding)
        {
            float age = now - animationStartedAt;
            if (age >= lifetime)
            {
                return false;
            }

            if (camera == null || parentRect == null)
            {
                label.enabled = false;
                return true;
            }

            Vector3 screenPoint = camera.WorldToScreenPoint(
                worldPosition + Vector3.up * heightOffset);
            bool onScreen = screenPoint.z > 0f &&
                            screenPoint.x >= -padding &&
                            screenPoint.y >= -padding &&
                            screenPoint.x <= Screen.width + padding &&
                            screenPoint.y <= Screen.height + padding;
            label.enabled = onScreen;
            if (!onScreen ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect,
                    screenPoint,
                    null,
                    out Vector2 canvasPoint))
            {
                return true;
            }

            float normalized = Mathf.Clamp01(age / lifetime);
            float rise = Mathf.Lerp(0f, IsCritical ? 76f : 60f, EaseOutCubic(normalized));
            float shake = IsCritical && age < 0.08f
                ? Mathf.Sin(age * 170f) * (1f - age / 0.08f) * 4f
                : 0f;
            Transform.anchoredPosition = canvasPoint + new Vector2(horizontalOffset + shake, rise);

            float baseScale = IsCritical ? 1.08f : 1f;
            float scale;
            if (age < 0.08f)
            {
                scale = Mathf.Lerp(0.7f, 1.16f, Mathf.SmoothStep(0f, 1f, age / 0.08f));
            }
            else if (age < 0.16f)
            {
                scale = Mathf.Lerp(1.16f, 1f, (age - 0.08f) / 0.08f);
            }
            else
            {
                scale = 1f;
            }
            Transform.localScale = Vector3.one * (scale * baseScale);

            float fadeStart = lifetime - FadeDuration;
            label.alpha = age <= fadeStart
                ? 1f
                : 1f - Mathf.Clamp01((age - fadeStart) / FadeDuration);
            return true;
        }

        public void Reset()
        {
            amount = 0;
            label.enabled = true;
            label.alpha = 1f;
            label.SetText(string.Empty);
            Transform.anchoredPosition = Vector2.zero;
            Transform.localScale = Vector3.one;
        }

        private void UpdateNumber()
        {
            label.SetText("{0:0}", amount);
        }

        private static float EaseOutCubic(float value)
        {
            float inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
        }
    }
}
