using UnityEngine;
using Mediapipe.Unity.Sample;

public class Scenecontroller : MonoBehaviour
{
    public GameObject objectToClose;
    public GameObject objectToOpen;

    // Switch between two objects; if the closing one has a webcam running, stop it first.
    public void SwitchObject()
    {
        StartCoroutine(SwitchObjectRoutine());
    }

    private System.Collections.IEnumerator SwitchObjectRoutine()
    {
        if (objectToClose != null)
        {
            var webcam = objectToClose.GetComponentInChildren<Webcam>(true);
            if (webcam != null)
            {
                webcam.turnOffWebcam(true);
            }

            // Ensure the camera is fully stopped so the next scene can start it fresh
            if (ImageSourceProvider.ImageSource != null && ImageSourceProvider.ImageSource.isPlaying)
            {
                ImageSourceProvider.ImageSource.Stop();
            }

            // Give the hardware some time to release the camera resource (crucial for some devices)
            yield return new WaitForSeconds(1.0f);

            objectToClose.SetActive(false);
            Debug.Log("xxxxxxx");
        }

        if (objectToOpen != null)
        {
            objectToOpen.SetActive(true);
        }
    }

    public void SwitchBack()
    {
        if (objectToOpen != null)
        {
            var webcam = objectToOpen.GetComponentInChildren<Webcam>(true);
            if (webcam != null)
            {
                webcam.turnOffWebcam(true);
            }

            // Stop MediaPipe/Webcam from the game scene
            ImageSourceProvider.ImageSource?.Stop();
            objectToOpen.SetActive(false);
        }

        if (objectToClose != null)
        {
            objectToClose.SetActive(true);

            // Optional: Auto-start webcam if needed, or let the user press the button.
            // var webcam = objectToClose.GetComponentInChildren<Webcam>(true);
            // if (webcam != null) webcam.turnOnWebcam();
        }
    }
}
