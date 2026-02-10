using UnityEngine;

public class bgchangeScript : MonoBehaviour
{
    [System.Serializable]
    public class ObjectTriggers
    {
        public string name; // Just for labeling in Inspector
        public GameObject targetObject;

        [Header("Triggers")]
        public string triggerIn = "IN";
        public string triggerIdle = "IDLE";
        public string triggerOut = "OUT";
        public string triggerOIdle = "OIDLE";
    }

    [Header("Settings")]
    public ObjectTriggers[] managedObjects;

    [Header("References")]
    public CardScript cardScript;

    // Track current active BG to close it automatically
    private int currentActiveIndex = -1;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Debug.Log("bgchangeScript: Script has STARTED.");
    }

    // Update is called once per frame
    void Update()
    {

    }

    [Header("Animation Settings")]
    public float delayIn = 1.0f;
    public float delayOut = 1.0f;

    private Coroutine mainTransitionRoutine;

    // Main Entry Point
    public void TriggerProduct(int index)
    {
        // Adjust index to 0-based for array (Input 1-6 -> Array 0-5)
        int arrayIndex = index - 1;

        if (managedObjects == null || arrayIndex < 0 || arrayIndex >= managedObjects.Length)
        {
            Debug.LogWarning($"bgchangeScript: Invalid index {index}");
            return;
        }

        // Prevent re-triggering if already active
        if (currentActiveIndex == arrayIndex)
        {
            // Debug.Log($"bgchangeScript: Product {index} is already active. Skipping transition.");
            return;
        }

        // Stop any running transition to avoid conflicts
        if (mainTransitionRoutine != null) StopCoroutine(mainTransitionRoutine);

        // Start new sequential transition
        mainTransitionRoutine = StartCoroutine(TransitionSequence(arrayIndex));
    }

    private System.Collections.IEnumerator TransitionSequence(int targetIndex)
    {
        // 1. Close Previous (if any)
        if (currentActiveIndex != -1 && currentActiveIndex != targetIndex)
        {
            // Verify index validity before accessing
            if (currentActiveIndex < managedObjects.Length)
            {
                yield return StartCoroutine(PlayCloseRoutine(managedObjects[currentActiveIndex]));
                // Wait a tiny bit extra for safety/smoothness if needed, or rely on PlayCloseRoutine's delay
            }
        }

        // 2. Open New
        currentActiveIndex = targetIndex;
        yield return StartCoroutine(PlayOpenRoutine(managedObjects[currentActiveIndex]));

        mainTransitionRoutine = null;
    }

    public void CloseCurrentBG()
    {
        if (currentActiveIndex != -1 && managedObjects != null && currentActiveIndex < managedObjects.Length)
        {
            StartCoroutine(PlayCloseRoutine(managedObjects[currentActiveIndex]));
            currentActiveIndex = -1;
        }
    }


    private System.Collections.IEnumerator PlayOpenRoutine(ObjectTriggers item)
    {
        if (item.targetObject == null) yield break;
        Animator animator = item.targetObject.GetComponent<Animator>();
        if (animator == null || !animator.isActiveAndEnabled) yield break;

        // 1. Reset Competing Triggers
        if (!string.IsNullOrEmpty(item.triggerOut)) animator.ResetTrigger(item.triggerOut);
        if (!string.IsNullOrEmpty(item.triggerOIdle)) animator.ResetTrigger(item.triggerOIdle);

        // 2. Play IN
        if (!string.IsNullOrEmpty(item.triggerIn))
        {
            // Debug.Log($"bgchangeScript: '{item.name}' -> IN ({item.triggerIn})");
            animator.SetTrigger(item.triggerIn);
        }

        // 3. Wait
        yield return new WaitForSeconds(delayIn);

        // 4. Play IDLE
        if (!string.IsNullOrEmpty(item.triggerIdle))
        {
            // Debug.Log($"bgchangeScript: '{item.name}' -> IDLE ({item.triggerIdle})");
            animator.SetTrigger(item.triggerIdle);
        }
    }

    private System.Collections.IEnumerator PlayCloseRoutine(ObjectTriggers item)
    {
        if (item.targetObject == null) yield break;
        Animator animator = item.targetObject.GetComponent<Animator>();
        if (animator == null || !animator.isActiveAndEnabled) yield break;

        // 1. Reset Competing Triggers
        if (!string.IsNullOrEmpty(item.triggerIn)) animator.ResetTrigger(item.triggerIn);
        if (!string.IsNullOrEmpty(item.triggerIdle)) animator.ResetTrigger(item.triggerIdle);

        // 2. Play OUT
        if (!string.IsNullOrEmpty(item.triggerOut))
        {
            // Debug.Log($"bgchangeScript: '{item.name}' -> OUT ({item.triggerOut})");
            animator.SetTrigger(item.triggerOut);
        }

        // 3. Wait
        yield return new WaitForSeconds(delayOut);

        // 4. Play OIDLE
        if (!string.IsNullOrEmpty(item.triggerOIdle))
        {
            // Debug.Log($"bgchangeScript: '{item.name}' -> OIDLE ({item.triggerOIdle})");
            animator.SetTrigger(item.triggerOIdle);
        }
    }

    [ContextMenu("Test OpenBG1")] public void OpenBG1() => TriggerProduct(1);
    [ContextMenu("Test OpenBG2")] public void OpenBG2() => TriggerProduct(2);
    [ContextMenu("Test OpenBG3")] public void OpenBG3() => TriggerProduct(3);
    [ContextMenu("Test OpenBG4")] public void OpenBG4() => TriggerProduct(4);
    [ContextMenu("Test OpenBG5")] public void OpenBG5() => TriggerProduct(5);
    [ContextMenu("Test OpenBG6")] public void OpenBG6() => TriggerProduct(6);

    [ContextMenu("Force Close Current")] public void ForceClose() => CloseCurrentBG();


}
