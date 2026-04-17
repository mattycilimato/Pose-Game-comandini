using UnityEngine;

public class PoseGameManager : MonoBehaviour
{
    public System.Action<int> OnPoseCompleted;
    public System.Action OnSequenceCompleted;
    public System.Action OnGameOver;

    [Header("Sequenza di pose")]
    public PoseDefinition[] posesSequence;

    [Header("Matching")]
    public float matchThreshold = 0.10f;
    public float holdTimeRequired = 1.5f;
    public bool autoAdvanceOnHold = true;
    [Range(0f, 1f)] public float minLandmarkVisibility = 0.5f;
    public int requiredComparedLandmarks = 8;
    public bool useWeightedGameplayMatching = true;

    [Header("Debug")]
    public int currentPoseIndex = 0;
    public float currentPoseMatchTime = 0f;
    public float lastError = float.MaxValue;
    public bool isCurrentPoseMatched = false;
    public bool isGameOver = false;

    PoseLandmarkSerializable[] currentPlayerPoseNormalized;

    public PoseDefinition GetCurrentTargetPose()
    {
        if (posesSequence == null || posesSequence.Length == 0) return null;
        if (currentPoseIndex < 0 || currentPoseIndex >= posesSequence.Length) return null;
        return posesSequence[currentPoseIndex];
    }
    public void UpdateCurrentPlayerPose(PoseLandmarkSerializable[] rawLandmarks)
    {
        if (rawLandmarks == null || rawLandmarks.Length == 0) return;
        currentPlayerPoseNormalized = PoseUtils.NormalizePose(rawLandmarks);
    }

    public void ClearCurrentPlayerPose()
    {
        currentPlayerPoseNormalized = null;
        isCurrentPoseMatched = false;
        currentPoseMatchTime = 0f;
        lastError = float.MaxValue;
    }

    void Update()
    {
        if (isGameOver) return;
        if (posesSequence == null || posesSequence.Length == 0) return;
        if (currentPoseIndex >= posesSequence.Length) return;

        var target = posesSequence[currentPoseIndex];
        if (target == null || target.normalizedLandmarks == null || target.normalizedLandmarks.Length == 0)
            return;

        if (currentPlayerPoseNormalized == null)
        {
            isCurrentPoseMatched = false;
            currentPoseMatchTime = 0f;
            lastError = float.MaxValue;
            return;
        }

        int comparedCount;
        if (useWeightedGameplayMatching)
        {
            lastError = PoseUtils.ComputePoseErrorWeighted(
                target.normalizedLandmarks,
                currentPlayerPoseNormalized,
                PoseUtils.GameplayLandmarkIndices,
                PoseUtils.GameplayLandmarkWeights,
                minLandmarkVisibility,
                out comparedCount);
        }
        else
        {
            lastError = PoseUtils.ComputePoseErrorWeighted(
                target.normalizedLandmarks,
                currentPlayerPoseNormalized,
                null,
                null,
                minLandmarkVisibility,
                out comparedCount);
        }

        isCurrentPoseMatched = comparedCount >= requiredComparedLandmarks && lastError < matchThreshold;

        if (isCurrentPoseMatched)
        {
            currentPoseMatchTime += Time.deltaTime;
            if (autoAdvanceOnHold && currentPoseMatchTime >= holdTimeRequired)
            {
                CompleteCurrentPose();
            }
        }
        else
        {
            currentPoseMatchTime = 0f;
        }
    }

    public bool CompletePoseFromObstacle(int expectedPoseIndex)
    {
        if (isGameOver) return false;
        if (expectedPoseIndex != currentPoseIndex) return false;
        if (!isCurrentPoseMatched) return false;

        CompleteCurrentPose();
        return true;
    }

    public void TriggerGameOver()
    {
        if (isGameOver) return;
        isGameOver = true;
        OnGameOver?.Invoke();
    }

    private void CompleteCurrentPose()
    {
        Debug.Log("Posa " + currentPoseIndex + " completata");
        OnPoseCompleted?.Invoke(currentPoseIndex);

        currentPoseIndex++;
        currentPoseMatchTime = 0f;

        if (currentPoseIndex >= posesSequence.Length)
        {
            OnSequenceCompleted?.Invoke();
        }
    }
}