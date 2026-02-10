using UnityEngine;
using System.Collections;

public class StageManager : MonoBehaviour
{
    public static StageManager Instance { get; private set; }

    public enum Stage
    {
        FirstPage,      // Idle / Start
        MainPage,       // Chatting / Dialogue
        Carousel,       // Product Selection
        ProductDetail   // Specific Product / Video
    }

    [Header("State")]
    public Stage currentStage = Stage.FirstPage;
    public Stage previousStage = Stage.FirstPage;

    [Header("UI Control")]
    public GameObject startButton; // User assigned Start Button

    [Header("Controllers")]
    public FAQController faqController;
    public CorouselScript carouselScript;
    public CardScript cardScript;
    public VideoController videoController;
    // public LoopChat loopChat; // Re-added (REMOVED per user request)
    public Dialoguebox dialogueBox; // Added

    private Coroutine showStartButtonCoroutine; // ✅ เก็บ reference ของ Coroutine
    private bool wasAiSpeaking = false; // ✅ Track AI speaking state for idle timer

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        // Auto-assign if missing
        if (faqController == null) faqController = FindFirstObjectByType<FAQController>();
        if (carouselScript == null) carouselScript = FindFirstObjectByType<CorouselScript>();
        if (cardScript == null) cardScript = FindFirstObjectByType<CardScript>();
        if (videoController == null) videoController = FindFirstObjectByType<VideoController>();
        // if (loopChat == null) loopChat = FindFirstObjectByType<LoopChat>(); // (REMOVED)
        if (dialogueBox == null) dialogueBox = FindFirstObjectByType<Dialoguebox>(); // Added

        // Init State
        ProcessStageChange(Stage.FirstPage, Stage.FirstPage); // Replaced EnterStage
        ResetIdleTimer();

        // Hook up Instance Events for Idle
        // Hook up Instance Events for Idle
        RecordAudio recordAudio = FindFirstObjectByType<RecordAudio>();
        if (recordAudio != null)
        {
            recordAudio.OnStartRecording += HandleMicStarted;
            recordAudio.OnTextSent += ResetIdleTimer;
        }
    }

    private void Update()
    {
        // 1. Check User Input (Touch / Mouse / Key)
        if (Input.anyKey || Input.touchCount > 0 || Input.GetMouseButton(0))
        {
            ResetIdleTimer();
        }

        // ✅ Reset Timer AFTER AI finishes speaking (not during) - User Request
        bool isAiSpeaking = RecordAudio.Instance != null && RecordAudio.Instance.IsPlaying;
        if (wasAiSpeaking && !isAiSpeaking)
        {
            // AI just finished speaking -> Reset idle timer now
            ResetIdleTimer();
            Debug.Log($"[StageManager] ⏱️ AI พูดเสร็จแล้ว - เริ่มนับถอยหลัง Idle Timeout ({idleTimeout}s)");
        }
        wasAiSpeaking = isAiSpeaking;

        // 2. Check Timeout
        if (currentStage != Stage.FirstPage)
        {
            // ✅ ProductDetail Specific Timeout (User Request)
            if (currentStage == Stage.ProductDetail)
            {
                if (Time.time - productDetailLastActivityTime > productDetailIdleTimeout)
                {
                    Debug.Log($"[StageManager] ProductDetail Idle Timeout ({productDetailIdleTimeout}s) reached. Return to FirstPage.");

                    // Stop AI Audio & Reset All States
                    if (RecordAudio.Instance != null) RecordAudio.Instance.CancelEverything();

                    // ✅ Unlock FAQ buttons
                    if (faqController != null) faqController.ForceUnlockButtons();

                    // Clear DialogueBox
                    if (dialogueBox != null) dialogueBox.ClearDialogue();

                    // ✅ Go to FirstPage instead of GoBack (User Request)
                    GoToFirstPage();

                    // ✅ Trigger GoFirst animation
                    if (videoController != null && videoController.aiAnimator != null)
                    {
                        videoController.aiAnimator.SetTrigger("GoFirst");
                    }

                    // ✅ Hide Mic Button when going to FirstPage (User Request)
                    if (videoController != null && videoController.micButton != null)
                    {
                        videoController.micButton.SetActive(false);
                    }

                    ResetProductDetailIdleTimer();
                }
            }
            // Global Timeout (for other stages)
            else if (Time.time - lastActivityTime > idleTimeout)
            {
                Debug.Log($"[StageManager] Idle Timeout ({idleTimeout}s) reached. Return to FirstPage.");

                // ✅ Stop AI Audio & Reset All States on Timeout
                if (RecordAudio.Instance != null) RecordAudio.Instance.CancelEverything();

                // ✅ Unlock FAQ buttons
                if (faqController != null) faqController.ForceUnlockButtons();

                // ✅ Clear DialogueBox on Timeout (User Request)
                if (dialogueBox != null) dialogueBox.ClearDialogue();

                GoToFirstPage();

                // ✅ Trigger GoFirst เฉพาะตอน Timeout (User Request)
                if (videoController != null && videoController.aiAnimator != null)
                {
                    videoController.aiAnimator.SetTrigger("GoFirst");
                }

                ResetIdleTimer(); // Reset to prevent continuous triggering
            }
        }
        else
        {
            lastActivityTime = Time.time;
        }
    }

    public void SwitchStage(Stage newStage)
    {
        if (currentStage == newStage) return;

        // Debug.Log($"[StageManager] Switching from {currentStage} to {newStage}");

        Stage oldStage = currentStage;
        previousStage = oldStage;
        currentStage = newStage;

        ProcessStageChange(oldStage, newStage);
    }

    // --- State Machine Logic (Unified) ---

    private void ProcessStageChange(Stage from, Stage to)
    {
        Debug.Log($"[StageManager] Process Stage: {from} -> {to}");

        // ✅ ถ้า from == to (เช่น ตอน Init) ข้าม EXIT logic ไปเลย เรียกแค่ ENTER logic
        bool isInitialization = (from == to);

        // 1. EXIT LOGIC (Based on 'from') - ข้ามถ้าเป็น Initialization
        if (!isInitialization)
            switch (from)
            {
                case Stage.FirstPage:
                    // No specific hide needed for FAQ here as 'to' stage will handle it
                    // LoopChat is handled in 'to'

                    // ✅ ยกเลิก Coroutine และซ่อนปุ่มทันทีเมื่อออกจาก FirstPage
                    if (showStartButtonCoroutine != null)
                    {
                        StopCoroutine(showStartButtonCoroutine);
                        showStartButtonCoroutine = null;
                    }
                    if (startButton != null) startButton.SetActive(false);

                    // ✅ Trigger OutFist (User Request)
                    if (videoController != null && videoController.aiAnimator != null)
                    {
                        videoController.aiAnimator.SetTrigger("OutFist");
                    }

                    // ✅ Trigger TextOUT when exiting FirstPage (User Request)
                    if (videoController != null && videoController.textAnimator != null)
                    {
                        videoController.textAnimator.SetTrigger("TextOUT");
                    }
                    break;

                case Stage.MainPage:
                    // FAQ Hide handled by 'to' logic mostly, but good to ensure
                    // if (faqController != null) faqController.Hide(); 
                    break;

                case Stage.Carousel:
                    if (carouselScript != null) carouselScript.HideCarousel();
                    break;

                case Stage.ProductDetail:
                    if (cardScript != null) cardScript.HideCard();
                    if (videoController != null) videoController.StopVideo();
                    break;
            }

        // 2. ENTER LOGIC (Based on 'to')
        switch (to)
        {
            case Stage.FirstPage:
                // ✅ Hide FAQ and Mic on FirstPage (User Request)
                if (faqController != null)
                {
                    faqController.Hide();
                    faqController.gameObject.SetActive(false); // Force Disable
                }
                if (videoController != null && videoController.micButton != null)
                    videoController.micButton.SetActive(false);

                // ✅ Show Start Button on FirstPage (หลังจาก AI animation)
                showStartButtonCoroutine = StartCoroutine(ShowStartButtonWithDelay());

                // ✅ Hide DialogueBox on FirstPage
                if (dialogueBox != null) dialogueBox.gameObject.SetActive(false);

                // ✅ Trigger TextIN when entering FirstPage (User Request)
                // Skip if already on FirstPage (e.g., initialization)
                if (!isInitialization && videoController != null && videoController.textAnimator != null)
                {
                    videoController.textAnimator.SetTrigger("TextIN");
                }

                // if (loopChat != null) loopChat.RestartLoop();
                break;

            case Stage.MainPage:
                // ✅ Ensure FAQ and Mic are visible on MainPage
                if (faqController != null)
                {
                    // faqController.gameObject.SetActive(true); // Moved to Routine
                    // faqController.Show();
                    StartCoroutine(ShowFAQWithDelay());
                }
                if (videoController != null && videoController.micButton != null)
                    videoController.micButton.SetActive(true);

                // ✅ Hide Start Button on MainPage
                if (startButton != null) startButton.SetActive(false);

                // ✅ Show DialogueBox on MainPage
                if (dialogueBox != null) dialogueBox.gameObject.SetActive(true);

                // if (loopChat != null) loopChat.StopLoop();
                break;

            case Stage.Carousel:
                if (carouselScript != null) carouselScript.ShowCarousel();
                // ✅ Hide FAQ and disable GameObject (User Request)
                if (faqController != null)
                {
                    faqController.Hide();
                    faqController.gameObject.SetActive(false);
                }
                // ✅ Show Mic Button and DialogueBox (User Request)
                if (videoController != null && videoController.micButton != null)
                    videoController.micButton.SetActive(true);
                if (dialogueBox != null) dialogueBox.gameObject.SetActive(true);
                break;

            case Stage.ProductDetail:
                // Specific product/video is handled by caller
                if (faqController != null)
                {
                    faqController.Hide();
                    faqController.gameObject.SetActive(false);
                }
                // ✅ Show Mic Button and DialogueBox (User Request)
                if (videoController != null && videoController.micButton != null)
                    videoController.micButton.SetActive(true);
                if (dialogueBox != null) dialogueBox.gameObject.SetActive(true);
                break;
        }
    }

    // --- Public Shortcuts ---

    public void GoToFirstPage() => SwitchStage(Stage.FirstPage);
    public void GoToMainPage() => SwitchStage(Stage.MainPage);
    public void GoToCarousel() => SwitchStage(Stage.Carousel);
    public void GoToProductDetail() => SwitchStage(Stage.ProductDetail);

    // ✅ New Function to manually start conversation (User Request)
    public void StartConversation()
    {
        if (currentStage == Stage.FirstPage)
        {
            GoToMainPage();
        }
    }

    public void GoBack()
    {
        // ✅ ถ้า currentStage กับ previousStage เป็น Stage เดียวกัน ไม่ต้องเปลี่ยนหน้า
        if (currentStage == previousStage)
        {
            Debug.Log($"[StageManager] GoBack skipped: currentStage ({currentStage}) == previousStage ({previousStage})");
            return;
        }

        Debug.Log($"[StageManager] Going Back from {currentStage} to {previousStage}");

        // 1. Stop AI Audio (Cut voice)
        if (RecordAudio.Instance != null) RecordAudio.Instance.StopPlayback();
        else
        {
            var ra = FindFirstObjectByType<RecordAudio>();
            if (ra != null) ra.StopPlayback();
        }

        // 2. Clear Dialogue Box
        if (dialogueBox != null) dialogueBox.ClearDialogue();
        else
        {
            var db = FindFirstObjectByType<Dialoguebox>();
            if (db != null) db.ClearDialogue();
        }

        // 3. Trigger ButtonBack (Restore Mic Button) ONLY if NOT returning to FirstPage (where it should be hidden)
        if (previousStage != Stage.FirstPage)
        {
            if (videoController != null && videoController.micButton != null)
            {
                Animator micAnim = videoController.micButton.GetComponent<Animator>();
                if (micAnim != null)
                {
                    // Clear Triggers to prevent stacking/conflicts
                    micAnim.ResetTrigger("ButtonMove");
                    micAnim.ResetTrigger("ButtonBack");

                    micAnim.SetTrigger("ButtonBack");
                }
            }
        }

        SwitchStage(previousStage);
    }

    // --- Global Idle Timeout ---
    [Header("Idle Settings")]
    public float idleTimeout = 180.0f;

    [Header("ProductDetail Idle Settings")]
    public float productDetailIdleTimeout = 30.0f; // Timeout เฉพาะหน้า ProductDetail
    private float productDetailLastActivityTime;
    private float lastActivityTime;

    public void ResetIdleTimer()
    {
        lastActivityTime = Time.time;
        // ✅ Also reset ProductDetail timer when global timer is reset
        productDetailLastActivityTime = Time.time;
    }

    // ✅ Separate reset for ProductDetail timer only
    public void ResetProductDetailIdleTimer()
    {
        productDetailLastActivityTime = Time.time;
    }

    // --- Centralized Event Handling ---

    private void OnEnable()
    {
        // Subscribe to Global Events
        RecordAudio.OnFunctionCallResult += HandleFunctionCall;
        RecordAudio.OnPlayVideoUrl += HandleVideoUrl;
        RecordAudio.OnTranscription += HandleAiResponse; // Reset idle on AI response

        // Idle Reset Events
        if (RecordAudio.Instance != null)
        {
            RecordAudio.Instance.OnStartRecording += HandleMicStarted;
            RecordAudio.Instance.OnTextSent += ResetIdleTimer;
        }
        else
        {
            // Fallback if Instance is not yet set
            RecordAudio recordAudio = FindFirstObjectByType<RecordAudio>();
            if (recordAudio != null)
            {
                recordAudio.OnStartRecording += HandleMicStarted;
                recordAudio.OnTextSent += ResetIdleTimer;
            }
        }
    }

    private void OnDisable()
    {
        // Unsubscribe
        RecordAudio.OnFunctionCallResult -= HandleFunctionCall;
        RecordAudio.OnPlayVideoUrl -= HandleVideoUrl;
        RecordAudio.OnTranscription -= HandleAiResponse;

        // Unsubscribe instance events
        RecordAudio recordAudio = RecordAudio.Instance ?? FindFirstObjectByType<RecordAudio>();
        if (recordAudio != null)
        {
            recordAudio.OnStartRecording -= HandleMicStarted;
            recordAudio.OnTextSent -= ResetIdleTimer;
        }
    }

    private void HandleMicStarted()
    {
        ResetIdleTimer();

        // Only switch to MainPage if we are starting from the FirstPage (Idle state)
        // If we are in Carousel or ProductDetail, we stay there to continue the context.
        /* REMOVED: Auto-navigation to MainPage on Mic Start (User Request)
        if (currentStage == Stage.FirstPage)
        {
            GoToMainPage();
        }
        */
    }

    private void HandleAiResponse(string text)
    {
        ResetIdleTimer(); // AI is speaking/responding -> Reset Idle
    }

    private void HandleFunctionCall(RecordAudio.FunctionCallEntry entry)
    {
        ResetIdleTimer(); // AI Acting count as activity? Maybe yes, to prevent cutting off AI.

        // Central Logic for "Dispatcher"
        string actionName = entry.name;
        Debug.Log($"[StageManager] Received Function Call: {actionName}");

        if (actionName == "product_card")
        {
            HandleProductCardAction(entry);
        }
        else if (actionName == "show_product")
        {
            HandleShowProductAction(entry);
        }
    }

    private void HandleProductCardAction(RecordAudio.FunctionCallEntry entry)
    {
        string action = entry.response?.action ?? entry.args?.action;

        if (action == "show")
        {
            // Show Carousel -> Hide Others
            if (currentStage != Stage.Carousel) SwitchStage(Stage.Carousel);
        }
        else if (action == "hide")
        {
            // Close Carousel -> Go Back to Main
            if (currentStage == Stage.Carousel) SwitchStage(Stage.MainPage);

            // Explicitly ensure others are closed if needed
            if (carouselScript != null) carouselScript.HideCarousel();
        }
    }

    private void HandleShowProductAction(RecordAudio.FunctionCallEntry entry)
    {
        string productName = entry.response?.product ?? entry.args?.product;
        Debug.Log($"[StageManager] Show Product Requested: {productName}");

        // Switch to ProductDetail Stage
        SwitchStage(Stage.ProductDetail);

        // Direct CardScript to show the specific product
        if (cardScript != null)
        {
            // Note: CardScript.SwitchProductRoutine is private, we might need public 'ShowProduct'
            // For now, let's assume we can call the public ShowProduct or similar.
            // Wait, CardScript.ShowProduct is private. We need to check CardScript access.
            // CardScript has 'SwitchProductRoutine' (private) and 'ShowCard' (public ctx menu).
            // We should EXPOSE a public method in CardScript to trigger this specific logic.
            // Let's use SendMessage or ensure we add a public wrapper in the next step.
            // For now, I will assume a public method 'ShowProduct(string)' exists or I will add it.
            // Actually, looking at CardScript, it has 'HandleFunctionCall' which did the work.
            // I will add a 'PublicShowProduct(string)' to CardScript in the next step.
            cardScript.ShowProductPublic(productName);
        }
    }

    private void HandleVideoUrl(string url)
    {
        ResetIdleTimer();
        Debug.Log($"[StageManager] Video URL Received: {url}");

        SwitchStage(Stage.ProductDetail); // Video is part of ProductDetail or its own stage context

        if (videoController != null)
        {
            videoController.PlayResolvedVideo(url);
        }
    }

    private IEnumerator ShowFAQWithDelay()
    {
        if (faqController != null)
        {
            faqController.gameObject.SetActive(true);
            // Wait 1 second before triggering animation
            yield return new WaitForSeconds(1.0f);
            faqController.Show();
        }
    }

    private IEnumerator ShowStartButtonWithDelay()
    {
        // ✅ รอให้ AI animation เล่นก่อน แล้วค่อยแสดงปุ่ม (User Request)
        yield return new WaitForSeconds(1.0f);
        if (startButton != null) startButton.SetActive(true);
    }
}
