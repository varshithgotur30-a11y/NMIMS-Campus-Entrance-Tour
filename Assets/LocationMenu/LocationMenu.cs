using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// Serializable entry for a Location Menu destination button.
/// Editable in the Unity Inspector.
/// </summary>
[System.Serializable]
public class LocationMenuItem
{
    [Tooltip("Display name on the menu button")]
    public string locationName;

    public LocationMenuItem(string name)
    {
        locationName = name;
    }
}

/// <summary>
/// World-space quick navigation menu positioned in the upper-right field of view.
/// Automatically creates itself at runtime without editing SampleScene.unity.
/// Supports ray-plane intersection math for VR right controller pointer and desktop mouse.
/// Strictly non-interfering: no colliders, no GraphicRaycasters, no EventSystems.
/// </summary>
public class LocationMenu : MonoBehaviour
{
    [Header("Tour Navigation Reference")]
    [Tooltip("The VideoNavigation controller in the scene (auto-discovered if null)")]
    public VideoNavigation navigation;

    [Header("Menu Locations (Editable in Inspector)")]
    [Tooltip("Location entries matching tour room indices 0 to 4")]
    public List<LocationMenuItem> menuItems = new List<LocationMenuItem>()
    {
        new LocationMenuItem("Fountain"),
        new LocationMenuItem("Garden"),
        new LocationMenuItem("Stage"),
        new LocationMenuItem("College Entrance"),
        new LocationMenuItem("Campus Entrance")
    };

    [Header("Placement & Soft Follow (Top-Right FOV)")]
    [Tooltip("Distance in front of active camera in meters")]
    public float forwardDistance = 1.8f;

    [Tooltip("Horizontal offset (positive = right)")]
    public float horizontalOffset = 0.55f;

    [Tooltip("Vertical offset (positive = up)")]
    public float verticalOffset = 0.35f;

    [Tooltip("Smoothing speed for following camera movement (lag)")]
    public float followSpeed = 4.0f;

    [Tooltip("Smoothing speed for billboarding rotation")]
    public float rotationSpeed = 6.0f;

    [Header("Visibility & Toggle Controls")]
    [Tooltip("Whether the menu is visible when the tour starts")]
    public bool isVisibleByDefault = true;

    [Tooltip("Desktop keyboard key to show/hide the location menu")]
    public Key toggleKey = Key.M;

    [Tooltip("Allow Left Controller Y button (secondary button) to toggle visibility in VR")]
    public bool enableVRToggle = true;

    [Tooltip("Fade transition duration in seconds")]
    public float fadeDuration = 0.20f;

    [Header("Visual Dimensions & Colors")]
    public Vector2 panelSize = new Vector2(210f, 270f);
    public Vector2 buttonSize = new Vector2(186f, 36f);
    public float worldScale = 0.002f;

    public Color panelBorderColor = new Color(0.0f, 0.92f, 1.0f, 0.75f);
    public Color panelBgColor = new Color(0.04f, 0.08f, 0.16f, 0.92f);

    public Color normalBtnBorderColor = new Color(0.0f, 0.85f, 1.0f, 0.40f);
    public Color normalBtnBgColor = new Color(0.06f, 0.12f, 0.22f, 0.85f);
    public Color normalTextColor = new Color(0.85f, 0.90f, 0.95f, 0.95f);

    public Color currentBtnBorderColor = new Color(1.0f, 0.85f, 0.15f, 1.0f);
    public Color currentBtnBgColor = new Color(0.24f, 0.20f, 0.05f, 0.92f);
    public Color currentTextColor = new Color(1.0f, 0.92f, 0.50f, 1.0f);

    public Color hoverBtnBorderColor = new Color(0.20f, 0.95f, 1.0f, 1.0f);
    public Color hoverBtnBgColor = new Color(0.08f, 0.28f, 0.44f, 0.95f);
    public Color hoverTextColor = Color.white;

    // Runtime procedural UI
    private GameObject canvasObj;
    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private RectTransform panelRect;
    private RectTransform hoverCursorRect;

    private List<RectTransform> buttonTransforms = new List<RectTransform>();
    private List<Image> buttonBorderImages = new List<Image>();
    private List<Image> buttonBgImages = new List<Image>();
    private List<TextMeshProUGUI> buttonTexts = new List<TextMeshProUGUI>();
    private List<Rect> buttonLocalRects = new List<Rect>();
    private float[] currentButtonScales;

    // State & Tracking
    private Camera vrCamera;
    private bool hasSnappedInitialPosition = false;
    private int currentRoomIndex = 0;
    private int hoveredButtonIndex = -1;
    private float clickCooldownTimer = 0f;
    private Coroutine fadeCoroutine;

    // VR Controller Tracking
    private VRControllerVisualizer rightControllerVisualizer;
    private UnityEngine.XR.InputDevice leftXRDevice;
    private bool wasLeftSecondaryPressed = false;

    // Desktop Mouse Drag Tracking
    private Vector2 mouseDownPosition;
    private bool isMouseDragging = false;

    // Audio & Sprites
    private AudioSource audioSource;
    private static Sprite roundedRectSprite;
    private static Sprite cursorDotSprite;

    #region Auto-Setup Runtime Initializer

    /// <summary>
    /// Discovers VideoNavigation and spawns the LocationMenu GameObject at runtime
    /// without modifying SampleScene.unity.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        VideoNavigation nav = UnityEngine.Object.FindFirstObjectByType<VideoNavigation>();
        if (nav == null)
        {
            Debug.LogWarning("[LocationMenu] VideoNavigation not found in scene. LocationMenu will not be spawned.");
            return;
        }

        if (UnityEngine.Object.FindFirstObjectByType<LocationMenu>() != null)
        {
            return; // Already exists
        }

        GameObject menuObj = new GameObject("LocationMenu");
        LocationMenu menu = menuObj.AddComponent<LocationMenu>();
        menu.navigation = nav;
        Debug.Log("[LocationMenu] Successfully initialized runtime LocationMenu.");
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        FindActiveCamera();
        BuildProceduralMenu();
        SetupAudio();
    }

    private void OnEnable()
    {
        if (navigation == null)
        {
            navigation = UnityEngine.Object.FindFirstObjectByType<VideoNavigation>();
        }

        if (navigation != null)
        {
            navigation.OnRoomChanged -= HandleRoomChanged;
            navigation.OnRoomChanged += HandleRoomChanged;
        }

        UnityEngine.XR.InputDevices.deviceConnected -= OnDeviceConnected;
        UnityEngine.XR.InputDevices.deviceConnected += OnDeviceConnected;
    }

    private void Start()
    {
        FindActiveCamera();

        // Sync initial room index
        int initialIndex = 0;
        if (navigation != null)
        {
            initialIndex = navigation.CurrentVideoIndex;
        }

        if (initialIndex < 0 || (menuItems != null && initialIndex >= menuItems.Count))
        {
            initialIndex = 0;
        }

        currentRoomIndex = initialIndex;
        RefreshButtonVisuals();

        // Snap to initial position immediately
        SnapToCamera();

        // Initial visibility
        if (canvasGroup != null)
        {
            canvasGroup.alpha = isVisibleByDefault ? 1f : 0f;
        }
    }

    private void Update()
    {
        CheckToggleInput();
        ProcessInteraction();
        AnimateButtonScales();
    }

    private void LateUpdate()
    {
        UpdateSoftFollow();
    }

    private void OnDisable()
    {
        if (navigation != null)
        {
            navigation.OnRoomChanged -= HandleRoomChanged;
        }

        UnityEngine.XR.InputDevices.deviceConnected -= OnDeviceConnected;

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        if (navigation != null)
        {
            navigation.OnRoomChanged -= HandleRoomChanged;
        }

        UnityEngine.XR.InputDevices.deviceConnected -= OnDeviceConnected;
    }

    #endregion

    #region Procedural UI Construction

    /// <summary>
    /// Builds the World-Space frosted-glass menu with 5 stacked buttons and TMP labels.
    /// Strictly has NO Colliders, NO GraphicRaycasters, and raycastTarget = false.
    /// </summary>
    private void BuildProceduralMenu()
    {
        // 1. World Space Canvas
        canvasObj = new GameObject("MenuCanvas");
        canvasObj.transform.SetParent(transform, false);
        canvasObj.transform.localPosition = Vector3.zero;
        canvasObj.transform.localRotation = Quaternion.identity;

        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 12; // In front of 360 sphere and location panel

        canvasGroup = canvasObj.AddComponent<CanvasGroup>();
        canvasGroup.alpha = isVisibleByDefault ? 1f : 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        panelRect = canvasObj.GetComponent<RectTransform>();
        if (panelRect == null) panelRect = canvasObj.AddComponent<RectTransform>();
        panelRect.sizeDelta = panelSize;
        panelRect.localScale = Vector3.one * worldScale;

        // 2. Outer Border Box
        GameObject borderObj = new GameObject("Box_Border");
        borderObj.transform.SetParent(canvasObj.transform, false);
        RectTransform borderRect = borderObj.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.sprite = GetRoundedRectSprite();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = panelBorderColor;
        borderImg.raycastTarget = false;

        // 3. Inner Frosted Slate Background (inset 2.5px)
        GameObject bgObj = new GameObject("Box_Background");
        bgObj.transform.SetParent(borderObj.transform, false);
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = new Vector2(2.5f, 2.5f);
        bgRect.offsetMax = new Vector2(-2.5f, -2.5f);

        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.sprite = GetRoundedRectSprite();
        bgImg.type = Image.Type.Sliced;
        bgImg.color = panelBgColor;
        bgImg.raycastTarget = false;

        // 4. Header Label ("LOCATIONS")
        GameObject headerObj = new GameObject("Text_Header");
        headerObj.transform.SetParent(bgObj.transform, false);
        RectTransform headerRect = headerObj.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.offsetMin = new Vector2(12f, -32f);
        headerRect.offsetMax = new Vector2(-12f, -10f);

        TextMeshProUGUI headerText = headerObj.AddComponent<TextMeshProUGUI>();
        headerText.fontSize = 12f;
        headerText.fontStyle = FontStyles.Bold;
        headerText.alignment = TextAlignmentOptions.Center;
        headerText.characterSpacing = 3.0f;
        headerText.color = panelBorderColor;
        headerText.raycastTarget = false;
        headerText.text = "LOCATIONS";

        // 5. Build 5 Stacked Pill Buttons
        int count = (menuItems != null && menuItems.Count > 0) ? menuItems.Count : 5;
        currentButtonScales = new float[count];

        float startY = 68f;
        float ySpacing = 44f;

        for (int i = 0; i < count; i++)
        {
            float yPos = startY - (i * ySpacing);
            BuildButton(bgObj.transform, i, yPos);
        }

        // 6. Subtle Hover Cursor Dot on Canvas Plane
        GameObject cursorObj = new GameObject("HoverCursorDot");
        cursorObj.transform.SetParent(bgObj.transform, false);
        hoverCursorRect = cursorObj.AddComponent<RectTransform>();
        hoverCursorRect.sizeDelta = new Vector2(8f, 8f);
        hoverCursorRect.anchoredPosition = new Vector2(9999f, 9999f); // offscreen initially

        Image cursorImg = cursorObj.AddComponent<Image>();
        cursorImg.sprite = GetCursorDotSprite();
        cursorImg.color = new Color(0.2f, 0.95f, 1.0f, 0.85f);
        cursorImg.raycastTarget = false;
    }

    private void BuildButton(Transform parent, int index, float yPos)
    {
        string labelName = (menuItems != null && index < menuItems.Count) ? menuItems[index].locationName : $"Location {index + 1}";

        // Button Root Object
        GameObject btnObj = new GameObject($"Btn_Location_{index}");
        btnObj.transform.SetParent(parent, false);

        RectTransform bRect = btnObj.AddComponent<RectTransform>();
        bRect.sizeDelta = buttonSize;
        bRect.anchoredPosition = new Vector2(0f, yPos);
        buttonTransforms.Add(bRect);
        currentButtonScales[index] = 1.0f;

        // Cache local AABB rectangle for mathematical ray-plane hit tests
        float halfW = buttonSize.x * 0.5f;
        float halfH = buttonSize.y * 0.5f;
        Rect localBox = new Rect(-halfW, yPos - halfH, buttonSize.x, buttonSize.y);
        buttonLocalRects.Add(localBox);

        // Button Outer Border
        GameObject borderObj = new GameObject("Btn_Border");
        borderObj.transform.SetParent(btnObj.transform, false);
        RectTransform borderRect = borderObj.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.sprite = GetRoundedRectSprite();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = normalBtnBorderColor;
        borderImg.raycastTarget = false;
        buttonBorderImages.Add(borderImg);

        // Button Inner Background (inset 2px)
        GameObject bgObj = new GameObject("Btn_Background");
        bgObj.transform.SetParent(borderObj.transform, false);
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = new Vector2(2f, 2f);
        bgRect.offsetMax = new Vector2(-2f, -2f);

        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.sprite = GetRoundedRectSprite();
        bgImg.type = Image.Type.Sliced;
        bgImg.color = normalBtnBgColor;
        bgImg.raycastTarget = false;
        buttonBgImages.Add(bgImg);

        // Button Text Label
        GameObject textObj = new GameObject("Btn_Text");
        textObj.transform.SetParent(bgObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 0f);
        textRect.offsetMax = new Vector2(-8f, 0f);

        TextMeshProUGUI bText = textObj.AddComponent<TextMeshProUGUI>();
        bText.fontSize = 14f;
        bText.fontStyle = FontStyles.Bold;
        bText.alignment = TextAlignmentOptions.Center;
        bText.color = normalTextColor;
        bText.textWrappingMode = TextWrappingModes.NoWrap;
        bText.raycastTarget = false;
        bText.text = labelName;
        buttonTexts.Add(bText);
    }

    private static Sprite GetRoundedRectSprite()
    {
        if (roundedRectSprite != null) return roundedRectSprite;

        int size = 64;
        int radius = 16;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Proc_LocationMenu_RoundedRect";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color white = Color.white;
        Color clear = new Color(1f, 1f, 1f, 0f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int cx = (x < radius) ? radius : (x >= size - radius) ? size - 1 - radius : x;
                int cy = (y < radius) ? radius : (y >= size - radius) ? size - 1 - radius : y;

                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                if (dist <= radius - 1f)
                {
                    tex.SetPixel(x, y, white);
                }
                else if (dist <= radius)
                {
                    float a = 1.0f - (dist - (radius - 1f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
                else
                {
                    tex.SetPixel(x, y, clear);
                }
            }
        }

        tex.Apply();
        Vector4 border = new Vector4(radius + 2, radius + 2, radius + 2, radius + 2);
        roundedRectSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        return roundedRectSprite;
    }

    private static Sprite GetCursorDotSprite()
    {
        if (cursorDotSprite != null) return cursorDotSprite;

        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Proc_HoverDot";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float center = (size - 1) * 0.5f;
        float radius = center - 1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                if (dist <= radius - 1f)
                {
                    tex.SetPixel(x, y, Color.white);
                }
                else if (dist <= radius)
                {
                    float a = 1.0f - (dist - (radius - 1f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }

        tex.Apply();
        cursorDotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return cursorDotSprite;
    }

    #endregion

    #region Interaction (Ray-Plane Math, Zero Colliders)

    private void ProcessInteraction()
    {
        if (canvasGroup == null || canvasGroup.alpha < 0.05f)
        {
            hoveredButtonIndex = -1;
            HideHoverDot();
            return;
        }

        bool hasRay = false;
        Ray ray = default;
        bool isClickTriggered = false;

        // 1. Try VR Controller Ray from Right Hand
        FindRightController();
        if (rightControllerVisualizer != null && rightControllerVisualizer.IsTracked)
        {
            Transform tip = rightControllerVisualizer.PointerTip;
            ray = new Ray(tip.position, tip.forward);
            isClickTriggered = rightControllerVisualizer.WasTriggerPressedThisFrame;
            hasRay = true;
        }
        else
        {
            // VR Fallback directly via UnityEngine.XR.InputDevices
            UnityEngine.XR.InputDevice rightDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (rightDevice.isValid &&
                rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 rPos) &&
                rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rRot))
            {
                ray = new Ray(rPos, rRot * Vector3.forward);
                if (rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool triggerDown))
                {
                    isClickTriggered = triggerDown;
                }
                hasRay = true;
            }
        }

        // 2. Desktop Mouse Ray (if VR ray is inactive)
        if (!hasRay)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && vrCamera != null)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                bool leftDown = Mouse.current.leftButton.wasPressedThisFrame;
                bool leftHeld = Mouse.current.leftButton.isPressed;
                bool leftUp = Mouse.current.leftButton.wasReleasedThisFrame;

                if (leftDown)
                {
                    mouseDownPosition = mousePos;
                    isMouseDragging = false;
                }
                else if (leftHeld)
                {
                    if (Vector2.Distance(mousePos, mouseDownPosition) > 6.0f)
                    {
                        isMouseDragging = true;
                    }
                }

                ray = vrCamera.ScreenPointToRay(mousePos);
                isClickTriggered = leftUp && !isMouseDragging;
                hasRay = true;
            }
#endif
        }

        if (!hasRay)
        {
            hoveredButtonIndex = -1;
            HideHoverDot();
            RefreshButtonVisuals();
            return;
        }

        // 3. Mathematical Ray-Plane Intersection with Menu Canvas
        // Canvas front faces along -transform.forward (towards camera)
        Plane menuPlane = new Plane(-transform.forward, transform.position);

        if (menuPlane.Raycast(ray, out float enterDist) && enterDist > 0.01f && enterDist < 10.0f)
        {
            Vector3 worldHit = ray.GetPoint(enterDist);
            Vector3 localHit = panelRect.InverseTransformPoint(worldHit);

            // Check if hit is within the overall menu panel bounds
            float halfW = panelSize.x * 0.5f;
            float halfH = panelSize.y * 0.5f;

            if (localHit.x >= -halfW && localHit.x <= halfW && localHit.y >= -halfH && localHit.y <= halfH)
            {
                UpdateHoverDot(localHit);

                // Check button bounding boxes
                int hitBtn = -1;
                for (int i = 0; i < buttonLocalRects.Count; i++)
                {
                    if (buttonLocalRects[i].Contains(new Vector2(localHit.x, localHit.y)))
                    {
                        hitBtn = i;
                        break;
                    }
                }

                if (hitBtn != hoveredButtonIndex)
                {
                    hoveredButtonIndex = hitBtn;
                    RefreshButtonVisuals();
                }

                if (isClickTriggered && hoveredButtonIndex >= 0)
                {
                    OnButtonClicked(hoveredButtonIndex);
                }

                return;
            }
        }

        // Ray missed or outside panel
        if (hoveredButtonIndex != -1)
        {
            hoveredButtonIndex = -1;
            RefreshButtonVisuals();
        }
        HideHoverDot();
    }

    private void UpdateHoverDot(Vector3 localHit)
    {
        if (hoverCursorRect != null)
        {
            hoverCursorRect.anchoredPosition = new Vector2(localHit.x, localHit.y);
            if (!hoverCursorRect.gameObject.activeSelf) hoverCursorRect.gameObject.SetActive(true);
        }
    }

    private void HideHoverDot()
    {
        if (hoverCursorRect != null && hoverCursorRect.gameObject.activeSelf)
        {
            hoverCursorRect.gameObject.SetActive(false);
        }
    }

    private void OnButtonClicked(int roomIndex)
    {
        // Ignore if clicking the location the user is already at
        if (roomIndex == currentRoomIndex) return;

        // Ignore while video is in transition
        if (navigation != null && navigation.IsTransitioning) return;

        // Local 1.2s click cooldown
        if (Time.unscaledTime < clickCooldownTimer) return;
        clickCooldownTimer = Time.unscaledTime + 1.2f;

        PlayChimeClick();

        if (navigation != null)
        {
            navigation.PlayVideo(roomIndex);
        }
    }

    private void HandleRoomChanged(int newRoomIndex)
    {
        currentRoomIndex = newRoomIndex;
        hoveredButtonIndex = -1;
        RefreshButtonVisuals();
    }

    private void RefreshButtonVisuals()
    {
        for (int i = 0; i < buttonBorderImages.Count; i++)
        {
            if (i == currentRoomIndex)
            {
                // Current Room: Golden Highlight, non-clickable
                buttonBorderImages[i].color = currentBtnBorderColor;
                buttonBgImages[i].color = currentBtnBgColor;
                buttonTexts[i].color = currentTextColor;
            }
            else if (i == hoveredButtonIndex)
            {
                // Hovered: Bright Cyan Highlight
                buttonBorderImages[i].color = hoverBtnBorderColor;
                buttonBgImages[i].color = hoverBtnBgColor;
                buttonTexts[i].color = hoverTextColor;
            }
            else
            {
                // Normal
                buttonBorderImages[i].color = normalBtnBorderColor;
                buttonBgImages[i].color = normalBtnBgColor;
                buttonTexts[i].color = normalTextColor;
            }
        }
    }

    private void AnimateButtonScales()
    {
        if (buttonTransforms == null || currentButtonScales == null) return;

        for (int i = 0; i < buttonTransforms.Count; i++)
        {
            float targetScale = (i == hoveredButtonIndex && i != currentRoomIndex) ? 1.05f : 1.0f;
            currentButtonScales[i] = Mathf.Lerp(currentButtonScales[i], targetScale, 15f * Time.unscaledDeltaTime);
            buttonTransforms[i].localScale = Vector3.one * currentButtonScales[i];
        }
    }

    #endregion

    #region Toggle Controls (M Key & VR Y Button)

    /// <summary>
    /// Shows or hides the location menu with a smooth fade.
    /// </summary>
    public void ToggleVisibility()
    {
        if (!gameObject.activeInHierarchy) return;

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        bool currentlyVisible = canvasGroup != null && canvasGroup.alpha > 0.4f;
        float targetAlpha = currentlyVisible ? 0f : 1f;
        fadeCoroutine = StartCoroutine(FadeRoutine(targetAlpha));
    }

    private IEnumerator FadeRoutine(float targetAlpha)
    {
        if (canvasGroup == null) yield break;

        float from = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            canvasGroup.alpha = Mathf.Lerp(from, targetAlpha, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
        fadeCoroutine = null;
    }

    private void CheckToggleInput()
    {
        // 1. Desktop Keyboard Toggle (M key)
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            try
            {
                if (Keyboard.current[toggleKey].wasPressedThisFrame)
                {
                    ToggleVisibility();
                    return;
                }
            }
            catch
            {
                // Graceful fallback
            }
        }
#endif

        // 2. VR Left Controller Y Button Toggle
        if (enableVRToggle)
        {
            CheckVRToggle();
        }
    }

    private void CheckVRToggle()
    {
        if (!leftXRDevice.isValid)
        {
            leftXRDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (!leftXRDevice.isValid) return;
        }

        if (leftXRDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool isPressed))
        {
            if (isPressed && !wasLeftSecondaryPressed)
            {
                ToggleVisibility();
            }
            wasLeftSecondaryPressed = isPressed;
        }
        else
        {
            wasLeftSecondaryPressed = false;
        }
    }

    private void OnDeviceConnected(UnityEngine.XR.InputDevice dev)
    {
        if ((dev.characteristics & UnityEngine.XR.InputDeviceCharacteristics.Left) != 0)
        {
            leftXRDevice = dev;
        }
    }

    #endregion

    #region VR Camera Soft Follow & Helpers

    private void FindActiveCamera()
    {
        if (vrCamera != null && vrCamera.isActiveAndEnabled) return;

        vrCamera = Camera.main;
        if (vrCamera == null)
        {
            GameObject camObj = GameObject.FindWithTag("MainCamera");
            if (camObj != null) vrCamera = camObj.GetComponent<Camera>();
        }

        if (vrCamera == null)
        {
            var cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                if (cams[i] != null && cams[i].isActiveAndEnabled)
                {
                    vrCamera = cams[i];
                    break;
                }
            }
        }
    }

    private void FindRightController()
    {
        if (rightControllerVisualizer != null && rightControllerVisualizer.isActiveAndEnabled) return;

        var allVisualizers = UnityEngine.Object.FindObjectsByType<VRControllerVisualizer>(FindObjectsSortMode.None);
        foreach (var v in allVisualizers)
        {
            if (v.handNode == UnityEngine.XR.XRNode.RightHand)
            {
                rightControllerVisualizer = v;
                break;
            }
        }
        if (rightControllerVisualizer == null && allVisualizers.Length > 0)
        {
            rightControllerVisualizer = allVisualizers[0];
        }
    }

    private void SnapToCamera()
    {
        if (vrCamera == null) FindActiveCamera();
        if (vrCamera == null) return;

        Vector3 camPos = vrCamera.transform.position;
        Vector3 camFwd = vrCamera.transform.forward;
        Vector3 camRight = vrCamera.transform.right;

        Vector3 targetPos = camPos + (camFwd * forwardDistance) + (camRight * horizontalOffset) + (Vector3.up * verticalOffset);
        transform.position = targetPos;

        Vector3 lookDir = transform.position - camPos;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
        }

        hasSnappedInitialPosition = true;
    }

    private void UpdateSoftFollow()
    {
        if (vrCamera == null)
        {
            FindActiveCamera();
            if (vrCamera == null) return;
        }

        Vector3 camPos = vrCamera.transform.position;
        Vector3 camFwd = vrCamera.transform.forward;
        Vector3 camRight = vrCamera.transform.right;

        Vector3 targetPos = camPos + (camFwd * forwardDistance) + (camRight * horizontalOffset) + (Vector3.up * verticalOffset);

        if (!hasSnappedInitialPosition)
        {
            SnapToCamera();
            return;
        }

        // Soft position lag
        transform.position = Vector3.Lerp(transform.position, targetPos, followSpeed * Time.unscaledDeltaTime);

        // Soft billboard rotation facing camera (upright, zero roll)
        Vector3 lookDir = transform.position - camPos;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.unscaledDeltaTime);
        }
    }

    private void SetupAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 0.5f;
        audioSource.playOnAwake = false;
        audioSource.volume = 0.6f;

        int sampleRate = 44100;
        int samples = sampleRate / 14; // ~71ms
        float[] data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            data[i] = Mathf.Sin(2f * Mathf.PI * 1150f * (i / (float)sampleRate)) * (1f - t);
        }
        AudioClip clip = AudioClip.Create("MenuChime", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        audioSource.clip = clip;
    }

    private void PlayChimeClick()
    {
        if (audioSource != null)
        {
            audioSource.Play();
        }
    }

    #endregion
}
