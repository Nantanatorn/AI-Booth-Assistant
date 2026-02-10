using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;
using WebSocketSharp;
using System.Collections.Concurrent;
using UnityEngine.EventSystems;

public class RecordAudio : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{

    // Singleton guard so WebSocket connects only once
    private static RecordAudio _instance;
    private static WebSocket _sharedWs;
    private bool _isPrimary;

    private AudioClip recordedClip;
    [SerializeField] AudioSource audioSource;
    [SerializeField] string wsUrl = "ws://localhost:3100/audio";
    [SerializeField] private Webcam webcam; // assign Webcam component in inspector to allow fn-call control
    private string filePath = "rec.wav";
    private string directoryPath = Application.streamingAssetsPath;
    private float startTime;
    private float recordingLength;
    private WebSocket ws;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    public static event Action<FunctionCallEntry> OnFunctionCallResult;

    private void Awake()
    {
        // Auto-assign Webcam if not wired in Inspector (helps when dragging is blocked)
        if (webcam == null)
        {
            // webcam = FindObjectOfType<Webcam>(); // deprecated in newer Unity versions
            webcam = FindFirstObjectByType<Webcam>();
            if (webcam != null)
            {
                Debug.Log("RecordAudio auto-assigned Webcam from scene");
            }
            else
            {
                Debug.LogWarning("RecordAudio: Webcam reference is missing; assign in Inspector or keep webcam component in the scene");
            }
        }

        // Prevent multiple instances / connections
        if (_instance != null && _instance != this)
        {
            // Reuse existing shared WebSocket; keep component active but forward calls to the primary
            _isPrimary = false;
            ws = _sharedWs;
            return;
        }
        _isPrimary = true;
        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (_sharedWs == null)
        {
            _sharedWs = new WebSocket(wsUrl);
            _sharedWs.OnOpen += (_, __) => Debug.Log("WS connected: " + wsUrl);
            _sharedWs.OnError += (_, e) => Debug.LogError("WS error: " + e.Message);
            _sharedWs.OnClose += (_, __) => Debug.Log("WS closed");
            _sharedWs.OnMessage += OnWsMessage;
            _sharedWs.ConnectAsync();
        }
        ws = _sharedWs;
    }

    public void StartRecording()
    {
        if (!_isPrimary)
        {
            _instance?.StartRecording();
            return;
        }

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            Debug.LogWarning("No microphone devices available.");
            return;
        }

        string device = Microphone.devices[0];
        int sampleRate = 24000;
        int lengthSec = 3599;

        recordedClip = Microphone.Start(device, false, lengthSec, sampleRate);
        startTime = Time.realtimeSinceStartup;
        Debug.Log("Recording Started");
    }

    //     public void PushToTalk()
    // {   

    //     if (Input.GetMouseButtonDown(0))
    //     {   
    //         Debug.Log("Hold: Start Recording");
    //         StartRecording();      
    //     }
    //     if (Input.GetMouseButtonUp(0))
    //     {
    //         Debug.Log("Release: Stop Recording");
    //         StopRecording();      
    //     }

    // }


    public void OnPointerDown(PointerEventData eventData)
    {
        Debug.Log("UI Button Pressed: Start Recording");
        StartRecording();
    }

    // ฟังก์ชันนี้ทำงานเมื่อนิ้วปล่อยจากปุ่ม UI (หยุดอัด)
    public void OnPointerUp(PointerEventData eventData)
    {
        Debug.Log("UI Button Released: Stop Recording");
        StopRecording();
    }

    public void StopRecording()
    {
        if (!_isPrimary)
        {
            _instance?.StopRecording();
            return;
        }
        Debug.Log("Recording Stopped");
        Microphone.End(null);
        recordingLength = Time.realtimeSinceStartup - startTime;
        recordedClip = TrimClip(recordedClip, recordingLength);
        SaveRecording();
        SendRecordingToNode();
        SendTurnComplete();
    }

    public void PlayRecording()
    {
        if (!_isPrimary)
        {
            _instance?.PlayRecording();
            return;
        }
        Debug.Log("Playing Recording ");
        audioSource.clip = recordedClip;
        audioSource.Play();
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out var action))
        {
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("Main thread action failed: " + ex.Message); }
        }


    }

    public void SaveRecording()
    {
        if (recordedClip != null)
        {
            filePath = Path.Combine(directoryPath, filePath);
            Wav.Save(filePath, recordedClip);
            Debug.Log("Recording saved as" + filePath);
        }
        else
        {

            Debug.LogError("Nothing to save");

        }
    }

    private AudioClip TrimClip(AudioClip clip, float length)
    {
        int samples = (int)(clip.frequency * length);
        float[] data = new float[samples];
        clip.GetData(data, 0);

        AudioClip trimmedClip = AudioClip.Create(clip.name, samples, clip.channels, clip.frequency, false);
        trimmedClip.SetData(data, 0);

        return trimmedClip;
    }


    private void OnDestroy()
    {
        if (_instance == this)
        {
            _sharedWs?.CloseAsync();
            _sharedWs = null;
            _instance = null;
        }
    }

    private void OnWsMessage(object sender, MessageEventArgs e)
    {
        try
        {
            // Quick check for function_call_log broadcasts
            var typeOnly = JsonUtility.FromJson<WsTypeOnly>(e.Data);
            if (typeOnly != null && typeOnly.type == "function_call_log")
            {
                mainThreadActions.Enqueue(() => HandleFunctionCallLog(e.Data));
                return;
            }
            if (typeOnly != null && typeOnly.type == "function_call_result")
            {
                mainThreadActions.Enqueue(() => HandleFunctionCallResult(e.Data));
                return;
            }

            var msg = JsonUtility.FromJson<WsAudioMessage>(e.Data);
            if (msg == null) return;

            // Play only audio_broadcast messages
            if (!string.IsNullOrEmpty(msg.audio))
            {
                if (string.IsNullOrEmpty(msg.type) || msg.type != "audio_broadcast")
                {
                    Debug.Log($"Skip playing incoming audio type={msg.type}");
                    return;
                }
                int sampleRate = msg.sampleRate > 0 ? msg.sampleRate : 24000;
                Debug.Log($"WS broadcast received: audio length={msg.audio.Length}, sampleRate={sampleRate}");
                mainThreadActions.Enqueue(() => PlayIncomingPcm(msg.audio, sampleRate));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("WS message parse failed: " + ex.Message);
        }
    }

    private void HandleFunctionCallLog(string json)
    {
        try
        {
            var log = JsonUtility.FromJson<WsFunctionCallLog>(json);
            if (log?.entry == null)
            {
                Debug.LogWarning("WS function_call_log missing entry payload");
                return;
            }

            string argsSummary = BuildArgsSummary(log.entry.args);
            string name = string.IsNullOrEmpty(log.entry.name) ? "unknown" : log.entry.name;
            string id = string.IsNullOrEmpty(log.entry.id) ? "n/a" : log.entry.id;
            string timestamp = log.entry.ts > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(log.entry.ts).ToLocalTime().ToString("u")
                : "n/a";

            Debug.Log($"WS function_call_log received name={name} id={id} ts={timestamp} args={argsSummary}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to process function_call_log: " + ex.Message);
        }
    }

    private void HandleFunctionCallResult(string json)
    {
        try
        {
            var result = JsonUtility.FromJson<WsFunctionCallResult>(json);
            if (result?.entry == null)
            {
                Debug.LogWarning("WS function_call_result missing entry payload");
                return;
            }

            string argsSummary = BuildArgsSummary(result.entry.response ?? result.entry.args);
            string name = string.IsNullOrEmpty(result.entry.name) ? "unknown" : result.entry.name;
            string id = string.IsNullOrEmpty(result.entry.id) ? "n/a" : result.entry.id;
            string timestamp = result.entry.ts > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(result.entry.ts).ToLocalTime().ToString("u")
                : "n/a";

            Debug.Log($"WS function_call_result received name={name} id={id} ts={timestamp} args={argsSummary}");
            HandleWebcamAction(result.entry);
            OnFunctionCallResult?.Invoke(result.entry);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to process function_call_result: " + ex.Message);
        }
    }

    private void HandleWebcamAction(FunctionCallEntry entry)
    {
        string action = entry?.response?.action ?? entry?.args?.action;
        if (string.IsNullOrEmpty(action)) return;

        Debug.Log($"[RecordAudio] Received Action: {action}");

        if (webcam == null)
        {
            Debug.LogWarning("Webcam component not assigned; cannot execute webcam action");
            return;
        }

        if (string.Equals(action, "start", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "cam-start", StringComparison.OrdinalIgnoreCase))
        {
            webcam.turnOnWebcam();
            Debug.Log($"Webcam turned ON via function_call_result ({action})");
        }
        else if (string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(action, "cam-stop", StringComparison.OrdinalIgnoreCase))
        {
            webcam.turnOffWebcam();
            Debug.Log($"Webcam turned OFF via function_call_result ({action})");
        }
        else if (action.StartsWith("tapgame-", StringComparison.OrdinalIgnoreCase))
        {
            // Handled by ScenecontrolWS
        }
        else
        {
            Debug.LogWarning($"Unknown webcam action from function_call_result: {action}");
        }
    }


    private string BuildArgsSummary(FunctionCallArgs args)
    {
        if (args == null) return "(no args)";

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(args.foodname))
        {
            sb.Append($"foodname={args.foodname}; ");
        }
        if (args.AsiaCountries != null && args.AsiaCountries.Length > 0)
        {
            sb.Append($"AsiaCountries=[{string.Join(",", args.AsiaCountries)}]; ");
        }
        if (!string.IsNullOrEmpty(args.action))
        {
            sb.Append($"action={args.action}; ");
        }

        if (sb.Length == 0) return "(empty args)";
        return sb.ToString().Trim();
    }

    // Build a JSON payload {type:"audio", data:"<base64-wav>"} ready for sending to Node /audio
    // Usage example (wire it up elsewhere): ws.Send(BuildAudioPayload(recordedClip));
    private string BuildAudioPayload(AudioClip clip)
    {
        if (clip == null) throw new InvalidOperationException("No audio clip to send");

        byte[] wavBytes = AudioClipToWavBytes(clip);
        string base64 = Convert.ToBase64String(wavBytes);
        return $"{{\"type\":\"audio\",\"data\":\"{base64}\"}}";
    }

    // Convert Unity AudioClip (float samples -1..1) to WAV PCM16 (mono/stereo as per clip)
    private byte[] AudioClipToWavBytes(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        byte[] pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Mathf.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(v & 0xff);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }

        int sampleRate = clip.frequency;
        int channels = clip.channels;
        int byteRate = sampleRate * channels * 2;
        int blockAlign = channels * 2;

        byte[] header = new byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        BitConverter.GetBytes(36 + pcm.Length).CopyTo(header, 4);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
        BitConverter.GetBytes(16).CopyTo(header, 16);           // Subchunk1Size
        BitConverter.GetBytes((short)1).CopyTo(header, 20);     // PCM
        BitConverter.GetBytes((short)channels).CopyTo(header, 22);
        BitConverter.GetBytes(sampleRate).CopyTo(header, 24);
        BitConverter.GetBytes(byteRate).CopyTo(header, 28);
        BitConverter.GetBytes((short)blockAlign).CopyTo(header, 32);
        BitConverter.GetBytes((short)16).CopyTo(header, 34);    // bits per sample
        Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
        BitConverter.GetBytes(pcm.Length).CopyTo(header, 40);

        byte[] wav = new byte[header.Length + pcm.Length];
        Buffer.BlockCopy(header, 0, wav, 0, header.Length);
        Buffer.BlockCopy(pcm, 0, wav, header.Length, pcm.Length);
        return wav;
    }

    // Send the current recorded clip to Node via WebSocket (/audio)
    private void SendRecordingToNode()
    {
        if (recordedClip == null)
        {
            Debug.LogWarning("No recorded clip to send");
            return;
        }
        if (ws == null || ws.ReadyState != WebSocketState.Open)
        {
            Debug.LogWarning("WebSocket not connected; cannot send audio");
            return;
        }

        string payload = BuildAudioPayload(recordedClip);
        ws.SendAsync(payload, null);
        Debug.Log($"Sent audio payload to Node (samples: {recordedClip.samples}, channels: {recordedClip.channels}, freq: {recordedClip.frequency}, payloadLength: {payload.Length})");
    }

    // Notify downstream that the turn ended (after mic was released)
    private void SendTurnComplete()
    {
        if (ws == null || ws.ReadyState != WebSocketState.Open)
        {
            Debug.LogWarning("WebSocket not connected; cannot send turn_complete");
            return;
        }

        string payload = BuildTurnCompletePayload();
        ws.SendAsync(payload, null);
        Debug.Log("Sent turn_complete payload after mic stop");
    }

    private string BuildTurnCompletePayload()
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int durationMs = recordingLength > 0 ? Mathf.Max(0, (int)(recordingLength * 1000f)) : 0;
        return $"{{\"type\":\"turn_complete\",\"duration_ms\":{durationMs},\"ts\":{ts}}}";
    }

    // Play PCM16 base64 (mono) coming from server (e.g., audio_broadcast / ai_response)
    private void PlayIncomingPcm(string base64Pcm, int sampleRate)
    {
        try
        {
            byte[] pcmBytes = Convert.FromBase64String(base64Pcm);
            int sampleCount = pcmBytes.Length / 2; // int16
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short v = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
                samples[i] = Mathf.Clamp(v / 32768f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Incoming", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            audioSource.PlayOneShot(clip);
            Debug.Log($"Playing incoming audio from server (samples: {sampleCount}, sampleRate: {sampleRate})");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to play incoming audio: " + ex.Message);
        }
    }

    [Serializable]
    public class WsAudioMessage
    {
        public string type;
        public string audio;
        public int sampleRate;
    }

    [Serializable]
    public class WsFunctionCallLog
    {
        public string type;
        public FunctionCallEntry entry;
    }

    [Serializable]
    public class WsFunctionCallResult
    {
        public string type;
        public FunctionCallEntry entry;
    }

    [Serializable]
    public class FunctionCallEntry
    {
        public string id;
        public string name;
        public FunctionCallArgs args;
        public FunctionCallArgs response;
        public long ts;
    }

    [Serializable]
    public class FunctionCallArgs
    {
        public string foodname;
        public string[] AsiaCountries;
        public string action;
    }

    [Serializable]
    public class WsTypeOnly
    {
        public string type;
    }
}
