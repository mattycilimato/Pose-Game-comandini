using UnityEngine;

[System.Serializable]
public struct PoseLandmarkSerializable
{
    public float x;
    public float y;
    public float z;
    public float visibility;

    public Vector3 ToVector3() => new Vector3(x, y, z);

    public PoseLandmarkSerializable(Vector3 v, float visibility = 1f)
    {
        x = v.x;
        y = v.y;
        z = v.z;
        this.visibility = visibility;
    }
}

public static class PoseUtils
{
    // Indici MediaPipe standard
    public const int LEFT_SHOULDER = 11;
    public const int RIGHT_SHOULDER = 12;
    public const int LEFT_HIP = 23;
    public const int RIGHT_HIP = 24;
    public const int LEFT_ELBOW = 13;
    public const int RIGHT_ELBOW = 14;
    public const int LEFT_WRIST = 15;
    public const int RIGHT_WRIST = 16;
    public const int LEFT_KNEE = 25;
    public const int RIGHT_KNEE = 26;
    public const int LEFT_ANKLE = 27;
    public const int RIGHT_ANKLE = 28;

    // Subset principale per il matching gameplay.
    public static readonly int[] GameplayLandmarkIndices =
    {
        LEFT_SHOULDER, RIGHT_SHOULDER,
        LEFT_ELBOW, RIGHT_ELBOW,
        LEFT_WRIST, RIGHT_WRIST,
        LEFT_HIP, RIGHT_HIP,
        LEFT_KNEE, RIGHT_KNEE,
        LEFT_ANKLE, RIGHT_ANKLE
    };

    // Pesi associati agli indici sopra (stessa lunghezza).
    public static readonly float[] GameplayLandmarkWeights =
    {
        1.2f, 1.2f, // shoulders
        1.3f, 1.3f, // elbows
        1.5f, 1.5f, // wrists
        1.2f, 1.2f, // hips
        1.1f, 1.1f, // knees
        1.0f, 1.0f  // ankles
    };

    public static PoseLandmarkSerializable[] NormalizePose(PoseLandmarkSerializable[] input)
    {
        if (input == null || input.Length == 0)
            return input;

        var result = new PoseLandmarkSerializable[input.Length];

        Vector3 leftHip = input[LEFT_HIP].ToVector3();
        Vector3 rightHip = input[RIGHT_HIP].ToVector3();
        Vector3 hipCenter = (leftHip + rightHip) * 0.5f;

        Vector3 leftShoulder = input[LEFT_SHOULDER].ToVector3();
        Vector3 rightShoulder = input[RIGHT_SHOULDER].ToVector3();
        float shoulderDist = Vector3.Distance(leftShoulder, rightShoulder);
        float scale = shoulderDist > 0.0001f ? shoulderDist : 1f;

        for (int i = 0; i < input.Length; i++)
        {
            Vector3 v = input[i].ToVector3();
            Vector3 normalized = (v - hipCenter) / scale;
            result[i] = new PoseLandmarkSerializable(normalized, input[i].visibility);
        }

        return result;
    }

    public static float ComputePoseError(
        PoseLandmarkSerializable[] a,
        PoseLandmarkSerializable[] b)
    {
        int compared;
        return ComputePoseErrorWeighted(
            a,
            b,
            null,
            null,
            0f,
            out compared);
    }

    public static float ComputePoseErrorWeighted(
        PoseLandmarkSerializable[] a,
        PoseLandmarkSerializable[] b,
        int[] indices,
        float[] weights,
        float minVisibility,
        out int comparedCount)
    {
        comparedCount = 0;

        if (a == null || b == null) return float.MaxValue;
        if (a.Length == 0 || b.Length == 0) return float.MaxValue;

        float weightedDistanceSum = 0f;
        float weightsSum = 0f;

        if (indices == null || indices.Length == 0)
        {
            int n = Mathf.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i].visibility < minVisibility || b[i].visibility < minVisibility) continue;

                weightedDistanceSum += Vector3.Distance(a[i].ToVector3(), b[i].ToVector3());
                weightsSum += 1f;
                comparedCount++;
            }
        }
        else
        {
            for (int i = 0; i < indices.Length; i++)
            {
                int idx = indices[i];
                if (idx < 0 || idx >= a.Length || idx >= b.Length) continue;
                if (a[idx].visibility < minVisibility || b[idx].visibility < minVisibility) continue;

                float w = (weights != null && i < weights.Length) ? Mathf.Max(0.0001f, weights[i]) : 1f;
                weightedDistanceSum += Vector3.Distance(a[idx].ToVector3(), b[idx].ToVector3()) * w;
                weightsSum += w;
                comparedCount++;
            }
        }

        if (weightsSum <= 0f || comparedCount == 0) return float.MaxValue;
        return weightedDistanceSum / weightsSum;
    }
}