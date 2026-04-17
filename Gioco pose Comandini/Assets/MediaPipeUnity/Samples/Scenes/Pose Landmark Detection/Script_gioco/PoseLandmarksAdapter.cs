using UnityEngine;
using Mediapipe.Tasks.Vision.PoseLandmarker;           // PoseLandmarkerResult
using Mediapipe.Tasks.Components.Containers;          // NormalizedLandmarks, NormalizedLandmark

public class PoseLandmarkerAdapter : MonoBehaviour
{
    [Header("Destinazioni")]
    public PoseRecorder poseRecorder;        // scena editor pose (può essere null in gioco)
    public PoseGameManager poseGameManager;  // scena di gioco (può essere null in editor)

    [Header("Stato pose (ultimo frame)")]
    public PoseLandmarkSerializable[] LatestNormalizedPose { get; private set; }

    /// <summary>
    /// Chiamato da PoseLandmarkerRunner, con il risultato grezzo di MediaPipe.
    /// </summary>
    public void OnPoseResult(PoseLandmarkerResult result)
    {
        // 1) Lista di tutte le persone rilevate
        var poses = result.poseLandmarks;        // List<NormalizedLandmarks>

        if (poses == null || poses.Count == 0)
            return;

        // 2) Prima persona
        var firstPose = poses[0];               // NormalizedLandmarks

        // 3) Lista dei punti della prima persona
        var list = firstPose.landmarks;         // List<NormalizedLandmark>

        if (list == null || list.Count == 0)
            return;

        int count = list.Count;
        var serialized = new PoseLandmarkSerializable[count];

        for (int i = 0; i < count; i++)
        {
            var lm = list[i];                  // tipo NormalizedLandmark (API Tasks)
            var v = new Vector3(lm.x, lm.y, lm.z);

            // Non usiamo HasVisibility per evitare problemi di tipo:
            float visibility = 1f;

            serialized[i] = new PoseLandmarkSerializable(v, visibility);
        }

        // Consistenza con il gioco: NormalizePose centra sulle anche e scala sulla distanza tra spalle.
        LatestNormalizedPose = PoseUtils.NormalizePose(serialized);

        if (poseRecorder != null)
            poseRecorder.UpdateCurrentPose(serialized);

        if (poseGameManager != null)
            poseGameManager.UpdateCurrentPlayerPose(serialized);
    }
}