using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class LoopChat : MonoBehaviour
{
    [System.Serializable]
    public struct ChatMessage
    {
        public SenderType sender;
        [TextArea(3, 10)] public string message;
        public float delayAfter; // Time to wait before showing the next message
    }

    public enum SenderType
    {
        AI,
        User
    }

    [Header("UI Objects (Refactored to use DialogueBox)")]
    // [SerializeField] private TMP_Text aiTextObj; // Removed
    // [SerializeField] private TMP_Text userTextObj; // Removed
    // [SerializeField] private GameObject aiBubbleParent; // Removed
    // [SerializeField] private GameObject userBubbleParent; // Removed

    [Header("Loop Settings")]
    [SerializeField] private float typingSpeed = 0.05f;
    [SerializeField] private bool loopConversation = true;
    [SerializeField] private float restartDelay = 3.0f; // Time to wait before restarting the loop

    [Header("Idle Settings")]
    [SerializeField] private float idleRestartTime = 180f;

    [Header("Text Size")]
    [SerializeField] private float loopFontSize = 55f; // Font size during loop playback
    private float originalFontSize = 0f;

    [Header("Preset Conversation")]
    [SerializeField] private List<ChatMessage> presetMessages = new List<ChatMessage>();

    [Header("AI Text Reference (for font size)")]
    [SerializeField] private TMPro.TMP_Text aiDialogueText; // ลากมาจาก DialogueBox

    private RecordAudio recordAudio;

    private Dialoguebox dialogueBox;

    private void Start()
    {
        // Find dependencies
        recordAudio = Object.FindFirstObjectByType<RecordAudio>();
        dialogueBox = Object.FindFirstObjectByType<Dialoguebox>();

        if (recordAudio != null)
        {
            recordAudio.OnStartRecording += OnMicStart;
            recordAudio.OnTextSent += OnMicStart; // Treat FAQ send like mic start
            recordAudio.OnStopRecording += OnMicStop;
        }
        else
        {
            Debug.LogWarning("[LoopChat] RecordAudio not found! Loop stopping will not work.");
        }

        // Start immediately on load
        RestartLoop();
    }

    private void OnDestroy()
    {
        if (recordAudio != null)
        {
            recordAudio.OnStartRecording -= OnMicStart;
            recordAudio.OnTextSent -= OnMicStart;
            recordAudio.OnStopRecording -= OnMicStop;
        }
    }

    private void OnMicStart()
    {
        // 1. Stop everything
        StopAllCoroutines();

        // 2. Clear & Hide UI (Interaction Mode)
        // ClearChat(); // Old local clear
        // SetParentsActive(false); // Old bubble clear

        // Interruption: Clear DialogueBox immediately to prepare for User input
        if (dialogueBox != null)
        {
            dialogueBox.ClearDialogue();
        }

        // Debug.Log("[LoopChat] Mic Start -> Stopped Loop & Hidden UI.");
    }

    private void OnMicStop(bool isValidTurn)
    {
        // 3. Start Idle Timer
        StopAllCoroutines(); // Ensure no duplicates
        StartCoroutine(IdleTimer());
        // Debug.Log($"[LoopChat] Mic Stop -> Idle Timer Started ({idleRestartTime}s).");
    }

    private IEnumerator IdleTimer()
    {
        yield return new WaitForSeconds(idleRestartTime);
        RestartLoop();
    }

    public void RestartLoop()
    {
        // Stop any running timers/loops
        StopAllCoroutines();

        // ✅ ตั้ง font size สำหรับ loop
        if (aiDialogueText != null)
        {
            if (originalFontSize == 0f) originalFontSize = aiDialogueText.fontSize; // บันทึกค่าเดิมครั้งแรก
            aiDialogueText.fontSize = loopFontSize;
        }

        ClearChat();
        if (presetMessages.Count > 0)
        {
            StartCoroutine(ChatLoopRoutine());
            // Debug.Log("[LoopChat] Loop Restarted.");
        }
    }

    public void StopLoop(bool clearChat = true)
    {
        StopAllCoroutines();

        // ✅ คืน font size เดิม
        if (aiDialogueText != null && originalFontSize > 0f)
        {
            aiDialogueText.fontSize = originalFontSize;
        }

        if (clearChat)
        {
            ClearChat();
        }
        Debug.Log($"[LoopChat] Loop Stopped. Clear: {clearChat}");
    }

    // Removed SetParentsActive as parents are managed by DialogueBox logic or not needed here

    private IEnumerator ChatLoopRoutine()
    {
        while (true)
        {
            // Start of a new conversation loop
            if (dialogueBox != null) dialogueBox.ClearDialogue();
            yield return new WaitForSeconds(0.5f);

            foreach (var msg in presetMessages)
            {
                yield return StartCoroutine(ShowMessageRoutine(msg));

                float delay = msg.delayAfter > 0 ? msg.delayAfter : 1.0f;
                yield return new WaitForSeconds(delay);
            }

            if (!loopConversation) break;

            yield return new WaitForSeconds(restartDelay);
        }
    }

    private IEnumerator ShowMessageRoutine(ChatMessage msg)
    {
        if (dialogueBox == null) yield break;

        // Determine sender
        if (msg.sender == SenderType.AI)
        {
            dialogueBox.DisplayAiMessage(msg.message, false);
        }
        else
        {
            dialogueBox.DisplayUserMessage(msg.message);
        }

        // Wait for typing duration (Approximate based on length)
        // Since DialogueBox handles the actual typing coroutine, we just wait here to sync the loop flow.
        float estimatedDuration = msg.message.Length * typingSpeed;
        yield return new WaitForSeconds(estimatedDuration);
    }

    private void ClearChat()
    {
        // Wrapper for potential legacy calls, but mostly unused now
        if (dialogueBox != null) dialogueBox.ClearDialogue();
    }
}
