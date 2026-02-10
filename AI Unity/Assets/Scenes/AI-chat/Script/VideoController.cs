using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;

[RequireComponent(typeof(UnityEngine.Video.VideoPlayer))]
public class VideoController : MonoBehaviour
{
    public UnityEngine.Video.VideoPlayer videoPlayer;

    [Header("Display Settings")]
    public RenderTexture targetRenderTexture;
    public RawImage targetRawImage;
    [Range(0f, 1f)] public float defaultVolume = 0.5f;
    public bool muteAudio = true;

    [Header("UI Control")]
    public GameObject micButton; // User assigned Mic Button
    public GameObject glowObject; // Glow effect shown during video

    [Header("Timeout Settings")]
    public float maxVideoDuration = 180.0f; // Seconds before forcing close

    void Start()
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<UnityEngine.Video.VideoPlayer>();

        videoPlayer.loopPointReached += OnVideoLoopPointReached;

        videoPlayer.prepareCompleted += (source) => UpdateAspectRatio();
        SetupDisplay();

        // Subscribe to RecordAudio event
        // RecordAudio.OnPlayVideoUrl += PlayResolvedVideo; // DISABLED: Centralized in StageManager
    }

    void OnDestroy()
    {
        // RecordAudio.OnPlayVideoUrl -= PlayResolvedVideo; // DISABLED
    }

    public event System.Action<bool> OnVideoFinished;
    public event System.Action OnVideoStarted;

    void SetupDisplay()
    {
        if (targetRawImage != null && targetRenderTexture == null && videoPlayer.targetTexture == null)
        {
            targetRenderTexture = new RenderTexture(1920, 1080, 16, RenderTextureFormat.ARGB32);
            targetRenderTexture.Create();
            videoPlayer.targetTexture = targetRenderTexture;
            targetRawImage.texture = targetRenderTexture;
        }
        else if (targetRenderTexture != null)
        {
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = targetRenderTexture;
            if (targetRawImage != null) targetRawImage.texture = targetRenderTexture;
        }
        else if (videoPlayer.targetTexture != null)
        {
            if (targetRawImage != null) targetRawImage.texture = videoPlayer.targetTexture;
        }
    }

    void UpdateAspectRatio()
    {
        // if (targetRawImage != null && videoPlayer.texture != null)
        // {
        //     AspectRatioFitter fitter = targetRawImage.GetComponent<AspectRatioFitter>();
        //     if (fitter == null) fitter = targetRawImage.gameObject.AddComponent<AspectRatioFitter>();

        //     fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        //     fitter.aspectRatio = (float)videoPlayer.width / (float)videoPlayer.height;
        // }
    }

    [Header("UI Control")]
    public Animator clipAnimator;
    public float delayIn = 1.0f;  // Time for VIN animation
    public float delayOut = 1.0f; // Time for VOUT animation

    [Header("AI Animation")]
    public Animator aiAnimator;
    public Animator textAnimator; // ✅ NEW: Animator for Text (TextIN/TextOUT)
    public string aiTriggerShow = "AIShow";
    public string aiTriggerStop = "AI-Stop";
    public float aiShowDuration = 2.0f; // Delay to allow AI to show up before video starts

    // Public property to check if video is effectively active
    public bool IsVideoActive => isOpening || (videoPlayer != null && videoPlayer.isPlaying);

    private Coroutine currentVideoRoutine;
    private Coroutine currentCloseRoutine;
    private Coroutine enableMicCoroutine; // ✅ Track EnableMicWithDelay
    private bool isOpening = false;

    public void PlayResolvedVideo(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return;

        // Stop any running sequences
        if (currentVideoRoutine != null) StopCoroutine(currentVideoRoutine);
        if (currentCloseRoutine != null) StopCoroutine(currentCloseRoutine);

        // ✅ Cancel pending EnableMicWithDelay to prevent ButtonBack conflict
        if (enableMicCoroutine != null) StopCoroutine(enableMicCoroutine);

        currentVideoRoutine = StartCoroutine(PlayVideoSequence(filename));
    }

    // ✅ New Public Method to Trigger AI Show Animation
    public void TriggerAIAnimation()
    {
        if (aiAnimator != null)
        {
            // Debug.Log("[VideoController] Triggering AI Show Animation manually.");
            aiAnimator.ResetTrigger(aiTriggerStop);
            aiAnimator.SetTrigger(aiTriggerShow);
        }
    }

    IEnumerator PlayVideoSequence(string filename)
    {
        // 1. Prepare Path
        string videoPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Video", filename);
        // Debug.Log($"📹 Playing Video from StreamingAssets: {videoPath}");

        // Hide FAQ when Video starts
        FAQController faq = Object.FindFirstObjectByType<FAQController>();
        if (faq != null) faq.Hide();

        // NOT Hide Mic Button -> Trigger ButtonMove
        if (micButton != null)
        {
            Animator micAnim = micButton.GetComponent<Animator>();
            if (micAnim != null)
            {
                // ✅ Reset Triggers ก่อน Set เพื่อป้องกัน animation ซ้อน (Fix: Move then Back issue)
                micAnim.ResetTrigger("ButtonMove");
                micAnim.ResetTrigger("ButtonBack");
                micAnim.SetTrigger("ButtonMove");
            }
        }

        isOpening = true;
        OnVideoStarted?.Invoke(); // Notify listeners that video is starting

        // Show AI when video starts (as requested)
        if (aiAnimator != null)
        {
            aiAnimator.ResetTrigger(aiTriggerStop);
            aiAnimator.SetTrigger(aiTriggerShow);

            // Wait for AI to start appearing before showing clip
            yield return new WaitForSeconds(aiShowDuration);
        }

        if (!System.IO.File.Exists(videoPath))
        {
            Debug.LogError($"❌ Video File Not Found: {videoPath} (Check filename in Inspector vs Folder)");
            isOpening = false;
            yield break;
        }


        // Check if we can skip preparation (Optimization)
        bool isAlreadyPrepared = videoPlayer.url == videoPath && videoPlayer.isPrepared;

        if (!isAlreadyPrepared)
        {
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = videoPath;
            videoPlayer.isLooping = true; // Loop until timeout

            // Robust Reset: Only stop if we are changing video or need fresh prepare
            if (videoPlayer.isPlaying) videoPlayer.Stop();

            videoPlayer.Prepare();
        }
        else
        {
            // Debug.Log("[VideoController] Video already prepared. Skipping Prepare().");
            // Ensure looping is still set even if we skip prepare
            videoPlayer.isLooping = true;
        }

        // Hide Screen Initial ONLY if we are bringing up a new video that might delay
        // If it's already prepared, we might want to show it sooner, but let's keep the fade-in logic consistent.
        if (targetRawImage != null) targetRawImage.color = new Color(1, 1, 1, 0);

        // 2. Show Panel (VIN)
        if (clipAnimator != null)
        {
            // Reset Animator using Rebind (Cleaner than SetActive toggle)
            clipAnimator.Rebind();
            clipAnimator.Update(0f); // Force update to apply default state immediately

            // Clean Reset
            ResetAllTriggers();
            clipAnimator.SetTrigger("Clipshow");

            yield return new WaitForSeconds(delayIn);
        }

        // Wait until preparedness (with timeout)
        // Wait until preparedness (with timeout)
        // Only wait if not already prepared
        if (!videoPlayer.isPrepared)
        {
            float prepareTimeout = 5.0f;
            while (!videoPlayer.isPrepared && prepareTimeout > 0)
            {
                prepareTimeout -= Time.deltaTime;
                yield return null;
            }
        }

        if (!videoPlayer.isPrepared)
        {
            Debug.LogError("[VideoController] Video Prepare Timed Out!");
            // Proceed anyway or handle error? Proceeding might show black screen but better than hanging.
            // We'll trust Play() might kickstart it or it's dead.
        }

        // Set Audio Volume (Must be done after Preparation to ensure tracks exist)
        videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        videoPlayer.SetDirectAudioVolume(0, muteAudio ? 0f : defaultVolume);

        // Show Screen
        if (targetRawImage != null) targetRawImage.color = Color.white;
        videoPlayer.Play();

        // ✅ Show Glow after video starts playing
        if (glowObject != null) glowObject.SetActive(true);

        // 3.5 Wait for Video to ACTUALLY Start (Safety for isPlaying flag)
        float startTimeout = 3.0f;
        while (!videoPlayer.isPlaying && startTimeout > 0)
        {
            startTimeout -= Time.deltaTime;
            yield return null;
        }

        // Wait for video end
        // Note: loopPointReached event is better, but Coroutine wait works for linear flow
        bool isPlaying = true;

        // Local event handler to detect end (Logic updated: Loop until timeout, so we don't stop on loop point)
        // UnityEngine.Video.VideoPlayer.EventHandler onComplete = (vp) => isPlaying = false; 
        // videoPlayer.loopPointReached += onComplete; // Removed to allow looping

        float currentPlaybackTime = 0f;
        bool shouldResume = true;

        while (true)
        {
            // Infinite loop until StopCoroutine is called by StopVideo()
            // (Removed: ResetIdleTimer - User Request)

            // 2. Failsafe for Looping
            // If video stops playing but we expect it to loop (and we are in this while true)
            // waiting for loopPointReached to trigger restart.

            yield return new WaitForSeconds(1.0f); // Check every second is enough
        }

        // videoPlayer.loopPointReached -= onComplete; // Removed listener removal since we didn't add it

        // 4. Hide Panel (VOUT)
        if (clipAnimator != null)
        {
            ResetAllTriggers();
            clipAnimator.SetTrigger("Cliphide");

            // Stop AI when video ends
            if (aiAnimator != null)
            {
                aiAnimator.ResetTrigger(aiTriggerShow);
                aiAnimator.SetTrigger(aiTriggerStop);
            }

            yield return new WaitForSeconds(delayOut);

            isOpening = false;

            // Reset Loops & Triggers before final state
            ResetAllTriggers();

            // Wait a tiny bit for Animator to digest the trigger/state change
            yield return new WaitForSeconds(0.2f);
            OnVideoFinished?.Invoke(shouldResume);
        }
        else
        {
            // If Animator is missing, just finish
            isOpening = false;
            OnVideoFinished?.Invoke(shouldResume);
        }
    }

    public void StopVideo(bool restoreUI = true)
    {
        isOpening = false; // FORCE RESET OPENING STATE

        if (currentVideoRoutine != null) StopCoroutine(currentVideoRoutine);

        // Stop Player immediately
        if (videoPlayer.isPlaying) videoPlayer.Stop();

        // ✅ Reset URL หลังจากปิด video (User Request)
        videoPlayer.url = null;

        // ✅ Hide Glow when video stops
        if (glowObject != null) glowObject.SetActive(false);

        // Always trigger ButtonBack on Mic Button when stopped (Control via restoreUI)
        if (restoreUI && micButton != null)
        {
            Animator micAnim = micButton.GetComponent<Animator>();
            if (micAnim != null)
            {
                // ✅ Reset Triggers ก่อน Set เพื่อป้องกัน animation ซ้อน
                micAnim.ResetTrigger("ButtonMove");
                micAnim.ResetTrigger("ButtonBack");
                micAnim.SetTrigger("ButtonBack");
            }
        }

        // Always Show FAQ logic
        // FAQController faq = Object.FindFirstObjectByType<FAQController>();
        // if (faq != null) faq.Show(); // DISABLED: Controlled by StageManager

        // Play VOUT animation if panel is active
        if (clipAnimator != null && clipAnimator.gameObject.activeSelf)
        {
            if (currentCloseRoutine != null) StopCoroutine(currentCloseRoutine);
            currentCloseRoutine = StartCoroutine(ClosePanelRoutine(false));
        }
    }

    IEnumerator ClosePanelRoutine(bool shouldResume)
    {
        ResetAllTriggers();
        clipAnimator.SetTrigger("Cliphide");

        // Stop AI when video ends
        if (aiAnimator != null)
        {
            aiAnimator.ResetTrigger(aiTriggerShow);
            aiAnimator.SetTrigger(aiTriggerStop);
        }

        yield return new WaitForSeconds(delayOut);

        // Check if a new open sequence started while we were waiting
        if (isOpening) yield break;

        // Video finished or stopped
        isOpening = false;

        // Reset Loops & Triggers before final state
        // Debug.Log("VideoController: Forced Play(VOIDLE)");

        yield return new WaitForSeconds(0.2f);
        if (!isOpening)
        {
            // clipAnimator.gameObject.SetActive(false); // Keep Active to preserve State
        }

        // State Restoration Logic (Only on Timeout/Forced Stop)
        if (!shouldResume)
        {
            // Debug.Log("[VideoController] Timeout/Stop -> Restoring Idle State (FAQ Show + LoopChat Restart)");

            // 1. Show FAQ
            // FAQController faq = Object.FindFirstObjectByType<FAQController>();
            // if (faq != null) faq.Show(); // DISABLED: Controlled by StageManager

            // 2. Restart Chat Loop ONLY if we are back in FirstPage/Idle
            bool shouldRestartLoop = true;
            if (StageManager.Instance != null && StageManager.Instance.currentStage != StageManager.Stage.FirstPage)
            {
                shouldRestartLoop = false;
            }

            if (shouldRestartLoop)
            {
                // LoopChat loopChat = Object.FindFirstObjectByType<LoopChat>();
                // if (loopChat != null) loopChat.RestartLoop();
            }

            // 3. Enable Mic Button (moved outside valid check to ensure it always comes back)
            // if (micButton != null) micButton.SetActive(true); 
        }

        // Always re-enable Mic Button when panel closes (Delayed)
        // ✅ Track coroutine so it can be cancelled if new video starts
        enableMicCoroutine = StartCoroutine(EnableMicWithDelay());

        OnVideoFinished?.Invoke(shouldResume);
    }

    private IEnumerator EnableMicWithDelay()
    {
        yield return new WaitForSeconds(1.0f);

        // ✅ ถ้ากำลังเปิด Video ใหม่อยู่ ไม่ต้องทำอะไร (ป้องกัน conflict)
        if (isOpening)
        {
            enableMicCoroutine = null;
            yield break;
        }

        // ✅ ตรวจสอบว่าไม่อยู่ใน FirstPage ก่อนแสดงปุ่ม
        if (StageManager.Instance != null && StageManager.Instance.currentStage == StageManager.Stage.FirstPage)
        {
            if (micButton != null) micButton.SetActive(false);
        }
        else
        {
            if (micButton != null) micButton.SetActive(true);
        }

        enableMicCoroutine = null;
    }

    // Test Button in Inspector
    [ContextMenu("Test Trigger Video")]
    public void TestTrigger()
    {
        if (FindFirstObjectByType<RecordAudio>() != null)
        {
            FindFirstObjectByType<RecordAudio>().TriggerVideoPlayback();
        }
        else
        {
            Debug.LogError("RecordAudio not found!");
        }
    }

    private void ResetAllTriggers()
    {
        if (clipAnimator != null)
        {
            clipAnimator.ResetTrigger("Clipshow");
            clipAnimator.ResetTrigger("Cliphide");
        }
    }

    private void OnVideoLoopPointReached(UnityEngine.Video.VideoPlayer vp)
    {
        // Debug.Log("[VideoController] Loop Point Reached. Ensuring loop continues.");
        // If for some reason isLooping is off, force it.
        // Or if it stopped, play.
        if (!vp.isPlaying)
        {
            vp.Play();
        }
    }
}