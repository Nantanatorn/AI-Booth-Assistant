using UnityEngine;

public class ButtonScript : MonoBehaviour
{
    public UnityEngine.UI.Image targetImage;
    public Sprite pressedSprite;
    public Sprite normalSprite;

    private void Start()
    {
        if (targetImage == null) targetImage = GetComponent<UnityEngine.UI.Image>();
        if (targetImage != null && normalSprite == null) normalSprite = targetImage.sprite;
    }

    // Call this via Button OnClick
    public void OnClickChange()
    {
        if (targetImage == null || pressedSprite == null) return;

        targetImage.sprite = pressedSprite;
    }

    public void OnClickRestore()
    {
        if (targetImage == null || normalSprite == null) return;

        targetImage.sprite = normalSprite;
    }
}
