using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;
using WebSocketSharp;
using System.Collections.Concurrent;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class RecordAudio : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // Singleton guard
    private static RecordAudio _instance;
    public static RecordAudio Instance => _instance; // Public Getter
    private static WebSocket _sharedWs;
    private bool _isPrimary;

    [Header("Audio Settings")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] string wsUrl = "ws://localhost:3100/audio";
    [SerializeField] private Webcam webcam;
    [SerializeField] private Image statusButtonImage;
    [SerializeField] private Text statusButtonText;
    [SerializeField] private GameObject ripplePrefab; // Prefab สำหรับ Ripple Effect
    [SerializeField] private float rippleSpawnRate = 0.7f; // ความถี่ในการปล่อย (ปรับให้น้อยลงเพื่อลดการทับกัน)
    [SerializeField] private float rippleExpandSpeed = 0.8f; // ความเร็วในการขยาย
    [SerializeField] private Color rippleColor = new Color(0, 1, 1, 0.15f); // สีของ Ripple (ลด Alpha ลงเพื่อให้ซ้อนกันสวยขึ้น)
    [SerializeField] private float rippleMinScale = 3.0f; // ขนาดเล็กสุด (ปรับกลับเป็น 3 เพื่อให้เหมือนเดิมตอนไม่พูด)
    [SerializeField] private float rippleVolumeSensitivity = 5.0f; // ความไวต่อเสียง (ลดลงหน่อยเพราะฐานใหญ่แล้ว)

    [Header("Connection Settings")]
    [SerializeField] private bool autoReconnect = true;
    [SerializeField] private float reconnectDelay = 3.0f;
    private string sessionId;
    private bool isReconnecting = false;

    [Header("UI Colors")]
    [SerializeField] private Color recordingColor = Color.red;
    [SerializeField] private Color stoppedColor = Color.white;

    // Recording State
    private AudioClip recordingClip;
    private string micDevice;
    private int recordingSampleRate = 24000; // Reverted to 24kHz per user request
    private int lastSamplePos = 0;
    private bool isRecording = false;
    private Coroutine recordingCoroutine;
    private Coroutine rippleCoroutine; // Coroutine สำหรับ Ripple
    private Coroutine thinkingTimeoutCoroutine; // ✅ NEW: Timeout for AI thinking state

    [Header("Thinking Timeout")]
    [SerializeField] private float thinkingTimeout = 10.0f; // 10 seconds


    // Playback State
    private ConcurrentQueue<float[]> audioQueue = new ConcurrentQueue<float[]>();
    private bool isPlaying = false;

    // Public getters for state
    public bool IsRecording => isRecording;
    public bool IsPlaying => isPlaying;
    public bool IsProcessing { get; private set; }
    public float CurrentMicVolume { get; private set; } // Current Volume (0-1)

    // WebSocket
    private WebSocket ws;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    public static event Action<FunctionCallEntry> OnFunctionCallResult;
    public static event Action<string> OnTranscription; // New Event for Dialogue
    public static event Action<string> OnUserTranscription; // New Event for User Text
    public static event Action<string> OnPlayVideoUrl; // ✅ NEW: Event for YouTube Video
    public event Action OnStartRecording;
    public event Action<bool> OnStopRecording; // ✅ Changed to Action<bool>
    public event Action OnTextSent; // New Event for FAQ UI control
    public event Action OnForceIdle; // ✅ New Event: Force Idle Trigger

    private void Awake()
    {
        // --- 1. Singleton & Init ---
        if (webcam == null) webcam = FindFirstObjectByType<Webcam>();

        if (audioSource != null)
        {
            audioSource.spatialBlend = 0f; // Force 2D Sound (Global)
            audioSource.loop = false;
        }

        if (_instance != null && _instance != this)
        {
            _isPrimary = false;
            // Note: Secondary instances don't manage the WS connection or session ID
            return;
        }
        _isPrimary = true;
        _instance = this;
        DontDestroyOnLoad(gameObject);

        // --- 2. Session ID Init ---
        // สร้าง Session ID เพื่อให้จำ Context เดิมได้เมื่อ Reconnect
        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = Guid.NewGuid().ToString();
            Debug.Log($"🆔 New Session ID: {sessionId}");
        }

        // --- 3. WebSocket Init ---
        ConnectWebSocket();
    }

    private void ConnectWebSocket()
    {
        if (_sharedWs != null && _sharedWs.ReadyState == WebSocketState.Open) return;

        // Append Session ID to URL
        string finalUrl = $"{wsUrl}?sessionId={sessionId}";
        Debug.Log($"🔌 Connecting to: {finalUrl}");

        _sharedWs = new WebSocket(finalUrl);
        _sharedWs.OnOpen += (_, __) =>
        {
            Debug.Log("✅ WS Connected");
            isReconnecting = false;
        };
        _sharedWs.OnError += (_, e) => Debug.LogError("❌ WS Error: " + e.Message);

        // Handle Closure & Reconnect
        _sharedWs.OnClose += (sender, e) =>
        {
            Debug.LogWarning($"⚠️ WS Closed (Code: {e.Code}, Reason: {e.Reason})");
            if (autoReconnect && _instance != null) // Don't reconnect if app is closing
            {
                mainThreadActions.Enqueue(() =>
                {
                    if (!isReconnecting) StartCoroutine(ReconnectRoutine());
                });
            }
        };

        _sharedWs.OnMessage += OnWsMessage;
        _sharedWs.ConnectAsync();

        ws = _sharedWs;
    }

    private IEnumerator ReconnectRoutine()
    {
        isReconnecting = true;
        Debug.Log($"⏳ Attempting to reconnect in {reconnectDelay} seconds...");
        yield return new WaitForSeconds(reconnectDelay);

        if (_instance == null) yield break; // Safety check

        Debug.Log("🔄 Reconnecting...");
        isReconnecting = false; // Allow OnClose to trigger retry if this fails
        ConnectWebSocket();
    }

    private void Update()
    {
        // --- Manual Release Check (Fix for Tap/Hold issue on Touchscreen) ---
        if (isPressed)
        {
            bool shouldRelease = false;

            // Check Touch input first (for actual touchscreen)
            if (Input.touchCount > 0)
            {
                // Check if ALL touches have ended or been canceled
                bool anyTouchActive = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled)
                    {
                        anyTouchActive = true;
                        break;
                    }
                }
                if (!anyTouchActive)
                {
                    shouldRelease = true;
                    Debug.Log($"[RecordAudio] Touch Released (Frame: {Time.frameCount})");
                }
            }
            // Fallback: Check Mouse input (for Editor/Desktop)
            else if (Input.GetMouseButtonUp(0))
            {
                shouldRelease = true;
                Debug.Log($"[RecordAudio] Mouse Released (Frame: {Time.frameCount})");
            }

            if (shouldRelease)
            {
                isPressed = false;
                StopTalking();
            }
        }

        // Execute queued actions on main thread
        while (mainThreadActions.TryDequeue(out var action))
        {
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("Action failed: " + ex.Message); }
        }

        // Process Audio Queue for Playback
        if (!isPlaying && audioQueue.Count > 0)
        {
            StartCoroutine(PlayAudioQueue());
        }
    }

    // --- PUSH TO TALK INTERFACE ---

    // Wrapper methods for Inspector (Event Trigger)
    public void StartTalking()
    {
        // Debug.Log("Push-to-Talk (Inspector): STARTED");
        isPressed = true; // ✅ Enable "Global Release Check" for both Mouse & Touch
        StartStreaming();
    }

    public void StopTalking()
    {
        // Debug.Log("Push-to-Talk (Inspector): STOPPED");
        StopStreaming();
    }

    public void ToggleTalk()
    {
        if (isRecording)
        {
            StopTalking();
        }
        else
        {
            StartTalking();
        }
    }

    private bool ignoreIncomingAudio = false;

    // ✅ New Method to Stop AI Playback immediately
    public void StopPlayback()
    {
        // Debug.Log("[RecordAudio] Stopping Playback requested.");
        ignoreIncomingAudio = true; // Block remaining chunks for this turn

        // Signal Server to Interrupt/Stop Generation
        if (ws != null && ws.ReadyState == WebSocketState.Open)
        {
            try { ws.SendAsync("{\"type\":\"interrupt\"}", null); }
            catch { /* Ignore send errors during stop */ }
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.timeSamples = 0; // รีเซ็ตหัวอ่าน (Hard Reset like StartStreaming)
        }

        // Clear any pending audio chunks
        while (audioQueue.TryDequeue(out var _)) { }

        // รีเซ็ตหัวเขียน (Hard Reset like StartStreaming)
        writePos = 0;
        isPlaying = false;
        IsProcessing = false; // ✅ Reset Processing State explicitly on Stop/Interrupt
    }

    // ✅ New Method to Force Cancel Everything (Recording & Playback)
    public void CancelEverything()
    {
        // 1. Stop Playback (Output)
        StopPlayback();

        // 2. Stop Recording (Input) if active
        if (isRecording)
        {
            // Force stop locally
            Microphone.End(micDevice);
            isRecording = false;
            if (recordingCoroutine != null) StopCoroutine(recordingCoroutine);
            
            if (rippleCoroutine != null) StopCoroutine(rippleCoroutine); // Stop Ripple
            if (statusButtonImage != null) statusButtonImage.color = stoppedColor;

            // Don't send activity_end if we are cancelling? 
            // Or send it but we already set ignoreIncomingAudio in StopPlayback called above?
            // StopPlayback sets ignoreIncomingAudio = true.
            // So any result from this recording stopping will be ignored.
        }

        // 3. Force Reset State
        IsProcessing = false;
        isPlaying = false;
        isPressed = false; // Reset push-to-talk state

        OnForceIdle?.Invoke(); // ✅ Notify listeners to force idle
    }

    // Flag to track pressing state
    private bool isPressed = false;

    // Interface implementations (kept for direct script usage if needed)
    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
        StartTalking();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // Logic moved to Update
    }

    // --- IDragHandler Implementation to prevent ScrollRect from stealing events ---
    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) { }

    // --- 1. RECORDING & STREAMING LOGIC ---

    private float recordingStartTime; // Timestamp to detect short taps

    private void StartStreaming()
    {
        if (!_isPrimary) { _instance?.StartStreaming(); return; }

        // ✅ Stop AI audio immediately when user starts talking
        StopPlayback();

        recordingStartTime = Time.time; // ✅ Track start time

        // ignoreIncomingAudio จะถูก reset เป็น false ใน StopStreaming() ตอนพร้อมรับคำตอบ AI ใหม่

        if (statusButtonImage != null) statusButtonImage.color = recordingColor; // ... (rest of StartStreaming)

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("Error: No microphone devices found!");
            return;
        }
        foreach (var device in Microphone.devices)
        {
            // Debug.Log("Available Mic: " + device);
        }

        micDevice = Microphone.devices[0];
        Debug.Log($"Starting mic: {micDevice}");

        recordingClip = Microphone.Start(micDevice, true, 10, recordingSampleRate);

        if (Microphone.IsRecording(micDevice))
        {
            // Debug.Log("Microphone started loop recording.");
        }
        else
        {
            Debug.LogError("Microphone failed to start!");
            return;
        }

        // ✅ Reset Playback State
        audioSource.Stop();
        audioSource.timeSamples = 0; // รีเซ็ตหัวอ่าน
        writePos = 0; // รีเซ็ตหัวเขียน

        // เคลียร์ Queue เก่าที่อาจค้างอยู่
        while (audioQueue.TryDequeue(out var _)) { }

        lastSamplePos = 0;
        isRecording = true;

        if (ws != null && ws.ReadyState == WebSocketState.Open)
        {
            IsProcessing = false;
            string json = "{\"type\":\"activity_start\"}";
            ws.SendAsync(json, null);
        }

        int actualFreq = recordingClip.frequency;
        int channels = recordingClip.channels;
        Debug.Log($"Mic Started. Rate: {actualFreq}, Channe ls: {channels}");
        recordingCoroutine = StartCoroutine(StreamMicrophoneData(actualFreq, channels));

        OnStartRecording?.Invoke();
        if (ripplePrefab != null) // เริ่มปล่อย Ripple
        {
            rippleCoroutine = StartCoroutine(SpawnRippleRoutine());
        }
    }

    private void StopStreaming()
    {
        if (!_isPrimary) { _instance?.StopStreaming(); return; }
        if (!isRecording) return;

        if (statusButtonImage != null) statusButtonImage.color = stoppedColor;

        isRecording = false;
        Microphone.End(micDevice);
        if (recordingCoroutine != null) StopCoroutine(recordingCoroutine);

        // Notify Server: Turn Complete
        if (ws != null && ws.ReadyState == WebSocketState.Open)
        {
            string json = "{\"type\":\"activity_end\"}";
            ws.SendAsync(json, null);
        }

        // ✅ Reset mute flag ONLY if it was a real conversation (not a short tap)
        bool isValidTurn = true;
        // Reduced threshold to 0.20f to avoid accidental ignores on short words (e.g. "Ok", "Stop")
        if (Time.time - recordingStartTime >= 0.20f)
        {
            ignoreIncomingAudio = false;
        }
        else
        {
            Debug.Log($"🚫 Short Tap ({(Time.time - recordingStartTime):F2}s) - Ignoring AI Response");
            isValidTurn = false; // Mark as invalid
            // OnShortTapCancellation?.Invoke(); // ✅ Trigger Idle Reset
        }


        if (isValidTurn)
        {
            IsProcessing = true;
            // ✅ Start thinking timeout
            if (thinkingTimeoutCoroutine != null) StopCoroutine(thinkingTimeoutCoroutine);
            thinkingTimeoutCoroutine = StartCoroutine(ThinkingTimeoutRoutine());
        }

        if (rippleCoroutine != null) StopCoroutine(rippleCoroutine); // หยุด Ripple
        OnStopRecording?.Invoke(isValidTurn); // ✅ Pass flag
    }

    // ✅ NEW: Thinking timeout routine - sends Cant action after 10 seconds
    private IEnumerator ThinkingTimeoutRoutine()
    {
        yield return new WaitForSeconds(thinkingTimeout);
        
        if (IsProcessing) // Still waiting for AI response
        {
            Debug.LogWarning($"⏰ AI Thinking Timeout ({thinkingTimeout}s) - Sending Cant action");
            SendCantAction();
        }
        thinkingTimeoutCoroutine = null;
    }

    // ✅ NEW: Send Cant action to server
    public void SendCantAction()
    {
        if (ws != null && ws.ReadyState == WebSocketState.Open)
        {
            string json = "{\"type\":\"action\",\"action\":\"Cant\"}";
            ws.SendAsync(json, null);
            Debug.Log("🚫 Sent Cant action to server");
        }
    }

    private IEnumerator StreamMicrophoneData(int actualSampleRate, int channels)
    {
        // Debug.Log($"Stream Coroutine Started. Rate: {actualSampleRate}, Channels: {channels}");
        while (isRecording)
        {
            int currentPos = Microphone.GetPosition(micDevice);
            if (currentPos < 0 || lastSamplePos == currentPos)
            {
                yield return new WaitForSeconds(0.05f); // Wait a bit if no new data
                continue;
            }

            // Calculate FRAMES (not total samples if stereo)
            int framesToRead = 0;
            if (currentPos > lastSamplePos)
                framesToRead = currentPos - lastSamplePos;
            else
                framesToRead = (recordingClip.samples - lastSamplePos) + currentPos;

            if (framesToRead > 0)
            {
                // Unity buffer is interleaved (L,R,L,R). Total array size = frames * channels
                float[] rawSamples = new float[framesToRead * channels];

                if (lastSamplePos + framesToRead <= recordingClip.samples)
                {
                    recordingClip.GetData(rawSamples, lastSamplePos);
                }
                else
                {
                    // Handle Wrap-around
                    int endPartFrames = recordingClip.samples - lastSamplePos;
                    int wrapPartFrames = framesToRead - endPartFrames;

                    float[] part1 = new float[endPartFrames * channels];
                    float[] part2 = new float[wrapPartFrames * channels];

                    recordingClip.GetData(part1, lastSamplePos);
                    recordingClip.GetData(part2, 0);
                    Array.Copy(part1, 0, rawSamples, 0, part1.Length);
                    Array.Copy(part2, 0, rawSamples, part1.Length, part2.Length);
                }

                // Downmix to Mono
                float[] monoSamples;
                if (channels > 1)
                {
                    monoSamples = new float[framesToRead];
                    for (int i = 0; i < framesToRead; i++)
                    {
                        float sum = 0f;
                        for (int c = 0; c < channels; c++)
                        {
                            sum += rawSamples[i * channels + c];
                        }
                        monoSamples[i] = sum / channels;
                    }
                }
                else
                {
                    monoSamples = rawSamples;
                }

                // Check volume (Max Amp)
                float maxVol = 0f;
                foreach (var s in monoSamples) if (Mathf.Abs(s) > maxVol) maxVol = Mathf.Abs(s);
                CurrentMicVolume = maxVol; // Update public property
                // Debug.Log($"Chunk Frames: {framesToRead}, Max Vol: {maxVol:F4}");

                // Send Chunk (Always Mono)
                // ✅ Echo Cancellation (Basic): Don't send audio while AI is talking
                if (!isPlaying)
                {
                    SendAudioChunk(monoSamples, actualSampleRate);
                }
                lastSamplePos = currentPos;
            }

            // Send frequence: ~20 times per second
            yield return new WaitForSeconds(0.05f);
        }
    }

    private void SendAudioChunk(float[] samples, int sampleRate)
    {
        if (ws == null)
        {
            Debug.LogError("WS is null");
            return;
        }
        if (ws.ReadyState != WebSocketState.Open)
        {
            // Debug.LogWarning($"WS not open (State: {ws.ReadyState}). Skipping chunk.");
            return;
        }

        // Convert float[] to PCM16 byte[]
        byte[] pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Mathf.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(v & 0xff);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }

        string base64 = Convert.ToBase64String(pcm);
        // JSON: { type: "audio_stream", data: "...", sampleRate: 24000 }
        string json = $"{{\"type\":\"audio_stream\",\"data\":\"{base64}\",\"sampleRate\":{sampleRate}}}";
        ws.SendAsync(json, null);
    }

    public void TriggerVideoPlayback()
    {
        if (ws == null || ws.ReadyState != WebSocketState.Open)
        {
            Debug.LogError("Cannot trigger video: WS Disconnected");
            return;
        }
        string json = "{\"type\":\"play_video\"}";
        ws.SendAsync(json, null);
    }

    private void SendTurnComplete()
    {
        if (ws == null || ws.ReadyState != WebSocketState.Open) return;
        string json = $"{{\"type\":\"turn_complete\"}}";
        ws.SendAsync(json, null);
    }

    public void SendTextQuery(string text)
    {
        // Prevent sending while AI is processing or playing
        if (IsProcessing || isPlaying)
        {
            Debug.Log($"[RecordAudio] Blocked - AI is busy (Processing:{IsProcessing}, Playing:{isPlaying})");
            return;
        }

        IsProcessing = true; // Lock immediately
        ignoreIncomingAudio = false; // Reset mute flag for new query

        if (ws == null || ws.ReadyState != WebSocketState.Open)
        {
            Debug.LogError("Cannot send text query: WS Disconnected");
            IsProcessing = false; // Unlock on error
            return;
        }
        // Construct JSON manually to avoid extra allocations/classes if simple
        // { "type": "text_input", "text": "..." }
        string escapedText = text.Replace("\"", "\\\""); // Simple escape
        string json = $"{{\"type\":\"text_input\",\"text\":\"{escapedText}\"}}";
        ws.SendAsync(json, null);
        Debug.Log($"[RecordAudio] Sent Text Query: {text}");

        OnTextSent?.Invoke();
    }


    // --- 2. PLAYBACK LOGIC ---

    private void OnWsMessage(object sender, MessageEventArgs e)
    {
        try
        {
            var meta = JsonUtility.FromJson<WsMeta>(e.Data);

            // 2.1 Handle Function Calls
            if (meta.type == "function_call_result")
            {
                if (ignoreIncomingAudio) return; // ✅ Block actions if interrupted
                var res = JsonUtility.FromJson<WsFunctionCallResult>(e.Data);
                mainThreadActions.Enqueue(() => ProcessFunctionCall(res.entry));
                return;
            }

            // 2.2 Handle Audio Broadcast (Streaming Output)
            if (meta.type == "audio_broadcast")
            {
                if (ignoreIncomingAudio) return; // Discard chunks if playback was stopped

                // ✅ Cancel thinking timeout - AI is responding
                if (thinkingTimeoutCoroutine != null)
                {
                    mainThreadActions.Enqueue(() => {
                        if (thinkingTimeoutCoroutine != null)
                        {
                            StopCoroutine(thinkingTimeoutCoroutine);
                            thinkingTimeoutCoroutine = null;
                        }
                    });
                }

                var msg = JsonUtility.FromJson<WsAudioMessage>(e.Data);
                if (!string.IsNullOrEmpty(msg.audio))
                {
                    // Debug.Log($"📩 Unity Received Audio: {msg.audio.Length} chars");
                    byte[] pcm = Convert.FromBase64String(msg.audio);
                    float[] samples = PcmToFloats(pcm);
                    audioQueue.Enqueue(samples);
                }
            }

            // 2.3 Handle Audio Transcription (Text Response - AI)
            if (meta.type == "audio_transcription")
            {
                if (ignoreIncomingAudio) return; // ✅ Discard text if muted/interrupted

                var trans = JsonUtility.FromJson<WsTranscription>(e.Data);
                if (!string.IsNullOrEmpty(trans.text))
                {
                    // Debug.Log($"📝 Transcription (AI): {trans.text}");
                    mainThreadActions.Enqueue(() => OnTranscription?.Invoke(trans.text));
                }
            }

            // 2.4 Handle Input Transcription (User Text via Tool)
            if (meta.type == "input_audio_transcription")
            {
                if (ignoreIncomingAudio) return; // ✅ Discard user text if interrupted
                var trans = JsonUtility.FromJson<WsTranscription>(e.Data);
                if (!string.IsNullOrEmpty(trans.text))
                {
                    Debug.Log($"🎤 Transcription (User): {trans.text}");
                    mainThreadActions.Enqueue(() => OnUserTranscription?.Invoke(trans.text));
                }
            }

            // ✅ 2.5 Handle Video URL (YouTube)
            if (meta.type == "video_url")
            {
                if (ignoreIncomingAudio) return; // ✅ Block video if interrupted
                var videoMsg = JsonUtility.FromJson<WsVideoUrl>(e.Data);
                if (!string.IsNullOrEmpty(videoMsg.url))
                {
                    Debug.Log($"📹 Received Video URL: {videoMsg.url}");
                    mainThreadActions.Enqueue(() => OnPlayVideoUrl?.Invoke(videoMsg.url));
                }
            }
        }
        catch (Exception ex) { Debug.LogWarning("WS Parse Error: " + ex.Message); }
    }

    private int audioBufferLen = 30;
    private int audioFrequency = 24000;
    private AudioClip streamClip;
    private int writePos = 0;
    private bool bufferInitialized = false;

    private IEnumerator PlayAudioQueue()
    {
        isPlaying = true;
        IsProcessing = false;

        while (audioQueue.Count > 0 || isPlaying)
        {
            // 1. Initial Buffer Creation
            if (!bufferInitialized || streamClip == null)
            {
                streamClip = AudioClip.Create("StreamBuffer", audioFrequency * audioBufferLen, 1, audioFrequency, false);
                audioSource.clip = streamClip;
                audioSource.loop = true;
                writePos = 0;
                bufferInitialized = true;
                Debug.Log("🎵 Audio Buffer Initialized");
            }

            // 2. Playback Control (Anti-Looping)
            if (audioSource.isPlaying)
            {
                int playPos = audioSource.timeSamples;
                // Calculate distance between playPos and writePos
                // If playPos is "too close" to writePos (meaning we ran out of data), Pause.
                // We use a safe threshold (e.g. 0.2s = 4800 samples)
                int threshold = (int)(audioFrequency * 0.1f);

                // Simple check: if playPos is chasing writePos and gets too close
                // (Note: This simple check assumes we don't wrap around wildly. For full circular safety, we need distance logic)
                int distance = writePos - playPos;
                if (distance < 0) distance += streamClip.samples; // Wrap around distance

                if (distance < threshold && audioQueue.Count == 0)
                {
                    // Debug.Log("⏸️ Buffer caught up. Pausing.");
                    audioSource.Pause(); // Pause, don't Stop, to resume smoothly
                }
            }

            // 3. Dequeue & Write
            if (audioQueue.TryDequeue(out var samples))
            {
                // Write Logic with Wrap-Around
                if (writePos + samples.Length <= streamClip.samples)
                {
                    streamClip.SetData(samples, writePos);
                    writePos += samples.Length;
                }
                else
                {
                    // Split Data
                    int endSpace = streamClip.samples - writePos;
                    float[] part1 = new float[endSpace];
                    float[] part2 = new float[samples.Length - endSpace];

                    Array.Copy(samples, 0, part1, 0, endSpace);
                    Array.Copy(samples, endSpace, part2, 0, part2.Length);

                    streamClip.SetData(part1, writePos);
                    streamClip.SetData(part2, 0);

                    writePos = part2.Length;
                    Debug.Log("🔄 Buffer Wrapped Around");
                }

                // Correct writePos just in case
                if (writePos >= streamClip.samples) writePos %= streamClip.samples;

                // 4. Resume Playback if Paused
                if (!audioSource.isPlaying && writePos > samples.Length) // Ensure we have written "something"
                {
                    audioSource.Play();
                    Debug.Log("▶️ Resuming Playback");
                }
            }
            else
            {
                // Idle check
                if (audioQueue.Count == 0)
                {
                    // If queue is empty AND AudioSource is paused (meaning we caught up/finished buffer), stop.
                    if (!audioSource.isPlaying)
                    {
                        break;
                    }
                }
                yield return null;
            }
        }

        isPlaying = false;
    }
    private float[] PcmToFloats(byte[] pcm)
    {
        int sampleCount = pcm.Length / 2;
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short v = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            samples[i] = v / 32768f;
        }
        return samples;
    }

    // --- 3. HELPER CLEANUP ---

    private void ProcessFunctionCall(FunctionCallEntry entry)
    {
        IsProcessing = false;
        Debug.Log($"Action Received: {entry.response?.action ?? entry.args?.action}");
        HandleWebcamAction(entry);
        OnFunctionCallResult?.Invoke(entry);
    }

    private void HandleWebcamAction(FunctionCallEntry entry)
    {
        string action = entry?.response?.action ?? entry?.args?.action;
        if (string.IsNullOrEmpty(action) || webcam == null) return;

        if (action.Equals("start", StringComparison.OrdinalIgnoreCase) || action.Equals("cam-start", StringComparison.OrdinalIgnoreCase))
            webcam.turnOnWebcam();
        else if (action.Equals("stop", StringComparison.OrdinalIgnoreCase) || action.Equals("cam-stop", StringComparison.OrdinalIgnoreCase))
            webcam.turnOffWebcam();
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _sharedWs?.CloseAsync();
            _sharedWs = null;
        }
    }

    // --- 4. DTOs ---

    [Serializable] class WsMeta { public string type; }
    [Serializable] class WsAudioMessage { public string type; public string audio; public int sampleRate; }
    [Serializable] class WsFunctionCallResult { public string type; public FunctionCallEntry entry; }
    [Serializable] class WsTranscription { public string type; public string text; }
    [Serializable] class WsVideoUrl { public string type; public string url; public string title; } // ✅ NEW DTO // New DTO

    [Serializable]
    public class FunctionCallEntry
    {
        public string id;
        public string name;
        public FunctionCallArgs args;
        public FunctionCallArgs response;
    }

    [Serializable]
    public class FunctionCallArgs
    {
        public string action;
        public string product; // New field for product name
        public string foodname;
        public string[] AsiaCountries;
    }


    private IEnumerator SpawnRippleRoutine()
    {
        while (isRecording)
        {
            if (ripplePrefab != null && statusButtonImage != null)
            {
                // Spawn ripple ที่ตำแหน่งปุ่ม
                // Spawn ripple ที่ตำแหน่งปุ่ม (ใช้ parent เดียวกับปุ่ม เพื่อให้จัดลำดับ layer ได้)
                GameObject ripple = Instantiate(ripplePrefab, statusButtonImage.transform.position, Quaternion.identity, statusButtonImage.transform.parent);

                // ตั้งค่าสี
                Image rippleImg = ripple.GetComponent<Image>();
                if (rippleImg != null)
                {
                    rippleImg.color = rippleColor;
                }

                // ย้ายไปอยู่หลังปุ่ม (Render ก่อนปุ่ม) - ใช้ SetAsFirstSibling เพื่อให้อยู่ล่างสุด
                ripple.transform.SetAsFirstSibling();

                // ตั้งค่าความเร็ว (Override Prefab)
                RIppleEffect effect = ripple.GetComponent<RIppleEffect>();
                if (effect != null)
                {
                    effect.expandSpeed = rippleExpandSpeed;

                    // Dynamic Scale based on Volume
                    float dynamicScale = rippleMinScale + (CurrentMicVolume * rippleVolumeSensitivity);
                    effect.targetScale = dynamicScale;
                }


                // ตรวจสอบว่ามี Script RIppleEffect หรือไม่ ถ้าไม่มีก็ทำงานได้ แต่ถ้ามีจะใช้สีที่ตั้งค่าไป StartAlpha ได้ถูกต้อง
                // ตรวจสอบว่ามี Script RIppleEffect หรือไม่ ถ้าไม่มีก็ทำงานได้ แต่ถ้ามีจะใช้สีที่ตั้งค่าไป StartAlpha ได้ถูกต้อง
            }
            yield return new WaitForSeconds(rippleSpawnRate); // ใช้ตัวแปรควบคุมความถี่
        }
    }
}
