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
    public bool autoCalibrateShoulderWidth = true;
    public float shoulderWidthCalibrationMultiplier = 1f;
    [Tooltip("Scala generale degli offset della posa dopo la normalizzazione.")]
    public float poseScale = 1f;
    [Tooltip("Moltiplicatore asse X (movimenti laterali).")]
    public float poseScaleX = 1f;
    [Tooltip("Moltiplicatore asse Y (alzare/abbassare arti).")]
    public float poseScaleY = 1f;
    [Tooltip("Moltiplicatore asse Z (avanti/indietro).")]
    public float poseScaleZ = 1f;
    [Tooltip("Boost aggiuntivo sulle gambe in laterale/verticale.")]
    public float legLateralBoost = 1.25f;
    public float legVerticalBoost = 1.6f;
    [Tooltip("Boost aggiuntivo sulle braccia in laterale/verticale.")]
    public float armLateralBoost = 1.1f;
    public float armVerticalBoost = 1.15f;

    [Header("Conversion")]
    public bool invertY = true;             // MediaPipe y cresce verso il basso => Unity verso l'alto
    public float zSign = -1f;              // spesso serve 1/-1 in base a come MediaPipe esprime la profondita'
    public float ikWeight = 1f;            // peso IK (0..1)
    public float rotateSmoothing = 12f;   // smoothing rotazione
    public bool enableBodyYawRotation = false; // evita che l'avatar si giri automaticamente
    [Tooltip("Bilanciamento stima yaw: 1=solo spalle, 0=solo anche.")]
    [Range(0f, 1f)] public float shoulderYawBlend = 0.75f;
    [Tooltip("Soglia minima per aggiornare la rotazione, evita jitter.")]
    public float yawDeadzoneDegrees = 2f;
    [Tooltip("Offset manuale yaw in gradi.")]
    public float yawOffsetDegrees = 0f;
    [Tooltip("Calibra automaticamente l'offset yaw quando parte la scena.")]
    public bool autoCalibrateYawOffset = true;

    [Header("IK Stabilita")]
    [Range(0f, 1f)] public float minVisibilityForIK = 0.45f;
    [Range(0f, 1f)] public float minVisibilityForLowerBody = 0.2f;
    public bool holdLastValidTargets = true;
    public float ikTargetSmoothing = 18f;
    public float ikHintSmoothing = 22f;
    public bool enableAdaptiveSmoothing = true;
    public float fastMotionSpeed = 2.0f;
    public float fastMotionSmoothing = 10f;
    public float slowMotionSmoothing = 24f;

    [Header("IK Vincoli Anatomici")]
    [Tooltip("Se attivo limita i target alla lunghezza arti. Disattivalo per massima ampiezza.")]
    public bool clampReachTargets = true;
    public float maxArmReachMultiplier = 1.05f;
    public float maxLegReachMultiplier = 1.08f;
    [Tooltip("Limite hard anti-esplosione target (sempre attivo, anche con clampReachTargets OFF).")]
    public float maxArmReachHardMultiplier = 1.35f;
    public float maxLegReachHardMultiplier = 1.35f;
    [Tooltip("Amplifica quanto i target mano si allontanano dalla spalla.")]
    public float handReachGain = 1.25f;
    [Tooltip("Amplifica quanto i target piede si allontanano dall'anca.")]
    public float footReachGain = 1.35f;
    [Tooltip("Evita target di mani/piedi troppo dietro il busto (causa pose bloccate).")]
    public bool preventLimbBehindBody = true;
    public float maxArmBackwardMeters = 0.08f;
    public float maxLegBackwardMeters = 0.04f;

    [Header("Pole Hint Stabilita")]
    public float armHintSideOffset = 0.10f;
    public float armHintForwardOffset = 0.08f;
    public float legHintSideOffset = 0.08f;
    public float legHintForwardOffset = 0.10f;
    [Range(0f, 1f)] public float landmarkHintBlend = 0.35f;

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

    private Vector3 _shoulderSpanWorld; // per la rotazione yaw
    private bool _targetsInitialized;
    private bool _yawCalibrated;
    private float _autoYawOffsetDegrees;
    private bool _previousClampReachTargets;

    private float _leftArmReach = 0.55f;
    private float _rightArmReach = 0.55f;
    private float _leftLegReach = 0.90f;
    private float _rightLegReach = 0.90f;

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
        TryAutoCalibrateShoulderWidth();
        CacheLimbReachFromAvatar();
        _previousClampReachTargets = clampReachTargets;
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

    private void TryAutoCalibrateShoulderWidth()
    {
        if (!autoCalibrateShoulderWidth || animator == null || !animator.isHuman) return;

        Transform leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (leftUpperArm == null || rightUpperArm == null) return;

        float distance = Vector3.Distance(leftUpperArm.position, rightUpperArm.position);
        if (distance <= 0.0001f) return;

        shoulderWidthMeters = distance * Mathf.Max(0.1f, shoulderWidthCalibrationMultiplier);
    }

    private void CacheLimbReachFromAvatar()
    {
        if (animator == null || !animator.isHuman) return;

        _leftArmReach = Mathf.Max(0.1f, ComputeChainLength(
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand));
        _rightArmReach = Mathf.Max(0.1f, ComputeChainLength(
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand));
        _leftLegReach = Mathf.Max(0.2f, ComputeChainLength(
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot));
        _rightLegReach = Mathf.Max(0.2f, ComputeChainLength(
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot));
    }

    private float ComputeChainLength(HumanBodyBones a, HumanBodyBones b, HumanBodyBones c)
    {
        Transform ta = animator.GetBoneTransform(a);
        Transform tb = animator.GetBoneTransform(b);
        Transform tc = animator.GetBoneTransform(c);
        if (ta == null || tb == null || tc == null) return 0f;
        return Vector3.Distance(ta.position, tb.position) + Vector3.Distance(tb.position, tc.position);
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

        if (_previousClampReachTargets != clampReachTargets)
        {
            // Reset dei target quando si cambia modalità clamp:
            // evita trascinarsi valori "esplosi" presi con clamp disattivo.
            _targetsInitialized = false;
            _previousClampReachTargets = clampReachTargets;
        }

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
        Vector3 leftShoulderPose = PoseToWorldOffset(lmLeftShoulder, 1f, 1f, 1f);
        Vector3 rightShoulderPose = PoseToWorldOffset(lmRightShoulder, 1f, 1f, 1f);
        Vector3 leftHipPose = PoseToWorldOffset(lmLeftHip, 1f, 1f, 1f);
        Vector3 rightHipPose = PoseToWorldOffset(lmRightHip, 1f, 1f, 1f);
        Vector3 shoulderSpan = (rightShoulderPose - leftShoulderPose);
        Vector3 hipSpan = (rightHipPose - leftHipPose);
        _shoulderSpanWorld = Vector3.Lerp(hipSpan, shoulderSpan, shoulderYawBlend); // world-space offset vector

        Vector3 desiredLeftHandTarget = ResolveTargetFromLandmark(lmLeftWrist, _leftHandTarget, armLateralBoost, armVerticalBoost, 1f, minVisibilityForIK);
        Vector3 desiredRightHandTarget = ResolveTargetFromLandmark(lmRightWrist, _rightHandTarget, armLateralBoost, armVerticalBoost, 1f, minVisibilityForIK);
        Vector3 desiredLeftFootTarget = ResolveTargetFromLandmark(lmLeftAnkle, _leftFootTarget, legLateralBoost, legVerticalBoost, 1f, minVisibilityForLowerBody);
        Vector3 desiredRightFootTarget = ResolveTargetFromLandmark(lmRightAnkle, _rightFootTarget, legLateralBoost, legVerticalBoost, 1f, minVisibilityForLowerBody);

        Vector3 leftShoulderAnchor = GetBonePosition(HumanBodyBones.LeftUpperArm, hipAnchor.position);
        Vector3 rightShoulderAnchor = GetBonePosition(HumanBodyBones.RightUpperArm, hipAnchor.position);
        Vector3 leftHipAnchor = GetBonePosition(HumanBodyBones.LeftUpperLeg, hipAnchor.position);
        Vector3 rightHipAnchor = GetBonePosition(HumanBodyBones.RightUpperLeg, hipAnchor.position);

        desiredLeftHandTarget = ScaleFromAnchor(desiredLeftHandTarget, leftShoulderAnchor, handReachGain);
        desiredRightHandTarget = ScaleFromAnchor(desiredRightHandTarget, rightShoulderAnchor, handReachGain);
        desiredLeftFootTarget = ScaleFromAnchor(desiredLeftFootTarget, leftHipAnchor, footReachGain);
        desiredRightFootTarget = ScaleFromAnchor(desiredRightFootTarget, rightHipAnchor, footReachGain);

        if (preventLimbBehindBody)
        {
            desiredLeftHandTarget = LimitBackwardTarget(desiredLeftHandTarget, leftShoulderAnchor, maxArmBackwardMeters);
            desiredRightHandTarget = LimitBackwardTarget(desiredRightHandTarget, rightShoulderAnchor, maxArmBackwardMeters);
            desiredLeftFootTarget = LimitBackwardTarget(desiredLeftFootTarget, leftHipAnchor, maxLegBackwardMeters);
            desiredRightFootTarget = LimitBackwardTarget(desiredRightFootTarget, rightHipAnchor, maxLegBackwardMeters);
        }

        if (clampReachTargets)
        {
            desiredLeftHandTarget = ClampReach(desiredLeftHandTarget, leftShoulderAnchor, _leftArmReach, maxArmReachMultiplier);
            desiredRightHandTarget = ClampReach(desiredRightHandTarget, rightShoulderAnchor, _rightArmReach, maxArmReachMultiplier);
            desiredLeftFootTarget = ClampReach(desiredLeftFootTarget, leftHipAnchor, _leftLegReach, maxLegReachMultiplier);
            desiredRightFootTarget = ClampReach(desiredRightFootTarget, rightHipAnchor, _rightLegReach, maxLegReachMultiplier);
        }

        // Hard safety clamp sempre attivo (previene freeze in pose impossibili).
        desiredLeftHandTarget = ClampReach(desiredLeftHandTarget, leftShoulderAnchor, _leftArmReach, maxArmReachHardMultiplier);
        desiredRightHandTarget = ClampReach(desiredRightHandTarget, rightShoulderAnchor, _rightArmReach, maxArmReachHardMultiplier);
        desiredLeftFootTarget = ClampReach(desiredLeftFootTarget, leftHipAnchor, _leftLegReach, maxLegReachHardMultiplier);
        desiredRightFootTarget = ClampReach(desiredRightFootTarget, rightHipAnchor, _rightLegReach, maxLegReachHardMultiplier);

        Vector3 desiredLeftElbowHint = ResolveArmHint(
            true,
            leftShoulderAnchor,
            desiredLeftHandTarget,
            lmLeftElbow,
            _leftElbowHint);
        Vector3 desiredRightElbowHint = ResolveArmHint(
            false,
            rightShoulderAnchor,
            desiredRightHandTarget,
            lmRightElbow,
            _rightElbowHint);
        Vector3 desiredLeftKneeHint = ResolveLegHint(
            true,
            leftHipAnchor,
            desiredLeftFootTarget,
            lmLeftKnee,
            _leftKneeHint);
        Vector3 desiredRightKneeHint = ResolveLegHint(
            false,
            rightHipAnchor,
            desiredRightFootTarget,
            lmRightKnee,
            _rightKneeHint);

        if (!_targetsInitialized)
        {
            _leftHandTarget = desiredLeftHandTarget;
            _rightHandTarget = desiredRightHandTarget;
            _leftElbowHint = desiredLeftElbowHint;
            _rightElbowHint = desiredRightElbowHint;
            _leftFootTarget = desiredLeftFootTarget;
            _rightFootTarget = desiredRightFootTarget;
            _leftKneeHint = desiredLeftKneeHint;
            _rightKneeHint = desiredRightKneeHint;
            _targetsInitialized = true;
        }
        else
        {
            float targetS = GetAdaptiveSmoothing(_leftHandTarget, desiredLeftHandTarget);
            float hintS = GetAdaptiveSmoothing(_leftElbowHint, desiredLeftElbowHint);
            float targetT = 1f - Mathf.Exp(-Mathf.Max(0f, targetS) * Time.deltaTime);
            float hintT = 1f - Mathf.Exp(-Mathf.Max(0f, hintS + (ikHintSmoothing - ikTargetSmoothing)) * Time.deltaTime);

            _leftHandTarget = Vector3.Lerp(_leftHandTarget, desiredLeftHandTarget, targetT);
            _rightHandTarget = Vector3.Lerp(_rightHandTarget, desiredRightHandTarget, targetT);
            _leftFootTarget = Vector3.Lerp(_leftFootTarget, desiredLeftFootTarget, targetT);
            _rightFootTarget = Vector3.Lerp(_rightFootTarget, desiredRightFootTarget, targetT);
            _leftElbowHint = Vector3.Lerp(_leftElbowHint, desiredLeftElbowHint, hintT);
            _rightElbowHint = Vector3.Lerp(_rightElbowHint, desiredRightElbowHint, hintT);
            _leftKneeHint = Vector3.Lerp(_leftKneeHint, desiredLeftKneeHint, hintT);
            _rightKneeHint = Vector3.Lerp(_rightKneeHint, desiredRightKneeHint, hintT);
        }

        // Rotazione yaw opzionale (solo orientamento sinistra/destra).
        if (enableBodyYawRotation)
        {
            RotateByShouldersYaw();
        }
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

    private Vector3 PoseToWorldOffset(PoseLandmarkSerializable lm, float localXMul, float localYMul, float localZMul)
    {
        // Pose: hip-centered, y in MediaPipe va "down" => invertY per Unity.
        float y = invertY ? -lm.y : lm.y;
        float z = lm.z * zSign;

        Vector3 poseLocal = new Vector3(
            lm.x * poseScaleX * localXMul,
            y * poseScaleY * localYMul,
            z * poseScaleZ * localZMul) * (shoulderWidthMeters * Mathf.Max(0.01f, poseScale));
        return poseSpace.TransformVector(poseLocal);
    }

    private Vector3 ResolveTargetFromLandmark(
        PoseLandmarkSerializable lm,
        Vector3 currentValue,
        float localXMul,
        float localYMul,
        float localZMul,
        float requiredVisibility)
    {
        bool hasValidVisibility = lm.visibility >= requiredVisibility;
        if (!hasValidVisibility && holdLastValidTargets && _targetsInitialized)
            return currentValue;

        return hipAnchor.position + PoseToWorldOffset(lm, localXMul, localYMul, localZMul);
    }

    private float GetAdaptiveSmoothing(Vector3 current, Vector3 target)
    {
        if (!enableAdaptiveSmoothing) return ikTargetSmoothing;

        float speed = (target - current).magnitude / Mathf.Max(0.0001f, Time.deltaTime);
        float t = Mathf.Clamp01(speed / Mathf.Max(0.1f, fastMotionSpeed));
        return Mathf.Lerp(slowMotionSmoothing, fastMotionSmoothing, t);
    }

    private Vector3 GetBonePosition(HumanBodyBones bone, Vector3 fallback)
    {
        if (animator == null) return fallback;
        Transform t = animator.GetBoneTransform(bone);
        return t != null ? t.position : fallback;
    }

    private Vector3 ClampReach(Vector3 target, Vector3 anchor, float reach, float multiplier)
    {
        float maxDistance = Mathf.Max(0.05f, reach * Mathf.Max(0.5f, multiplier));
        Vector3 delta = target - anchor;
        if (delta.sqrMagnitude <= maxDistance * maxDistance) return target;
        return anchor + delta.normalized * maxDistance;
    }

    private Vector3 ScaleFromAnchor(Vector3 target, Vector3 anchor, float gain)
    {
        float g = Mathf.Max(0.01f, gain);
        return anchor + (target - anchor) * g;
    }

    private Vector3 LimitBackwardTarget(Vector3 target, Vector3 anchor, float maxBackwardMeters)
    {
        Transform basis = poseSpace != null ? poseSpace : (rootToRotate != null ? rootToRotate : transform);
        Vector3 localDelta = basis.InverseTransformVector(target - anchor);
        float maxBackward = -Mathf.Max(0f, maxBackwardMeters);
        if (localDelta.z < maxBackward)
            localDelta.z = maxBackward;
        return anchor + basis.TransformVector(localDelta);
    }

    private Vector3 ResolveArmHint(
        bool isLeft,
        Vector3 shoulder,
        Vector3 handTarget,
        PoseLandmarkSerializable elbowLandmark,
        Vector3 currentHint)
    {
        Vector3 axis = handTarget - shoulder;
        if (axis.sqrMagnitude < 0.0001f) return currentHint;

        Vector3 upRef = poseSpace != null ? poseSpace.up : Vector3.up;
        Vector3 side = Vector3.Cross(axis.normalized, upRef).normalized;
        if (side.sqrMagnitude < 0.0001f)
            side = isLeft ? Vector3.left : Vector3.right;
        if (isLeft) side = -side;

        Vector3 forward = Vector3.Cross(side, axis.normalized).normalized;
        Vector3 proceduralHint = shoulder
            + axis * 0.45f
            + side * armHintSideOffset
            + forward * armHintForwardOffset;

        Vector3 landmarkHint = ResolveTargetFromLandmark(
            elbowLandmark,
            currentHint,
            armLateralBoost,
            armVerticalBoost,
            1f,
            minVisibilityForIK);
        if (elbowLandmark.visibility < minVisibilityForIK) return proceduralHint;

        return Vector3.Lerp(proceduralHint, landmarkHint, landmarkHintBlend);
    }

    private Vector3 ResolveLegHint(
        bool isLeft,
        Vector3 hip,
        Vector3 footTarget,
        PoseLandmarkSerializable kneeLandmark,
        Vector3 currentHint)
    {
        Vector3 axis = footTarget - hip;
        if (axis.sqrMagnitude < 0.0001f) return currentHint;

        Vector3 forwardRef = poseSpace != null ? poseSpace.forward : Vector3.forward;
        Vector3 side = Vector3.Cross(forwardRef, axis.normalized).normalized;
        if (side.sqrMagnitude < 0.0001f)
            side = isLeft ? Vector3.left : Vector3.right;
        if (isLeft) side = -side;

        Vector3 forward = Vector3.Cross(axis.normalized, side).normalized;
        Vector3 proceduralHint = hip
            + axis * 0.5f
            + side * legHintSideOffset
            + forward * legHintForwardOffset;

        Vector3 landmarkHint = ResolveTargetFromLandmark(
            kneeLandmark,
            currentHint,
            legLateralBoost,
            legVerticalBoost,
            1f,
            minVisibilityForLowerBody);
        if (kneeLandmark.visibility < minVisibilityForLowerBody) return proceduralHint;

        return Vector3.Lerp(proceduralHint, landmarkHint, landmarkHintBlend);
    }

    private void RotateByShouldersYaw()
    {
        if (rootToRotate == null) return;
        if (_shoulderSpanWorld.sqrMagnitude < 0.000001f) return;

        Vector3 desiredRight = Vector3.ProjectOnPlane(_shoulderSpanWorld.normalized, Vector3.up);
        if (desiredRight.sqrMagnitude < 0.000001f) return;
        desiredRight.Normalize();

        Vector3 desiredForward = Vector3.Cross(Vector3.up, desiredRight); // destra->avanti

        // Se l'avatar è molto verticale, evita rotazioni errate.
        if (desiredForward.sqrMagnitude < 0.000001f) return;
        desiredForward.Normalize();

        Quaternion measuredRot = Quaternion.LookRotation(desiredForward, Vector3.up);
        float measuredYaw = measuredRot.eulerAngles.y;

        if (!_yawCalibrated)
        {
            _autoYawOffsetDegrees = autoCalibrateYawOffset
                ? Mathf.DeltaAngle(measuredYaw, rootToRotate.eulerAngles.y)
                : 0f;
            _yawCalibrated = true;
        }

        float targetYaw = measuredYaw + _autoYawOffsetDegrees + yawOffsetDegrees;
        float deltaYaw = Mathf.Abs(Mathf.DeltaAngle(rootToRotate.eulerAngles.y, targetYaw));
        if (deltaYaw < Mathf.Max(0f, yawDeadzoneDegrees)) return;

        Quaternion targetRot = Quaternion.Euler(0f, targetYaw, 0f);
        rootToRotate.rotation = Quaternion.Slerp(rootToRotate.rotation, targetRot, rotateSmoothing * Time.deltaTime);
    }
}

