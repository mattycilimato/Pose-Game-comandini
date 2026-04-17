using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PoseRecorder : MonoBehaviour
{
    [Header("Debug")]
    public PoseLandmarkSerializable[] currentRawLandmarks;
    public PoseLandmarkSerializable[] currentNormalizedLandmarks;

    public void UpdateCurrentPose(PoseLandmarkSerializable[] landmarks)
    {
        if (landmarks == null || landmarks.Length == 0) return;

        currentRawLandmarks = landmarks;
        currentNormalizedLandmarks = PoseUtils.NormalizePose(currentRawLandmarks);
    }

#if UNITY_EDITOR
    [ContextMenu("Save Current Pose As Asset")]
    public void SaveCurrentPoseAsAsset()
    {
        if (currentNormalizedLandmarks == null || currentNormalizedLandmarks.Length == 0)
        {
            Debug.LogWarning("Nessuna posa corrente da salvare.");
            return;
        }

        string folderPath = "Assets/Poses";
        if (!AssetDatabase.IsValidFolder(folderPath))
            AssetDatabase.CreateFolder("Assets", "Poses");

        string assetPath = EditorUtility.SaveFilePanelInProject(
            "Salva Posa",
            "NewPose",
            "asset",
            "Scegli un nome per la posa",
            folderPath
        );

        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.Log("Salvataggio annullato.");
            return;
        }

        var poseAsset = ScriptableObject.CreateInstance<PoseDefinition>();
        poseAsset.poseName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        poseAsset.normalizedLandmarks = currentNormalizedLandmarks;

        AssetDatabase.CreateAsset(poseAsset, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Posa salvata in: " + assetPath);
    }
#endif
}