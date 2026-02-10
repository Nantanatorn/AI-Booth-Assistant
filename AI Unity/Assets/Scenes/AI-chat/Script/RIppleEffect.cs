using UnityEngine;
using UnityEngine.UI;

public class RIppleEffect : MonoBehaviour
{
    public float expandSpeed = 1f; // ความเร็วในการขยาย (ยิ่งน้อยยิ่งช้าและนุ่มนวล)
    private Image img;
    private float timer = 0;

    private float startAlpha;

    void Start()
    {
        img = GetComponent<Image>();
        transform.localScale = Vector3.one; // เริ่มที่ขนาดปกติ
        startAlpha = img.color.a; // เก็บค่า Alpha เริ่มต้น
    }

    public float targetScale = 3f; // Default target scale

    void Update()
    {
        timer += Time.deltaTime * expandSpeed;

        // ขยายขนาดขึ้นเรื่อยๆ
        transform.localScale = Vector3.one * Mathf.Lerp(1f, targetScale, timer);

        // ค่อยๆ จางหายไป
        Color c = img.color;
        c.a = Mathf.Lerp(startAlpha, 0f, timer); // ใช้ startAlpha แทน 0.4f
        img.color = c;

        // ถ้าจางจนมองไม่เห็นแล้ว ให้ลบทิ้งเพื่อประหยัด RAM
        if (timer >= 1f) Destroy(gameObject);
    }
}
