using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR;
using UnityEngine.InputSystem;

/// <summary>
/// Serializable container holding location title and description for a tour stop.
/// Fully editable in the Unity Inspector.
/// </summary>
[System.Serializable]
public class LocationInfo
{
    [Tooltip("Location title displayed in bold (editable)")]
    public string title;

    [Tooltip("Short location description, 1-2 lines (editable)")]
    [TextArea(2, 4)]
    public string description;

    public LocationInfo(string title, string description)
    {
        this.title = title;
        this.description = description;
    }
}

/// <summary>
/// World-space floating location info panel for the 360 VR Tour.
/// Automatically spawns at runtime without modifying SampleScene.unity.
/// Provides soft head-follow, frosted-glass visuals, smooth fades, and toggle controls.
/// </summary>
public class LocationPanel : MonoBehaviour
{
    [Header("Tour Navigation Reference")]
    [Tooltip("The VideoNavigation controller in the scene (auto-discovered if null)")]
    public VideoNavigation navigation;

    [Header("Location Content (Editable in Inspector)")]
    [Tooltip("List of location details corresponding to tour rooms 0 to 4")]
    public List<LocationInfo> locations = new List<LocationInfo>()
    {
        new LocationInfo(
            "Fountain Plaza",
            "The central campus fountain and gathering plaza, surrounded by peaceful seating and student activity areas."
        ),
        new LocationInfo(
            "Botanical Garden",
            "Lush landscaped walking paths and greenery connecting the main campus academic facilities."
        ),
        new LocationInfo(
            "Open-Air Stage",
            "The central amphitheater and performing arts stage hosting campus ceremonies, cultural programs, and events."
        ),
        new LocationInfo(
            "College Entrance",
            "Main academic building entryway featuring reception desk, security turnstiles, and student bulletin boards."
        ),
        new LocationInfo(
            "Campus Entrance",
            "The grand campus entrance portal welcoming students, faculty, and university visitors."
        )
    };

    [Header("Fade & Display Timers")]
    [Tooltip("Duration of fade in and fade out transitions in seconds")]
    public float fadeDuration = 0.25f;

    [Tooltip("How long the panel stays visible before automatically fading out")]
    public float autoHideDuration = 5.0f;

    [Tooltip("Whether to automatically fade out the panel after autoHideDuration")]
    public bool enableAutoHide = true;

    [Header("Toggle Controls")]
    [Tooltip("Desktop keyboard key to show/hide the location panel")]
    public Key toggleKey = Key.L;

    [Tooltip("Allow the Left Controller Y button (secondary button) to toggle visibility in VR")]
    public bool enableVRToggle = true;

    [Header("VR Placement & Soft Follow")]
    [Tooltip("Distance in front of the active camera in meters")]
    public float forwardDistance = 2.0f;

    [Tooltip("Horizontal offset relative to camera facing (negative = left, positive = right)")]
    public float horizontalOffset = -0.50f;

    [Tooltip("Vertical offset below camera eye level (negative = below, positive = above)")]
    public float verticalOffset = -0.35f;

    [Tooltip("Lerp speed for following camera position movement")]
    public float followSpeed = 4.0f;

    [Tooltip("Slerp speed for billboarding to face camera")]
    public float rotationSpeed = 6.0f;

    [Header("Visual Styling")]
    [Tooltip("Size of the canvas panel in UI units")]
    public Vector2 panelSize = new Vector2(460f, 150f);

    [Tooltip("World scale multiplier (460 x 0.002 = ~0.92m wide in VR space)")]
    public float worldScale = 0.002f;

    [Tooltip("Outer glowing border color matching hotspot aesthetic")]
    public Color borderColor = new Color(0.0f, 0.92f, 1.0f, 0.85f);

    [Tooltip("Deep frosted dark slate glass background")]
    public Color backgroundColor = new Color(0.04f, 0.08f, 0.16f, 0.88f);

    [Tooltip("Accent color for 'Stop X of 5' counter")]
    public Color counterColor = new Color(0.0f, 0.92f, 1.0f, 1.0f);

    [Tooltip("Main title text color")]
    public Color titleColor = Color.white;

    [Tooltip("Description text color")]
    public Color descriptionColor = new Color(0.88f, 0.92f, 0.96f, 0.95f);

    // Runtime procedural UI references
    private GameObject canvasObj;
    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private RectTransform canvasRect;
    private Image borderImage;
    private Image bgImage;
    private TextMeshProUGUI counterText;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI descriptionText;

    // Tracking state
    private Camera vrCamera;
    private bool hasSnappedInitialPosition = false;
    private int currentRoomIndex = 0;
    private Coroutine transitionCoroutine;
    private Coroutine manualToggleCoroutine;

    // VR Input tracking
    private UnityEngine.XR.InputDevice leftControllerDevice;
    private bool wasSecondaryButtonPressed = false;

    // Cached procedural sprite
    private static Sprite roundedRectSprite;

    #region Auto-Setup Runtime Initializer

    /// <summary>
    /// Automatically discovers VideoNavigation and instantiates the LocationPanel
    /// when the scene loads, without modifying SampleScene.unity.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        VideoNavigation nav = UnityEngine.Object.FindFirstObjectByType<VideoNavigation>();
        if (nav == null)
        {
            Debug.LogWarning("[LocationPanel] VideoNavigation not found in scene. LocationPanel will not be spawned.");
            return;
        }

        if (UnityEngine.Object.FindFirstObjectByType<LocationPanel>() != null)
        {
            return; // Already exists
        }

        GameObject panelObj = new GameObject("LocationPanel");
        LocationPanel panel = panelObj.AddComponent<LocationPanel>();
        panel.navigation = nav;
        Debug.Log("[LocationPanel] Successfully initialized runtime LocationPanel.");
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        FindActiveCamera();
        BuildProceduralPanel();
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

        InputDevices.deviceConnected -= OnDeviceConnected;
        InputDevices.deviceConnected += OnDeviceConnected;
    }

    private void Start()
    {
        FindActiveCamera();

        // 1. Read the starting room index directly from VideoNavigation to handle timing races
        int initialIndex = 0;
        if (navigation != null)
        {
            initialIndex = navigation.CurrentVideoIndex;
        }

        if (initialIndex < 0 || (locations != null && locations.Count > 0 && initialIndex >= locations.Count))
        {
            initialIndex = 0;
        }

        currentRoomIndex = initialIndex;
        UpdateContent(currentRoomIndex);

        // 2. Snap immediately to initial position in front of camera
        SnapToCamera();

        // 3. Start initial display: fade in, display for 5s, fade out
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            transitionCoroutine = StartCoroutine(RoomTransitionRoutine(initialIndex, false));
        }
    }

    private void Update()
    {
        CheckToggleInput();
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

        InputDevices.deviceConnected -= OnDeviceConnected;

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        if (manualToggleCoroutine != null)
        {
            StopCoroutine(manualToggleCoroutine);
            manualToggleCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        if (navigation != null)
        {
            navigation.OnRoomChanged -= HandleRoomChanged;
        }

        InputDevices.deviceConnected -= OnDeviceConnected;
    }

    #endregion

    #region Procedural UI Construction

    /// <summary>
    /// Builds the World-Space frosted glass Canvas with TextMeshPro components.
    /// Strictly guarantees NO colliders and NO raycast targets so hotspots are never blocked.
    /// </summary>
    private void BuildProceduralPanel()
    {
        // 1. Canvas Root GameObject
        canvasObj = new GameObject("PanelCanvas");
        canvasObj.transform.SetParent(transform, false);
        canvasObj.transform.localPosition = Vector3.zero;
        canvasObj.transform.localRotation = Quaternion.identity;

        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 10; // Renders cleanly in front of panoramic sphere

        canvasGroup = canvasObj.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        canvasRect = canvasObj.GetComponent<RectTransform>();
        if (canvasRect == null) canvasRect = canvasObj.AddComponent<RectTransform>();
        canvasRect.sizeDelta = panelSize;
        canvasRect.localScale = Vector3.one * worldScale;

        // NOTE: Strictly DO NOT add GraphicRaycaster or EventSystem to avoid stealing hotspot clicks

        // 2. Outer Glowing Frosted Border
        GameObject borderObj = new GameObject("Panel_Border");
        borderObj.transform.SetParent(canvasObj.transform, false);
        RectTransform borderRect = borderObj.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;

        borderImage = borderObj.AddComponent<Image>();
        borderImage.sprite = GetRoundedRectSprite();
        borderImage.type = Image.Type.Sliced;
        borderImage.color = borderColor;
        borderImage.raycastTarget = false;

        // 3. Inner Frosted Slate Glass Background (inset by 2.5px for crisp neon rim)
        GameObject bgObj = new GameObject("Panel_Background");
        bgObj.transform.SetParent(borderObj.transform, false);
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = new Vector2(2.5f, 2.5f);
        bgRect.offsetMax = new Vector2(-2.5f, -2.5f);

        bgImage = bgObj.AddComponent<Image>();
        bgImage.sprite = GetRoundedRectSprite();
        bgImage.type = Image.Type.Sliced;
        bgImage.color = backgroundColor;
        bgImage.raycastTarget = false;

        // 4. Counter Header Label ("STOP X OF 5")
        GameObject counterObj = new GameObject("Text_Counter");
        counterObj.transform.SetParent(bgObj.transform, false);
        RectTransform counterRect = counterObj.AddComponent<RectTransform>();
        counterRect.anchorMin = new Vector2(0f, 1f);
        counterRect.anchorMax = new Vector2(1f, 1f);
        counterRect.offsetMin = new Vector2(24f, -34f);
        counterRect.offsetMax = new Vector2(-24f, -12f);

        counterText = counterObj.AddComponent<TextMeshProUGUI>();
        counterText.fontSize = 13f;
        counterText.fontStyle = FontStyles.Bold;
        counterText.alignment = TextAlignmentOptions.MidlineLeft;
        counterText.color = counterColor;
        counterText.raycastTarget = false;
        counterText.text = "STOP 1 OF 5";

        // 5. Main Title Label
        GameObject titleObj = new GameObject("Text_Title");
        titleObj.transform.SetParent(bgObj.transform, false);
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.offsetMin = new Vector2(24f, -68f);
        titleRect.offsetMax = new Vector2(-24f, -34f);

        titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.fontSize = 22f;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.MidlineLeft;
        titleText.color = titleColor;
        titleText.raycastTarget = false;
        titleText.text = "Fountain Plaza";

        // 6. Description Label (Wraps across 2 lines comfortably)
        GameObject descObj = new GameObject("Text_Description");
        descObj.transform.SetParent(bgObj.transform, false);
        RectTransform descRect = descObj.AddComponent<RectTransform>();
        descRect.anchorMin = new Vector2(0f, 0f);
        descRect.anchorMax = new Vector2(1f, 1f);
        descRect.offsetMin = new Vector2(24f, 12f);
        descRect.offsetMax = new Vector2(-24f, -70f);

        descriptionText = descObj.AddComponent<TextMeshProUGUI>();
        descriptionText.fontSize = 14.5f;
        descriptionText.fontStyle = FontStyles.Normal;
        descriptionText.alignment = TextAlignmentOptions.TopLeft;
        descriptionText.color = descriptionColor;
        descriptionText.raycastTarget = false;
        descriptionText.textWrappingMode = TextWrappingModes.Normal;
        descriptionText.text = "The central campus fountain and gathering plaza.";
    }

    /// <summary>
    /// Generates a reusable 9-sliced rounded rectangle sprite with antialiased corners.
    /// </summary>
    private static Sprite GetRoundedRectSprite()
    {
        if (roundedRectSprite != null) return roundedRectSprite;

        int size = 64;
        int radius = 16;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Proc_LocationPanel_RoundedRect";
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
                    float alpha = 1.0f - (dist - (radius - 1f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
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

    #endregion

    #region Room Transition & Content Updates

    private void HandleRoomChanged(int newRoomIndex)
    {
        if (!gameObject.activeInHierarchy) return;

        currentRoomIndex = newRoomIndex;

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }

        if (manualToggleCoroutine != null)
        {
            StopCoroutine(manualToggleCoroutine);
            manualToggleCoroutine = null;
        }

        transitionCoroutine = StartCoroutine(RoomTransitionRoutine(newRoomIndex, true));
    }

    private IEnumerator RoomTransitionRoutine(int newRoomIndex, bool fadeOutFirst)
    {
        // 1. Fade out if currently visible
        if (fadeOutFirst && canvasGroup != null && canvasGroup.alpha > 0.01f)
        {
            yield return FadeCanvasGroup(canvasGroup.alpha, 0f, fadeDuration);
        }

        // 2. Update content while invisible
        UpdateContent(newRoomIndex);

        // 3. Fade in
        yield return FadeCanvasGroup(0f, 1f, fadeDuration);

        // 4. Stay visible for autoHideDuration then fade out
        if (enableAutoHide)
        {
            float timer = 0f;
            while (timer < autoHideDuration)
            {
                timer += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return FadeCanvasGroup(1f, 0f, fadeDuration);
        }

        transitionCoroutine = null;
    }

    private void UpdateContent(int roomIndex)
    {
        int totalStops = (locations != null && locations.Count > 0) ? locations.Count : 5;
        int stopNumber = Mathf.Clamp(roomIndex + 1, 1, totalStops);

        if (counterText != null)
        {
            counterText.text = $"STOP {stopNumber} OF {totalStops}";
        }

        string titleStr = $"Location {stopNumber}";
        string descStr = "Explore the 360 panoramic view of this campus area.";

        if (locations != null && roomIndex >= 0 && roomIndex < locations.Count)
        {
            var loc = locations[roomIndex];
            if (!string.IsNullOrEmpty(loc.title)) titleStr = loc.title;
            if (!string.IsNullOrEmpty(loc.description)) descStr = loc.description;
        }
        else if (navigation != null && navigation.roomTitles != null && roomIndex >= 0 && roomIndex < navigation.roomTitles.Length)
        {
            titleStr = navigation.roomTitles[roomIndex];
        }

        if (titleText != null) titleText.text = titleStr;
        if (descriptionText != null) descriptionText.text = descStr;
    }

    private IEnumerator FadeCanvasGroup(float from, float to, float duration)
    {
        if (canvasGroup == null) yield break;

        if (duration <= 0.001f)
        {
            canvasGroup.alpha = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            canvasGroup.alpha = Mathf.Lerp(from, to, smoothT);
            yield return null;
        }

        canvasGroup.alpha = to;
    }

    #endregion

    #region Visibility Toggle (Keyboard & VR Controller)

    /// <summary>
    /// Toggles the panel visibility manually. Called via L key or VR Left Controller Y button.
    /// </summary>
    public void ToggleVisibility()
    {
        if (!gameObject.activeInHierarchy) return;

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        if (manualToggleCoroutine != null)
        {
            StopCoroutine(manualToggleCoroutine);
            manualToggleCoroutine = null;
        }

        if (canvasGroup != null && canvasGroup.alpha > 0.4f)
        {
            // Currently visible -> fade out to hide
            manualToggleCoroutine = StartCoroutine(FadeCanvasGroup(canvasGroup.alpha, 0f, fadeDuration));
        }
        else
        {
            // Currently hidden -> fade in
            manualToggleCoroutine = StartCoroutine(ManualShowRoutine());
        }
    }

    private IEnumerator ManualShowRoutine()
    {
        yield return FadeCanvasGroup(canvasGroup != null ? canvasGroup.alpha : 0f, 1f, fadeDuration);

        if (enableAutoHide)
        {
            float timer = 0f;
            while (timer < autoHideDuration)
            {
                timer += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return FadeCanvasGroup(1f, 0f, fadeDuration);
        }

        manualToggleCoroutine = null;
    }

    private void CheckToggleInput()
    {
        // 1. Desktop Keyboard Toggle (L key)
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
                // Graceful fallback if custom key lookup fails
            }
        }
#endif

        // 2. VR Left Controller Y button (Secondary Button on Left Hand)
        if (enableVRToggle)
        {
            CheckVRToggle();
        }
    }

    private void CheckVRToggle()
    {
        if (!leftControllerDevice.isValid)
        {
            leftControllerDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (!leftControllerDevice.isValid) return;
        }

        if (leftControllerDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool isPressed))
        {
            if (isPressed && !wasSecondaryButtonPressed)
            {
                ToggleVisibility();
            }
            wasSecondaryButtonPressed = isPressed;
        }
        else
        {
            wasSecondaryButtonPressed = false;
        }
    }

    private void OnDeviceConnected(UnityEngine.XR.InputDevice dev)
    {
        if ((dev.characteristics & UnityEngine.XR.InputDeviceCharacteristics.Left) != 0)
        {
            leftControllerDevice = dev;
        }
    }

    #endregion

    #region VR Camera Soft Follow & Billboarding

    private void FindActiveCamera()
    {
        if (vrCamera != null && vrCamera.isActiveAndEnabled) return;

        vrCamera = Camera.main;
        if (vrCamera == null)
        {
            GameObject mainCamObj = GameObject.FindWithTag("MainCamera");
            if (mainCamObj != null) vrCamera = mainCamObj.GetComponent<Camera>();
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

        // Soft billboard rotation facing camera (upright with no roll)
        Vector3 lookDir = transform.position - camPos;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.unscaledDeltaTime);
        }
    }

    #endregion
}
