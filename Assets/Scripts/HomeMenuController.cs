using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Phase A: main menu with app logo, Start (loads editor scene), and Credits panel. Builds UI at runtime.
/// </summary>
public class HomeMenuController : MonoBehaviour
{
    private const string AmbientAudioObjectName = "MenuAmbientAudio";
    private const string CreditsBodyText =
        "MR Blueprint\nDevStudio 2026 by Logitech\n\nSkylar Knight\nDiego Medina Molina\nVishnu Sai Vardhan Bodapati";

    [SerializeField] private string editorSceneName = "MainScene";
    [SerializeField] private float menuDistance = 1.35f;
    [SerializeField] private float minimumMenuHeight = 1.35f;
    [SerializeField] private float menuVerticalOffset;
    [SerializeField] private float menuWorldScale = 0.0015f;
    [SerializeField] private Vector2 menuCanvasSize = new Vector2(900f, 620f);
    [SerializeField] private int trackedPlacementFrames = 12;

    [Header("Logo")]
    [SerializeField] private string logo3dObjectName = "logo";
    [SerializeField] private Vector2 logoCanvasAnchor = new Vector2(0.5f, 0.62f);
    [SerializeField] private Vector2 logoCanvasAnchoredPosition = Vector2.zero;
    [SerializeField] private float logo3dTargetHeight = 220f;
    [SerializeField] private float logo3dCanvasDepth = 20f;
    [SerializeField] private Vector2 startButtonAnchoredPosition = new Vector2(0f, -112f);
    [SerializeField] private Vector2 creditsButtonAnchoredPosition = new Vector2(0f, -192f);
    [SerializeField] private Vector2 quitButtonAnchoredPosition = new Vector2(0f, -256f);

    [Header("Credits")]
    [SerializeField] private float creditsPanelCanvasDepth = -80f;

    [Header("Background (HomeMenu scene only)")]
    [SerializeField] private AudioClip menuAmbientClip;
    [SerializeField, Range(0f, 1f)] private float menuAmbientVolume = 0.1f;

    private GameObject _creditsRoot;
    private Canvas _canvas;
    private RectTransform _canvasRect;
    private Transform _logo3dRoot;
    private int _pendingPlacementFrames;

    private void Awake()
    {
        EnsureMainCamera();
        EnsureEventSystem();
        EnsureControllerRays();
        BuildUi();
        _pendingPlacementFrames = Mathf.Max(1, trackedPlacementFrames);
        PositionMenuInFrontOfCamera();
        EnsureAmbientPlaying();
    }

    private void OnEnable()
    {
        EnsureAmbientPlaying();
    }

    private void Start()
    {
        EnsureAmbientPlaying();
    }

    private AudioSource _ambientSource;

    private void OnDestroy()
    {
        if (_ambientSource != null)
        {
            _ambientSource.Stop();
        }
    }

    private void EnsureAmbientSource()
    {
        if (menuAmbientClip == null)
            return;

        if (menuAmbientClip.loadState == AudioDataLoadState.Unloaded)
            menuAmbientClip.LoadAudioData();

        if (_ambientSource == null)
        {
            var existing = transform.Find(AmbientAudioObjectName);
            if (existing != null)
                _ambientSource = existing.GetComponent<AudioSource>();

            if (_ambientSource == null)
            {
                var ambientGo = existing != null ? existing.gameObject : new GameObject(AmbientAudioObjectName);
                ambientGo.transform.SetParent(transform, false);
                _ambientSource = ambientGo.AddComponent<AudioSource>();
            }
        }

        _ambientSource.clip = menuAmbientClip;
        _ambientSource.loop = true;
        _ambientSource.volume = menuAmbientVolume;
        _ambientSource.spatialBlend = 0f;
        _ambientSource.playOnAwake = false;
        _ambientSource.ignoreListenerPause = true;
    }

    private void EnsureAmbientPlaying()
    {
        EnsureAmbientSource();
        if (_ambientSource == null || menuAmbientClip == null || menuAmbientClip.loadState == AudioDataLoadState.Failed)
            return;

        if (!_ambientSource.gameObject.activeSelf)
            _ambientSource.gameObject.SetActive(true);

        if (!_ambientSource.enabled)
            _ambientSource.enabled = true;

        if (_ambientSource.clip != menuAmbientClip)
            _ambientSource.clip = menuAmbientClip;

        _ambientSource.loop = true;
        _ambientSource.volume = menuAmbientVolume;
        _ambientSource.spatialBlend = 0f;
        _ambientSource.playOnAwake = false;
        _ambientSource.ignoreListenerPause = true;

        if (!_ambientSource.isPlaying && menuAmbientClip.loadState != AudioDataLoadState.Loading)
            _ambientSource.Play();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            EnsureAmbientPlaying();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus)
            EnsureAmbientPlaying();
    }

    private void LateUpdate()
    {
        if (_pendingPlacementFrames > 0)
        {
            PositionMenuInFrontOfCamera();
            _pendingPlacementFrames--;
        }

        EnsureAmbientPlaying();
    }

    private static void EnsureMainCamera()
    {
        if (ResolveMenuCamera() != null)
            return;

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
        cam.transform.position = new Vector3(0f, 0f, -10f);
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null)
            return;

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }

    private void BuildUi()
    {
        var canvasGo = new GameObject("HomeMenuCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        _canvas = canvas;
        _canvasRect = canvasGo.GetComponent<RectTransform>();

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = ResolveMenuCamera();
        canvas.sortingOrder = 0;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.dynamicPixelsPerUnit = 10f;
        canvasGo.AddComponent<GraphicRaycaster>();

        _canvasRect.sizeDelta = menuCanvasSize;
        _canvasRect.localScale = Vector3.one * Mathf.Max(0.0001f, menuWorldScale);

        var font = MrBlueprintUiFont.GetDefault();

        if (!TryPlaceLogo3d(canvasGo.transform))
            BuildTitleFallback(canvasGo.transform, font);

        CreateMenuButton(canvasGo.transform, font, "Start", startButtonAnchoredPosition, new Vector2(280f, 52f), LoadEditor);
        MXInkStatusPill.Create(
            canvasGo.transform,
            font,
            "MXInkHomeStatus",
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            startButtonAnchoredPosition + new Vector2(0f, -42f),
            new Vector2(280f, 24f));
        CreateMenuButton(canvasGo.transform, font, "Credits", creditsButtonAnchoredPosition, new Vector2(200f, 40f), ShowCredits);
        CreateMenuButton(canvasGo.transform, font, "Quit", quitButtonAnchoredPosition, new Vector2(200f, 40f), QuitApp);

        _creditsRoot = BuildCreditsPanel(canvasGo.transform, font, HideCredits, creditsPanelCanvasDepth);
        _creditsRoot.SetActive(false);
    }

    private bool TryPlaceLogo3d(Transform canvasTransform)
    {
        var logo = ResolveLogo3dRoot();
        if (logo == null)
        {
            Debug.LogWarning(
                $"HomeMenuController: Could not find a 3D logo object named '{logo3dObjectName}'. Using text title.");
            return false;
        }

        _logo3dRoot = logo;
        _logo3dRoot.gameObject.SetActive(true);
        if (_logo3dRoot.parent != canvasTransform)
            _logo3dRoot.SetParent(canvasTransform, true);

        LayoutLogo3d();
        return true;
    }

    private Transform ResolveLogo3dRoot()
    {
        if (_logo3dRoot != null)
            return _logo3dRoot;

        if (string.IsNullOrWhiteSpace(logo3dObjectName))
            return null;

        var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var candidate in transforms)
        {
            if (candidate == null
                || !string.Equals(candidate.name, logo3dObjectName, System.StringComparison.OrdinalIgnoreCase)
                || candidate.GetComponentInChildren<Renderer>(true) == null)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private void LayoutLogo3d()
    {
        if (_logo3dRoot == null || _canvasRect == null)
            return;

        var target = GetLogoCanvasLocalPosition();
        _logo3dRoot.localPosition = target;

        if (logo3dTargetHeight > 0f
            && TryGetLogoCanvasBounds(out var bounds)
            && bounds.size.y > 0.001f)
        {
            var scaleFactor = logo3dTargetHeight / bounds.size.y;
            _logo3dRoot.localScale *= scaleFactor;
        }

        if (TryGetLogoCanvasBounds(out var centeredBounds))
            _logo3dRoot.localPosition += target - centeredBounds.center;
    }

    private Vector3 GetLogoCanvasLocalPosition()
    {
        var size = _canvasRect != null ? _canvasRect.rect.size : menuCanvasSize;
        if (size.x <= 0.001f || size.y <= 0.001f)
            size = menuCanvasSize;

        var pivot = _canvasRect != null ? _canvasRect.pivot : new Vector2(0.5f, 0.5f);
        return new Vector3(
            (logoCanvasAnchor.x - pivot.x) * size.x + logoCanvasAnchoredPosition.x,
            (logoCanvasAnchor.y - pivot.y) * size.y + logoCanvasAnchoredPosition.y,
            logo3dCanvasDepth);
    }

    private bool TryGetLogoCanvasBounds(out Bounds bounds)
    {
        bounds = default;
        if (_logo3dRoot == null || _canvasRect == null)
            return false;

        var renderers = _logo3dRoot.GetComponentsInChildren<Renderer>(true);
        var hasBounds = false;
        foreach (var logoRenderer in renderers)
        {
            if (logoRenderer == null)
                continue;

            var localBounds = logoRenderer.localBounds;
            var min = localBounds.min;
            var max = localBounds.max;

            EncapsulateLogoBoundsCorner(logoRenderer.transform.TransformPoint(new Vector3(min.x, min.y, min.z)),
                ref bounds, ref hasBounds);
            EncapsulateLogoBoundsCorner(logoRenderer.transform.TransformPoint(new Vector3(min.x, min.y, max.z)),
                ref bounds, ref hasBounds);
            EncapsulateLogoBoundsCorner(logoRenderer.transform.TransformPoint(new Vector3(min.x, max.y, min.z)),
                ref bounds, ref hasBounds);
            EncapsulateLogoBoundsCorner(logoRenderer.transform.TransformPoint(new Vector3(min.x, max.y, max.z)),
                ref bounds, ref hasBounds);
            EncapsulateLogoBoundsCorner(logoRenderer.transform.TransformPoint(new Vector3(max.x, min.y, min.z)),
                ref bounds, ref hasBounds);
            EncapsulateLogoBoundsCorner(logoRenderer.transform.TransformPoint(new Vector3(max.x, min.y, max.z)),
                ref bounds, ref hasBounds);
            EncapsulateLogoBoundsCorner(logoRenderer.transform.TransformPoint(new Vector3(max.x, max.y, min.z)),
                ref bounds, ref hasBounds);
            EncapsulateLogoBoundsCorner(logoRenderer.transform.TransformPoint(new Vector3(max.x, max.y, max.z)),
                ref bounds, ref hasBounds);
        }

        return hasBounds;
    }

    private void EncapsulateLogoBoundsCorner(Vector3 worldPoint, ref Bounds bounds, ref bool hasBounds)
    {
        var canvasPoint = _canvasRect.InverseTransformPoint(worldPoint);
        if (!hasBounds)
        {
            bounds = new Bounds(canvasPoint, Vector3.zero);
            hasBounds = true;
            return;
        }

        bounds.Encapsulate(canvasPoint);
    }

    private static void BuildTitleFallback(Transform canvas, Font font)
    {
        var titleGo = new GameObject("TitleFallback");
        titleGo.transform.SetParent(canvas, false);
        var title = titleGo.AddComponent<Text>();
        MrBlueprintUiFont.Apply(title, font);
        title.fontSize = 48;
        title.fontStyle = FontStyle.Bold;
        title.color = Color.white;
        title.text = "MR Blueprint";
        title.alignment = TextAnchor.MiddleCenter;
        var titleRt = titleGo.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 0.58f);
        titleRt.anchorMax = new Vector2(0.5f, 0.58f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.anchoredPosition = Vector2.zero;
        titleRt.sizeDelta = new Vector2(900f, 120f);
    }

    private void EnsureControllerRays()
    {
        if (Object.FindFirstObjectByType<NonStylusControllerRayVisuals>(FindObjectsInactive.Include) != null)
            return;

        var raysGo = new GameObject("NonStylusControllerRayVisuals");
        raysGo.transform.SetParent(transform, false);
        raysGo.AddComponent<NonStylusControllerRayVisuals>();
    }

    private void PositionMenuInFrontOfCamera()
    {
        if (_canvasRect == null)
            return;

        var cam = ResolveMenuCamera();
        if (_canvas != null)
        {
            _canvas.worldCamera = cam;
        }

        var anchorPosition = cam != null ? cam.transform.position : transform.position + Vector3.up * minimumMenuHeight;
        anchorPosition.y = Mathf.Max(anchorPosition.y, minimumMenuHeight);
        var forward = cam != null ? cam.transform.forward : transform.forward;
        var flatForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (flatForward.sqrMagnitude <= 0.0001f)
        {
            flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }

        if (flatForward.sqrMagnitude <= 0.0001f)
        {
            flatForward = Vector3.forward;
        }

        flatForward.Normalize();
        _canvasRect.position = anchorPosition + flatForward * Mathf.Max(0.25f, menuDistance)
                                                 + Vector3.up * menuVerticalOffset;
        _canvasRect.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
        _canvasRect.localScale = Vector3.one * Mathf.Max(0.0001f, menuWorldScale);
    }

    private static Camera ResolveMenuCamera()
    {
        var main = Camera.main;
        if (main != null && main.isActiveAndEnabled)
            return main;

        var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var camera in cameras)
        {
            if (camera != null && camera.isActiveAndEnabled && camera.name == "CenterEyeAnchor")
                return camera;
        }

        foreach (var camera in cameras)
        {
            if (camera != null && camera.isActiveAndEnabled)
                return camera;
        }

        return main;
    }

    public static Button CreateMenuButton(Transform parent, Font font, string label, Vector2 anchoredPos, Vector2 size,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.22f, 0.42f, 0.72f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null)
            btn.onClick.AddListener(onClick);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var t = textGo.AddComponent<Text>();
        MrBlueprintUiFont.Apply(t, font);
        t.fontSize = 22;
        t.color = Color.white;
        t.text = label;
        t.alignment = TextAnchor.MiddleCenter;
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        return btn;
    }

    public static GameObject BuildCreditsPanel(Transform canvas, Font font, UnityEngine.Events.UnityAction onBack,
        float canvasDepth = 0f)
    {
        var root = new GameObject("CreditsPanel");
        root.transform.SetParent(canvas, false);
        root.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        SetRectTransformDepth(rootRt, canvasDepth);

        var box = new GameObject("Box");
        box.transform.SetParent(root.transform, false);
        box.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.14f, 0.98f);
        var boxRt = box.GetComponent<RectTransform>();
        boxRt.anchorMin = new Vector2(0.5f, 0.5f);
        boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.anchoredPosition = Vector2.zero;
        boxRt.sizeDelta = new Vector2(520f, 280f);

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(box.transform, false);
        var body = bodyGo.AddComponent<Text>();
        MrBlueprintUiFont.Apply(body, font);
        body.fontSize = 20;
        body.color = new Color(0.9f, 0.9f, 0.92f);
        body.text = CreditsBodyText;
        body.alignment = TextAnchor.MiddleCenter;
        var bodyRt = bodyGo.GetComponent<RectTransform>();
        bodyRt.anchorMin = new Vector2(0f, 0.2f);
        bodyRt.anchorMax = new Vector2(1f, 1f);
        bodyRt.offsetMin = new Vector2(24f, 16f);
        bodyRt.offsetMax = new Vector2(-24f, -16f);

        CreateMenuButton(box.transform, font, "Back", new Vector2(0f, -100f), new Vector2(160f, 40f), onBack);

        return root;
    }

    private static void SetRectTransformDepth(RectTransform rect, float z)
    {
        if (rect == null)
            return;

        var anchoredPosition = rect.anchoredPosition3D;
        anchoredPosition.z = z;
        rect.anchoredPosition3D = anchoredPosition;
    }

    public void LoadEditor()
    {
        if (string.IsNullOrEmpty(editorSceneName))
        {
            Debug.LogError("HomeMenuController: editor scene name is empty.");
            return;
        }

        SceneManager.LoadScene(editorSceneName, LoadSceneMode.Single);
    }

    public void QuitApp()
    {
        QuitApplication();
    }

    public static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ShowCredits()
    {
        if (_creditsRoot != null)
            _creditsRoot.SetActive(true);
    }

    private void HideCredits()
    {
        if (_creditsRoot != null)
            _creditsRoot.SetActive(false);
    }
}
