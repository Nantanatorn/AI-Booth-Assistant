using UnityEngine;

public class Statushanding : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private RecordAudio recordAudio;

    [Header("Triggers")]
    [SerializeField] private string idleTrigger = "idle";
    [SerializeField] private string listeningTrigger = "Listening";
    [SerializeField] private string thinkingTrigger = "Thinking";
    [SerializeField] private string talkingTrigger = "Talking";

    private enum State
    {
        Idle,
        Listening,
        Thinking,
        Talking
    }

    private State _currentState = State.Idle;

    private void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (recordAudio == null) recordAudio = FindObjectOfType<RecordAudio>();

        if (recordAudio != null)
        {
             recordAudio.OnForceIdle += ForceIdle; // Subscribe
        }
    }

    private void OnDestroy()
    {
        if (recordAudio != null)
        {
             recordAudio.OnForceIdle -= ForceIdle;
        }
    }

    public void ForceIdle()
    {
        // Debug.Log("[Statushanding] Force Idle Triggered");
        ResetAllTriggers();
        animator.SetTrigger(idleTrigger);
        _currentState = State.Idle;
    }

    private void Update()
    {
        if (recordAudio == null || animator == null) return;

        // Priority Logic:
        // 1. Listening (Mic ON)
        // 2. Talking (Audio Playing)
        // 3. Thinking (Processing / Waiting for response)
        // 4. Idle (Nothing happening)

        if (recordAudio.IsRecording)
        {
            SetState(State.Listening);
        }
        else if (recordAudio.IsPlaying)
        {
            SetState(State.Talking);
        }
        else if (recordAudio.IsProcessing)
        {
            SetState(State.Thinking);
        }
        else
        {
            SetState(State.Idle);
        }
    }

    private void SetState(State newState)
    {
        if (_currentState == newState) return;

        _currentState = newState;
        UpdateAnimator();
    }

    private void UpdateAnimator()
    {
        string triggerToSet = "";

        switch (_currentState)
        {
            case State.Idle:
                triggerToSet = idleTrigger;
                break;
            case State.Listening:
                triggerToSet = listeningTrigger;
                break;
            case State.Thinking:
                triggerToSet = thinkingTrigger;
                break;
            case State.Talking:
                triggerToSet = talkingTrigger;
                break;
        }

        // Reset other triggers to ensure clean transition
        ResetAllTriggers();

        // Check if trigger is already active (optional safety) before setting
        if (!string.IsNullOrEmpty(triggerToSet))
        {
            animator.SetTrigger(triggerToSet);
        }
    }

    private void ResetAllTriggers()
    {
        animator.ResetTrigger(idleTrigger);
        animator.ResetTrigger(listeningTrigger);
        animator.ResetTrigger(thinkingTrigger);
        animator.ResetTrigger(talkingTrigger);
    }
}
