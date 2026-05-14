using System.Collections;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Unity;
using Mediapipe.Unity.Experimental;
using Mediapipe.Unity.Sample;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using MPImage = Mediapipe.Image;

public class AvatarBodyTrackingController : VisionTaskApiRunner<PoseLandmarker>
{
  [System.Serializable]
  private class BoneBinding
  {
    public HumanBodyBones bone;
    public int fromLandmark;
    public int toLandmark;
    [HideInInspector] public Transform transform;
    [HideInInspector] public Quaternion initialLocalRotation;
    [HideInInspector] public Vector3 initialAxisLocal;
  }

  [Header("Avatar")]
  [SerializeField] private Animator avatarAnimator;
  [SerializeField] private Transform avatarRoot;
  [SerializeField] private float rotationSmoothing = 12f;
  [SerializeField] private float rootMoveSmoothing = 8f;
  [SerializeField] private bool mirrorX = true;
  [SerializeField] private bool keepFeetGrounded = true;
  [SerializeField] private float feetGroundingWeight = 1f;
  [SerializeField] private bool useGroundRaycast = true;
  [SerializeField] private LayerMask groundLayers = ~0;
  [SerializeField] private float groundRaycastDistance = 3f;
  [SerializeField] private float groundFootOffset = 0.01f;
  [SerializeField] private float footPitchOffset = -10f;
  [SerializeField][Range(0f, 1f)] private float lowerBodyDepthInfluence = 0.25f;
  [SerializeField][Range(0f, 1f)] private float torsoDepthInfluence = 0.35f;
  [SerializeField][Range(0f, 1f)] private float torsoUprightBlend = 0.2f;
  [Header("Squat Assist")]
  [SerializeField] private bool enableSquatAssist = true;
  [SerializeField] private float squatRootLowering = 0.2f;
  [SerializeField][Range(0f, 1f)] private float squatDepthBoost = 1f;
  [SerializeField] private float squatStartKneeAngle = 165f;
  [SerializeField] private float squatFullKneeAngle = 95f;
  [SerializeField] private float squatSmoothing = 10f;

  [Header("Pose Tracking")]
  [SerializeField] private Mediapipe.Tasks.Core.BaseOptions.Delegate inferenceDelegate = Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU;
  [SerializeField] private string modelAssetPath = "pose_landmarker_full.bytes";
  [SerializeField] private float minDetectionConfidence = 0.5f;
  [SerializeField] private float minPresenceConfidence = 0.5f;
  [SerializeField] private float minTrackingConfidence = 0.5f;

  [Header("Debug Skeleton")]
  [SerializeField] private bool showDebugSkeleton = true;
  [SerializeField] private float debugPointSize = 0.03f;
  [SerializeField] private Color debugPointColor = Color.cyan;
  [SerializeField] private Color debugLineColor = Color.yellow;

  [Header("Webcam Preview")]
  [SerializeField] private bool showWebcamPreview = false;
  [SerializeField] private KeyCode togglePreviewKey = KeyCode.V;
  [SerializeField][Range(0f, 1f)] private float webcamPreviewOpacity = 1f;

  private readonly List<BoneBinding> _boneBindings = new()
  {
    new BoneBinding{ bone = HumanBodyBones.LeftUpperArm, fromLandmark = 11, toLandmark = 13 },
    new BoneBinding{ bone = HumanBodyBones.LeftLowerArm, fromLandmark = 13, toLandmark = 15 },
    new BoneBinding{ bone = HumanBodyBones.RightUpperArm, fromLandmark = 12, toLandmark = 14 },
    new BoneBinding{ bone = HumanBodyBones.RightLowerArm, fromLandmark = 14, toLandmark = 16 },
    new BoneBinding{ bone = HumanBodyBones.LeftUpperLeg, fromLandmark = 23, toLandmark = 25 },
    new BoneBinding{ bone = HumanBodyBones.LeftLowerLeg, fromLandmark = 25, toLandmark = 27 },
    new BoneBinding{ bone = HumanBodyBones.LeftFoot, fromLandmark = 27, toLandmark = 31 },
    new BoneBinding{ bone = HumanBodyBones.RightUpperLeg, fromLandmark = 24, toLandmark = 26 },
    new BoneBinding{ bone = HumanBodyBones.RightLowerLeg, fromLandmark = 26, toLandmark = 28 },
    new BoneBinding{ bone = HumanBodyBones.RightFoot, fromLandmark = 28, toLandmark = 32 },
    new BoneBinding{ bone = HumanBodyBones.Spine, fromLandmark = 23, toLandmark = 11 },
    new BoneBinding{ bone = HumanBodyBones.Chest, fromLandmark = 24, toLandmark = 12 },
    new BoneBinding{ bone = HumanBodyBones.Neck, fromLandmark = 11, toLandmark = 0 },
    new BoneBinding{ bone = HumanBodyBones.Head, fromLandmark = 0, toLandmark = 1 },
  };

  private TextureFramePool _textureFramePool;
  private readonly object _poseLock = new();
  private readonly List<NormalizedLandmark> _latestLandmarks = new();
  private readonly List<Transform> _debugPoints = new();
  private readonly List<LineRenderer> _debugLines = new();
  private bool _hasPose;
  private Vector3 _initialRootPosition;
  private float _initialHipY;
  private float _currentRootYOffset;
  private Transform _leftFootTransform;
  private Transform _rightFootTransform;
  private float _initialFootMinY;
  private bool _hasFootReference;
  private float _currentSquatAmount;
  private ImageSource _imageSource;
  private Canvas _previewCanvas;
  private RawImage _previewRawImage;

  private static readonly int[] PoseConnectionPairs =
  {
    11, 12, 11, 13, 13, 15, 12, 14, 14, 16,
    11, 23, 12, 24, 23, 24, 23, 25, 25, 27, 27, 31,
    24, 26, 26, 28, 28, 32, 15, 17, 15, 19, 15, 21,
    16, 18, 16, 20, 16, 22, 27, 29, 29, 31, 28, 30, 30, 32,
    0, 1, 1, 2, 2, 3, 3, 7, 0, 4, 4, 5, 5, 6, 6, 8, 9, 10
  };

  public bool HasPose => _hasPose;

  public override void Stop()
  {
    base.Stop();
    _textureFramePool?.Dispose();
    _textureFramePool = null;
  }

  protected override IEnumerator Start()
  {
    if (avatarRoot == null)
    {
      avatarRoot = transform;
    }

    _initialRootPosition = avatarRoot.position;
    CacheBones();
    CacheFootReference();
    SetupDebugSkeleton();

    yield return base.Start();
  }

  private void LateUpdate()
  {
    HandlePreviewToggleInput();
    UpdateWebcamPreview();

    List<NormalizedLandmark> landmarksSnapshot;
    lock (_poseLock)
    {
      if (!_hasPose || _latestLandmarks.Count < 29)
      {
        return;
      }
      landmarksSnapshot = new List<NormalizedLandmark>(_latestLandmarks);
    }

    if (landmarksSnapshot.Count < 29)
    {
      return;
    }

    ApplyRootMotion(landmarksSnapshot);
    ApplyBoneRotations(landmarksSnapshot);
    UpdateDebugSkeleton(landmarksSnapshot);
  }

  protected override IEnumerator Run()
  {
    yield return AssetLoader.PrepareAssetAsync(modelAssetPath);

    var options = new PoseLandmarkerOptions(
      new Mediapipe.Tasks.Core.BaseOptions(inferenceDelegate, modelAssetPath: modelAssetPath),
      runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
      numPoses: 1,
      minPoseDetectionConfidence: minDetectionConfidence,
      minPosePresenceConfidence: minPresenceConfidence,
      minTrackingConfidence: minTrackingConfidence,
      outputSegmentationMasks: false,
      resultCallback: OnPoseResult
    );

    taskApi = PoseLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
    var imageSource = ImageSourceProvider.ImageSource;
    _imageSource = imageSource;

    yield return imageSource.Play();

    if (!imageSource.isPrepared)
    {
      Debug.LogError("Image source non disponibile.");
      yield break;
    }

    _textureFramePool = new TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);
    var transformationOptions = imageSource.GetTransformationOptions();
    var imageProcessingOptions = new Mediapipe.Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: 0);
    var flipHorizontally = transformationOptions.flipHorizontally;
    var flipVertically = transformationOptions.flipVertically;
    SetupWebcamPreviewIfNeeded();

    AsyncGPUReadbackRequest req = default;
    var waitUntilReqDone = new WaitUntil(() => req.done);
    var waitForEndOfFrame = new WaitForEndOfFrame();

    while (true)
    {
      if (isPaused)
      {
        yield return new WaitWhile(() => isPaused);
      }

      if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
      {
        yield return waitForEndOfFrame;
        continue;
      }

      req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
      yield return waitUntilReqDone;

      if (req.hasError)
      {
        textureFrame.Release();
        continue;
      }

      using var image = textureFrame.BuildCPUImage();
      textureFrame.Release();
      taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
    }
  }

  private void OnPoseResult(PoseLandmarkerResult result, MPImage image, long timestamp)
  {
    if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
    {
      lock (_poseLock)
      {
        _hasPose = false;
        _latestLandmarks.Clear();
      }
      return;
    }

    var source = result.poseLandmarks[0].landmarks;
    if (source == null || source.Count == 0)
    {
      lock (_poseLock)
      {
        _hasPose = false;
        _latestLandmarks.Clear();
      }
      return;
    }

    lock (_poseLock)
    {
      _latestLandmarks.Clear();
      _latestLandmarks.AddRange(source);
      _hasPose = true;
    }

    if (_initialHipY <= 0f && source.Count > 24)
    {
      _initialHipY = Average(source[23].y, source[24].y);
    }
  }

  private void CacheBones()
  {
    if (avatarAnimator == null || !avatarAnimator.isHuman)
    {
      Debug.LogWarning("Animator non humanoid o mancante. Verifica il rig del modello.");
      return;
    }

    foreach (var binding in _boneBindings)
    {
      binding.transform = avatarAnimator.GetBoneTransform(binding.bone);
      if (binding.transform != null)
      {
        binding.initialLocalRotation = binding.transform.localRotation;
        var referenceDirection = binding.transform.forward;
        if (binding.transform.childCount > 0)
        {
          var childDirection = (binding.transform.GetChild(0).position - binding.transform.position);
          if (childDirection.sqrMagnitude > 0.00001f)
          {
            referenceDirection = childDirection.normalized;
          }
        }

        var parent = binding.transform.parent;
        binding.initialAxisLocal = parent != null
          ? parent.InverseTransformDirection(referenceDirection).normalized
          : referenceDirection.normalized;
      }
    }
  }

  private void ApplyRootMotion(List<NormalizedLandmark> landmarks)
  {
    if (landmarks.Count < 25 || _initialHipY <= 0f)
    {
      return;
    }

    var squatAmount = ComputeSquatAmount(landmarks);
    _currentSquatAmount = Mathf.Lerp(_currentSquatAmount, squatAmount, Time.deltaTime * squatSmoothing);

    var hipY = Average(landmarks[23].y, landmarks[24].y);
    var targetOffset = Mathf.Clamp((_initialHipY - hipY) * 1.2f, -0.35f, 0.35f);
    _currentRootYOffset = Mathf.Lerp(_currentRootYOffset, targetOffset, Time.deltaTime * rootMoveSmoothing);
    var target = _initialRootPosition + new Vector3(0f, _currentRootYOffset, 0f);
    target.y -= _currentSquatAmount * Mathf.Max(0f, squatRootLowering);
    var hasRaycastGrounding = false;

    if (useGroundRaycast && _hasFootReference && TryGetGroundFootY(out var groundFootY))
    {
      var currentFootMinY = GetCurrentFootMinY();
      var raycastCorrection = groundFootY - currentFootMinY;
      target.y += raycastCorrection;
      hasRaycastGrounding = true;
    }

    if (!hasRaycastGrounding && keepFeetGrounded && _hasFootReference)
    {
      var currentFootMinY = GetCurrentFootMinY();
      var groundingCorrection = Mathf.Clamp(_initialFootMinY - currentFootMinY, -0.5f, 0.5f);
      target.y += groundingCorrection * Mathf.Clamp01(feetGroundingWeight);
    }

    avatarRoot.position = Vector3.Lerp(avatarRoot.position, target, Time.deltaTime * rootMoveSmoothing);
  }

  private void ApplyBoneRotations(List<NormalizedLandmark> landmarks)
  {
    foreach (var binding in _boneBindings)
    {
      if (binding.transform == null)
      {
        continue;
      }

      if (!TryGetLandmark(landmarks, binding.fromLandmark, out var fromLm) ||
          !TryGetLandmark(landmarks, binding.toLandmark, out var toLm))
      {
        continue;
      }

      var a = ToWorldVector(fromLm);
      var b = ToWorldVector(toLm);
      var directionWorld = (b - a);
      directionWorld = FilterDirectionForBone(binding.bone, directionWorld);
      if (directionWorld.sqrMagnitude < 0.0001f)
      {
        continue;
      }

      var parent = binding.transform.parent;
      var targetAxisLocal = parent != null
        ? parent.InverseTransformDirection(directionWorld.normalized)
        : directionWorld.normalized;

      if (targetAxisLocal.sqrMagnitude < 0.0001f || binding.initialAxisLocal.sqrMagnitude < 0.0001f)
      {
        continue;
      }

      var delta = Quaternion.FromToRotation(binding.initialAxisLocal, targetAxisLocal.normalized);
      var targetLocalRotation = delta * binding.initialLocalRotation;
      if (binding.bone == HumanBodyBones.LeftFoot || binding.bone == HumanBodyBones.RightFoot)
      {
        targetLocalRotation *= Quaternion.Euler(footPitchOffset, 0f, 0f);
      }
      binding.transform.localRotation = Quaternion.Slerp(binding.transform.localRotation, targetLocalRotation, Time.deltaTime * rotationSmoothing);
    }
  }

  private Vector3 ToWorldVector(NormalizedLandmark landmark)
  {
    var x = mirrorX ? (0.5f - landmark.x) : (landmark.x - 0.5f);
    var y = 1f - landmark.y;
    var z = -landmark.z;
    return new Vector3(x, y, z);
  }

  private static float Average(float a, float b)
  {
    return (a + b) * 0.5f;
  }

  private static bool TryGetLandmark(List<NormalizedLandmark> landmarks, int index, out NormalizedLandmark value)
  {
    if (index >= 0 && index < landmarks.Count)
    {
      value = landmarks[index];
      return true;
    }

    value = default;
    return false;
  }

  private Vector3 FilterDirectionForBone(HumanBodyBones bone, Vector3 directionWorld)
  {
    switch (bone)
    {
      case HumanBodyBones.LeftUpperLeg:
      case HumanBodyBones.LeftLowerLeg:
      case HumanBodyBones.RightUpperLeg:
      case HumanBodyBones.RightLowerLeg:
      case HumanBodyBones.LeftFoot:
      case HumanBodyBones.RightFoot:
        var dynamicLowerBodyDepth = Mathf.Lerp(
          Mathf.Clamp01(lowerBodyDepthInfluence),
          1f,
          Mathf.Clamp01(_currentSquatAmount * Mathf.Clamp01(squatDepthBoost))
        );
        directionWorld.z *= dynamicLowerBodyDepth;
        break;

      case HumanBodyBones.Spine:
      case HumanBodyBones.Chest:
      case HumanBodyBones.Neck:
      case HumanBodyBones.Head:
        directionWorld.z *= Mathf.Clamp01(torsoDepthInfluence);
        directionWorld = Vector3.Slerp(directionWorld, Vector3.up * directionWorld.magnitude, Mathf.Clamp01(torsoUprightBlend));
        break;
    }

    return directionWorld;
  }

  private float ComputeSquatAmount(List<NormalizedLandmark> landmarks)
  {
    if (!enableSquatAssist || landmarks.Count < 29)
    {
      return 0f;
    }

    var left = ComputeKneeBendAmount(landmarks, 23, 25, 27);
    var right = ComputeKneeBendAmount(landmarks, 24, 26, 28);
    return (left + right) * 0.5f;
  }

  private float ComputeKneeBendAmount(List<NormalizedLandmark> landmarks, int hipIndex, int kneeIndex, int ankleIndex)
  {
    if (!TryGetLandmark(landmarks, hipIndex, out var hipLm) ||
        !TryGetLandmark(landmarks, kneeIndex, out var kneeLm) ||
        !TryGetLandmark(landmarks, ankleIndex, out var ankleLm))
    {
      return 0f;
    }

    var hip = ToWorldVector(hipLm);
    var knee = ToWorldVector(kneeLm);
    var ankle = ToWorldVector(ankleLm);

    var upper = (hip - knee);
    var lower = (ankle - knee);
    if (upper.sqrMagnitude < 0.0001f || lower.sqrMagnitude < 0.0001f)
    {
      return 0f;
    }

    var kneeAngle = Vector3.Angle(upper, lower);
    var denom = Mathf.Max(1f, squatStartKneeAngle - squatFullKneeAngle);
    return Mathf.Clamp01((squatStartKneeAngle - kneeAngle) / denom);
  }

  private void CacheFootReference()
  {
    if (avatarAnimator == null || !avatarAnimator.isHuman)
    {
      return;
    }

    _leftFootTransform = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
    _rightFootTransform = avatarAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
    if (_leftFootTransform == null || _rightFootTransform == null)
    {
      _hasFootReference = false;
      return;
    }

    _initialFootMinY = Mathf.Min(_leftFootTransform.position.y, _rightFootTransform.position.y);
    _hasFootReference = true;
  }

  private float GetCurrentFootMinY()
  {
    if (!_hasFootReference || _leftFootTransform == null || _rightFootTransform == null)
    {
      return avatarRoot != null ? avatarRoot.position.y : transform.position.y;
    }

    return Mathf.Min(_leftFootTransform.position.y, _rightFootTransform.position.y);
  }

  private bool TryGetGroundFootY(out float value)
  {
    value = 0f;
    if (_leftFootTransform == null || _rightFootTransform == null)
    {
      return false;
    }

    var footMid = (_leftFootTransform.position + _rightFootTransform.position) * 0.5f;
    var rayOrigin = footMid + Vector3.up * 1.5f;

    if (!Physics.Raycast(rayOrigin, Vector3.down, out var hit, groundRaycastDistance, groundLayers, QueryTriggerInteraction.Ignore))
    {
      return false;
    }

    value = hit.point.y + groundFootOffset;
    return true;
  }

  private void SetupWebcamPreviewIfNeeded()
  {
    if (_previewCanvas != null || !showWebcamPreview)
    {
      return;
    }

    var canvasGo = new GameObject("WebcamPreviewCanvas");
    _previewCanvas = canvasGo.AddComponent<Canvas>();
    _previewCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
    _previewCanvas.sortingOrder = -1000;
    canvasGo.AddComponent<CanvasScaler>();
    canvasGo.AddComponent<GraphicRaycaster>();

    var rawImageGo = new GameObject("WebcamPreview");
    rawImageGo.transform.SetParent(canvasGo.transform, false);
    _previewRawImage = rawImageGo.AddComponent<RawImage>();
    _previewRawImage.raycastTarget = false;
    _previewRawImage.color = new Color(1f, 1f, 1f, Mathf.Clamp01(webcamPreviewOpacity));

    var rect = _previewRawImage.rectTransform;
    rect.anchorMin = Vector2.zero;
    rect.anchorMax = Vector2.one;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;

    SetWebcamPreviewVisible(showWebcamPreview);
  }

  private void HandlePreviewToggleInput()
  {
    if (IsTogglePreviewPressedThisFrame())
    {
      showWebcamPreview = !showWebcamPreview;
      if (showWebcamPreview)
      {
        SetupWebcamPreviewIfNeeded();
      }

      SetWebcamPreviewVisible(showWebcamPreview);
    }
  }

  private bool IsTogglePreviewPressedThisFrame()
  {
    if (togglePreviewKey == KeyCode.None)
    {
      return false;
    }

#if ENABLE_INPUT_SYSTEM
    var keyboard = Keyboard.current;
    var mappedKey = ToInputSystemKey(togglePreviewKey);
    if (keyboard != null && mappedKey.HasValue)
    {
      return keyboard[mappedKey.Value].wasPressedThisFrame;
    }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
    return Input.GetKeyDown(togglePreviewKey);
#else
    return false;
#endif
  }

#if ENABLE_INPUT_SYSTEM
  private static Key? ToInputSystemKey(KeyCode keyCode)
  {
    if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
    {
      return (Key)((int)Key.A + ((int)keyCode - (int)KeyCode.A));
    }

    if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
    {
      return (Key)((int)Key.Digit0 + ((int)keyCode - (int)KeyCode.Alpha0));
    }

    return keyCode switch
    {
      KeyCode.Space => Key.Space,
      KeyCode.Tab => Key.Tab,
      KeyCode.Escape => Key.Escape,
      KeyCode.Return => Key.Enter,
      KeyCode.Backspace => Key.Backspace,
      KeyCode.LeftShift => Key.LeftShift,
      KeyCode.RightShift => Key.RightShift,
      KeyCode.LeftControl => Key.LeftCtrl,
      KeyCode.RightControl => Key.RightCtrl,
      KeyCode.LeftAlt => Key.LeftAlt,
      KeyCode.RightAlt => Key.RightAlt,
      _ => null,
    };
  }
#endif

  private void UpdateWebcamPreview()
  {
    if (showWebcamPreview && _previewCanvas == null)
    {
      SetupWebcamPreviewIfNeeded();
    }

    if (_previewCanvas != null && _previewCanvas.enabled != showWebcamPreview)
    {
      SetWebcamPreviewVisible(showWebcamPreview);
    }

    if (_previewRawImage == null || _imageSource == null)
    {
      return;
    }

    if (_previewRawImage.texture == null)
    {
      _previewRawImage.texture = _imageSource.GetCurrentTexture();
    }

    _previewRawImage.color = new Color(1f, 1f, 1f, Mathf.Clamp01(webcamPreviewOpacity));
  }

  private void SetWebcamPreviewVisible(bool isVisible)
  {
    if (_previewCanvas != null)
    {
      _previewCanvas.enabled = isVisible;
    }
  }

  private void SetupDebugSkeleton()
  {
    if (!showDebugSkeleton)
    {
      return;
    }

    for (var i = 0; i < 33; i++)
    {
      var point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
      point.name = $"MP_Point_{i}";
      point.transform.SetParent(transform, false);
      point.transform.localScale = Vector3.one * debugPointSize;
      var collider = point.GetComponent<Collider>();
      if (collider != null)
      {
        Destroy(collider);
      }

      var renderer = point.GetComponent<Renderer>();
      if (renderer != null)
      {
        renderer.material.color = debugPointColor;
      }
      _debugPoints.Add(point.transform);
    }

    for (var i = 0; i < PoseConnectionPairs.Length; i += 2)
    {
      var lineGo = new GameObject($"MP_Line_{i / 2}");
      lineGo.transform.SetParent(transform, false);
      var line = lineGo.AddComponent<LineRenderer>();
      line.positionCount = 2;
      line.startWidth = debugPointSize * 0.45f;
      line.endWidth = debugPointSize * 0.45f;
      line.useWorldSpace = true;
      line.material = new Material(Shader.Find("Sprites/Default"));
      line.startColor = debugLineColor;
      line.endColor = debugLineColor;
      _debugLines.Add(line);
    }
  }

  private void UpdateDebugSkeleton(List<NormalizedLandmark> landmarks)
  {
    if (!showDebugSkeleton || _debugPoints.Count == 0)
    {
      return;
    }

    var origin = avatarRoot != null ? avatarRoot.position : transform.position;
    var scale = 2f;
    for (var i = 0; i < _debugPoints.Count; i++)
    {
      if (!TryGetLandmark(landmarks, i, out var lm))
      {
        continue;
      }

      var p = ToWorldVector(lm) * scale + origin + new Vector3(0f, 1f, 0f);
      _debugPoints[i].position = p;
    }

    for (var i = 0; i < _debugLines.Count; i++)
    {
      var from = PoseConnectionPairs[i * 2];
      var to = PoseConnectionPairs[i * 2 + 1];
      if (from < _debugPoints.Count && to < _debugPoints.Count)
      {
        _debugLines[i].SetPosition(0, _debugPoints[from].position);
        _debugLines[i].SetPosition(1, _debugPoints[to].position);
      }
    }
  }
}
