using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class Dialoguebox : MonoBehaviour
{
    public static event System.Action<bool> OnVisibilityChanged; // Event for external scripts
    public static event System.Action OnDialogueStarting; // Event triggers BEFORE showing (for delay)

    [Header("UI Components")]
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private TMP_Text UserText;
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private GameObject userPanel;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private float typingSpeed = 0.05f;

    [Header("Settings")]
    [SerializeField] private float autoHideDuration = 90f;

    [Header("Text Layout")]
    [Tooltip("Left, Top, Right, Bottom")]
    [SerializeField] private Vector4 textPadding = new Vector4(50, 30, 50, 30);

    private string targetTextBuffer = "";
    private string userTextBuffer = ""; // (ใหม่) บัฟเฟอร์สำหรับข้อความ User
    private Coroutine typingCoroutine;
    private Coroutine userTypingCoroutine; // (ใหม่) ตัวพิมพ์ User
    private Coroutine hideCoroutine;
    private RecordAudio recordAudio;
    private Coroutine userHideCoroutine;
    private Coroutine delayShowCoroutine; // (ใหม่) รอ 1 วิค่อยโชว์

    [Header("User Bubble Auto Size")]
    [SerializeField] private RectTransform userBubbleRect;   // ลาก PanelUserbox มาใส่
    [SerializeField] private float userBubbleMaxWidth = 700f; // ความกว้างสูงสุดของบับเบิล
    [SerializeField] private Vector2 userBubblePadding = new Vector2(60f, 20f); // (L+R, T+B) หรือปรับตามชอบ

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null && dialoguePanel != null) canvasGroup = dialoguePanel.GetComponent<CanvasGroup>();

        // Find RecordAudio in Awake or fallback to OnEnable
        if (recordAudio == null) recordAudio = FindFirstObjectByType<RecordAudio>();

        SetVisibility(false);
        SetUserVisibility(false); // ซ่อน User Panel ตั้งแต่เริ่มต้น
    }

    private Coroutine loadingCoroutine; // New coroutine for loading animation

    private void Start()
    {
        // --- Setup ของ AI (เหมือนเดิม) ---
        GameObject panelObj = dialoguePanel != null ? dialoguePanel : gameObject;
        if (panelObj.GetComponent<RectMask2D>() == null) panelObj.AddComponent<RectMask2D>();

        if (scrollRect == null) scrollRect = panelObj.GetComponent<ScrollRect>();
        if (scrollRect == null) scrollRect = panelObj.AddComponent<ScrollRect>();

        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.viewport = panelObj.GetComponent<RectTransform>();

        if (dialogueText != null) scrollRect.content = dialogueText.rectTransform;

        if (dialogueText != null)
        {
            if (dialogueText.GetComponent<ContentSizeFitter>() == null)
            {
                var fitter = dialogueText.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            dialogueText.alignment = TextAlignmentOptions.TopLeft;
            dialogueText.overflowMode = TextOverflowModes.Overflow;
            dialogueText.enableWordWrapping = true;
            dialogueText.margin = textPadding;

            RectTransform txtRect = dialogueText.rectTransform;
            txtRect.pivot = new Vector2(0.5f, 1f);
            txtRect.anchorMin = new Vector2(0f, 1f);
            txtRect.anchorMax = new Vector2(1f, 1f);
            txtRect.anchoredPosition = new Vector2(0f, 0f);
            txtRect.sizeDelta = new Vector2(0f, txtRect.sizeDelta.y);

        }

        // --- (ใหม่) Setup ของ UserText ให้ยืดหยุ่น ---
        if (UserText != null)
        {
            // ทำให้ Text สูงตามข้อความอัตโนมัติ
            // ทำให้ Text สูงตามข้อความอัตโนมัติ (ปิดไว้เพราะใช้ Manual Calculation แทน)
            /*
            if (UserText.GetComponent<ContentSizeFitter>() == null)
            {
                var fitter = UserText.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; // ยืดความสูง
            }
            */
            UserText.enableWordWrapping = true; // ตัดบรรทัด


            // Setup RectTransform to grow UPWARDS from the BOTTOM to avoid overlap
            RectTransform userRect = UserText.rectTransform;
            userRect.pivot = new Vector2(0.5f, 0f); // Pivot Bottom-Center
            userRect.anchorMin = new Vector2(0f, 0f); // Anchor Bottom-Left
            userRect.anchorMax = new Vector2(1f, 0f); // Anchor Bottom-Right
            userRect.anchoredPosition = new Vector2(0f, 50f); // 50 units from bottom
        }
        // ------------------------------------------

        // Subscription check in Start in case Awake missed it or it was re-instantiated
    }

    private void Update() { }

    private void OnEnable()
    {
        // Find RecordAudio if null (safety)
        if (recordAudio == null) recordAudio = FindFirstObjectByType<RecordAudio>();

        // Clean up first to avoid double subscription
        RecordAudio.OnTranscription -= HandleTranscription;
        RecordAudio.OnUserTranscription -= HandleUserTranscription;

        RecordAudio.OnTranscription += HandleTranscription;
        RecordAudio.OnUserTranscription += HandleUserTranscription;

        // Re-subscribe to State Events (Missing in previous logic)
        if (recordAudio != null)
        {
            recordAudio.OnStartRecording -= ShowListeningStatus;
            recordAudio.OnStartRecording -= ShowListeningStatus;
            recordAudio.OnStopRecording -= ShowLoadingStatus;
            recordAudio.OnTextSent -= OnNewTextQuery;
            recordAudio.OnTextSent -= OnNewTextQuery;

            recordAudio.OnStartRecording += ShowListeningStatus;
            recordAudio.OnStopRecording += ShowLoadingStatus;
            recordAudio.OnTextSent += OnNewTextQuery;
        }
    }

    private void OnDisable()
    {
        // Unsubscribe all
        RecordAudio.OnTranscription -= HandleTranscription;
        RecordAudio.OnUserTranscription -= HandleUserTranscription;

        if (recordAudio != null)
        {
            recordAudio.OnStartRecording -= ShowListeningStatus;
            recordAudio.OnStopRecording -= ShowLoadingStatus;
            recordAudio.OnTextSent -= OnNewTextQuery;
        }
    }

    private void SetVisibility(bool isVisible)
    {
        // Debug.Log($"[Dialoguebox] SetVisibility: {isVisible}");
        OnVisibilityChanged?.Invoke(isVisible); // Trigger event

        if (isVisible && dialoguePanel != null && dialoguePanel != gameObject) dialoguePanel.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = isVisible ? 1f : 0f;
            canvasGroup.blocksRaycasts = isVisible;
        }
        else if (dialoguePanel != null && dialoguePanel != gameObject) dialoguePanel.SetActive(isVisible);
    }

    private void SetUserVisibility(bool isVisible)
    {
        Debug.Log($"[Dialoguebox] SetUserVisibility: {isVisible}"); // ✅ Debug Log
        if (userPanel != null) userPanel.SetActive(isVisible);
    }

    private void ShowListeningStatus()
    {
        // ✅ Only clear AI text, keep User panel visible for transcription
        targetTextBuffer = "";
        if (dialogueText != null) dialogueText.text = "";
        
        // ✅ NEW: Clear User Text as well to prevent old FAQ spam from showing
        userTextBuffer = "";
        if (UserText != null) UserText.text = "";
        
        if (typingCoroutine != null) { StopCoroutine(typingCoroutine); typingCoroutine = null; }
        if (loadingCoroutine != null) { StopCoroutine(loadingCoroutine); loadingCoroutine = null; }
        if (delayShowCoroutine != null) { StopCoroutine(delayShowCoroutine); delayShowCoroutine = null; }

        // Show AI panel with "Listening" text
        if (dialogueText != null)
        {
            SetVisibility(true);
            dialogueText.text = "(กำลังฟัง...)";
        }
    }

    private void ShowLoadingStatus(bool isValidTurn)
    {
        if (!isValidTurn)
        {
            Debug.Log("[Dialoguebox] Invalid Turn (Short Tap) -> Clearing Dialogue");
            ClearDialogue();
            return;
        }

        // ✅ Clear previous AI text before new response (User Request)
        targetTextBuffer = "";
        if (dialogueText != null) dialogueText.text = "";
        if (typingCoroutine != null) { StopCoroutine(typingCoroutine); typingCoroutine = null; }

        // Don't clear UserText here, just AI text
        if (dialogueText != null)
        {
            SetVisibility(true);
            if (loadingCoroutine != null) StopCoroutine(loadingCoroutine);
            loadingCoroutine = StartCoroutine(LoadingRoutine());
        }
    }

    // ✅ NEW: Called when FAQ/Carousel sends a text query
    private void OnNewTextQuery()
    {
        // Clear previous AI text
        targetTextBuffer = "";
        if (dialogueText != null) dialogueText.text = "";
        if (typingCoroutine != null) { StopCoroutine(typingCoroutine); typingCoroutine = null; }

        // Show loading animation
        SetVisibility(true);
        if (loadingCoroutine != null) StopCoroutine(loadingCoroutine);
        loadingCoroutine = StartCoroutine(LoadingRoutine());
    }

    private IEnumerator LoadingRoutine()
    {
        if (dialogueText == null) yield break;

        while (true)
        {
            dialogueText.text = ".";
            yield return new WaitForSeconds(0.5f);
            dialogueText.text = "..";
            yield return new WaitForSeconds(0.5f);
            dialogueText.text = "...";
            yield return new WaitForSeconds(0.5f);
        }
    }

    public void ClearDialogue()
    {
        // Debug.Log("[Dialoguebox] ClearDialogue Called");
        // Clear AI
        targetTextBuffer = "";
        if (dialogueText != null) dialogueText.text = "";

        // Clear User (ใหม่)
        userTextBuffer = "";
        if (UserText != null) UserText.text = "";

        SetVisibility(false);
        SetUserVisibility(false);

        if (typingCoroutine != null) { StopCoroutine(typingCoroutine); typingCoroutine = null; }
        if (userTypingCoroutine != null) { StopCoroutine(userTypingCoroutine); userTypingCoroutine = null; }
        if (hideCoroutine != null) { StopCoroutine(hideCoroutine); hideCoroutine = null; }
        if (userHideCoroutine != null) { StopCoroutine(userHideCoroutine); userHideCoroutine = null; }
        if (loadingCoroutine != null) { StopCoroutine(loadingCoroutine); loadingCoroutine = null; }

        // (สำคัญ) ถ้ากำลังรอเปิด (Delay 1 วิ) แล้ว user กดพูดแทรก -> ยกเลิกการเปิดเดี๋ยวนี้
        if (delayShowCoroutine != null)
        {
            StopCoroutine(delayShowCoroutine);
            delayShowCoroutine = null;
        }
    }

    private void HandleTranscription(string text)
    {
        // Fix for text accumulation/overflow: Check if incoming text is an accumulated update
        // If the new text starts with what we already have, assume it's a full update (replace).
        // Otherwise, assume it's a new chunk (append).
        
        // Logic Adjustment:
        // 1. Sanitize incoming text (only remove newlines, preserve AI spacing)
        string cleanText = text.Replace("\n", " ").Replace("\r", "");

        if (!string.IsNullOrEmpty(targetTextBuffer) && cleanText.StartsWith(targetTextBuffer))
        {
             DisplayAiMessage(cleanText, false, true); // Replace = false (conceptually), but IsExtension = true
        }
        else
        {
             DisplayAiMessage(cleanText, true, false); // Append = true
        }
    }

    public void DisplayAiMessage(string text, bool append = true, bool isExtension = false)
    {
        Debug.Log($"[Dialoguebox] Msg: {text}, Append: {append}, Ext: {isExtension}");

        // Stop loading animation if running
        if (loadingCoroutine != null)
        {
            StopCoroutine(loadingCoroutine);
            loadingCoroutine = null;
        }

        if (!append && !isExtension)
        {
            // Force Reset for new message (Only if NOT an extension)
            targetTextBuffer = "";
            if (dialogueText != null) dialogueText.text = "";
        }

        // Clear placeholder text if it matches loading text or listening
        if (dialogueText != null && (dialogueText.text == "(กำลังฟัง...)" || dialogueText.text.StartsWith(".")))
        {
            dialogueText.text = "";
        }

        if (isExtension)
        {
            // Just update buffer, don't clear text
            targetTextBuffer = text; 
        }
        else
        {
            // ✅ Add space between chunks for better readability
            if (!string.IsNullOrEmpty(targetTextBuffer) && !targetTextBuffer.EndsWith(" ") && !text.StartsWith(" "))
            {
                targetTextBuffer += " "; // Add space between chunks
            }
            targetTextBuffer += text;
        }

        if (hideCoroutine != null) StopCoroutine(hideCoroutine);

        // เช็คว่าตอนนี้เปิดอยู่หรือไม่ (ถ้าเปิดอยู่แล้วไม่ต้องรอ)
        // FIXED: ถ้ามี canvasGroup ต้องเช็ค alpha > 0 เท่านั้น (เพราะ ActiveSelf อาจจะเปิดค้างไว้แต่ Alpha = 0)
        bool isAlreadyVisible = false;
        if (canvasGroup != null)
        {
            isAlreadyVisible = canvasGroup.alpha > 0.1f;
        }
        else if (dialoguePanel != null)
        {
            isAlreadyVisible = dialoguePanel.activeSelf;
        }

        // Debug.Log($"[Dialoguebox] isAlreadyVisible: {isAlreadyVisible}, delayShowCoroutine: {delayShowCoroutine}");

        if (isAlreadyVisible)
        {
            // แสดงอยู่แล้ว พิมพ์ต่อเลยไม่ต้องรอ
            if (typingCoroutine == null) typingCoroutine = StartCoroutine(TypeBuffer());
        }
        else
        {
            // ยังปิดอยู่ -> สั่งให้ Carousel ปิดก่อน -> รอ 1 วิ -> ค่อยเปิดตัวเอง
            if (delayShowCoroutine == null)
            {
                // Debug.Log("[Dialoguebox] Starting Delay Show Routine");
                OnDialogueStarting?.Invoke(); // บอกให้คนอื่น (Carousel) รีบหลบไปก่อน
                delayShowCoroutine = StartCoroutine(ShowWithDelayRoutine());
            }
        }
    }

    private IEnumerator ShowWithDelayRoutine()
    {
        yield return new WaitForSeconds(1f); // รอ 1 วินาที

        SetVisibility(true);
        if (typingCoroutine == null) typingCoroutine = StartCoroutine(TypeBuffer());

        delayShowCoroutine = null;
    }

    // --- (แก้ไข) ส่วนของ User ให้พิมพ์ทีละตัว ---
    private void HandleUserTranscription(string text)
    {
         // Logic similar to AI: Check for extension
        bool isExtension = false;
        if (!string.IsNullOrEmpty(userTextBuffer) && text.StartsWith(userTextBuffer))
        {
            isExtension = true;
        }
        
        DisplayUserMessage(text, isExtension);
    }

    public void DisplayUserMessage(string text, bool isExtension = false)
    {
        if (UserText != null)
        {
            // ✅ Cancel AutoHideRoutine เมื่อ User พูด (ป้องกัน User Panel หาย)
            if (hideCoroutine != null)
            {
                StopCoroutine(hideCoroutine);
                hideCoroutine = null;
            }

            SetVisibility(true); // ✅ Force Master Visibility (Alpha = 1)
            SetUserVisibility(true);

            // ✅ Clean User Text (Prevent newlines)
            userTextBuffer = text.Replace("\n", " ").Replace("\r", "");

            // If extension, only restart coroutine if it stopped.
            // If running, the loop will catch up because userTextBuffer is updated.
            if (userTypingCoroutine == null)
            {
                userTypingCoroutine = StartCoroutine(UserTypeBuffer(isExtension));
            }
        }
    }

    // Coroutine พิมพ์ข้อความ User
    private IEnumerator UserTypeBuffer(bool isExtension)
    {
        if (UserText == null) yield break;

        // เคลียร์ข้อความเก่าก่อนเริ่มพิมพ์ใหม่ เฉพาะเมื่อไม่ใช่ Extension
        if (!isExtension) UserText.text = "";

        while (UserText.text.Length < userTextBuffer.Length)
        {
            UserText.text += userTextBuffer[UserText.text.Length];
            UpdateUserBubbleSize(); // ✅ ปรับบับเบิลทุกครั้งที่เพิ่มตัวอักษร
            yield return new WaitForSeconds(typingSpeed);
        }
        UpdateUserBubbleSize();
        userTypingCoroutine = null;
    }
    // ------------------------------------------

    private IEnumerator TypeBuffer()
    {
        if (dialogueText == null) yield break;

        while (dialogueText.text.Length < targetTextBuffer.Length)
        {
            int currentIndex = dialogueText.text.Length;
            if (currentIndex < targetTextBuffer.Length)
            {
                dialogueText.text += targetTextBuffer[currentIndex];

                if (scrollRect != null)
                {
                    // ✅ Always force scroll to bottom while typing
                    Canvas.ForceUpdateCanvases();
                    scrollRect.verticalNormalizedPosition = 0f;
                }
            }
            yield return new WaitForSeconds(typingSpeed);
        }
        typingCoroutine = null;
        hideCoroutine = StartCoroutine(AutoHideRoutine());
    }

    private IEnumerator AutoHideRoutine()
    {
        yield return new WaitForSeconds(autoHideDuration);

        // ✅ ไม่ซ่อนถ้า User กำลังพิมพ์อยู่ (Fix: User panel disappearing)
        if (userTypingCoroutine != null)
        {
            Debug.Log("[Dialoguebox] AutoHide skipped: User is typing");
            yield break;
        }

        Debug.Log("[Dialoguebox] AutoHide triggered after " + autoHideDuration + "s");
        SetVisibility(false);
        if (dialogueText != null) dialogueText.text = "";
        targetTextBuffer = "";
        SetUserVisibility(false);
        if (UserText != null) UserText.text = "";
    }

    public void UpdateUserBubbleSize()
    {
        if (UserText == null || userBubbleRect == null) return;

        // ✅ FIX: Force Pivot to (0.5, 1) Top so bubble expands DOWNWARD
        if (userBubbleRect.pivot.y != 1f)
        {
            userBubbleRect.pivot = new Vector2(0.5f, 1f);
        }
        // Also ensure anchors if needed, but Pivot is the main key for "Growth Direction" logic in SetSizeWithCurrentAnchors

        // พื้นที่ให้ตัวหนังสือจริง ๆ (หัก padding)
        float maxTextWidth = Mathf.Max(0f, userBubbleMaxWidth - userBubblePadding.x);

        // ขอ preferred size จาก TMP (จะคำนวณให้รวม wrap เมื่อมี max width)
        Vector2 pref = UserText.GetPreferredValues(UserText.text, maxTextWidth, 0f);

        float bubbleW = Mathf.Min(pref.x + userBubblePadding.x, userBubbleMaxWidth);
        float bubbleH = pref.y + userBubblePadding.y;

        userBubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, bubbleW);
        userBubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bubbleH);

        // ให้ text ยืดเต็มบับเบิล แล้วเว้นขอบด้วย margin
        UserText.rectTransform.anchorMin = new Vector2(0f, 0f);
        UserText.rectTransform.anchorMax = new Vector2(1f, 1f);
        UserText.rectTransform.offsetMin = new Vector2(userBubblePadding.x * 0.5f, userBubblePadding.y * 0.5f);
        UserText.rectTransform.offsetMax = new Vector2(-userBubblePadding.x * 0.5f, -userBubblePadding.y * 0.5f);
    }
}