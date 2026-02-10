using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class CorouselScript : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    [Header("Components")]
    public ScrollRect scrollRect;
    public RectTransform contentPanel;
    public RectTransform sampleItem;

    [Header("Settings")]
    public int startingIndex = 1; // กำหนดให้เริ่มที่ใบที่ 1 (ใบกลาง)
    public float snapSpeed = 10f;
    public float showDelay = 1.5f; // Delay before showing carousel
    public float autoCloseTime = 180f; // Seconds before auto-closing carousel

    [Header("Animation Settings")]
    public bool startFromEnd = false; // เปิดใช้งานโหมดเริ่มจากท้าย
    public int targetIndex = 0; // เป้าหมายที่จะเลื่อนมาหา (Index 0 = อันแรก)
    public float startupSpeed = 500f; // ความเร็วในการเลื่อนตอนเปิด (pixels/sec)

    [Header("Scale Settings")]
    public float scaleOffset = 400f;
    public float maxScale = 1.2f;
    public float minScale = 0.8f;

    [Header("UI Control")]
    public GameObject carouselContainer; // The parent object to show/hide
    public GameObject backButton; // NEW: Back Button reference
    public Animator animator; // Animator for fade effects

    // ตัวแปรระบบ
    private float itemFullWidth;
    private int itemCount;
    private bool isDragging = false;
    private bool isSnapping = false;
    private bool isStartupScroll = false; // เช็คว่ากำลังเลื่อนตอนเปิดอยู่ไหม
    private Vector2 targetPos;
    private float lastShowTime = 0f; // เวลาล่าสุดที่สั่งเปิด (กันไม่ให้หุบเองทันที)
    private RecordAudio recordAudio; // Reference to RecordAudio
    private Coroutine autoCloseCoroutine;
    private float lastInteractionTime;
    private CardScript cardScript; // Reference to CardScript for video checking
    private bool isProcessingClick = false; // Prevent rapid card clicks

    IEnumerator Start()
    {
        // รอ 1 เฟรมเพื่อให้ UI Layout คำนวณขนาดให้เสร็จก่อน
        yield return null;

        // 1. คำนวณขนาด Item และ Spacing
        HorizontalLayoutGroup layoutGroup = contentPanel.GetComponent<HorizontalLayoutGroup>();
        float spacing = layoutGroup ? layoutGroup.spacing : 0f;

        if (sampleItem == null && contentPanel.childCount > 0)
            sampleItem = contentPanel.GetChild(0).GetComponent<RectTransform>();

        if (sampleItem != null)
            itemFullWidth = sampleItem.rect.width + spacing;

        itemCount = contentPanel.childCount;

        // Debug.Log($"Carousel Init: Count={itemCount}, Width={itemFullWidth}, StartFromEnd={startFromEnd}");

        // 2. ตรวจสอบโหมดการเริ่ม (ทำเฉพาะกรณีไม่ได้เล่น Intro)
        if (!isIntroductionRunning)
        {
            if (startFromEnd)
            {
                // เริ่มที่ตัวสุดท้าย
                int lastIndex = itemCount - 1;
                float startX = lastIndex * itemFullWidth;
                contentPanel.anchoredPosition = new Vector2(-startX, contentPanel.anchoredPosition.y);

                // ตั้งเป้าไปที่ targetIndex
                float targetX = targetIndex * itemFullWidth;
                targetPos = new Vector2(-targetX, contentPanel.anchoredPosition.y);

                isSnapping = true; // สั่งให้เริ่มเลื่อน
                isStartupScroll = true; // ระบุว่าเป็นช่วง Startup Animation

                // Debug.Log($"Carousel Startup: Moving from Index {lastIndex} to {targetIndex}");
            }
            else
            {
                // เริ่มที่ startingIndex ทันที (แบบเดิม)
                float startX = startingIndex * itemFullWidth;
                contentPanel.anchoredPosition = new Vector2(-startX, contentPanel.anchoredPosition.y);
            }
        }

        // อัปเดตขนาดให้ใบที่เลือกใหญ่ขึ้นทันที
        UpdateScale();

        // 3. Setup Click Listeners
        SetupClickListeners();

        // 4. Setup Back Button handled in ShowCarousel
        if (backButton != null) backButton.SetActive(false);
    }

    private bool isIntroductionRunning = false;

    private void OnEnable()
    {
        // Debug.Log("CorouselScript: OnEnable Called"); // Check if script is enabling
        // RecordAudio.OnFunctionCallResult += HandleFunctionCall; // DISABLED: Centralized in StageManager
        // Dialoguebox.OnDialogueStarting += OnDialogueStarting; // DISABLED: Conflicts with StageManager showing carousel

        recordAudio = FindFirstObjectByType<RecordAudio>();
        if (recordAudio != null) recordAudio.OnStartRecording += OnMicStarted;

        cardScript = FindFirstObjectByType<CardScript>();
    }

    // Trigger animations - Moved to HandleFunctionCall to prevent conflicts
    /*
    if (animator != null)
    {
        StartCoroutine(PlayAnimationSequence());
    }
    else
    {
        Debug.LogError("CorouselScript: Animator is not assigned in the Inspector!");
    }
    */


    private IEnumerator PlayAnimationSequence()
    {
        yield return null; // Wait 1 frame for SetActive to stabilize

        // Debug.Log("AnimSequence: Playing 'Fade up'");
        animator.Play("Fade up");

        yield return new WaitForSeconds(0.5f); // Duration of Fade up

        // Debug.Log("AnimSequence: Playing 'idle'");
        animator.Play("idle");

    }

    private IEnumerator PlayHideAnimationSequence()
    {
        // Debug.Log("HideAnimSequence: Playing 'Fade out'");
        animator.Play("Fade out");

        yield return new WaitForSeconds(0.2f); // Duration of Fade out

        // Debug.Log("HideAnimSequence: Playing 'idle out'");
        animator.Play("idle out");
        yield return new WaitForSeconds(0.2f); // Duration of idle out before disable

        if (carouselContainer != null)
        {
            // Hide Back Button
            if (backButton != null) backButton.SetActive(false);

            // Debug.Log("HideAnimSequence: Deactivating Container");
            carouselContainer.SetActive(false);

            // Restore FAQ when Carousel is gone
            // FAQController faq = Object.FindFirstObjectByType<FAQController>();
            // if (faq != null) faq.Show(); // DISABLED: Controlled by StageManager
        }
    }

    // ResetAllTriggers is no longer needed with Play()


    private void OnDisable()
    {
        // RecordAudio.OnFunctionCallResult -= HandleFunctionCall; // DISABLED
        // Dialoguebox.OnDialogueStarting -= OnDialogueStarting;
        if (recordAudio != null) recordAudio.OnStartRecording -= OnMicStarted;
    }

    private void OnMicStarted()
    {
        // ถ้าผู้ใช้กดไมค์ แปลว่าจบช่วง "ดูของ" แล้ว ให้ยกเลิกเวลาหน่วงได้เลย
        // เพื่อให้ Dialogue ครั้งต่อไปสามารถสั่งปิด Carousel ได้ทันทีไม่ต้องรอ 3 วิ
        lastShowTime = 0f;
    }

    private void OnDialogueStarting()
    {
        // ถ้าเพิ่งสั่ง Show มาไม่ถึง 3 วินาที อย่าเพิ่งหุบ (เพราะ AI อาจจะกำลังพูดแนะนำสินค้านั้นอยู่)
        if (Time.time - lastShowTime < 3.0f)
        {
            // Debug.Log($"[Carousel] Ignoring Auto-Hide (Just shown at {lastShowTime})");
            return;
        }

        // เมื่อ Dialogue เริ่มเตรียมตัวแสดง (ก่อน 1 วิ) ให้รีบซ่อน Carousel ทันที
        // Call via StageManager logic
        if (StageManager.Instance != null && StageManager.Instance.currentStage == StageManager.Stage.Carousel)
        {
            StageManager.Instance.GoToMainPage();
        }
        else
        {
            HideCarousel();
        }
    }

    public void HideCarousel()
    {
        if (carouselContainer != null && carouselContainer.activeSelf)
        {
            if (animator != null)
            {
                if (autoCloseCoroutine != null) StopCoroutine(autoCloseCoroutine);
                StopAllCoroutines();
                StartCoroutine(PlayHideAnimationSequence());
            }
            else
            {
                carouselContainer.SetActive(false);
            }

            // Notify StageManager we are leaving Carousel (e.g., back to Main or IDLE?)
            // If we hide explicitly, we might want to default to Main Page or First Page?
            // VISUAL ONLY: StageManager handles logic now.
        }
    }

    // --- NEW: Public Close Method for Button/Logic ---
    public void CloseCarousel()
    {
        // Debug.Log("[Carousel] CloseCarousel requested via StageManager");
        if (StageManager.Instance != null)
        {
            StageManager.Instance.GoToMainPage(); // Or GoBack()
        }
        else
        {
            HideCarousel(); // Fallback
        }
    }

    private void HandleFunctionCall(RecordAudio.FunctionCallEntry entry)
    {
        string actionName = entry.name;
        if (actionName == "product_card")
        {
            string action = entry.response?.action ?? entry.args?.action;
            // Debug.Log($"Carousel Action: {action}");

            if (carouselContainer != null)
            {
                if (action == "show")
                {
                    StopAllCoroutines(); // Stop previous animations
                    StartCoroutine(ShowCarouselDelayed());
                }
                else if (action == "hide")
                {
                    HideCarousel(); // Use the animation sequence
                }
            }
        }
    }

    public void ShowCarousel()
    {
        // Debug.Log("[CorouselScript] ShowCarousel called externally.");
        StopAllCoroutines();
        StartCoroutine(ShowCarouselDelayed());
    }

    private IEnumerator ShowCarouselDelayed()
    {
        // Debug.Log($"[Carousel] Waiting {showDelay}s before showing...");

        // Hide FAQ when Carousel starts processing
        FAQController faq = Object.FindFirstObjectByType<FAQController>();
        if (faq != null) faq.Hide();

        yield return new WaitForSeconds(showDelay);

        // Reduce race condition: Hide FAQ again just before showing
        if (faq != null) faq.Hide();

        isIntroductionRunning = true; // Flag to prevent Start from overriding

        // Update StageManager if not already set (Avoid loop)
        if (StageManager.Instance != null && StageManager.Instance.currentStage != StageManager.Stage.Carousel)
        {
            StageManager.Instance.SwitchStage(StageManager.Stage.Carousel);
        }

        carouselContainer.SetActive(true);
        if (backButton != null)
        {
            backButton.SetActive(true); // Show Back Button
            Button btn = backButton.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(CloseCarousel);
            }
        }
        if (autoCloseCoroutine != null) StopCoroutine(autoCloseCoroutine);
        autoCloseCoroutine = StartCoroutine(AutoCloseRoutine());

        lastShowTime = Time.time;
        lastInteractionTime = Time.time;

        if (animator != null)
        {
            StartCoroutine(PlayAnimationSequence());
        }

        // เริ่ม Animation การเลื่อน (Intro Scroll)
        StartCoroutine(PlayIntroScrollSequence());
    }

    private IEnumerator PlayIntroScrollSequence()
    {
        // Debug.Log("[Carousel] Starting Intro Sequence");
        isSnapping = false; // Reset Snapping

        // Init Dimensions ถ้ายังไม่ได้คำนวณ (เผื่อ Start ยังไม่รัน)
        if (itemFullWidth == 0 || itemCount == 0)
        {
            // 1. คำนวณขนาด Item และ Spacing (ยืม logic จาก Start)
            HorizontalLayoutGroup layoutGroup = contentPanel.GetComponent<HorizontalLayoutGroup>();
            float spacing = layoutGroup ? layoutGroup.spacing : 0f;

            if (sampleItem == null && contentPanel.childCount > 0)
                sampleItem = contentPanel.GetChild(0).GetComponent<RectTransform>();

            if (sampleItem != null)
                itemFullWidth = sampleItem.rect.width + spacing;
        }
        // Reset Scale of all items
        for (int i = 0; i < itemCount; i++)
        {
            Transform t = contentPanel.GetChild(i);
            t.localScale = Vector3.one * minScale;
        }

        // Stop AI Audio immediately -- REMOVED to let AI finish talking
        // if (recordAudio != null) recordAudio.StopPlayback();
        // 1. เริ่มที่ตัวแรกสุด (Index 0)
        // Debug.Log("[Carousel] Step 1: Jump to Start");
        float startX = 0;
        contentPanel.anchoredPosition = new Vector2(-startX, contentPanel.anchoredPosition.y);

        yield return new WaitForSeconds(0.1f); // รอเล็กน้อย

        // Check again
        if (itemCount == 0) yield break;

        // 2. เลื่อนไปตัวสุดท้าย
        // Debug.Log("[Carousel] Step 2: Scroll to Last");
        int lastIndex = itemCount - 1;
        float lastX = lastIndex * itemFullWidth;
        targetPos = new Vector2(-lastX, contentPanel.anchoredPosition.y);

        isSnapping = true;
        isStartupScroll = true; // ใช้ความเร็วคงที่ (MoveTowards)

        // รอจนกว่าจะถึงเป้าหมาย (หรือใกล้เคียง) หรือ Timeout
        float timer = 0f;
        float maxWait = 3.0f; // 2 seconds timeout

        while (isSnapping && !isDragging)
        {
            if (Vector2.Distance(contentPanel.anchoredPosition, targetPos) < 10f)
            {
                break;
            }

            if (timer > maxWait)
            {
                // Debug.LogWarning("[Carousel] Intro Scroll Timeout (Step 2). Forcing next step.");
                break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        if (isDragging)
        {
            isIntroductionRunning = false;
            yield break;
        }

        // Force Snap
        contentPanel.anchoredPosition = targetPos;

        // Debug.Log("[Carousel] Reached End. Waiting...");
        yield return new WaitForSeconds(0.5f); // หยุดค้างที่ตัวท้ายแป๊บนึง

        // 3. หมุนกลับมาตรงกลาง (startingIndex)
        // Debug.Log($"[Carousel] Step 3: Return to Center ({startingIndex})");
        float centerX = startingIndex * itemFullWidth;
        targetPos = new Vector2(-centerX, contentPanel.anchoredPosition.y);

        isSnapping = true;
        isStartupScroll = false; // กลับมาใช้ความเร็ว Smooth (Lerp)

        isIntroductionRunning = false; // Done
        // Debug.Log("[Carousel] Intro Sequence Complete");
    }

    void Update()
    {
        // Logic Scale (ซูม)
        UpdateScale();

        // Logic Snap (เลื่อนเข้ากลาง)
        if (!isDragging && isSnapping)
        {
            if (isStartupScroll)
            {
                // ใช้ MoveTowards เพื่อให้ความเร็วคงที่ เห็นการ์ดครบทุกใบ
                contentPanel.anchoredPosition = Vector2.MoveTowards(contentPanel.anchoredPosition, targetPos, startupSpeed * Time.deltaTime);
            }
            else
            {
                // ใช้ Lerp เพื่อความนุ่มนวลเวลาเลื่อนปกติ
                contentPanel.anchoredPosition = Vector2.Lerp(contentPanel.anchoredPosition, targetPos, snapSpeed * Time.deltaTime);
            }

            if (Vector2.Distance(contentPanel.anchoredPosition, targetPos) < 1f)
            {
                contentPanel.anchoredPosition = targetPos;
                isSnapping = false;
                isStartupScroll = false; // จบช่วง Startup แล้ว
                scrollRect.velocity = Vector2.zero;
            }
        }
    }

    void UpdateScale()
    {
        if (contentPanel.childCount == 0) return;
        float centerX = scrollRect.transform.position.x;

        foreach (Transform child in contentPanel)
        {
            float dist = Mathf.Abs(child.position.x - centerX);
            float scaleRatio = 1 - Mathf.Clamp(dist / scaleOffset, 0f, 1f);
            child.localScale = Vector3.one * Mathf.Lerp(minScale, maxScale, scaleRatio);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true;
        isSnapping = false;
        isStartupScroll = false; // ถ้าใช้นิ้วเลื่อน ให้ยกเลิก Auto Scroll ทันที
        lastInteractionTime = Time.time; // Reset Timer
    }

    private IEnumerator AutoCloseRoutine()
    {
        lastInteractionTime = Time.time;
        while (Time.time - lastInteractionTime < autoCloseTime)
        {
            yield return new WaitForSeconds(1f);
        }

        // Debug.Log("[Carousel] Auto Auto-Close triggered due to inactivity.");
        Debug.Log("[Carousel] Auto Auto-Close triggered due to inactivity.");
        CloseCarousel();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
        if (contentPanel.childCount == 0 || itemFullWidth == 0) return;

        // คำนวณจุดที่จะ Snap
        float currentPosX = -contentPanel.anchoredPosition.x;
        int targetIndex = Mathf.RoundToInt(currentPosX / itemFullWidth);

        // จำกัดขอบเขต (ไม่ให้เลื่อนเลยตัวแรก หรือ เลยตัวสุดท้าย)
        targetIndex = Mathf.Clamp(targetIndex, 0, itemCount - 1);

        float targetX = targetIndex * itemFullWidth;
        targetPos = new Vector2(-targetX, contentPanel.anchoredPosition.y);

        isSnapping = true;
    }

    // --- NEW: Click to Ask Logic ---

    private void SetupClickListeners()
    {
        foreach (Transform child in contentPanel)
        {
            EventTrigger trigger = child.GetComponent<EventTrigger>();
            if (trigger == null) trigger = child.gameObject.AddComponent<EventTrigger>();
            trigger.triggers.Clear();

            // 1. Click Listener
            EventTrigger.Entry clickEntry = new EventTrigger.Entry();
            clickEntry.eventID = EventTriggerType.PointerClick;
            clickEntry.callback.AddListener((data) => { OnCardClicked(child.name, (BaseEventData)data); });
            trigger.triggers.Add(clickEntry);

            // 2. Forward Drag Events to ScrollRect/Parent
            // This ensures dragging the card works for scrolling
            AddEventTriggerListener(trigger, EventTriggerType.BeginDrag, (data) =>
                ExecuteEvents.Execute(scrollRect.gameObject, data as PointerEventData, ExecuteEvents.beginDragHandler));

            AddEventTriggerListener(trigger, EventTriggerType.Drag, (data) =>
                ExecuteEvents.Execute(scrollRect.gameObject, data as PointerEventData, ExecuteEvents.dragHandler));

            AddEventTriggerListener(trigger, EventTriggerType.EndDrag, (data) =>
                ExecuteEvents.Execute(scrollRect.gameObject, data as PointerEventData, ExecuteEvents.endDragHandler));
        }
    }

    private void AddEventTriggerListener(EventTrigger trigger, EventTriggerType eventType, System.Action<BaseEventData> callback)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = eventType;
        entry.callback.AddListener((data) => callback(data));
        trigger.triggers.Add(entry);
    }

    public void OnCardClicked(string productName, BaseEventData baseData)
    {
        // Prevent rapid clicks - ignore if already processing
        if (isProcessingClick)
        {
            Debug.Log("[Carousel] Blocked - already processing a click");
            return;
        }

        // 1. Check Drag Distance (Tap vs Scroll)
        PointerEventData data = baseData as PointerEventData;
        if (data != null)
        {
            float dragDistance = Vector2.Distance(data.pressPosition, data.position);
            // Increased from 20f to 80f for better TouchScreen sensitivity
            if (dragDistance > 80f) // Keeping 80f as dragThreshold is not defined in the provided context
            {
                // Debug.Log("Ignored click due to drag distance");
                return;
            }
        }

        // 2. Backup checks
        if (isDragging) return;
        if (scrollRect != null && Mathf.Abs(scrollRect.velocity.x) > 50f) return;

        // Lock immediately to prevent rapid clicks
        isProcessingClick = true;

        // Stop previous audio immediately
        if (recordAudio != null) recordAudio.StopPlayback();
        else
        {
            var ra = FindFirstObjectByType<RecordAudio>();
            if (ra != null) ra.StopPlayback();
        }

        // Debug.Log($"[Carousel] Card Tapped: {productName} | Press: {data.pressPosition}, Curr: {data.position}");

        // Call AI or Show Card directly
        bool hasVideo = false;
        bool productExists = false;

        if (cardScript != null && cardScript.products != null)
        {
            var match = cardScript.products.Find(p => string.Equals(p.productName, productName, System.StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                productExists = true;
                if (!string.IsNullOrEmpty(match.videoFilename))
                {
                    hasVideo = true;
                }
            }
        }
        else
        {
            Debug.LogError("[Corousel] CardScript or Products list is null!");
        }

        Debug.Log($"[Carousel] Clicked: '{productName}'. Exists: {productExists}, HasVideo: {hasVideo}");

        if (hasVideo && recordAudio != null)
        {
            // Debug.Log("[Carousel] Calling SendTextQuery...");
            recordAudio.SendTextQuery($"ขอข้อมูล {productName} หน่อย");
            // Debug.Log("[Carousel] SendTextQuery Called.");
        }
        else if (productExists)
        {
            Debug.Log($"[Carousel] No video found for {productName}. Asking AI...");
            // Unified Path: Ask AI even for static cards, so Server decides (and calls show_product)
            if (recordAudio != null)
            {
                recordAudio.SendTextQuery($"ขอข้อมูล {productName} หน่อย");
            }
        }
        else
        {
            // Fallback: If not in our list, just ask AI (Maybe it's a general question or AI knows about it)
            Debug.Log($"[Carousel] Product '{productName}' not found in local list. Fallback to AI.");
            if (recordAudio != null)
            {
                recordAudio.SendTextQuery($"ขอข้อมูล {productName} หน่อย");
            }
        }

        // Start watching for AI to finish
        StartCoroutine(WaitForAIToFinishThenUnlock());
    }

    private IEnumerator WaitForAIToFinishThenUnlock()
    {
        // Wait a bit for AI to start processing
        yield return new WaitForSeconds(0.5f);

        // Wait while AI is playing audio
        while (recordAudio != null && (recordAudio.IsPlaying || recordAudio.IsProcessing))
        {
            yield return new WaitForSeconds(0.2f);
        }

        // Re-enable clicks when AI finishes
        isProcessingClick = false;
    }
}