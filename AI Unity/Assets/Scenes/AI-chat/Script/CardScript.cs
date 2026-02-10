using UnityEngine;
using System.Collections;
using System.Collections.Generic;


public class CardScript : MonoBehaviour
{
    [System.Serializable]
    public class ProductData
    {
        public string productName;    // e.g. "Meet in touch"
        public GameObject productObj; // The GameObject to show
        public int bgIndex;           // The background index
        public string videoFilename;  // e.g. "productA.mp4" (in StreamingAssets)
    }

    [Header("Product List")]
    public List<ProductData> products = new List<ProductData>();

    [Header("Settings")]
    public Animator animator;
    public float delayIn = 1f;      // เวลาเล่น CIn ก่อนไป CIdle
    public float delayIdle = 3f;    // เวลาเล่น CIdle ก่อนไป Cout
    public float delayOut = 1f;     // เวลาเล่น Cout ก่อนไป CIdleOut
    public float delayIdleOut = 1f; // เวลาเล่น CIdleOut ก่อนวนกลับมา CIn
    public GameObject backButton;   // Reference to Shared Back Button

    private float lastShowTime = 0f;
    private bool isShowing = false;
    private RecordAudio recordAudio;

    private void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        // Hide all products initially
        HideAllProducts();
    }

    private void OnEnable()
    {
        // RecordAudio.OnFunctionCallResult += HandleFunctionCall; // DISABLED: Centralized in StageManager
        Dialoguebox.OnDialogueStarting += OnDialogueStarting;

        recordAudio = FindFirstObjectByType<RecordAudio>();
        recordAudio = FindFirstObjectByType<RecordAudio>();
        if (recordAudio != null) recordAudio.OnStartRecording += OnMicStarted;

        var video = FindFirstObjectByType<VideoController>();
        if (video != null)
        {
            video.OnVideoFinished += OnVideoFinished;
            video.OnVideoStarted += OnVideoStarted;
        }
    }

    private void OnDisable()
    {
        // RecordAudio.OnFunctionCallResult -= HandleFunctionCall; // DISABLED
        Dialoguebox.OnDialogueStarting -= OnDialogueStarting;
        // Dialoguebox.OnDialogueStarting -= OnDialogueStarting; // Removed duplicate
        if (recordAudio != null) recordAudio.OnStartRecording -= OnMicStarted;

        var video = FindFirstObjectByType<VideoController>();
        if (video != null)
        {
            video.OnVideoFinished -= OnVideoFinished;
            video.OnVideoStarted -= OnVideoStarted;
        }
    }

    private bool wasShowingBeforeInterruption = false;

    private void OnVideoStarted()
    {
        // If an external video starts while we are showing, hide the card but remember state
        if (isShowing)
        {
            // Debug.Log("[CardScript] External Video Started -> Hiding Card (will resume later)");
            wasShowingBeforeInterruption = true;
            HideCard(); // This sets isShowing = false
        }
    }

    private void OnMicStarted()
    {
        // User เปิดไมค์พูด -> จบช่วงโชว์ของ -> อนุญาตให้ปิดได้ทันทีเมื่อ Dialogue มา
        lastShowTime = 0f;
    }

    private void OnDialogueStarting()
    {
        // Debug.Log($"[CardScript] OnDialogueStarting Called. isShowing: {isShowing}, Time: {Time.time}, LastShow: {lastShowTime}");

        // ถ้าไม่ได้โชว์อยู่ ก็จบเลย
        if (!isShowing) return;

        // ถ้าเพิ่งโชว์ไม่ถึง 3 วิ อย่าเพิ่งรีบปิด
        if (Time.time - lastShowTime < 3.0f)
        {
            Debug.Log($"[CardScript] Ignoring Auto-Hide (Just shown at {lastShowTime})");
            return;
        }

        // Debug.Log("[CardScript] Auto-Hiding Card due to Dialogue Starting");
        HideCard();
    }

    private Coroutine currentRoutine;

    private void HandleFunctionCall(RecordAudio.FunctionCallEntry entry)
    {
        if (entry.name == "product_card")
        {
            string action = entry.response?.action ?? entry.args?.action;
            // string product = entry.response?.product ?? entry.args?.product; 

            // Debug.Log($"CardScript: Received product_card action: {action}");

            if (action == "hide")
            {
                HideCard();
            }
            else if (action == "show") // If switching to Carousel (show), hide the specific product card
            {
                HideCard();
            }
        }
        else if (entry.name == "show_product")
        {
            string product = entry.response?.product ?? entry.args?.product;
            // Debug.Log($"CardScript: Received show_product: {product}");

            // ใช้ Switch Routine แทนการเรียก ShowProduct/ShowCard ตรงๆ
            if (currentRoutine != null) StopCoroutine(currentRoutine);
            currentRoutine = StartCoroutine(SwitchProductRoutine(product));
        }
    }

    private void HideAllProducts()
    {
        if (products == null) return;
        foreach (var item in products)
        {
            if (item.productObj != null)
                item.productObj.SetActive(false);
        }
    }

    [Header("References")]
    public bgchangeScript bgChangeScript;
    public CorouselScript carouselScript; // Added reference

    private string currentProductName = "";

    private void ShowProduct(string productName)
    {
        currentProductName = productName;
        HideAllProducts();

        if (string.IsNullOrEmpty(productName) || products == null) return;

        // Find match in list
        ProductData match = products.Find(p => IsMatch(p.productName, productName));

        if (match != null)
        {
            SetActive(match.productObj);
            if (bgChangeScript != null)
            {
                bgChangeScript.TriggerProduct(match.bgIndex);
            }

            // Trigger Video if assigned
            if (!string.IsNullOrEmpty(match.videoFilename))
            {
                var player = FindFirstObjectByType<VideoController>();
                if (player != null)
                {
                    player.PlayResolvedVideo(match.videoFilename);
                }
            }
            else
            {
                // If no video, make sure to stop any previous video/panel
                var player = FindFirstObjectByType<VideoController>();
                if (player != null)
                {
                    player.StopVideo(false);
                }
            }
        }
        else
        {
            Debug.LogWarning($"[CardScript] Product not found: {productName}");
        }
    }

    private bool IsMatch(string input, string target)
    {
        return string.Equals(input, target, System.StringComparison.OrdinalIgnoreCase);
    }

    private void SetActive(GameObject obj)
    {
        if (obj != null) obj.SetActive(true);
    }

    // New Routine: ปิดอันเก่า -> เปลี่ยนของ -> เปิดอันใหม่
    private IEnumerator SwitchProductRoutine(string productName)
    {
        // Update StageManager IMMEDIATELY to stop LoopChat and handle state
        if (StageManager.Instance != null) StageManager.Instance.SwitchStage(StageManager.Stage.ProductDetail);

        // Hide FAQ when switching/showing products
        FAQController faq = Object.FindFirstObjectByType<FAQController>();
        if (faq != null) faq.Hide();
        // 1. ถ้า Carousel เปิดอยู่ สั่งปิดแล้วรอก่อน
        // 1. ถ้า Carousel เปิดอยู่ สั่งปิดแล้วรอก่อน
        if (carouselScript != null && carouselScript.carouselContainer.activeSelf)
        {
            Debug.Log("[CardScript] Carousel active. Closing and waiting...");
            carouselScript.HideCarousel();
            yield return new WaitForSeconds(0.5f); // Reduced wait time
        }

        // Optimization: If same product is already showing, keep it (don't close/re-open)
        if (isShowing && IsMatch(productName, currentProductName))
        {
            // ... existing optimization logic
            Debug.Log($"[CardScript] Keeping '{productName}' active (Skip reload)");
            lastShowTime = Time.time;
            yield break;
        }

        Debug.Log($"[CardScript] Swapping to product: {productName}");

        // 1. ถ้าเปิดอยู่ ให้ปิดก่อน (Animation OUT)
        if (isShowing)
        {
            // ... existing close logic
            if (bgChangeScript != null) bgChangeScript.CloseCurrentBG();

            // Stop Video immediately when starting to hide
            var player = FindFirstObjectByType<VideoController>();
            if (player != null) player.StopVideo(false);

            yield return StartCoroutine(PlayHideSequence());

            isShowing = false;
        }

        // 2. เปลี่ยนของข้างใน (Swap Product Logic)
        ShowProduct(productName);

        // Update StageManager (Moved to top)
        // if (StageManager.Instance != null) StageManager.Instance.SwitchStage(StageManager.Stage.ProductDetail);

        // --- NEW LOGIC: Check if this product launched a video ---
        // If the product has a video associated, ShowProduct() would have triggered it.
        // In that case, we do NOT want the Card UI to show up over the video.
        bool hasVideo = false;
        if (!string.IsNullOrEmpty(productName) && products != null)
        {
            var match = products.Find(p => IsMatch(p.productName, productName));
            if (match != null && !string.IsNullOrEmpty(match.videoFilename))
            {
                hasVideo = true;
            }
        }

        if (hasVideo)
        {
            // Debug.Log($"[CardScript] Product '{productName}' has video. Skipping Card UI animation (Visuals hidden, Logic active).");

            // Ensure the product object is HIDDEN while video plays
            // ShowProduct() enabled it, but since we are skipping the card animation, we should hide the content too.
            // When OnVideoFinished calls PlayShowSequence, we will need to ensure it gets re-enabled or ShowProduct is re-called?
            // Actually, PlayShowSequence just animates the container. If the content is hidden, animating container shows nothing.
            // So we should probably keep it hidden here, and in OnVideoFinished -> PlayShowSequence -> we might need to re-enable it?
            // BETTER APPROACH: Just hide it now. Since ShowProduct sets currentProductName, we can rely on that.

            if (!string.IsNullOrEmpty(productName) && products != null)
            {
                var match = products.Find(p => IsMatch(p.productName, productName));
                if (match != null && match.productObj != null)
                {
                    match.productObj.SetActive(false);
                }
            }

            // Critical Change:
            // We set isShowing = true, effectively marking this card as "Active but Obscured by Video".
            // The visual animation (PlayShowSequence) is SKIPPED here.
            // When the video finishes, OnVideoFinished() will observe isShowing == true and call PlayShowSequence() to reveal the card.
            isShowing = true;
            lastShowTime = Time.time; // Update time so auto-hide logic works correctly if needed

            // Ensure no lingering routine
            currentRoutine = null;
            yield break;
        }
        // ---------------------------------------------------------

        // 3. เปิดใหม่ (Animation IN)
        isShowing = true;
        lastShowTime = Time.time;

        // Show Back Button
        if (backButton != null)
        {
            backButton.SetActive(true);
            UnityEngine.UI.Button btn = backButton.GetComponent<UnityEngine.UI.Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(HideCard);
            }
        }

        yield return StartCoroutine(PlayShowSequence());

        // ✅ NEW: Trigger AI Animation for static cards (just like Video)
        // Since we are showing a card (Stage 3), we want the AI to pop up and explain it.
        var videoCtrl = FindFirstObjectByType<VideoController>();
        if (videoCtrl != null)
        {
            videoCtrl.TriggerAIAnimation();
        }

        currentRoutine = null;
    }

    [ContextMenu("Show Card")]
    public void ShowCard()
    {
        if (isShowing) return; // ถ้าโชว์อยู่แล้วไม่ต้องทำซ้ำ (หรืออยากให้ Restart ก็ลบบรรทัดนี้ได้)

        // Hide FAQ when Card is shown
        FAQController faq = Object.FindFirstObjectByType<FAQController>();
        if (faq != null) faq.Hide();

        isShowing = true;
        lastShowTime = Time.time;

        if (currentRoutine != null) StopCoroutine(currentRoutine);
        currentRoutine = StartCoroutine(PlayShowSequence());
    }

    [ContextMenu("Hide Card")]
    public void HideCard()
    {
        if (!isShowing)
        {
            // Failsafe: Ensure visual objects are off even if state is "hidden"
            HideAllProducts();
            return;
        }

        isShowing = false;

        // Close BG when hiding card
        if (bgChangeScript != null) bgChangeScript.CloseCurrentBG();

        // Stop Video when hiding card manually
        var player = FindFirstObjectByType<VideoController>();
        if (player != null) player.StopVideo(); // Restore UI (ButtonBack) when Closing Card

        if (currentRoutine != null) StopCoroutine(currentRoutine);
        currentRoutine = StartCoroutine(PlayHideSequence());

        // Back to Main Page? or Carousel?
        // Usually back to Carousel if it was open, or Main Page?
        // Let's default to Main Page for now, unless StageManager handles history.
        if (StageManager.Instance != null && StageManager.Instance.currentStage == StageManager.Stage.ProductDetail)
        {
            StageManager.Instance.SwitchStage(StageManager.Stage.MainPage);
        }

        // Stop AI Audio immediately
        if (recordAudio != null) recordAudio.StopPlayback();

        if (backButton != null) backButton.SetActive(false);
    }

    private void OnVideoFinished(bool shouldResume)
    {
        // Re-open card if it was supposed to be showing (product video) OR if it was interrupted
        // AND validation from VideoController says we should resume (not timeout/forced)
        if ((isShowing || wasShowingBeforeInterruption) && shouldResume)
        {
            // Debug.Log("[CardScript] Video finished -> Re-showing Card");
            wasShowingBeforeInterruption = false;

            if (currentRoutine != null) StopCoroutine(currentRoutine);
            isShowing = true; // Ensure flag is true if we are resuming

            // Restore content visibility because we might have hidden it in SwitchProductRoutine
            if (!string.IsNullOrEmpty(currentProductName))
            {
                // We reuse ShowProduct logic to ensure the object is active.
                var match = products.Find(p => IsMatch(p.productName, currentProductName));
                if (match != null && match.productObj != null)
                {
                    match.productObj.SetActive(true);
                }
            }

            currentRoutine = StartCoroutine(PlayShowSequence());
        }
    }

    IEnumerator PlayShowSequence()
    {
        // ✅ ถ้าสินค้ามี Video ไม่ต้อง trigger ButtonMove ที่นี่ (VideoController จะทำเอง)
        bool hasVideo = false;
        if (!string.IsNullOrEmpty(currentProductName) && products != null)
        {
            var match = products.Find(p => IsMatch(p.productName, currentProductName));
            if (match != null && !string.IsNullOrEmpty(match.videoFilename))
            {
                hasVideo = true;
            }
        }

        if (!hasVideo)
        {
            // Trigger Button Move (Hide Mic) - เฉพาะสินค้าที่ไม่มี Video
            TriggerMicButton("ButtonMove");
        }

        // 1. CIn (เข้า)
        animator.SetTrigger("CIn");
        // Debug.Log("Card: Showing (CIn)");
        yield return new WaitForSeconds(delayIn);

        // 2. CIdle (อยู่นิ่ง)
        animator.SetTrigger("CIdle");
        // Debug.Log("Card: Idle (CIdle)");
    }

    IEnumerator PlayHideSequence()
    {
        // 1. Cout (ออก)
        animator.SetTrigger("Cout");
        // Debug.Log("Card: Hiding (Cout)");
        yield return new WaitForSeconds(delayOut);

        // 2. CIdleOut (รอนอกจอ)
        animator.SetTrigger("CIdleOut");
        // Debug.Log("Card: Hidden (CIdleOut)");

        // Ensure products are disabled after animation
        HideAllProducts();
    }

    // --- NEW: Helper to Trigger Mic Button Animation ---
    private void TriggerMicButton(string triggerName)
    {
        var player = FindFirstObjectByType<VideoController>();
        if (player != null && player.micButton != null)
        {
            Animator micAnim = player.micButton.GetComponent<Animator>();
            if (micAnim != null)
            {
                micAnim.SetTrigger(triggerName);
                // Debug.Log($"[CardScript] Triggered Mic Button: {triggerName}");
            }
        }
    }

    // --- NEW: Public Method for StageManager ---
    public void ShowProductPublic(string productName)
    {
        // Debug.Log($"[CardScript] ShowProductPublic called for: {productName}");
        if (currentRoutine != null) StopCoroutine(currentRoutine);
        currentRoutine = StartCoroutine(SwitchProductRoutine(productName));
    }
}
