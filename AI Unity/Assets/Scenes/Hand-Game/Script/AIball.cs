using UnityEngine;


public class AIball : MonoBehaviour
{
    [Header("Setup")]
    public AudioSource aiAudioSource;
    public Renderer targetRenderer;

    [Header("Color Settings")]
    public Color activeColor = Color.red;
    public Color idleColor = Color.grey;

    [Header("Scale Settings")]
    [Tooltip("ตัวคูณขนาด: 1.0 คือเท่าเดิม, 1.2 คือขยาย 20%, 2.0 คือขยาย 2 เท่า")]
    public float sizeMultiplier = 1.5f; // แนะนำให้ลองปรับเล่นดู (1.2 - 2.0)

    [Header("General Settings")]
    [Range(0.001f, 0.1f)]
    public float sensitivity = 0.01f;
    public float smoothSpeed = 10f;

    private float[] audioSamples = new float[256];
    private float currentVolume;
    private Vector3 initialScale; // ตัวแปรเก็บขนาดเริ่มต้น (250,250,250)

    void Start()
    {
        // ✅ สำคัญ: จำค่าขนาดเริ่มต้นของวัตถุไว้ (ไม่ว่าจะ 1 หรือ 250 ก็จะจำไว้นี่)
        if (targetRenderer != null)
        {
            initialScale = targetRenderer.transform.localScale;
        }
    }

    void Update()
    {
        if (aiAudioSource == null || targetRenderer == null) return;

        // 1. คำนวณความดัง (RMS)
        if (aiAudioSource.isPlaying)
        {
            aiAudioSource.GetOutputData(audioSamples, 0);
            float sum = 0;
            foreach (var sample in audioSamples)
            {
                sum += sample * sample;
            }
            currentVolume = Mathf.Sqrt(sum / audioSamples.Length);
        }
        else
        {
            currentVolume = 0f;
        }

        // ---------------------------------------------------------
        // ส่วนจัดการสี (เหมือนเดิม)
        // ---------------------------------------------------------
        Color targetColor = (currentVolume > sensitivity) ? activeColor : idleColor;
        targetRenderer.material.color = Color.Lerp(targetRenderer.material.color, targetColor, Time.deltaTime * smoothSpeed);

        // ---------------------------------------------------------
        // ✅ ส่วนจัดการขนาด (ยืดหด)
        // ---------------------------------------------------------

        // คำนวณ Factor ว่าจะขยายเท่าไหร่ (0 = เท่าเดิม, 1 = ขยายเต็มที่)
        // เอาความดังมาคูณ 10 เพื่อให้เห็นผลชัดขึ้น (ปรับเลข 10 ได้ถ้ามันเด้งน้อยไป)
        float sizeFactor = Mathf.Clamp01(currentVolume * 10f);

        // เป้าหมายขนาด = ขนาดเดิม + (ส่วนขยาย * Factor)
        // สูตรนี้จะทำงานได้ดีกับวัตถุทุกขนาด ไม่ว่าจะเป็น 1 หรือ 250
        Vector3 targetScale = Vector3.Lerp(initialScale, initialScale * sizeMultiplier, sizeFactor);

        // สั่งเปลี่ยนขนาดแบบนุ่มนวล
        targetRenderer.transform.localScale = Vector3.Lerp(targetRenderer.transform.localScale, targetScale, Time.deltaTime * smoothSpeed);
    }
}
