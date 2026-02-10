using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class FAQController : MonoBehaviour
{
    public Animator animator;

    [Header("FAQ Settings")]
    [TextArea(3, 10)]
    public string[] questions;

    [Header("UI Generation")]
    public GameObject buttonPrefab;
    public Transform buttonContainer;

    public bool isShow = true; // Added to track state
    private RecordAudio recordAudio;
    private List<Button> faqButtons = new List<Button>(); // Store button references
    private bool buttonsDisabled = false;

    private Coroutine waitCoroutine; // Keep track of the coroutine

    private void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();

        recordAudio = Object.FindFirstObjectByType<RecordAudio>();
        if (recordAudio == null) Debug.LogWarning("[FAQController] RecordAudio instance not found in scene at Start.");
        else
        {
            recordAudio.OnStartRecording += OnMicStarted; // Subscribe to Mic event
        }

        GenerateButtons();
    }

    private void OnDestroy()
    {
        if (recordAudio != null)
        {
            recordAudio.OnStartRecording -= OnMicStarted;
        }
    }

    private void OnMicStarted()
    {
        // Safety Check: Cannot start coroutine if object is inactive
        if (!gameObject.activeInHierarchy) return;

        // Always ensuring buttons are locked when Mic starts (Visual + Logical)
        // This covers "FAQ -> Mic" (Extend Lock) AND "Idle -> Mic" (New Lock)
        
        // Stop any existing wait (e.g. waiting for FAQ response)
        if (waitCoroutine != null) StopCoroutine(waitCoroutine);

        // Force Disable Buttons
        SetButtonsInteractable(false);
            
        // Start waiting for the Mic interaction to respond
        waitCoroutine = StartCoroutine(WaitForAIResponseStart());
    }

    private void GenerateButtons()
    {
        if (buttonPrefab == null || buttonContainer == null)
        {
            // Debug.LogWarning("[FAQController] Button Prefab or Container not assigned.");
            return;
        }

        // Clear existing buttons
        foreach (Transform child in buttonContainer)
        {
            Destroy(child.gameObject);
        }

        // Generate new buttons
        for (int i = 0; i < questions.Length; i++)
        {
            string q = questions[i]; // Local copy for closure
            GameObject btnObj = Instantiate(buttonPrefab, buttonContainer);

            // Set Text
            TMP_Text txt = btnObj.GetComponentInChildren<TMP_Text>();
            if (txt != null) txt.text = q;

            // Add Listener
            Button btn = btnObj.GetComponent<Button>();
            if (btn != null)
            {
                faqButtons.Add(btn); // Store reference
                btn.onClick.AddListener(() => SendQuestion(q));
            }
        }
    }

    public void SendQuestion(int index)
    {
        if (index >= 0 && index < questions.Length)
        {
            SendQuestionInternal(questions[index]);
        }
        else
        {
            Debug.LogError($"[FAQController] Invalid question index: {index}. Array size: {questions.Length}");
        }
    }

    public void SendQuestion(string question)
    {
        SendQuestionInternal(question);
    }

    private void SendQuestionInternal(string question)
    {
        // Prevent rapid clicks - ignore if buttons already disabled
        if (buttonsDisabled) return;

        if (recordAudio == null)
        {
            recordAudio = Object.FindFirstObjectByType<RecordAudio>();
        }

        // Disable all FAQ buttons immediately
        SetButtonsInteractable(false);

        // ✅ Force Reset Everything (Force Idle) before starting new query
        if (recordAudio != null) recordAudio.CancelEverything();

        // Explicitly clear DialogueBox to ensure fresh start
        Dialoguebox dialogueBox = Object.FindFirstObjectByType<Dialoguebox>();
        if (dialogueBox != null)
        {
            dialogueBox.ClearDialogue();
        }

        if (recordAudio != null)
        {
            recordAudio.SendTextQuery(question);
            // Start watching for AI to finish speaking
            if (waitCoroutine != null) StopCoroutine(waitCoroutine);

            if (gameObject.activeInHierarchy)
            {
                waitCoroutine = StartCoroutine(WaitForAIResponseStart());
            }
        }
        else
        {
            Debug.LogError("[FAQController] RecordAudio instance not found!");
            SetButtonsInteractable(true); // Re-enable if error
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        buttonsDisabled = !interactable;
        foreach (var btn in faqButtons)
        {
            if (btn != null) btn.interactable = interactable;
        }
    }

    // ✅ Public method for StageManager to force unlock on timeout
    public void ForceUnlockButtons()
    {
        // Stop waiting coroutine if running
        if (waitCoroutine != null)
        {
            StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }
        SetButtonsInteractable(true);
    }

    private IEnumerator WaitForAIResponseStart()
    {
        // 1. Wait for Request to be acknowledged (IsProcessing=true OR IsRecording=true)
        float timeout = 2.0f;
        while (recordAudio != null && !recordAudio.IsProcessing && !recordAudio.IsRecording && !recordAudio.IsPlaying && timeout > 0)
        {
             timeout -= Time.deltaTime;
             yield return null;
        }

        // 2. Wait while System is Busy (Recording OR Processing)
        // Once this loop breaks, it means we are done recording AND done processing -> AI is starting to play (or idle)
        while (recordAudio != null && (recordAudio.IsProcessing || recordAudio.IsRecording))
        {
             yield return null;
        }

        // Re-enable buttons immediately when AI starts (Processing done)
        SetButtonsInteractable(true);
        waitCoroutine = null;
    }

    public void Hide()
    {
        if (isShow == false)
        {
            return;
        }

        if (animator != null)
        {
            animator.SetTrigger("FAQHide");
            isShow = false;
        }
        else
        {
            gameObject.SetActive(false); // Fallback
        }
        // Debug.Log("[FAQController] Hide Triggered.");
    }

    public void Show()
    {
        if (isShow == true)
        {
            return;
        }

        if (animator != null)
        {
            animator.SetTrigger("FAQShow");
            isShow = true;
        }
        else
        {
            gameObject.SetActive(true); // Fallback
        }
        // Debug.Log("[FAQController] Show Triggered.");
    }

    public void Default()
    {
        if (animator != null)
        {
            animator.SetTrigger("Default");
        }
    }
}
