using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;

public static class RelinkTourVideos
{
    private static readonly string[] VideoPathsH264 = new string[]
    {
        "Assets/Videos/GS010008_1_h264.mp4", // Fountain
        "Assets/Videos/GS010019_2_h264.mp4", // Garden
        "Assets/Videos/GS010024_3_h264.mp4", // Stage
        "Assets/Videos/GS010027_4_h264.mp4", // College Entrance
        "Assets/Videos/GS010029_5_h264.mp4"  // Campus Entrance
    };

    private static readonly string[] VideoPathsOriginal = new string[]
    {
        "Assets/Videos/GS010008_1.mp4",
        "Assets/Videos/GS010019_2.mp4",
        "Assets/Videos/GS010024_3.mp4",
        "Assets/Videos/GS010027_4.mp4",
        "Assets/Videos/GS010029_5.mp4"
    };

    [MenuItem("VR Tour/Relink Videos")]
    public static void RelinkVideos()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
        {
            scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }

        VideoNavigation nav = Object.FindFirstObjectByType<VideoNavigation>();
        if (nav == null)
        {
            Debug.LogError("[RelinkTourVideos] Could not find VideoNavigation component in SampleScene!");
            return;
        }

        SerializedObject so = new SerializedObject(nav);
        SerializedProperty videosProp = so.FindProperty("videos");
        if (videosProp == null)
        {
            Debug.LogError("[RelinkTourVideos] SerializedProperty 'videos' not found on VideoNavigation!");
            return;
        }

        videosProp.arraySize = 5;

        for (int i = 0; i < 5; i++)
        {
            VideoClip clip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPathsH264[i]);
            if (clip == null)
            {
                clip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPathsOriginal[i]);
            }

            if (clip == null)
            {
                Debug.LogError($"[RelinkTourVideos] Failed to load VideoClip for Element {i}. Checked {VideoPathsH264[i]} and {VideoPathsOriginal[i]}. Has it finished importing?");
                return;
            }

            videosProp.GetArrayElementAtIndex(i).objectReferenceValue = clip;
            Debug.Log($"[RelinkTourVideos] Assigned Element {i} -> {clip.name} ({AssetDatabase.GetAssetPath(clip)})");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(nav);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[RelinkTourVideos] Successfully relinked all 5 video clips to VideoNavigation and saved SampleScene!");
    }
}
