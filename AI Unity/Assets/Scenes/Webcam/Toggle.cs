using UnityEngine;

public class Toggle : MonoBehaviour
{
    public Webcam webcamScript;

    public void TurnOn()
    {
        Debug.Log("Turn On");
        webcamScript.turnOnWebcam();
    }

    // Update is called once per frame
    public void TurnOff()
    {
        Debug.Log("Turn Off");
        webcamScript.turnOffWebcam();
    }


    

}
