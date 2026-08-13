#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PlayerHudSceneBuilder
{
    private const string MenuPath = "Tools/Vampire Hunt/Build Player HUD in Open UI Scene";
    private const string HudRootName = "PlayerHUD_LowerLeft";
    private const string SpriteDirectory = "Assets/Art/UI/PlayerHUD";
    private const string PrefabPath = "Assets/Prefabs/UI/PlayerHUD_LowerLeft.prefab";

    private static readonly Color PanelColor = new(0.018f, 0.022f, 0.028f, 0.94f);
    private static readonly Color TrackColor = new(0.045f, 0.05f, 0.06f, 0.96f);
    private static readonly Color SilverColor = new(0.52f, 0.55f, 0.58f, 0.95f);
    private static readonly Color HealthColor = new(0.62f, 0.01f, 0.055f, 0.92f);
    private static readonly Color StaminaColor = new(0.015f, 0.38f, 0.39f, 0.92f);
    private static readonly Color PactColor = new(0.48f, 0.015f, 0.42f, 0.92f);

    [MenuItem(MenuPath)]
    public static void BuildInOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Player HUD] There is no loaded active scene.");
            return;
        }

        if (!string.Equals(scene.name, "UI", System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"[Player HUD] Open the UI scene first. Active scene: {scene.name}");
            return;
        }

        ImportSpriteAssets();

        GameObject existing = GameObject.Find(HudRootName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        Canvas canvas = FindOrCreateCanvas();
        GameObject hudRoot = CreateUIObject(HudRootName, canvas.transform);
        RectTransform hudRect = hudRoot.GetComponent<RectTransform>();
        hudRect.anchorMin = Vector2.zero;
        hudRect.anchorMax = Vector2.zero;
        hudRect.pivot = Vector2.zero;
        hudRect.anchoredPosition = new Vector2(48f, 42f);
        hudRect.sizeDelta = new Vector2(930f, 285f);

        CanvasGroup canvasGroup = hudRoot.AddComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        Image portrait = CreateSpriteImage(
            "PortraitMedallion",
            hudRoot.transform,
            LoadSprite("PlayerPortrait.png"),
            new Vector2(0f, 0f),
            new Vector2(225f, 250f));
        portrait.preserveAspect = true;

        Text identity = CreateText(
            "PlayerIdentity",
            portrait.transform,
            "PLAYER",
            23,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Color.white);
        SetRect(identity.rectTransform, new Vector2(25f, 3f), new Vector2(175f, 38f));

        BarParts health = CreateBar(
            hudRoot.transform,
            "Health",
            new Vector2(182f, 176f),
            new Vector2(710f, 66f),
            LoadSprite("HealthIcon.png"),
            "HEALTH",
            HealthColor,
            0.86f);

        BarParts stamina = CreateBar(
            hudRoot.transform,
            "Stamina",
            new Vector2(198f, 102f),
            new Vector2(665f, 58f),
            LoadSprite("StaminaIcon.png"),
            "STAMINA",
            StaminaColor,
            0.64f);

        BarParts bloodPact = CreateBar(
            hudRoot.transform,
            "BloodPact",
            new Vector2(212f, 32f),
            new Vector2(620f, 58f),
            LoadSprite("BloodPactIcon.png"),
            "BLOOD PACT",
            PactColor,
            0.42f);

        PlayerHudController controller = hudRoot.AddComponent<PlayerHudController>();
        controller.Configure(
            canvasGroup,
            health.Fill,
            health.Value,
            stamina.Fill,
            stamina.Value,
            bloodPact.Fill,
            bloodPact.Value);

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAssetAndConnect(
            hudRoot,
            PrefabPath,
            InteractionMode.AutomatedAction);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = hudRoot;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log($"[Player HUD] Built '{HudRootName}' in {scene.path} and saved {PrefabPath}.");
    }

    private static Canvas FindOrCreateCanvas()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Canvas candidate in canvases)
        {
            if (candidate.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return candidate;
            }
        }

        GameObject canvasObject = new("PlayerHUDCanvas", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Create Player HUD Canvas");
        canvasObject.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = false;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static BarParts CreateBar(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size,
        Sprite iconSprite,
        string title,
        Color fillColor,
        float previewFill)
    {
        GameObject barObject = CreateUIObject(name + "Bar", parent);
        RectTransform barRect = barObject.GetComponent<RectTransform>();
        SetRect(barRect, position, size);

        Image panel = barObject.AddComponent<Image>();
        panel.color = PanelColor;
        panel.raycastTarget = false;

        Outline outline = barObject.AddComponent<Outline>();
        outline.effectColor = SilverColor;
        outline.effectDistance = new Vector2(2f, -2f);

        Image topAccent = CreateSolidImage(
            "TopAccent",
            barObject.transform,
            new Color(fillColor.r, fillColor.g, fillColor.b, 1f));
        topAccent.rectTransform.anchorMin = new Vector2(0f, 1f);
        topAccent.rectTransform.anchorMax = new Vector2(1f, 1f);
        topAccent.rectTransform.pivot = new Vector2(0.5f, 1f);
        topAccent.rectTransform.anchoredPosition = Vector2.zero;
        topAccent.rectTransform.sizeDelta = new Vector2(0f, 3f);

        GameObject trackObject = CreateUIObject("Track", barObject.transform);
        RectTransform trackRect = trackObject.GetComponent<RectTransform>();
        trackRect.anchorMin = Vector2.zero;
        trackRect.anchorMax = Vector2.one;
        trackRect.offsetMin = new Vector2(66f, 8f);
        trackRect.offsetMax = new Vector2(-8f, -8f);
        Image track = trackObject.AddComponent<Image>();
        track.color = TrackColor;
        track.raycastTarget = false;

        Image fillImage = CreateSolidImage("Fill", trackObject.transform, fillColor);
        RectTransform fill = fillImage.rectTransform;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(previewFill, 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        fill.pivot = new Vector2(0f, 0.5f);

        Image icon = CreateSpriteImage(
            "Icon",
            barObject.transform,
            iconSprite,
            new Vector2(8f, 5f),
            new Vector2(54f, size.y - 10f));
        icon.preserveAspect = true;

        Text label = CreateText(
            "Label",
            barObject.transform,
            title,
            name == "Health" ? 24 : 21,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            Color.white);
        SetRect(label.rectTransform, new Vector2(82f, 6f), new Vector2(230f, size.y - 12f));

        Text value = CreateText(
            "Value",
            barObject.transform,
            $"{Mathf.RoundToInt(previewFill * 100f)} / 100",
            name == "Health" ? 24 : 21,
            FontStyle.Bold,
            TextAnchor.MiddleRight,
            Color.white);
        SetRect(value.rectTransform, new Vector2(size.x - 210f, 6f), new Vector2(190f, size.y - 12f));

        return new BarParts(fill, value);
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject gameObject = new(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(gameObject, "Create " + name);
        gameObject.layer = LayerMask.NameToLayer("UI");
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static Image CreateSolidImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = CreateUIObject(name, parent);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image CreateSpriteImage(
        string name,
        Transform parent,
        Sprite sprite,
        Vector2 position,
        Vector2 size)
    {
        GameObject imageObject = CreateUIObject(name, parent);
        Image image = imageObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.raycastTarget = false;
        SetRect(image.rectTransform, position, size);
        return image;
    }

    private static Text CreateText(
        string name,
        Transform parent,
        string content,
        int fontSize,
        FontStyle style,
        TextAnchor alignment,
        Color color)
    {
        GameObject textObject = CreateUIObject(name, parent);
        Text text = textObject.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;

        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void ImportSpriteAssets()
    {
        string[] filenames =
        {
            "PlayerPortrait.png",
            "HealthIcon.png",
            "StaminaIcon.png",
            "BloodPactIcon.png"
        };

        foreach (string filename in filenames)
        {
            string assetPath = $"{SpriteDirectory}/{filename}";
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
            {
                continue;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
    }

    private static Sprite LoadSprite(string filename)
    {
        string assetPath = $"{SpriteDirectory}/{filename}";
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite == null)
        {
            Debug.LogError($"[Player HUD] Missing sprite: {assetPath}");
        }
        return sprite;
    }

    private readonly struct BarParts
    {
        public readonly RectTransform Fill;
        public readonly Text Value;

        public BarParts(RectTransform fill, Text value)
        {
            Fill = fill;
            Value = value;
        }
    }
}
#endif
