using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using UnityEngine;

public class BallTrack : MonoBehaviour
{
  [Header("Mediapipe Sources")]
  [SerializeField] private HandLandmarkerRunner runner;
  [SerializeField] private Camera targetCamera;

  [Header("Ball Target")]
  [SerializeField] private Transform ball;

  [Header("Tracking Options")]
  [SerializeField] private int landmarkIndex = 8; // fingertip by default
  [SerializeField, Range(0f, 1f)] private float smooth = 0.2f;
  [SerializeField] private bool useWorldLandmarks = true; // use MediaPipe world coords (meters)

  [Header("Scale By Depth (meters)")]
  [SerializeField] private bool scaleWithDepth = false;
  [SerializeField] private float nearDepth = 0.25f; // distance for maxScale
  [SerializeField] private float farDepth = 1.2f; // distance for minScale (farther -> smaller)
  [SerializeField] private float minScale = 0.4f;
  [SerializeField] private float maxScale = 1.2f;
  [SerializeField] private float maxScaleClamp = 1.0f; // absolute cap to avoid oversize

  [Header("Normalized Z -> Depth Mapping")]
  [SerializeField] private float normNearZ = -0.1f; // normalized z (close = more negative)
  [SerializeField] private float normFarZ = 0.1f;  // normalized z (farther)
  [SerializeField] private float normNearDepth = 0.3f; // meters mapped from normNearZ
  [SerializeField] private float normFarDepth = 2.0f;  // meters mapped from normFarZ

  [Header("Depth Adjustment")]
  [SerializeField] private float depthMultiplier = 2.0f; // push farther/closer relative to camera
  [SerializeField] private float depthOffset = 0.0f;     // add/subtract meters from computed depth
  [SerializeField] private float minDepthFromCamera = 0.1f; // clamp minimum distance from camera
  [SerializeField] private float maxDepthFromCamera = 5.0f; // clamp maximum distance from camera
  [SerializeField] private float depthDivide = 1.0f; // divide raw depth from landmark (e.g., 2 = half distance)
  [SerializeField] private float maxForwardZ = -7f; // clamp world Z so it never goes beyond this toward camera

  private readonly object _resultLock = new object();
  private HandLandmarkerResult _latestResult;
  private bool _hasResult;
  private Vector3 _baseScale = Vector3.one;

  private void Awake()
  {
    if (targetCamera == null)
    {
      targetCamera = Camera.main;
    }

    if (ball != null)
    {
      _baseScale = ball.localScale;
    }
    else
    {
      _baseScale = transform.localScale;
    }
  }

  private void OnEnable()
  {
    if (runner != null)
    {
      runner.OnHandResult += HandleResult;
    }
  }

  private void OnDisable()
  {
    if (runner != null)
    {
      runner.OnHandResult -= HandleResult;
    }
  }

  private void HandleResult(HandLandmarkerResult result)
  {
    // Callback may come from a non-main thread; store and process on Update.
    lock (_resultLock)
    {
      result.CloneTo(ref _latestResult);
      _hasResult = true;
    }
  }

  private void Update()
  {
    if (ball == null || targetCamera == null)
    {
      return;
    }

    HandLandmarkerResult result;
    bool hasResult;
    lock (_resultLock)
    {
      result = _latestResult;
      hasResult = _hasResult;
      _hasResult = false;
    }

    if (!hasResult)
    {
      return;
    }

    Vector3 targetPos;
    float depthMeters;

    if (useWorldLandmarks)
    {
      var worldList = result.handWorldLandmarks;
      if (worldList == null || worldList.Count == 0)
      {
        return;
      }

      var firstHand = worldList[0];
      if (firstHand.landmarks == null || firstHand.landmarks.Count <= landmarkIndex)
      {
        return;
      }

      var lm = firstHand.landmarks[landmarkIndex];
      var cameraSpace = new Vector3(lm.x, lm.y, -lm.z); // MediaPipe world coords are camera-centric
      var depthCam = cameraSpace.z / Mathf.Max(0.0001f, depthDivide);
      depthCam = Mathf.Clamp((depthCam * depthMultiplier) + depthOffset, minDepthFromCamera, maxDepthFromCamera);
      targetPos = targetCamera.transform.TransformPoint(new Vector3(cameraSpace.x, cameraSpace.y, depthCam)); // convert to Unity world
      depthMeters = depthCam;
    }
    else
    {
      var normList = result.handLandmarks;
      if (normList == null || normList.Count == 0)
      {
        return;
      }

      var firstHand = normList[0];
      if (firstHand.landmarks == null || firstHand.landmarks.Count <= landmarkIndex)
      {
        return;
      }

      var lm = firstHand.landmarks[landmarkIndex];
      // Map normalized z to a camera-space depth in meters, then convert to world.
      float depth = Mathf.Lerp(normFarDepth, normNearDepth, Mathf.InverseLerp(normFarZ, normNearZ, lm.z));
      depth = depth / Mathf.Max(0.0001f, depthDivide);
      depth = (depth * depthMultiplier) + depthOffset;
      depth = Mathf.Clamp(depth, minDepthFromCamera, maxDepthFromCamera);
      targetPos = targetCamera.ViewportToWorldPoint(new Vector3(lm.x, 1f - lm.y, depth));
      depthMeters = depth;
    }

    if (smooth <= 0f)
    {
      ball.position = targetPos;
    }
    else
    {
      ball.position = Vector3.Lerp(ball.position, targetPos, smooth);
    }

    // Clamp world Z so the ball never crosses too close to camera.
    if (ball.position.z > maxForwardZ)
    {
      ball.position = new Vector3(ball.position.x, ball.position.y, maxForwardZ);
    }

    // Scale the ball optionally based on depth; otherwise keep original size.
    if (scaleWithDepth)
    {
      float t = Mathf.InverseLerp(farDepth, nearDepth, depthMeters);
      float scale = Mathf.Lerp(minScale, maxScale, t);
      scale = Mathf.Min(scale, maxScaleClamp);
      ball.localScale = _baseScale * scale;
      Debug.Log($"ball targetPos={targetPos} depthMeters={depthMeters} scale={scale}");
    }
    else
    {
      ball.localScale = _baseScale;
      Debug.Log($"ball targetPos={targetPos} depthMeters={depthMeters} scale=1");
    }

  }
}
