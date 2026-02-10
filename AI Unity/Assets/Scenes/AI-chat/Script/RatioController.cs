using UnityEngine;
using System.Collections;

public class RatioController : MonoBehaviour
{
    [Header("Minimum Size")]
    [SerializeField] private int minWidth = 270;  // 9:16 ratio
    [SerializeField] private int minHeight = 480;

    private int lastWidth;
    private int lastHeight;
    private Coroutine resizeCoroutine;

    void Start()
    {
        lastWidth = Screen.width;
        lastHeight = Screen.height;
    }

    void Update()
    {
        // ถ้า Width เปลี่ยน ให้รอจน resize เสร็จแล้วค่อย apply
        if (Screen.width != lastWidth)
        {
            lastWidth = Screen.width;
            
            // Cancel coroutine เก่า (ถ้ามี)
            if (resizeCoroutine != null)
                StopCoroutine(resizeCoroutine);
            
            // รอ 0.1 วินาที ถ้าไม่มีการ resize อีก ค่อย apply
            resizeCoroutine = StartCoroutine(ApplyResizeAfterDelay());
        }
        if (Screen.height != lastHeight)
        {
            lastHeight = Screen.height;
            
            // Cancel coroutine เก่า (ถ้ามี)
            if (resizeCoroutine != null)
                StopCoroutine(resizeCoroutine);
            
            // รอ 0.1 วินาที ถ้าไม่มีการ resize อีก ค่อย apply
            resizeCoroutine = StartCoroutine(ApplyResizeAfterDelay());
        }
    }

    IEnumerator ApplyResizeAfterDelay()
    {
        yield return new WaitForSeconds(0.1f);
        
        int w = Mathf.Max(Screen.width, minWidth);
        int h = Mathf.RoundToInt(w * 16f / 9f);
        h = Mathf.Max(h, minHeight);
        Screen.SetResolution(w, h, false);
        
        resizeCoroutine = null;
    }
}
