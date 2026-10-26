using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.InputSystem;

[System.Serializable]
public class HotspotDestination
{
    public int targetRoomIndex;
    public string label;
    public Vector3 localPosition;
}

[System.Serializable]
public class RoomTourPlan
{
    public string roomTitle;
    public HotspotDestination[] destinations;
}

public class VideoNavigation : MonoBehaviour
{
    [Header("Core 360 Video Components")]
    [Tooltip("The Unity VideoPlayer that plays the 360 video clips.")]
    public VideoPlayer videoPlayer;

    [Tooltip("List of all 5 360 video clips for the tour.")]
    public VideoClip[] videos;

    [Tooltip("High-resolution RenderTexture used to display the 360 video.")]
    public RenderTexture videoRenderTexture;

    [Header("VR Panoramic Sphere")]
    [Tooltip("The inward-facing sphere displaying the 360 video.")]
    public VRPanoramicSphere panoramicSphere;

    [Header("VR Rig / Camera Offset")]
    [Tooltip("Transform of Camera Offset to allow smooth 360 yaw rotation during desktop and simulator testing.")]
    public Transform cameraOffset;

    [Header("Tour Hotspots")]
    [Tooltip("List of destination hotspot GameObjects in the scene.")]
    public VideoHotspot[] hotspots;

    [Tooltip("The player's interactive cursor hotspot.")]
    public VRPointerHotspot pointerHotspot;

    [Header("Hotspot Visibility")]
    public bool hotspotsVisible = true;
    public bool HotspotsVisible
    {
        get => hotspotsVisible;
        set
        {
            hotspotsVisible = value;
            SetHotspotsActive(hotspotsVisible);
        }
    }

    [Header("Titles & Per-Room Spatial Navigation")]
    public string[] roomTitles = new string[]
    {
        "1. Fountain",
        "2. Garden",
        "3. Stage",
        "4. College Entrance",
        "5. Campus Entrance"
    };

    [Header("Playback & Transition Settings")]
    public bool autoPlayOnStart = true;
    public bool loopVideos = true;
    public float fadeDuration = 0.25f;

    [Header("Editor & Desktop Controls")]
    public bool enableEditorShortcuts = true;
    public float mouseLookSensitivity = 2.2f;

    // Public state & events
    public int CurrentVideoIndex => currentVideo;
    public bool IsTransitioning => isTransitioning;
    public event Action<int> OnRoomChanged;

    private int currentVideo = 0;
    private bool isTransitioning = false;
    private Camera vrCamera;
    private Vector2 mouseLookRotation;
    private RoomTourPlan[] tourPlans;

    private void Awake()
    {
        // 1. Ensure Camera clears to pitch black (NEVER show default procedural skybox)
        RenderSettings.skybox = null;
        vrCamera = Camera.main;
        if (vrCamera != null)
        {
            vrCamera.clearFlags = CameraClearFlags.SolidColor;
            vrCamera.backgroundColor = Color.black;
        }

        // 2. Discover Camera Offset for 360 yaw rotation
        if (cameraOffset == null)
        {
            GameObject offsetObj = GameObject.Find("Camera Offset");
            if (offsetObj != null) cameraOffset = offsetObj.transform;
        }

        // 3. Ensure VR Controllers exist and are visible
        EnsureVRControllers();

        // 4. Discover VideoPlayer
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null) videoPlayer = FindFirstObjectByType<VideoPlayer>();
        }

        // 5. Discover Panoramic Sphere
        if (panoramicSphere == null)
        {
            panoramicSphere = FindFirstObjectByType<VRPanoramicSphere>();
        }

        // 6. Discover Hotspots
        if (hotspots == null || hotspots.Length == 0)
        {
            hotspots = FindObjectsByType<VideoHotspot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        // 7. Ensure the Player Pointer Hotspot exists
        EnsurePointerHotspot();

        // 8. Disable old intrusive HUD if present
        GameObject hudObj = GameObject.Find("VR_TourHUD");
        if (hudObj != null)
        {
            hudObj.SetActive(false);
        }

        // 9. Initialize navigation destinations for all 5 rooms
        roomTitles = new string[]
        {
            "1. Fountain",
            "2. Garden",
            "3. Stage",
            "4. College Entrance",
            "5. Campus Entrance"
        };
        InitializeRoomTourPlans();

        // 10. Connect VideoPlayer to RenderTexture & Material
        ConfigureVideoPipeline();
    }

    private void Start()
    {
        if (autoPlayOnStart && videos != null && videos.Length > 0)
        {
            StartImmediatePlayback(0);
        }
    }

    private void Update()
    {
        HandleEditorDesktopControls();
    }

    /// <summary>
    /// Ensures 3D VR Controllers are attached and visible for both hands.
    /// </summary>
    private void EnsureVRControllers()
    {
        Transform rigParent = cameraOffset != null ? cameraOffset : transform;

        // Disable any template XR ray interactors to prevent rogue clicks
        var allBehaviours = rigParent.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in allBehaviours)
        {
            if (mb != null)
            {
                string tn = mb.GetType().Name;
                if (tn.Contains("XRRayInteractor") || tn.Contains("XRDirectInteractor") || tn.Contains("XRInteractorLineVisual"))
                {
                    mb.enabled = false;
                }
            }
        }

        // 1. Right Controller
        GameObject rightCtrl = GameObject.Find("RightController");
        if (rightCtrl == null)
        {
            rightCtrl = new GameObject("RightController");
            rightCtrl.transform.SetParent(rigParent, false);
        }

        var oldLine = rightCtrl.GetComponent<LineRenderer>();
        if (oldLine != null) oldLine.enabled = false;

        var rightVis = rightCtrl.GetComponent<VRControllerVisualizer>();
        if (rightVis == null)
        {
            rightVis = rightCtrl.AddComponent<VRControllerVisualizer>();
        }
        rightVis.handNode = UnityEngine.XR.XRNode.RightHand;
        rightVis.desktopOffset = new Vector3(0.20f, -0.16f, 0.44f);
        rightVis.showLaserBeam = true;

        // 2. Left Controller
        GameObject leftCtrl = GameObject.Find("LeftController");
        if (leftCtrl == null)
        {
            leftCtrl = new GameObject("LeftController");
            leftCtrl.transform.SetParent(rigParent, false);
        }

        var leftVis = leftCtrl.GetComponent<VRControllerVisualizer>();
        if (leftVis == null)
        {
            leftVis = leftCtrl.AddComponent<VRControllerVisualizer>();
        }
        leftVis.handNode = UnityEngine.XR.XRNode.LeftHand;
        leftVis.desktopOffset = new Vector3(-0.20f, -0.16f, 0.44f);
        leftVis.showLaserBeam = false; // Primary laser on right controller
    }

    /// <summary>
    /// Spawns the player's pointer hotspot if not already in the scene.
    /// </summary>
    private void EnsurePointerHotspot()
    {
        if (pointerHotspot == null)
        {
            pointerHotspot = FindFirstObjectByType<VRPointerHotspot>();
        }

        if (pointerHotspot == null)
        {
            GameObject ptrObj = new GameObject("VR_PointerHotspot");
            pointerHotspot = ptrObj.AddComponent<VRPointerHotspot>();
        }
    }

    /// <summary>
    /// Configures VideoPlayer with high-res RenderTexture output and continuous looping.
    /// </summary>
    public void ConfigureVideoPipeline()
    {
        if (videoPlayer == null) return;

        if (videoRenderTexture == null)
        {
            var rts = Resources.FindObjectsOfTypeAll<RenderTexture>();
            for (int i = 0; i < rts.Length; i++)
            {
                if (rts[i].name.Contains("VideoRenderTexture"))
                {
                    videoRenderTexture = rts[i];
                    break;
                }
            }

            if (videoRenderTexture == null)
            {
                videoRenderTexture = new RenderTexture(3840, 1920, 0, RenderTextureFormat.ARGB32);
                videoRenderTexture.name = "Dynamic_VideoRenderTexture";
            }
        }

        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = videoRenderTexture;
        videoPlayer.isLooping = loopVideos;
        videoPlayer.playOnAwake = true;
        videoPlayer.aspectRatio = VideoAspectRatio.FitInside;

        if (panoramicSphere != null)
        {
            panoramicSphere.SetFadeAmount(0f); // fully visible
            if (panoramicSphere.RuntimeMaterial != null && videoRenderTexture != null)
            {
                panoramicSphere.RuntimeMaterial.mainTexture = videoRenderTexture;
                panoramicSphere.RuntimeMaterial.SetTexture("_BaseMap", videoRenderTexture);
            }
        }
    }

    /// <summary>
    /// Builds physical navigation destinations matching the doorways in each 360 video.
    /// </summary>
    private void InitializeRoomTourPlans()
    {
        tourPlans = new RoomTourPlan[]
        {
            // Room 0: GS010033 (1. Fountain)
            new RoomTourPlan
            {
                roomTitle = roomTitles[0],
                destinations = new HotspotDestination[]
                {
                    // Garden doorway to the right
                    new HotspotDestination { targetRoomIndex = 1, label = "To Garden", localPosition = new Vector3(1.8f, -0.1f, 2.4f) },
                    // Stage corridor to the left
                    new HotspotDestination { targetRoomIndex = 2, label = "To Stage", localPosition = new Vector3(-2.4f, -0.1f, 1.8f) }
                }
            },

            // Room 1: GS010034 (2. Garden)
            new RoomTourPlan
            {
                roomTitle = roomTitles[1],
                destinations = new HotspotDestination[]
                {
                    // Exit back to Fountain behind
                    new HotspotDestination { targetRoomIndex = 0, label = "To Fountain", localPosition = new Vector3(0.0f, -0.1f, -3.0f) },
                    // Door to Stage on the left
                    new HotspotDestination { targetRoomIndex = 2, label = "To Stage", localPosition = new Vector3(-2.8f, -0.1f, 1.4f) },
                    // Pathway to College Entrance on the right
                    new HotspotDestination { targetRoomIndex = 3, label = "To College Entrance", localPosition = new Vector3(2.5f, -0.1f, 1.8f) }
                }
            },

            // Room 2: GS010035 (3. Stage)
            new RoomTourPlan
            {
                roomTitle = roomTitles[2],
                destinations = new HotspotDestination[]
                {
                    // Exit back to Garden
                    new HotspotDestination { targetRoomIndex = 1, label = "To Garden", localPosition = new Vector3(-2.2f, -0.1f, -2.4f) },
                    // Passage to College Entrance
                    new HotspotDestination { targetRoomIndex = 3, label = "To College Entrance", localPosition = new Vector3(2.2f, -0.1f, -2.4f) }
                }
            },

            // Room 3: GS010037 (4. College Entrance)
            new RoomTourPlan
            {
                roomTitle = roomTitles[3],
                destinations = new HotspotDestination[]
                {
                    // Pathway to Garden behind
                    new HotspotDestination { targetRoomIndex = 1, label = "To Garden", localPosition = new Vector3(-0.8f, -0.1f, -3.0f) },
                    // Pathway to Campus Entrance forward-right
                    new HotspotDestination { targetRoomIndex = 4, label = "To Campus Entrance", localPosition = new Vector3(2.8f, -0.1f, 1.8f) }
                }
            },

            // Room 4: GS010040 (5. Campus Entrance)
            new RoomTourPlan
            {
                roomTitle = roomTitles[4],
                destinations = new HotspotDestination[]
                {
                    // Return to College Entrance behind
                    new HotspotDestination { targetRoomIndex = 3, label = "To College Entrance", localPosition = new Vector3(-1.6f, -0.1f, -2.8f) },
                    // Exit to Garden on the left
                    new HotspotDestination { targetRoomIndex = 1, label = "To Garden", localPosition = new Vector3(-2.6f, -0.1f, 1.6f) }
                }
            }
        };
    }

    /// <summary>
    /// Starts initial playback on room 0 (Elevator Lobby) immediately without auto-skipping.
    /// </summary>
    private void StartImmediatePlayback(int index)
    {
        currentVideo = index;
        if (videoPlayer != null && videos != null && index < videos.Length)
        {
            videoPlayer.clip = videos[index];
            videoPlayer.isLooping = loopVideos;
            videoPlayer.Play();
        }

        UpdateHotspotsForRoom(currentVideo);

        if (pointerHotspot != null)
        {
            pointerHotspot.SetCooldown(1.5f);
        }

        OnRoomChanged?.Invoke(currentVideo);
    }

    /// <summary>
    /// Switches to the specified 360 video location with a smooth transition.
    /// </summary>
    public void PlayVideo(int index)
    {
        if (videos == null || videos.Length == 0) return;
        if (index < 0 || index >= videos.Length) return;
        if (isTransitioning) return;

        StartCoroutine(SwitchVideoRoutine(index));
    }

    private IEnumerator SwitchVideoRoutine(int index)
    {
        isTransitioning = true;

        if (pointerHotspot != null)
        {
            pointerHotspot.SetCooldown(2.0f);
        }

        // 1. Smooth Fade Out to black
        if (panoramicSphere != null)
        {
            yield return panoramicSphere.FadeToBlack(fadeDuration);
        }

        // 2. Update current video index
        currentVideo = index;

        // 3. Switch clip and prepare for seamless 360 decoding
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.clip = videos[index];
            videoPlayer.isLooping = loopVideos;
            videoPlayer.Prepare();

            float timer = 0f;
            float timeout = 2.0f;
            while (!videoPlayer.isPrepared && timer < timeout)
            {
                timer += Time.unscaledDeltaTime;
                yield return null;
            }

            videoPlayer.Play();
        }

        // 4. Update Hotspots for the new room
        UpdateHotspotsForRoom(currentVideo);

        // 5. Notify UI & listeners
        OnRoomChanged?.Invoke(currentVideo);

        // 6. Smooth Fade In from black
        if (panoramicSphere != null)
        {
            yield return panoramicSphere.FadeFromBlack(fadeDuration);
        }

        if (pointerHotspot != null)
        {
            pointerHotspot.SetCooldown(1.2f);
        }

        isTransitioning = false;
    }

    /// <summary>
    /// Configures destination hotspots to match the exact doorways in the active 360 video.
    /// </summary>
    public void UpdateHotspotsForRoom(int roomIndex)
    {
        if (hotspots == null || hotspots.Length == 0) return;

        // Deactivate all hotspots first
        for (int i = 0; i < hotspots.Length; i++)
        {
            if (hotspots[i] != null)
            {
                hotspots[i].gameObject.SetActive(false);
            }
        }

        if (!hotspotsVisible) return;
        if (tourPlans == null || roomIndex < 0 || roomIndex >= tourPlans.Length) return;

        RoomTourPlan plan = tourPlans[roomIndex];
        HotspotDestination[] destinations = plan.destinations;

        // Activate and position only the relevant destination portals
        for (int i = 0; i < destinations.Length && i < hotspots.Length; i++)
        {
            VideoHotspot hp = hotspots[i];
            if (hp == null) continue;

            HotspotDestination dest = destinations[i];
            hp.gameObject.SetActive(true);
            hp.targetVideoIndex = dest.targetRoomIndex;
            hp.hotspotTitle = dest.label;
            hp.transform.position = dest.localPosition;
            hp.UpdateLabelText();
            hp.ApplyDimensions();
        }
    }

    public void ToggleHotspots()
    {
        HotspotsVisible = !HotspotsVisible;
    }

    public void SetHotspotsActive(bool active)
    {
        hotspotsVisible = active;
        if (hotspots != null)
        {
            for (int i = 0; i < hotspots.Length; i++)
            {
                if (hotspots[i] != null)
                {
                    hotspots[i].gameObject.SetActive(active);
                }
            }
        }
        if (pointerHotspot != null)
        {
            pointerHotspot.gameObject.SetActive(active);
        }
    }

    // --- Tour Navigation Controls ---

    public void NextRoom()
    {
        if (videos == null || videos.Length == 0) return;
        PlayVideo((currentVideo + 1) % videos.Length);
    }

    public void PreviousRoom()
    {
        if (videos == null || videos.Length == 0) return;
        PlayVideo((currentVideo - 1 + videos.Length) % videos.Length);
    }

    public void TogglePlayPause()
    {
        if (videoPlayer == null) return;
        if (videoPlayer.isPlaying)
            videoPlayer.Pause();
        else
            videoPlayer.Play();
    }

    public void RestartCurrentVideo()
    {
        if (videoPlayer == null) return;
        videoPlayer.time = 0;
        videoPlayer.Play();
    }

    // Direct jump helpers
    public void GoToVideo1() => PlayVideo(0);
    public void GoToVideo2() => PlayVideo(1);
    public void GoToVideo3() => PlayVideo(2);
    public void GoToVideo4() => PlayVideo(3);
    public void GoToVideo5() => PlayVideo(4);

    /// <summary>
    /// Desktop & Play Mode testing controls allowing full 360 horizontal and vertical looking around.
    /// </summary>
    private void HandleEditorDesktopControls()
    {
        if (!enableEditorShortcuts) return;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) PlayVideo(0);
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) PlayVideo(1);
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) PlayVideo(2);
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) PlayVideo(3);
            if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) PlayVideo(4);

            if (kb.spaceKey.wasPressedThisFrame) TogglePlayPause();

            // Keyboard camera rotation
            float rotSpeed = 75f * Time.deltaTime;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) mouseLookRotation.y += rotSpeed;
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) mouseLookRotation.y -= rotSpeed;
            if (kb.upArrowKey.isPressed || kb.wKey.isPressed) mouseLookRotation.x -= rotSpeed;
            if (kb.downArrowKey.isPressed || kb.sKey.isPressed) mouseLookRotation.x += rotSpeed;
        }

        var mouse = Mouse.current;
        if (mouse != null)
        {
            // Allow looking around when dragging right button OR left button
            if (mouse.rightButton.isPressed || mouse.leftButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue() * (mouseLookSensitivity * 0.15f);
                mouseLookRotation.x -= delta.y;
                mouseLookRotation.y += delta.x;
            }
        }

        mouseLookRotation.x = Mathf.Clamp(mouseLookRotation.x, -85f, 85f);

        // Apply pitch (look up/down at ceiling and floor) to vrCamera
        if (vrCamera != null)
        {
            vrCamera.transform.localEulerAngles = new Vector3(mouseLookRotation.x, 0f, 0f);
        }

        // Apply yaw (turn around 360 degrees) to Camera Offset or camera
        if (cameraOffset != null)
        {
            cameraOffset.localEulerAngles = new Vector3(0f, mouseLookRotation.y, 0f);
        }
        else if (vrCamera != null)
        {
            vrCamera.transform.localEulerAngles = new Vector3(mouseLookRotation.x, mouseLookRotation.y, 0f);
        }
    }
}
