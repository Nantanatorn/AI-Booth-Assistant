// Copyright (c) 2021 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using UnityEngine;

namespace Mediapipe.Unity.Sample
{
  public class Bootstrap : MonoBehaviour
  {
    [SerializeField] private AppSettings _appSettings;
    [SerializeField] private bool _skipGlogInitInPlayer = true;

    private static Bootstrap _instance;
    private static bool _glogInitializing;
    private static bool _glogInitialized;
    public InferenceMode inferenceMode { get; private set; }
    public bool isFinished { get; private set; }
    private bool _isGlogInitialized;

    private void Awake()
    {
      // Ensure only one Bootstrap survives across scenes to avoid double InitGoogleLogging
      if (_instance != null && _instance != this)
      {
        Destroy(gameObject);
        return;
      }
      _instance = this;
      DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
      if (_instance != null && _instance != this)
      {
        return;
      }
      var _ = StartCoroutine(Init());
    }

    private IEnumerator Init()
    {
      if (_glogInitialized || _glogInitializing)
      {
        // Already initialized in this session; avoid double InitGoogleLogging crash.
        yield break;
      }
      _glogInitializing = true;

      try
      {
        Debug.Log("The configuration for the sample app can be modified using AppSettings.asset.");
#if !DEBUG && !DEVELOPMENT_BUILD
        Debug.LogWarning("Logging for the MediaPipeUnityPlugin will be suppressed. To enable logging, please check the 'Development Build' option and build.");
#endif

        Logger.MinLogLevel = _appSettings.logLevel;

        Protobuf.SetLogHandler(Protobuf.DefaultLogHandler);

        Debug.Log("Setting global flags...");
        _appSettings.ResetGlogFlags();
        // Skip glog init in player if requested to avoid double InitGoogleLogging.
        if (_skipGlogInitInPlayer && !Application.isEditor)
        {
          Debug.Log("[Bootstrap] Skipping Glog.Initialize in player build");
          _glogInitialized = true; // prevent later attempts
        }
        else
        {
          // If some other component already initialized glog, skip to avoid fatal.
          if (!_glogInitialized)
          {
            Glog.Initialize("MediaPipeUnityPlugin");
            _isGlogInitialized = true;
            _glogInitialized = true;
          }
          else
          {
            Debug.Log("[Bootstrap] Glog already initialized, skip re-init");
          }
        }

        Debug.Log("Initializing AssetLoader...");
        switch (_appSettings.assetLoaderType)
        {
          case AppSettings.AssetLoaderType.AssetBundle:
            {
              AssetLoader.Provide(new AssetBundleResourceManager("mediapipe"));
              break;
            }
          case AppSettings.AssetLoaderType.StreamingAssets:
            {
              AssetLoader.Provide(new StreamingAssetsResourceManager());
              break;
            }
          case AppSettings.AssetLoaderType.Local:
            {
#if UNITY_EDITOR
              AssetLoader.Provide(new LocalResourceManager());
              break;
#else
              Debug.LogError("LocalResourceManager is only supported on UnityEditor." +
                "To avoid this error, consider switching to the StreamingAssetsResourceManager and copying the required resources under StreamingAssets, for example.");
              yield break;
#endif
            }
          default:
            {
              Debug.LogError($"AssetLoaderType is unknown: {_appSettings.assetLoaderType}");
              yield break;
            }
        }

        DecideInferenceMode();
        if (inferenceMode == InferenceMode.GPU)
        {
          Debug.Log("Initializing GPU resources...");
          yield return GpuManager.Initialize();

          if (!GpuManager.IsInitialized)
          {
            Debug.LogWarning("If your native library is built for CPU, change 'Preferable Inference Mode' to CPU from the Inspector Window for AppSettings");
          }
        }

        Debug.Log("Preparing ImageSource...");
        ImageSourceProvider.Initialize(
          _appSettings.BuildWebCamSource(), _appSettings.BuildStaticImageSource(), _appSettings.BuildVideoSource());
        ImageSourceProvider.Switch(_appSettings.defaultImageSource);

        isFinished = true;
      }
      finally
      {
        _glogInitializing = false;
      }
    }

    private void DecideInferenceMode()
    {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_WIN
      if (_appSettings.preferableInferenceMode == InferenceMode.GPU) {
        Debug.LogWarning("Current platform does not support GPU inference mode, so falling back to CPU mode");
      }
      inferenceMode = InferenceMode.CPU;
#else
      inferenceMode = _appSettings.preferableInferenceMode;
#endif
    }

    private void OnApplicationQuit()
    {
      GpuManager.Shutdown();

      if (_isGlogInitialized)
      {
        Glog.Shutdown();
      }

      Protobuf.ResetLogHandler();
    }
  }
}
