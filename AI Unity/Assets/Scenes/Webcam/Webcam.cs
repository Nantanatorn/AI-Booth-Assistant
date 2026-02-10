using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample;
using ResolutionStruct = Mediapipe.Unity.ImageSource.ResolutionStruct;

public class Webcam : MonoBehaviour
{
    [SerializeField] private RawImage rawImage;
    [SerializeField] private int requestedWidth = 640;
    [SerializeField] private int requestedHeight = 480;
    [SerializeField] private int requestedFps = 30;
    [SerializeField] private float startTimeoutSeconds = 3f;
    [SerializeField] public GameObject webcamObject;
    [SerializeField] public GameObject AIBallObject;

    private Coroutine _startRoutine;
    private bool _isStarting;
    private WebCamSource _mpWebCamSource;

    private void Start()
    {
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
    }

    public void turnOnWebcam()
    {
        if (_isStarting)
        {
            Debug.Log("[Webcam] turnOn ignored; already starting");
            return;
        }

        if (webcamObject != null)
        {
            webcamObject.SetActive(true);
            AIBallObject.SetActive(false);
        }
        _startRoutine ??= StartCoroutine(StartMediaPipeWebcam());
    }

    private IEnumerator StartMediaPipeWebcam()
    {
        _isStarting = true;
        if (rawImage != null)
        {
            rawImage.enabled = false;
        }

        // Stop any active ImageSource (if initialized elsewhere).
        ImageSourceProvider.ImageSource?.Stop();

        // Ensure WebCamSource exists.
        if (ImageSourceProvider.ImageSource == null)
        {
            // Create minimal WebCamSource manually if Bootstrap hasn't run.
            var fallbackRes = new ResolutionStruct(requestedWidth, requestedHeight, requestedFps);
            _mpWebCamSource = new WebCamSource(requestedWidth, new[] { fallbackRes });
            ImageSourceProvider.Initialize(_mpWebCamSource, null, null);
            ImageSourceProvider.Switch(ImageSourceType.WebCamera);
        }
        else
        {
            ImageSourceProvider.Switch(ImageSourceType.WebCamera);
            _mpWebCamSource = ImageSourceProvider.ImageSource as WebCamSource;
        }

        if (_mpWebCamSource == null)
        {
            Debug.LogError("[Webcam] WebCamSource is not available.");
            _isStarting = false;
            _startRoutine = null;
            yield break;
        }

        // Choose closest resolution if available.
        var resolutions = _mpWebCamSource.availableResolutions;
        if (resolutions != null && resolutions.Length > 0)
        {
            int bestIdx = 0;
            int bestScore = int.MaxValue;
            for (int i = 0; i < resolutions.Length; i++)
            {
                var r = resolutions[i];
                int score = Mathf.Abs(r.width - requestedWidth) + Mathf.Abs(r.height - requestedHeight);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }
            _mpWebCamSource.SelectResolution(bestIdx);
            Debug.Log($"[Webcam] selected resolution {resolutions[bestIdx].width}x{resolutions[bestIdx].height} @ {resolutions[bestIdx].frameRate}");
        }
        var playRoutine = _mpWebCamSource.Play();

        // Add manual timeout guard.
        var timeout = Time.realtimeSinceStartup + startTimeoutSeconds;
        while (playRoutine.MoveNext())
        {
            if (Time.realtimeSinceStartup > timeout)
            {
                Debug.LogWarning("[Webcam] Play timeout");
                _isStarting = false;
                _startRoutine = null;
                yield break;
            }
            yield return playRoutine.Current;
        }

        var tex = _mpWebCamSource.GetCurrentTexture();
        if (tex == null)
        {
            Debug.LogError("[Webcam] MediaPipe WebCamSource returned null texture.");
        }
        else
        {
            Debug.Log($"[Webcam] MediaPipe WebCamSource started: {tex.width}x{tex.height}");
            if (rawImage != null)
            {
                rawImage.texture = tex;
                rawImage.enabled = true;
            }
        }

        _isStarting = false;
        _startRoutine = null;
    }

    public void turnOffWebcam(bool stopCamera = true)
    {
        _isStarting = false;
        if (_startRoutine != null)
        {
            StopCoroutine(_startRoutine);
            _startRoutine = null;
        }

        if (stopCamera)
        {
            _mpWebCamSource?.Stop();
        }

        if (rawImage != null)
        {
            rawImage.texture = null;
            rawImage.enabled = false;
        }

        if (webcamObject != null)
        {
            webcamObject.SetActive(false);
            AIBallObject.SetActive(true);
        }
    }
}
