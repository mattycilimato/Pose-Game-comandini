using UnityEngine;

[CreateAssetMenu(menuName = "PoseGame/PoseDefinition")]
public class PoseDefinition : ScriptableObject
{
    public string poseName;
    [TextArea] public string description;

    // Pose GIÀ normalizzata
    public PoseLandmarkSerializable[] normalizedLandmarks;
}