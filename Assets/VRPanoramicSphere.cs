using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VRPanoramicSphere : MonoBehaviour
{
    [Header("Sphere Settings")]
    [Tooltip("Radius of the panoramic sphere (default 50 units)")]
    public float sphereRadius = 50f;

    [Tooltip("The VR Camera to center the sphere on (defaults to Camera.main)")]
    public Transform targetCamera;

    [Header("Material & Fade Settings")]
    public Material panoramicMaterial;
    public Color normalTint = Color.white;
    public Color fadeColor = Color.black;
    public float defaultFadeDuration = 0.4f;

    private MeshRenderer meshRenderer;
    private Material runtimeMaterial;
    private Coroutine currentFadeCoroutine;

    private static readonly int ColorProp = Shader.PropertyToID("_Color");
    private static readonly int BaseMapProp = Shader.PropertyToID("_BaseMap");

    public MeshRenderer Renderer => meshRenderer != null ? meshRenderer : (meshRenderer = GetComponent<MeshRenderer>());
    public Material RuntimeMaterial => runtimeMaterial;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();

        if (panoramicMaterial != null)
        {
            runtimeMaterial = new Material(panoramicMaterial);
            meshRenderer.material = runtimeMaterial;
        }
        else if (meshRenderer.sharedMaterial != null)
        {
            runtimeMaterial = meshRenderer.material;
        }
        else
        {
            Shader s = Shader.Find("VR/Panoramic360Unlit");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Texture");
            runtimeMaterial = new Material(s);
            runtimeMaterial.SetFloat("_FlipX", 1f);
            meshRenderer.material = runtimeMaterial;
        }

        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }

        EnsureInvertedSphereMesh();
    }

    private void Start()
    {
        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }

        CenterOnCamera();
        SetFadeAmount(0f); // Fully visible initially
    }

    private void LateUpdate()
    {
        CenterOnCamera();
    }

    public void CenterOnCamera()
    {
        if (targetCamera != null)
        {
            transform.position = targetCamera.position;
        }
    }

    /// <summary>
    /// Fades the 360 video to black over the given duration.
    /// </summary>
    public Coroutine FadeToBlack(float duration = -1f, Action onComplete = null)
    {
        if (duration < 0f) duration = defaultFadeDuration;
        if (currentFadeCoroutine != null) StopCoroutine(currentFadeCoroutine);
        currentFadeCoroutine = StartCoroutine(FadeRoutine(0f, 1f, duration, onComplete));
        return currentFadeCoroutine;
    }

    /// <summary>
    /// Fades the 360 video from black back to full visibility.
    /// </summary>
    public Coroutine FadeFromBlack(float duration = -1f, Action onComplete = null)
    {
        if (duration < 0f) duration = defaultFadeDuration;
        if (currentFadeCoroutine != null) StopCoroutine(currentFadeCoroutine);
        currentFadeCoroutine = StartCoroutine(FadeRoutine(1f, 0f, duration, onComplete));
        return currentFadeCoroutine;
    }

    private IEnumerator FadeRoutine(float startFade, float endFade, float duration, Action onComplete)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float current = Mathf.Lerp(startFade, endFade, t);
            SetFadeAmount(current);
            yield return null;
        }

        SetFadeAmount(endFade);
        currentFadeCoroutine = null;
        onComplete?.Invoke();
    }

    /// <summary>
    /// Sets fade amount: 0 = fully normal/visible, 1 = completely black
    /// </summary>
    public void SetFadeAmount(float amount)
    {
        Color c = Color.Lerp(normalTint, fadeColor, Mathf.Clamp01(amount));
        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetColor(ColorProp, c);
        }
        else if (meshRenderer != null && meshRenderer.material != null)
        {
            meshRenderer.material.SetColor(ColorProp, c);
        }
    }

    /// <summary>
    /// Ensures a high quality inverted UV sphere mesh is assigned.
    /// </summary>
    private void EnsureInvertedSphereMesh()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null) return;

        // If no mesh or default sphere, we ensure inward normals / scale
        if (mf.sharedMesh == null)
        {
            GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mf.sharedMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tempSphere);
        }

        // Setting scale to sphereRadius ensures large coverage
        transform.localScale = new Vector3(sphereRadius, sphereRadius, sphereRadius);
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
        }
    }
}
