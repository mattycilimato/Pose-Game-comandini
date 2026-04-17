using UnityEngine;
using Mediapipe.Tasks.Vision.PoseLandmarker;           // PoseLandmarkerResult
using Mediapipe.Tasks.Components.Containers;          // NormalizedLandmarks, NormalizedLandmark

public class PoseLandmarkerAdapter : MonoBehaviour
{
    [Header("Destinazioni")]
    public PoseRecorder poseRecorder;        // scena editor pose (puù essere null in gioco)
    public PoseGameManager poseGameManager;  // scena di gioco (puù essere null in editor)

    [Header("Stato pose (ultimo frame)")]
    public PoseLandmarkSerializable[] LatestNormalizedPose { get; private set; }

    [Header("Stabilita tracking")]
    [Range(0f, 1f)] public float smoothingFactor = 0.65f;
    public bool enableSmoothing = true;
    public float poseLostTimeoutSeconds = 0.35f;

    private PoseLandmarkSerializable[] _lastSmoothedRawPose;
    private float _lastPoseSeenTime = -999f;
    private bool _isPoseAvailable;

    private void Update()
    {
        if (!_isPoseAvailable) return;
        if (Time.time - _lastPoseSeenTime < poseLostTimeoutSeconds) return;

        _isPoseAvailable = false;
        LatestNormalizedPose = null;
        _lastSmoothedRawPose = null;

        if (poseGameManager != null)
            poseGameManager.ClearCurrentPlayerPose();
    }

    /// <summary>
    /// Chiamato da PoseLandmarkerRunner, con il risultato grezzo di MediaPipe.
    /// </summary>
    public void OnPoseResult(PoseLandmarkerResult result)
    {
        // 1) Lista di tutte le persone rilevate
        var poses = result.poseLandmarks;        // List<NormalizedLandmarks>

        if (poses == null || poses.Count == 0)
        {
            return;
        }

        // 2) Prima persona
        var firstPose = poses[0];               // NormalizedLandmarks

        // 3) Lista dei punti della prima persona
        var list = firstPose.landmarks;         // List<NormalizedLandmark>

        if (list == null || list.Count == 0)
        {
            return;
        }

        int count = list.Count;
        var rawPose = new PoseLandmarkSerializable[count];

        for (int i = 0; i < count; i++)
        {
            var lm = list[i];                  // tipo NormalizedLandmark (API Tasks)
            var v = new Vector3(lm.x, lm.y, lm.z);

            // In alcune versioni API visibility Ë nullable (float?).
            float visibility = lm.visibility ?? 1f;

            rawPose[i] = new PoseLandmarkSerializable(v, visibility);
        }

        var poseToUse = enableSmoothing ? SmoothPose(rawPose) : rawPose;

        // Consistenza con il gioco: NormalizePose centra sulle anche e scala sulla distanza tra spalle.
        LatestNormalizedPose = PoseUtils.NormalizePose(poseToUse);
        _lastPoseSeenTime = Time.time;
        _isPoseAvailable = true;

        if (poseRecorder != null)
            poseRecorder.UpdateCurrentPose(poseToUse);

        if (poseGameManager != null)
            poseGameManager.UpdateCurrentPlayerPose(poseToUse);
    }

    private PoseLandmarkSerializable[] SmoothPose(PoseLandmarkSerializable[] currentRawPose)
    {
        if (currentRawPose == null || currentRawPose.Length == 0)
            return currentRawPose;

        float a = Mathf.Clamp01(smoothingFactor);
        if (_lastSmoothedRawPose == null || _lastSmoothedRawPose.Length != currentRawPose.Length)
        {
            _lastSmoothedRawPose = (PoseLandmarkSerializable[])currentRawPose.Clone();
            return _lastSmoothedRawPose;
        }

        var smoothed = new PoseLandmarkSerializable[currentRawPose.Length];
        for (int i = 0; i < currentRawPose.Length; i++)
        {
            Vector3 prev = _lastSmoothedRawPose[i].ToVector3();
            Vector3 curr = currentRawPose[i].ToVector3();
            Vector3 filtered = Vector3.Lerp(curr, prev, a);
            float visibility = Mathf.Lerp(currentRawPose[i].visibility, _lastSmoothedRawPose[i].visibility, a);
            smoothed[i] = new PoseLandmarkSerializable(filtered, visibility);
        }

        _lastSmoothedRawPose = smoothed;
        return smoothed;
    }
}