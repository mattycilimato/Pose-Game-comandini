using UnityEngine;

/// <summary>
/// Muove un avatar Humanoid (Mecanim) usando IK, basandosi sulle pose landmark di MediaPipe.
/// Mapping: hip-center -> radice avatar, spalle/anche -> scala/ancoraggio.
/// </summary>
public class WebcamPoseHumanoidIK : MonoBehaviour
{
    [Header("Input (MediaPipe)")]
    public PoseLandmarkerAdapter poseSource;

    [Header("Avatar")]
    public Animator animator;
    public Transform hipAnchor;                 // punto di ancoraggio (es. pelvis / root)
    public Transform rootToRotate;            // dove applicare la rotazione "yaw" (opzionale)

    [Header("Pose Space")]
    public Transform poseSpace;              // in genere la Camera usata per la webcam
    public float shoulderWidthMeters = 0.40f; // dopo NormalizePose: shoulderDist ~ 1
    [Tooltip("Moltiplicatore profondita' (asse Z) solo per le braccia. -1 inverte avanti/indietro.")]
    public float armDepthMultiplier = -1f;

    [Header("Conversion")]
    public bool invertY = true;             // MediaPipe y cresce verso il basso => Unity verso l'alto
    [Tooltip("Inverte l'asse X della posa (utile con webcam mirrorata).")]
    public bool mirrorPoseX = true;
    public float zSign = -1f;              // spesso serve 1/-1 in base a come MediaPipe esprime la profondita'
    public float ikWeight = 1f;            // peso IK (0..1)
    public float rotateSmoothing = 12f;   // smoothing rotazione
    [Tooltip("Se attivo, allo start il modello viene ruotato di 180° (di spalle).")]
    public bool spawnBackFacing = true;
    [Tooltip("Inverte la direzione dello yaw se la webcam e' mirrorata.")]
    public bool invertBodyYaw = false;
    [Tooltip("Soglia minima di ampiezza torso per aggiornare la rotazione.")]
    public float minTorsoYawSignal = 0.02f;

    [Header("Controllo animazione base")]
    [Tooltip("Se attivo, congela la timeline del controller animazioni (così sparisce la camminata di default).")]
    public bool freezeBaseAnimation = true;

    [Tooltip("Disabilita il root motion, così l'avatar non “cammina” per via della root transform dell'animazione.")]
    public bool disableRootMotion = true;

    // Targets in world space (calcolati da LatestNormalizedPose)
    private Vector3 _leftHandTarget;
    private Vector3 _rightHandTarget;
    private Vector3 _leftFootTarget;
    private Vector3 _rightFootTarget;

    private Vector3 _leftElbowHint;
    private Vector3 _rightElbowHint;
    private Vector3 _leftKneeHint;
    private Vector3 _rightKneeHint;

    private Vector3 _torsoRightWorld; // asse destro torso per la rotazione yaw
    private bool _initialFacingApplied;

    private void Reset()
    {
        animator = GetComponentInChildren<Animator>();
        hipAnchor = transform;
        rootToRotate = transform;
    }

    private void OnEnable()
    {
        ApplyAnimatorBaseControl();
    }

    private void Start()
    {
        // Serve se l'Animator viene inizializzato dopo di noi.
        ApplyAnimatorBaseControl();
        ApplyInitialFacing();
    }

    private void ApplyInitialFacing()
    {
        if (_initialFacingApplied) return;
        if (!spawnBackFacing) return;

        if (rootToRotate == null)
            rootToRotate = transform;

        rootToRotate.rotation = Quaternion.AngleAxis(180f, Vector3.up) * rootToRotate.rotation;
        _initialFacingApplied = true;
    }

    private void ApplyAnimatorBaseControl()
    {
        if (animator == null) return;

        if (disableRootMotion)
            animator.applyRootMotion = false;

        if (freezeBaseAnimation)
        {
            animator.speed = 0f;
            // Forza una valutazione immediata per non restare a "frame vecchi".
            animator.Update(0f);
        }
    }

    private void LateUpdate()
    {
        if (poseSource == null || animator == null) return;
        if (!animator.isHuman) return;
        if (poseSource.LatestNormalizedPose == null || poseSource.LatestNormalizedPose.Length == 0) return;

        // In assenza di poseSpace, assumiamo una "poseSpace" allineata al mondo.
        if (poseSpace == null)
            poseSpace = Camera.main != null ? Camera.main.transform : transform;

        if (hipAnchor == null)
            hipAnchor = transform;

        if (rootToRotate == null)
            rootToRotate = transform;

        // Local pose: hip-centered, scaled by NormalizePose.
        // Indici MediaPipe Pose:
        // 11 left shoulder, 12 right shoulder, 13 left elbow, 14 right elbow, 15 left wrist, 16 right wrist
        // 23 left hip, 24 right hip, 25 left knee, 26 right knee, 27 left ankle, 28 right ankle
        // Nota: per i piedi usiamo ankle; puoi passare a foot index (29/30) se hai landmark affidabili.
        PoseLandmarkSerializable lmLeftShoulder = poseSource.LatestNormalizedPose[11];
        PoseLandmarkSerializable lmRightShoulder = poseSource.LatestNormalizedPose[12];
        PoseLandmarkSerializable lmLeftElbow = poseSource.LatestNormalizedPose[13];
        PoseLandmarkSerializable lmRightElbow = poseSource.LatestNormalizedPose[14];
        PoseLandmarkSerializable lmLeftWrist = poseSource.LatestNormalizedPose[15];
        PoseLandmarkSerializable lmRightWrist = poseSource.LatestNormalizedPose[16];
        PoseLandmarkSerializable lmLeftHip = poseSource.LatestNormalizedPose[23];
        PoseLandmarkSerializable lmRightHip = poseSource.LatestNormalizedPose[24];
        PoseLandmarkSerializable lmLeftKnee = poseSource.LatestNormalizedPose[25];
        PoseLandmarkSerializable lmRightKnee = poseSource.LatestNormalizedPose[26];
        PoseLandmarkSerializable lmLeftAnkle = poseSource.LatestNormalizedPose[27];
        PoseLandmarkSerializable lmRightAnkle = poseSource.LatestNormalizedPose[28];

        Vector3 leftShoulderPose = PoseToWorldOffset(lmLeftShoulder);
        Vector3 rightShoulderPose = PoseToWorldOffset(lmRightShoulder);
        Vector3 leftHipPose = PoseToWorldOffset(lmLeftHip);
        Vector3 rightHipPose = PoseToWorldOffset(lmRightHip);
        Vector3 shoulderSpan = rightShoulderPose - leftShoulderPose;
        Vector3 hipSpan = rightHipPose - leftHipPose;
        _torsoRightWorld = (shoulderSpan + hipSpan) * 0.5f;

        _leftHandTarget = hipAnchor.position + PoseToWorldOffset(lmLeftWrist, armDepthMultiplier);
        _rightHandTarget = hipAnchor.position + PoseToWorldOffset(lmRightWrist, armDepthMultiplier);

        _leftElbowHint = hipAnchor.position + PoseToWorldOffset(lmLeftElbow, armDepthMultiplier);
        _rightElbowHint = hipAnchor.position + PoseToWorldOffset(lmRightElbow, armDepthMultiplier);

        _leftFootTarget = hipAnchor.position + PoseToWorldOffset(lmLeftAnkle);
        _rightFootTarget = hipAnchor.position + PoseToWorldOffset(lmRightAnkle);

        _leftKneeHint = hipAnchor.position + PoseToWorldOffset(lmLeftKnee);
        _rightKneeHint = hipAnchor.position + PoseToWorldOffset(lmRightKnee);

        // Rotazione yaw (solo orientamento sinistra/destra) per far "guardare" il corpo verso la posa.
        RotateByShouldersYaw();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || !animator.isHuman) return;
        if (ikWeight <= 0f) return;

        float w = Mathf.Clamp01(ikWeight);

        // Mani
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
        animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, w);
        animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, w);

        animator.SetIKPosition(AvatarIKGoal.LeftHand, _leftHandTarget);
        animator.SetIKPosition(AvatarIKGoal.RightHand, _rightHandTarget);
        animator.SetIKHintPosition(AvatarIKHint.LeftElbow, _leftElbowHint);
        animator.SetIKHintPosition(AvatarIKHint.RightElbow, _rightElbowHint);

        // Piedi
        animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, w);
        animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, w);
        animator.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, w);
        animator.SetIKHintPositionWeight(AvatarIKHint.RightKnee, w);

        animator.SetIKPosition(AvatarIKGoal.LeftFoot, _leftFootTarget);
        animator.SetIKPosition(AvatarIKGoal.RightFoot, _rightFootTarget);
        animator.SetIKHintPosition(AvatarIKHint.LeftKnee, _leftKneeHint);
        animator.SetIKHintPosition(AvatarIKHint.RightKnee, _rightKneeHint);
    }

    private Vector3 PoseToWorldOffset(PoseLandmarkSerializable lm, float localZMul = 1f)
    {
        // Pose: hip-centered, y in MediaPipe va "down" => invertY per Unity.
        float x = mirrorPoseX ? -lm.x : lm.x;
        float y = invertY ? -lm.y : lm.y;
        float z = lm.z * zSign * localZMul;

        Vector3 poseLocal = new Vector3(x, y, z) * shoulderWidthMeters;
        return poseSpace.TransformVector(poseLocal);
    }

    private void RotateByShouldersYaw()
    {
        if (rootToRotate == null) return;
        if (_torsoRightWorld.sqrMagnitude < minTorsoYawSignal * minTorsoYawSignal) return;

        Vector3 desiredRight = Vector3.ProjectOnPlane(_torsoRightWorld.normalized, Vector3.up);
        if (desiredRight.sqrMagnitude < 0.000001f) return;
        desiredRight.Normalize();

        if (invertBodyYaw)
            desiredRight = -desiredRight;

        Vector3 forwardA = Vector3.Cross(desiredRight, Vector3.up).normalized; // destra->avanti
        Vector3 forwardB = -forwardA;

        if (forwardA.sqrMagnitude < 0.000001f) return;

        Vector3 referenceForward = Vector3.ProjectOnPlane(rootToRotate.forward, Vector3.up);
        if (referenceForward.sqrMagnitude < 0.000001f)
            referenceForward = poseSpace != null ? Vector3.ProjectOnPlane(poseSpace.forward, Vector3.up) : Vector3.forward;
        if (referenceForward.sqrMagnitude < 0.000001f)
            referenceForward = Vector3.forward;
        referenceForward.Normalize();

        // Evita flip di 180°: sceglie il forward piu vicino a quello attuale.
        Vector3 desiredForward = Vector3.Dot(referenceForward, forwardA) >= Vector3.Dot(referenceForward, forwardB)
            ? forwardA
            : forwardB;

        Quaternion targetRot = Quaternion.LookRotation(desiredForward, Vector3.up);
        rootToRotate.rotation = Quaternion.Slerp(rootToRotate.rotation, targetRot, rotateSmoothing * Time.deltaTime);
    }
}

