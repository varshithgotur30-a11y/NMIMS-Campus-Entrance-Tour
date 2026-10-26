using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class VRControllerVisualizer : MonoBehaviour
{
    [Header("Hand Selection")]
    public XRNode handNode = XRNode.RightHand;

    [Header("Visual Colors")]
    public Color bodyColor = new Color(0.14f, 0.15f, 0.18f, 1.0f);   // Matte Dark Charcoal
    public Color accentColor = new Color(0.0f, 0.85f, 1.0f, 1.0f);  // Glowing Cyan LED
    public Color buttonColor = new Color(0.22f, 0.24f, 0.28f, 1.0f); // Button faceplate

    [Header("Laser Beam")]
    public bool showLaserBeam = true;
    public float laserLength = 3.5f;
    public Color laserColor = new Color(0.0f, 0.90f, 1.0f, 0.85f);

    [Header("Desktop / Simulator Fallback")]
    [Tooltip("Offset relative to camera when physical 6DOF controller tracking is idle.")]
    public Vector3 desktopOffset = new Vector3(0.20f, -0.16f, 0.44f);

    public bool IsTracked => isTracked;
    public bool IsTriggerPressed => isTriggerPressed;
    public bool WasTriggerPressedThisFrame => wasTriggerJustPressed;
    public Transform PointerTip => pointerTip != null ? pointerTip : transform;

    private InputDevice xrDevice;
    private bool isTracked = false;
    private bool isTriggerPressed = false;
    private bool wasTriggerPressedLastFrame = false;
    private bool wasTriggerJustPressed = false;
    private Camera mainCamera;
    private Transform modelRoot;
    private Transform pointerTip;
    private LineRenderer laserLine;
    private VideoHotspot lastHoveredHotspot = null;

    private void Awake()
    {
        mainCamera = Camera.main;
        BuildControllerModel();
    }

    private void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        FindXRDevice();
    }

    private void Update()
    {
        if (!xrDevice.isValid)
        {
            FindXRDevice();
        }

        UpdateControllerPose();
        UpdateLaserBeam();
    }

    private void FindXRDevice()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(handNode, devices);
        if (devices.Count > 0 && devices[0].isValid)
        {
            xrDevice = devices[0];
            return;
        }

        // Secondary search by characteristics
        InputDeviceCharacteristics charMask = InputDeviceCharacteristics.Controller | 
            (handNode == XRNode.RightHand ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left);
        InputDevices.GetDevicesWithCharacteristics(charMask, devices);
        if (devices.Count > 0 && devices[0].isValid)
        {
            xrDevice = devices[0];
        }
    }

    private void UpdateControllerPose()
    {
        isTracked = false;
        bool currentTrigger = false;

        // 1. Hardware reading from OpenXR / Meta Quest Touch controller
        if (xrDevice.isValid)
        {
            if (xrDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos) &&
                xrDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
            {
                if (pos.sqrMagnitude > 0.0001f)
                {
                    transform.localPosition = pos;
                    transform.localRotation = rot;
                    isTracked = true;
                }
            }

            // 1. Index Trigger (analog float > 0.18f is standard for responsive clicks)
            if (xrDevice.TryGetFeatureValue(CommonUsages.trigger, out float trigVal) && trigVal > 0.18f)
            {
                currentTrigger = true;
            }
            else if (xrDevice.TryGetFeatureValue(CommonUsages.triggerButton, out bool trig) && trig)
            {
                currentTrigger = true;
            }

            // 2. Face Buttons (A on right, X on left)
            if (!currentTrigger && xrDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool btn) && btn)
            {
                currentTrigger = true;
            }

            // 3. Secondary Buttons (B on right, Y on left)
            if (!currentTrigger && xrDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secBtn) && secBtn)
            {
                currentTrigger = true;
            }

            // 4. Firm Grip squeeze (> 0.5f)
            if (!currentTrigger && xrDevice.TryGetFeatureValue(CommonUsages.grip, out float gripVal) && gripVal > 0.50f)
            {
                currentTrigger = true;
            }
            else if (!currentTrigger && xrDevice.TryGetFeatureValue(CommonUsages.gripButton, out bool gripBtn) && gripBtn)
            {
                currentTrigger = true;
            }
        }

        // Edge detection: only true on the single frame when pressed down
        wasTriggerJustPressed = currentTrigger && !wasTriggerPressedLastFrame;
        wasTriggerPressedLastFrame = currentTrigger;
        isTriggerPressed = currentTrigger;

        // 2. Desktop / Simulator Fallback: Keep controller naturally in hand in front of camera
        if (!isTracked && mainCamera != null)
        {
            Vector3 targetPos = mainCamera.transform.position +
                                mainCamera.transform.right * desktopOffset.x +
                                mainCamera.transform.up * desktopOffset.y +
                                mainCamera.transform.forward * desktopOffset.z;

            // Tilt controller slightly forward-up like holding an Oculus Touch controller
            float tiltSign = handNode == XRNode.RightHand ? -1f : 1f;
            Quaternion targetRot = mainCamera.transform.rotation * Quaternion.Euler(16f, tiltSign * 8f, tiltSign * 2f);

            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 18f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 18f);
        }
    }

    private void UpdateLaserBeam()
    {
        if (laserLine == null || !showLaserBeam) return;

        Vector3 startPos = pointerTip != null ? pointerTip.position : transform.position;
        Vector3 forwardDir = pointerTip != null ? pointerTip.forward : transform.forward;
        Vector3 endPos = startPos + forwardDir * laserLength;

        VideoHotspot hitHotspot = null;

        // 1. Direct Raycast against scene objects
        if (Physics.Raycast(startPos, forwardDir, out RaycastHit hit, 30f))
        {
            endPos = hit.point;
            hitHotspot = hit.collider.GetComponent<VideoHotspot>();
            if (hitHotspot == null) hitHotspot = hit.collider.GetComponentInParent<VideoHotspot>();
        }

        // 2. Conical aim tolerance (detects if user is pointing straight at the button)
        if (hitHotspot == null)
        {
            var all = FindObjectsByType<VideoHotspot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            float bestAngle = 10.0f; // 10 degree cone
            for (int i = 0; i < all.Length; i++)
            {
                Vector3 toH = all[i].transform.position - startPos;
                float angle = Vector3.Angle(forwardDir, toH);
                if (angle < bestAngle && toH.magnitude < 12.0f)
                {
                    bestAngle = angle;
                    hitHotspot = all[i];
                }
            }
        }

        // Visual feedback & hover state
        if (hitHotspot != null)
        {
            hitHotspot.SetHoverState(true);
            lastHoveredHotspot = hitHotspot;
            laserLine.startColor = Color.yellow;
            laserLine.endColor = Color.white;
        }
        else
        {
            if (lastHoveredHotspot != null)
            {
                lastHoveredHotspot.SetHoverState(false);
                lastHoveredHotspot = null;
            }
            laserLine.startColor = laserColor;
            laserLine.endColor = laserColor;
        }

        // Click execution on trigger pull or button press while aiming!
        if (wasTriggerJustPressed && hitHotspot != null)
        {
            hitHotspot.SelectHotspot();
        }

        laserLine.SetPosition(0, startPos);
        laserLine.SetPosition(1, endPos);
    }

    /// <summary>
    /// Builds a sleek Meta Quest / Oculus Touch style controller 3D model.
    /// </summary>
    private void BuildControllerModel()
    {
        modelRoot = new GameObject("ControllerModel").transform;
        modelRoot.SetParent(transform, false);
        modelRoot.localScale = Vector3.one * 1.05f;

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        Material bodyMat = new Material(unlitShader) { color = bodyColor };
        Material accentMat = new Material(unlitShader) { color = accentColor };
        Material btnMat = new Material(unlitShader) { color = buttonColor };

        // 1. Ergonomic Grip Handle
        GameObject grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        grip.name = "Grip";
        grip.transform.SetParent(modelRoot, false);
        grip.transform.localPosition = new Vector3(0f, -0.06f, -0.04f);
        grip.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        grip.transform.localScale = new Vector3(0.032f, 0.065f, 0.036f);
        RemoveCollider(grip);
        ApplyMaterial(grip, bodyMat);

        // 2. Faceplate Top
        GameObject faceplate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        faceplate.name = "Faceplate";
        faceplate.transform.SetParent(modelRoot, false);
        faceplate.transform.localPosition = new Vector3(0f, 0.015f, 0.02f);
        faceplate.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
        faceplate.transform.localScale = new Vector3(0.045f, 0.008f, 0.045f);
        RemoveCollider(faceplate);
        ApplyMaterial(faceplate, btnMat);

        // 3. Thumbstick
        GameObject stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stick.name = "Thumbstick";
        stick.transform.SetParent(modelRoot, false);
        stick.transform.localPosition = new Vector3(0.008f, 0.026f, 0.026f);
        stick.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
        stick.transform.localScale = new Vector3(0.014f, 0.008f, 0.014f);
        RemoveCollider(stick);
        ApplyMaterial(stick, bodyMat);

        // 4. Tracking Sensor Ring (Iconic Meta Touch Ring)
        GameObject ringObj = new GameObject("TrackingRing");
        ringObj.transform.SetParent(modelRoot, false);
        ringObj.transform.localPosition = new Vector3(0f, 0.025f, 0.035f);
        ringObj.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);

        LineRenderer ringLine = ringObj.AddComponent<LineRenderer>();
        ringLine.useWorldSpace = false;
        ringLine.loop = true;
        ringLine.startWidth = 0.008f;
        ringLine.endWidth = 0.008f;
        ringLine.material = accentMat;
        ringLine.startColor = accentColor;
        ringLine.endColor = accentColor;

        int ringSegments = 32;
        ringLine.positionCount = ringSegments;
        float ringRadius = 0.052f;
        for (int i = 0; i < ringSegments; i++)
        {
            float angle = (float)i / ringSegments * Mathf.PI * 2f;
            ringLine.SetPosition(i, new Vector3(Mathf.Cos(angle) * ringRadius, 0f, Mathf.Sin(angle) * ringRadius));
        }

        // 5. Index Trigger
        GameObject trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trigger.name = "Trigger";
        trigger.transform.SetParent(modelRoot, false);
        trigger.transform.localPosition = new Vector3(0f, -0.012f, 0.025f);
        trigger.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
        trigger.transform.localScale = new Vector3(0.016f, 0.022f, 0.014f);
        RemoveCollider(trigger);
        ApplyMaterial(trigger, btnMat);

        // 6. Pointer Tip (emitter point for ray and laser beam)
        GameObject tipObj = new GameObject("PointerTip");
        tipObj.transform.SetParent(modelRoot, false);
        tipObj.transform.localPosition = new Vector3(0f, 0.018f, 0.065f);
        tipObj.transform.localRotation = Quaternion.identity;
        pointerTip = tipObj.transform;

        // 7. Glowing Laser Beam
        GameObject laserObj = new GameObject("LaserBeam");
        laserObj.transform.SetParent(modelRoot, false);
        laserLine = laserObj.AddComponent<LineRenderer>();
        laserLine.useWorldSpace = true;
        laserLine.startWidth = 0.006f;
        laserLine.endWidth = 0.002f;
        Material laserMat = new Material(unlitShader);
        laserLine.material = laserMat;
        laserLine.startColor = laserColor;
        laserLine.endColor = new Color(laserColor.r, laserColor.g, laserColor.b, 0.1f);
        laserLine.positionCount = 2;
    }

    private void RemoveCollider(GameObject obj)
    {
        Collider c = obj.GetComponent<Collider>();
        if (c != null) Destroy(c);
    }

    private void ApplyMaterial(GameObject obj, Material mat)
    {
        MeshRenderer r = obj.GetComponent<MeshRenderer>();
        if (r != null) r.material = mat;
    }
}
