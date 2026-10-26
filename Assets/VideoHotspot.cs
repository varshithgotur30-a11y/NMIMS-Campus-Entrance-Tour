using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

[RequireComponent(typeof(BoxCollider))]
public class VideoHotspot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Navigation Link")]
    [Tooltip("The navigation controller managing the 360 tour.")]
    public VideoNavigation navigation;

    [Tooltip("Target video index to switch to (0 to 4).")]
    public int targetVideoIndex = 1;

    [Tooltip("Display title shown on the TextMeshPro button.")]
    public string hotspotTitle = "HPC LAB";

    [Header("Comfortable Button Dimensions")]
    [Tooltip("Canvas size in UI pixels.")]
    public Vector2 buttonSize = new Vector2(330f, 72f);

    [Tooltip("World scale factor: 330 x 0.0035 = ~1.15m wide in 3D VR space (clean, balanced, clearly visible).")]
    public float worldScale = 0.0035f;

    [Tooltip("Font size for TextMeshPro label.")]
    public float fontSize = 24f;

    [Header("Sleek Frosted Glass Colors (Semi-Transparent)")]
    public Color normalBorderColor = new Color(0.0f, 0.92f, 1.0f, 1.0f);       // Vibrant luminous cyan
    public Color normalBgColor = new Color(0.04f, 0.08f, 0.16f, 0.88f);          // Deep frosted slate glass
    public Color normalTextColor = Color.white;
    public Color normalIconColor = new Color(0.0f, 0.92f, 1.0f, 1.0f);

    public Color hoverBorderColor = new Color(1.0f, 0.88f, 0.15f, 1.0f);       // Radiant gold
    public Color hoverBgColor = new Color(0.08f, 0.28f, 0.44f, 0.96f);           // Illuminated teal-gold
    public Color hoverTextColor = Color.white;
    public Color hoverIconColor = new Color(1.0f, 0.88f, 0.15f, 1.0f);

    [Header("Subtle Animation")]
    public float hoverScaleMultiplier = 1.10f;
    public bool enableBreathingPulse = true;
    public float pulseSpeed = 2.0f;
    public float pulseAmount = 0.02f; // Subtle 2% pulse

    private GameObject canvasObj;
    private Canvas canvas;
    private RectTransform canvasRect;
    private Image borderImage;
    private Image bgImage;
    private Image iconImage;
    private TextMeshProUGUI buttonText;
    private Button uiButton;
    private BoxCollider boxCollider;
    private Camera mainCamera;
    private bool isHovered = false;
    private Vector3 baseScale = Vector3.one;

    private static Sprite arrowSprite;

    private void Awake()
    {
        // 1. Disable any old mesh renderers/filters so raw sphere models never display
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = null;

        // 2. Disable legacy sphere colliders from early Unity templates
        SphereCollider sc = GetComponent<SphereCollider>();
        if (sc != null) sc.enabled = false;

        // 3. Clean up any legacy children
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }

        if (navigation == null)
        {
            navigation = FindFirstObjectByType<VideoNavigation>();
        }

        baseScale = Vector3.one;
        transform.localScale = baseScale;

        // 4. Build the sleek TextMeshPro button on a dedicated child GameObject
        BuildTextMeshProButton();
        SetupCollider();
    }

    private void Start()
    {
        mainCamera = Camera.main;
        UpdateLabelText();
    }

    private void LateUpdate()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        // Subtle breathing pulse or hover expansion on the parent transform
        Vector3 targetScale = baseScale;
        if (isHovered)
        {
            targetScale = baseScale * hoverScaleMultiplier;
        }
        else if (enableBreathingPulse)
        {
            float pulse = 1.0f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            targetScale = baseScale * pulse;
        }

        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * 12f);

        // Billboard button smoothly toward camera view
        if (mainCamera != null)
        {
            Vector3 lookDir = transform.position - mainCamera.transform.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }
    }

    /// <summary>
    /// Builds a comfortable, sleek World-Space TextMeshPro button.
    /// </summary>
    private void BuildTextMeshProButton()
    {
        // 1. Child Canvas GameObject with fixed worldScale
        canvasObj = new GameObject("ButtonCanvas");
        canvasObj.transform.SetParent(transform, false);
        canvasObj.transform.localPosition = Vector3.zero;
        canvasObj.transform.localRotation = Quaternion.identity;

        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        canvasRect = canvasObj.GetComponent<RectTransform>();
        if (canvasRect == null) canvasRect = canvasObj.AddComponent<RectTransform>();
        canvasRect.sizeDelta = buttonSize;
        canvasRect.localScale = Vector3.one * worldScale; // 330 x 0.0035 = 1.155m wide

        if (canvasObj.GetComponent<GraphicRaycaster>() == null)
        {
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        // 2. Outer Glowing Border Panel
        GameObject borderObj = new GameObject("Pill_Border");
        borderObj.transform.SetParent(canvasObj.transform, false);
        RectTransform borderRect = borderObj.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;

        borderImage = borderObj.AddComponent<Image>();
        borderImage.color = normalBorderColor;

        // 3. Inner Frosted Dark Slate Glass Panel (inset by 2.5 pixels for crisp border)
        GameObject bgObj = new GameObject("Pill_Background");
        bgObj.transform.SetParent(borderObj.transform, false);
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = new Vector2(3f, 3f);
        bgRect.offsetMax = new Vector2(-3f, -3f);

        bgImage = bgObj.AddComponent<Image>();
        bgImage.color = normalBgColor;

        // 4. Directional Procedural Arrow Icon (guaranteed zero missing font character warnings)
        GameObject iconObj = new GameObject("Pill_Icon");
        iconObj.transform.SetParent(bgObj.transform, false);
        RectTransform iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.sizeDelta = new Vector2(24f, 24f);
        iconRect.anchoredPosition = new Vector2(26f, 0f);

        iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = GetArrowSprite();
        iconImage.color = normalIconColor;

        // 5. Clean TextMeshPro Label
        GameObject textObj = new GameObject("Pill_Text");
        textObj.transform.SetParent(bgObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(50f, 2f);
        textRect.offsetMax = new Vector2(-16f, -2f);

        buttonText = textObj.AddComponent<TextMeshProUGUI>();
        buttonText.text = !string.IsNullOrEmpty(hotspotTitle) ? hotspotTitle.ToUpper() : $"HPC LAB {targetVideoIndex + 1}";
        buttonText.fontSize = fontSize;
        buttonText.fontStyle = FontStyles.Bold;
        buttonText.alignment = TextAlignmentOptions.MidlineLeft;
        buttonText.color = normalTextColor;
        buttonText.textWrappingMode = TextWrappingModes.NoWrap;

        // 6. Interactive UI Button component
        uiButton = borderObj.AddComponent<Button>();
        uiButton.targetGraphic = bgImage;
        uiButton.navigation = new Navigation { mode = Navigation.Mode.None };
        uiButton.onClick.AddListener(SelectHotspot);
    }

    private void SetupCollider()
    {
        boxCollider = GetComponent<BoxCollider>();
        if (boxCollider == null) boxCollider = gameObject.AddComponent<BoxCollider>();

        // Generous hit box for effortless raycast & pointer interaction
        float worldW = buttonSize.x * worldScale * 1.25f;
        float worldH = buttonSize.y * worldScale * 1.35f;
        boxCollider.size = new Vector3(worldW, worldH, 0.45f);
        boxCollider.center = Vector3.zero;
        boxCollider.isTrigger = false;
        boxCollider.enabled = true;
    }

    /// <summary>
    /// Enforces the unified button size and collider across all room transitions.
    /// </summary>
    public void ApplyDimensions()
    {
        if (canvasRect == null && canvasObj != null)
        {
            canvasRect = canvasObj.GetComponent<RectTransform>();
        }

        if (canvasRect != null)
        {
            canvasRect.sizeDelta = buttonSize;
            canvasRect.localScale = Vector3.one * worldScale;
        }

        if (buttonText != null)
        {
            buttonText.fontSize = fontSize;
        }

        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider>();
        }

        if (boxCollider != null)
        {
            float worldW = buttonSize.x * worldScale * 1.25f;
            float worldH = buttonSize.y * worldScale * 1.35f;
            boxCollider.size = new Vector3(worldW, worldH, 0.45f);
            boxCollider.center = Vector3.zero;
            boxCollider.enabled = true;
        }
    }

    public void UpdateLabelText()
    {
        ApplyDimensions();

        if (buttonText != null)
        {
            string label = !string.IsNullOrEmpty(hotspotTitle) ? hotspotTitle : $"HPC LAB {targetVideoIndex + 1}";
            buttonText.text = label.ToUpper();
        }
    }

    private static float lastGlobalClickTime = 0f;

    /// <summary>
    /// Triggered when the user deliberately clicks this hotspot button.
    /// </summary>
    public void SelectHotspot()
    {
        // Enforce 0.5 second cooldown debounce between room transitions to prevent accidental double-clicks
        if (Time.unscaledTime - lastGlobalClickTime < 0.5f) return;
        if (navigation != null && navigation.IsTransitioning) return;

        lastGlobalClickTime = Time.unscaledTime;

        StartCoroutine(ButtonFeedbackRoutine());
        PlayClickSound();

        if (navigation != null)
        {
            navigation.PlayVideo(targetVideoIndex);
        }
    }

    private IEnumerator ButtonFeedbackRoutine()
    {
        // Flash pure white on click for crisp visual feedback
        if (bgImage != null) bgImage.color = Color.white;
        if (borderImage != null) borderImage.color = Color.white;
        if (buttonText != null) buttonText.color = Color.black;
        if (iconImage != null) iconImage.color = Color.black;

        yield return new WaitForSecondsRealtime(0.12f);

        ApplyHoverVisuals(isHovered);
    }

    public void SetHoverState(bool hovered)
    {
        isHovered = hovered;
        ApplyHoverVisuals(hovered);
    }

    private void ApplyHoverVisuals(bool hovered)
    {
        if (borderImage != null)
            borderImage.color = hovered ? hoverBorderColor : normalBorderColor;

        if (bgImage != null)
            bgImage.color = hovered ? hoverBgColor : normalBgColor;

        if (buttonText != null)
            buttonText.color = hovered ? hoverTextColor : normalTextColor;

        if (iconImage != null)
            iconImage.color = hovered ? hoverIconColor : normalIconColor;
    }

    // --- Interaction Pointer Handlers ---
    public void OnPointerEnter(PointerEventData eventData) => SetHoverState(true);
    public void OnPointerExit(PointerEventData eventData) => SetHoverState(false);
    public void OnPointerClick(PointerEventData eventData) => SelectHotspot();
    private void OnMouseEnter() => SetHoverState(true);
    private void OnMouseExit() => SetHoverState(false);

    /// <summary>
    /// Procedurally generated sharp arrow sprite to avoid font glyph issues.
    /// </summary>
    private static Sprite GetArrowSprite()
    {
        if (arrowSprite != null) return arrowSprite;

        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color clear = new Color(0, 0, 0, 0);
        Color white = Color.white;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float ny = Mathf.Abs((y - 15.5f) / 12f);
                float nx = (x - 7f) / 18f;
                if (nx >= 0f && nx <= 1f && ny <= (1f - nx))
                {
                    tex.SetPixel(x, y, white);
                }
                else
                {
                    tex.SetPixel(x, y, clear);
                }
            }
        }
        tex.Apply();
        arrowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return arrowSprite;
    }

    /// <summary>
    /// Procedural pleasant audio chime click feedback.
    /// </summary>
    private void PlayClickSound()
    {
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0.5f;
            audioSource.playOnAwake = false;
        }

        if (audioSource.clip == null)
        {
            int sampleRate = 44100;
            int samples = sampleRate / 14; // ~71ms
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                data[i] = Mathf.Sin(2f * Mathf.PI * 1150f * (i / (float)sampleRate)) * (1f - t);
            }
            AudioClip clip = AudioClip.Create("ChimeClick", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            audioSource.clip = clip;
        }

        audioSource.volume = 0.6f;
        audioSource.Play();
    }
}
