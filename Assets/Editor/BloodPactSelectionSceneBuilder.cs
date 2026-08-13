#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class BloodPactSelectionSceneBuilder
{
    private const string MenuPath = "Tools/Vampire Hunt/Build Blood Pact Selection UI in Open UI Scene";
    private const string RootName = "BloodPactSelectionUI";
    private const string SpriteDirectory = "Assets/Art/UI/BloodPactSelection";
    private const string PrefabPath = "Assets/Prefabs/UI/BloodPactSelection.prefab";

    private static readonly Color White = new(0.94f, 0.93f, 0.9f, 1f);
    private static readonly Color Muted = new(0.68f, 0.66f, 0.65f, 1f);
    private static readonly Color Crimson = new(0.72f, 0.025f, 0.08f, 1f);

    [MenuItem(MenuPath)]
    public static void BuildInOpenScene()
    {
        Scene scene = FindLoadedUiScene();
        if (!scene.IsValid())
        {
            Debug.LogError("[Blood Pact UI] Load the UI scene first.");
            return;
        }

        BuildInScene(scene);
    }

    private static void BuildInScene(Scene scene)
    {

        ImportSpriteAssets();
        GameObject existing = FindRootObject(scene, RootName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        Canvas canvas = FindComponentInScene<Canvas>(scene);
        if (canvas == null)
        {
            Debug.LogError("[Blood Pact UI] No Canvas found in the UI scene.");
            return;
        }

        EnsureEventSystem(scene);

        GameObject root = CreateUIObject(RootName, canvas.transform);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        root.transform.SetAsLastSibling();

        CanvasGroup canvasGroup = root.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        Image backdrop = CreateImage("DimmedBackdrop", root.transform, null,
            new Color(0.005f, 0.006f, 0.009f, 0.9f));
        Stretch(backdrop.rectTransform);

        Image vignette = CreateImage("BloodMist", root.transform, null,
            new Color(0.18f, 0f, 0.025f, 0.34f));
        SetRect(vignette.rectTransform, new Vector2(0f, 735f), new Vector2(1920f, 345f));

        Text title = CreateText("Title", root.transform, "血契抉择", 54,
            FontStyle.Bold, TextAnchor.MiddleCenter, White);
        SetRect(title.rectTransform, new Vector2(560f, 947f), new Vector2(800f, 72f));

        Text subtitle = CreateText("Subtitle", root.transform,
            "猩红已满 · 选择一项血契力量", 24,
            FontStyle.Normal, TextAnchor.MiddleCenter, Muted);
        SetRect(subtitle.rectTransform, new Vector2(560f, 900f), new Vector2(800f, 42f));

        Image headerRune = CreateImage(
            "HeaderRune",
            root.transform,
            LoadSprite("ChalicePactEmblem.png"),
            Color.white);
        SetRect(headerRune.rectTransform, new Vector2(897f, 823f), new Vector2(126f, 126f));
        headerRune.preserveAspect = true;

        Sprite cardFrame = LoadSprite("BloodPactCardFrame.png");
        Sprite[] emblems =
        {
            LoadSprite("RavenPactEmblem.png"),
            LoadSprite("WolfPactEmblem.png"),
            LoadSprite("ChalicePactEmblem.png")
        };

        BloodPactCardView[] cardViews = new BloodPactCardView[3];
        Text[] localizedTexts = new Text[12];
        localizedTexts[0] = title;
        localizedTexts[1] = subtitle;

        float[] cardX = { 300f, 780f, 1260f };
        for (int index = 0; index < 3; index++)
        {
            cardViews[index] = CreateCard(
                root.transform,
                index,
                new Vector2(cardX[index], 160f),
                cardFrame,
                emblems[index],
                out Text cardTitle,
                out Text cardDescription,
                out Text selectLabel);
            localizedTexts[2 + index * 3] = cardTitle;
            localizedTexts[3 + index * 3] = cardDescription;
            localizedTexts[4 + index * 3] = selectLabel;
        }

        Text cost = CreateText("Cost", root.transform, "消耗 100 猩红值", 22,
            FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.9f, 0.24f, 0.3f, 1f));
        SetRect(cost.rectTransform, new Vector2(710f, 88f), new Vector2(500f, 44f));
        localizedTexts[11] = cost;

        BloodPactSelectionController controller = root.AddComponent<BloodPactSelectionController>();
        controller.Configure(canvasGroup, cardViews, emblems, localizedTexts);

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log($"[Blood Pact UI] Built selection UI in {scene.path} and saved {PrefabPath}.");
    }

    private static BloodPactCardView CreateCard(
        Transform parent,
        int index,
        Vector2 position,
        Sprite frameSprite,
        Sprite emblemSprite,
        out Text title,
        out Text description,
        out Text selectLabel)
    {
        GameObject card = CreateUIObject($"BloodPactCard_{index + 1}", parent);
        RectTransform rect = card.GetComponent<RectTransform>();
        SetRect(rect, position, new Vector2(360f, 650f));

        Image frame = card.AddComponent<Image>();
        frame.sprite = frameSprite;
        frame.color = Color.white;
        frame.preserveAspect = false;
        frame.raycastTarget = true;

        Button button = card.AddComponent<Button>();
        button.targetGraphic = frame;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.78f, 0.8f, 1f);
        colors.pressedColor = new Color(0.72f, 0.34f, 0.38f, 1f);
        colors.selectedColor = new Color(1f, 0.82f, 0.84f, 1f);
        colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.65f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        Outline outline = card.AddComponent<Outline>();
        outline.effectColor = new Color(Crimson.r, Crimson.g, Crimson.b, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);

        Text tier = CreateText("Tier", card.transform, $"TIER {index + 1}", 18,
            FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.86f, 0.25f, 0.3f, 1f));
        SetRect(tier.rectTransform, new Vector2(75f, 565f), new Vector2(210f, 32f));

        Image emblem = CreateImage("Emblem", card.transform, emblemSprite, Color.white);
        SetRect(emblem.rectTransform, new Vector2(86f, 354f), new Vector2(188f, 198f));
        emblem.preserveAspect = true;

        title = CreateText("PactName", card.transform, "血契名称", 29,
            FontStyle.Bold, TextAnchor.MiddleCenter, White);
        SetRect(title.rectTransform, new Vector2(42f, 292f), new Vector2(276f, 50f));

        Image divider = CreateImage("Divider", card.transform, null, Crimson);
        SetRect(divider.rectTransform, new Vector2(72f, 278f), new Vector2(216f, 2f));

        description = CreateText("Description", card.transform,
            "血契效果说明\n将在这里动态显示", 21,
            FontStyle.Normal, TextAnchor.UpperCenter, new Color(0.84f, 0.82f, 0.8f, 1f));
        description.horizontalOverflow = HorizontalWrapMode.Wrap;
        description.verticalOverflow = VerticalWrapMode.Truncate;
        description.lineSpacing = 1.15f;
        SetRect(description.rectTransform, new Vector2(48f, 88f), new Vector2(264f, 174f));

        selectLabel = CreateText("SelectLabel", card.transform, "缔结血契", 22,
            FontStyle.Bold, TextAnchor.MiddleCenter, White);
        SetRect(selectLabel.rectTransform, new Vector2(82f, 39f), new Vector2(196f, 40f));

        return new BloodPactCardView(card, button, emblem, title, tier, description);
    }

    private static void EnsureEventSystem(Scene scene)
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;

        GameObject eventObject = new("EventSystem");
        Undo.RegisterCreatedObjectUndo(eventObject, "Create EventSystem");
        SceneManager.MoveGameObjectToScene(eventObject, scene);
        eventObject.AddComponent<EventSystem>();
        InputSystemUIInputModule module = eventObject.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    private static Scene FindLoadedUiScene()
    {
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (scene.isLoaded &&
                string.Equals(scene.name, "UI", System.StringComparison.OrdinalIgnoreCase))
            {
                return scene;
            }
        }

        return default;
    }

    private static GameObject FindRootObject(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform match = FindChildRecursive(root.transform, name);
            if (match != null) return match.gameObject;
        }
        return null;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform match = FindChildRecursive(child, name);
            if (match != null) return match;
        }
        return null;
    }

    private static T FindComponentInScene<T>(Scene scene) where T : Component
    {
        T[] components = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (T component in components)
        {
            if (component.gameObject.scene == scene) return component;
        }
        return null;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject result = new(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(result, "Create " + name);
        result.layer = LayerMask.NameToLayer("UI");
        result.transform.SetParent(parent, false);
        return result;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        GameObject imageObject = CreateUIObject(name, parent);
        Image image = imageObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text CreateText(
        string name,
        Transform parent,
        string value,
        int size,
        FontStyle style,
        TextAnchor alignment,
        Color color)
    {
        GameObject textObject = CreateUIObject(name, parent);
        Text text = textObject.AddComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
        shadow.effectDistance = new Vector2(1.4f, -1.4f);
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void ImportSpriteAssets()
    {
        string[] files =
        {
            "BloodPactCardFrame.png",
            "RavenPactEmblem.png",
            "WolfPactEmblem.png",
            "ChalicePactEmblem.png"
        };

        foreach (string file in files)
        {
            string path = $"{SpriteDirectory}/{file}";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
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

    private static Sprite LoadSprite(string file)
    {
        string path = $"{SpriteDirectory}/{file}";
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogError($"[Blood Pact UI] Missing sprite: {path}");
        return sprite;
    }
}
#endif
