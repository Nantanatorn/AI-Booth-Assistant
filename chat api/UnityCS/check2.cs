using UnityEngine;
using System.IO;
using System;
using System.Text;
using WebSocketSharp;
using System.Collections.Concurrent;

public class RecordAudio : MonoBehaviour
{

    private AudioClip recordedClip;
    [SerializeField] AudioSource audioSource;
    [SerializeField] string wsUrl = "ws://localhost:3100/audio";
    private string filePath = "rec.wav";
    private string directoryPath = Application.streamingAssetsPath;
    private float startTime;
    private float recordingLength;
    private WebSocket ws;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    private void Awake()
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        ws = new WebSocket(wsUrl);
        ws.OnOpen += (_, __) => Debug.Log("WS connected: " + wsUrl);
        ws.OnError += (_, e) => Debug.LogError("WS error: " + e.Message);
        ws.OnClose += (_, __) => Debug.Log("WS closed");
        ws.OnMessage += OnWsMessage;
        ws.ConnectAsync();
    }

    public void StartRecording()
    {   
        
        string device = Microphone .devices[0];
        int sampleRate = 24000;
        int lengthSec =  3599;

        recordedClip = Microphone.Start(device, false, lengthSec, sampleRate);
        startTime = Time.realtimeSinceStartup;
        Debug.Log("Recording Started");
    }

        public void PushToTalk()
    {   
        
        if (Input.GetMouseButtonDown(0))
        {   
            Debug.Log("Hold: Start Recording");
            StartRecording();      
        }
        if (Input.GetMouseButtonUp(0))
        {
            Debug.Log("Release: Stop Recording");
            StopRecording();      
        }

    }


    

    public void StopRecording()
    {   
        Debug.Log("Recording Stopped");
        Microphone.End(null);
        recordingLength = Time.realtimeSinceStartup - startTime;
        recordedClip = TrimClip(recordedClip, recordingLength);
        SaveRecording();
        SendRecordingToNode();
    }

    public void PlayRecording()
    {
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

        PushToTalk();
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

        AudioClip trimmedClip = AudioClip.Create(clip.name, samples,clip.channels , clip.frequency, false);
        trimmedClip.SetData(data, 0);

        return trimmedClip;
    }

    private void OnDestroy()
    {
        ws?.CloseAsync();
    }

    private void OnWsMessage(object sender, MessageEventArgs e)
    {
        try
        {
            var msg = JsonUtility.FromJson<WsAudioMessage>(e.Data);
            if (msg == null) return;

            // Accept both ai_response and audio_broadcast
            if (!string.IsNullOrEmpty(msg.audio))
            {
                int sampleRate = msg.sampleRate > 0 ? msg.sampleRate : 24000;
                Debug.Log($"WS message received: audio length={msg.audio.Length}, sampleRate={sampleRate}");
                mainThreadActions.Enqueue(() => PlayIncomingPcm(msg.audio, sampleRate));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("WS message parse failed: " + ex.Message);
        }
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
    private class WsAudioMessage
    {
        public string type;
        public string audio;
        public int sampleRate;
    }
}
