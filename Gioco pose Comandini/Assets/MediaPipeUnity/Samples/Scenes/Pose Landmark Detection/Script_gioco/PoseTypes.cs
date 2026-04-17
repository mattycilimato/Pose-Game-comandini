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
        if (a == null || b == null) return float.MaxValue;
        if (a.Length != b.Length) return float.MaxValue;

        float sum = 0f;
        int n = a.Length;

        for (int i = 0; i < n; i++)
        {
            sum += Vector3.Distance(a[i].ToVector3(), b[i].ToVector3());
        }

        return sum / n;
    }
}