using UnityEngine;
using System;

public class ScenecontrolWS : MonoBehaviour
{
    [SerializeField] private Scenecontroller scenecontroller;

    private void OnEnable()
    {

        RecordAudio.OnFunctionCallResult += HandleSceneAction;
    }

    private void OnDisable()
    {

        RecordAudio.OnFunctionCallResult -= HandleSceneAction;
    }

    private void HandleSceneAction(RecordAudio.FunctionCallEntry entry)
    {
        string action = entry?.response?.action ?? entry?.args?.action;

        if (string.IsNullOrEmpty(action)) return;

        if (string.Equals(action, "tapgame-start", StringComparison.OrdinalIgnoreCase))
        {
            if (scenecontroller != null)
            {
                scenecontroller.SwitchObject();
                Debug.Log("Change Scene to HandGame");
            }
            else
            {
                Debug.LogError("[ScenecontrolWS] Scenecontroller reference is NULL!");
            }
        }
        else if (string.Equals(action, "tapgame-stop", StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log("[ScenecontrolWS] Action matches tapgame-stop. Switching back...");
            if (scenecontroller != null)
            {
                scenecontroller.SwitchBack();
                Debug.Log("Change Scene to Webcam");
            }
            else
            {
                Debug.LogError("[ScenecontrolWS] Scenecontroller reference is NULL!");
            }
        }
    }
}
