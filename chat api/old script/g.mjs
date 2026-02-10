// WebSocket audio receiver + Gemini reply (Unity sends base64 WAV, we forward to Gemini and play reply)
import dotenv from "dotenv";
import { WebSocketServer , WebSocket } from "ws";
import { GoogleGenAI, Modality } from "@google/genai";
import Speaker from "speaker";

dotenv.config();

if (!globalThis.WebSocket) {
  globalThis.WebSocket = WebSocket;
}

const GEMINI_API_KEY = process.env.GEMINI_API_KEY;
if (!GEMINI_API_KEY) {
  console.error("Missing GEMINI_API_KEY in environment");
  process.exit(1);
}

const PORT = process.env.PORT || 3000;
const MODEL = "gemini-2.5-flash-native-audio-preview-09-2025";

const ai = new GoogleGenAI({ apiKey: GEMINI_API_KEY });

const wss = new WebSocketServer({ port: PORT, path: "/audio" });
const clients = new Set();

wss.on("connection", (ws) => {
  // Unity (or other clients) send JSON { type: "audio", data: "<base64-wav>" }
  console.log("WS client connected");
  clients.add(ws);

  let session;
  let bufferedMessages = [];
  let pendingResolve = null;

  const collectTurnsFromQueue = () =>
    new Promise((resolve) => {
      // If already have turnComplete buffered, resolve immediately
      const idx = bufferedMessages.findIndex((m) => m?.serverContent?.turnComplete);
      if (idx !== -1) {
        const messages = bufferedMessages;
        bufferedMessages = [];
        resolve(messages);
        return;
      }
      pendingResolve = (msgs) => {
        resolve(msgs);
        pendingResolve = null;
      };
    });
  const sessionReady = ai.live
    .connect({
      model: MODEL,
      config: {
        responseModalities: [Modality.AUDIO],
        systemInstruction: "You are a helpful assistant. Respond in Thai.",
      },
      callbacks: {
        onopen: () => console.log("Gemini session opened"),
        onmessage: (message) => {
          bufferedMessages.push(message);
          if (message?.serverContent?.turnComplete && pendingResolve) {
            const msgs = bufferedMessages;
            bufferedMessages = [];
            pendingResolve(msgs);
          }
        },
        onerror: (e) => console.error("Gemini session error:", e?.message ?? e),
        onclose: () => console.log("Gemini session closed"),
      },
    })
    .then((s) => (session = s))
    .catch((err) => {
      console.error("Failed to create Gemini session:", err);
      ws.close();
    });

  ws.on("message", async (raw) => {
    try {
      const payload = JSON.parse(raw.toString());
      if (payload.type !== "audio" || !payload.data) return;

      const { pcmBase64, sampleRate, channels } = parseWavToPcm(payload.data);
      console.log(`Received audio: ${sampleRate} Hz, ${channels} ch`);

      const liveSession = session || (await sessionReady);
      if (!liveSession) throw new Error("Gemini session not ready");

      const result = await handleAudio(
        liveSession,
        pcmBase64,
        sampleRate,
        collectTurnsFromQueue
      );
      const response = { type: "ai_response", ...result };
      ws.send(JSON.stringify(response));
      if (result.audio) {
        // Broadcast Gemini audio to all connected clients
        broadcast({ type: "audio_broadcast", audio: result.audio, sampleRate: result.sampleRate || 24000 });
        // Play Gemini audio response on the server side for monitoring
        playAudio(result.audio, result.sampleRate || 24000);
      }
    } catch (err) {
      console.error("Audio handling failed:", err);
      ws.send(JSON.stringify({ type: "error", message: err.message }));
    }
  });

  ws.on("close", () => {
    console.log("WS client disconnected");
    session?.close?.();
    clients.delete(ws);
  });
  ws.on("error", (err) => console.error("WS error:", err));
});

console.log(`Listening on ws://localhost:${PORT}/audio`);

function parseWavToPcm(base64) {
  // Minimal WAV header parsing: PCM mono/stereo only
  const buf = Buffer.from(base64, "base64");
  if (buf.length < 44) throw new Error("Wave data too short");
  const audioFormat = buf.readUInt16LE(20);
  const channels = buf.readUInt16LE(22);
  const sampleRate = buf.readUInt32LE(24);
  if (audioFormat !== 1) throw new Error("Only PCM WAV supported");
  if (channels !== 1 && channels !== 2) throw new Error("Only mono/stereo supported");
  let offset = 12;
  while (offset + 8 <= buf.length) {
    const chunkId = buf.toString("ascii", offset, offset + 4);
    const chunkSize = buf.readUInt32LE(offset + 4);
    if (chunkId === "data") {
      const dataStart = offset + 8;
      const dataEnd = dataStart + chunkSize;
      const pcm = buf.slice(dataStart, dataEnd);
      return { pcmBase64: pcm.toString("base64"), sampleRate, channels };
    }
    offset += 8 + chunkSize;
  }
  throw new Error("No data chunk found in WAV");
}

async function handleAudio(session, pcmBase64, sampleRate, collectTurnsFromQueue) {
  // Send PCM audio over an existing Gemini live session
  session.sendRealtimeInput({ turn: { turnId: Date.now().toString() } });
  session.sendRealtimeInput({ inputStarted: {} });
  session.sendRealtimeInput({
    audio: { data: pcmBase64, mimeType: `audio/pcm;rate=${sampleRate}` },
  });
  session.sendRealtimeInput({ inputFinished: {} });

  const turns = await collectTurnsFromQueue();

  const text = turns
    .map((t) =>
      (t.serverContent?.modelTurn?.parts || [])
        .map((p) => p.text || "")
        .join("")
    )
    .join("")
    .trim();

  const combinedAudio = turns.reduce((acc, turn) => {
    if (turn.data) {
      const buffer = Buffer.from(turn.data, "base64");
      const intArray = new Int16Array(
        buffer.buffer,
        buffer.byteOffset,
        buffer.byteLength / Int16Array.BYTES_PER_ELEMENT
      );
      return acc.concat(Array.from(intArray));
    }
    return acc;
  }, []);

  const response = {};
  if (text) response.text = text;
  if (combinedAudio.length > 0) {
    const audioBuffer = new Int16Array(combinedAudio);
    response.audio = Buffer.from(audioBuffer.buffer).toString("base64");
    response.sampleRate = 24000; // model output rate
  }

  return response;
}

function playAudio(base64Pcm, sampleRate) {
  const pcmBuffer = Buffer.from(base64Pcm, "base64");
  const speaker = new Speaker({
    channels: 1,
    bitDepth: 16,
    sampleRate,
    signed: true,
  });
  speaker.write(pcmBuffer);
  speaker.end();
}

function broadcast(obj) {
  const msg = JSON.stringify(obj);
  for (const client of clients) {
    if (client.readyState === WebSocket.OPEN) {
      client.send(msg);
    }
  }
}
