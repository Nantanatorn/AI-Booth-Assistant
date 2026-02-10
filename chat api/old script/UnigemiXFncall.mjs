// WebSocket audio receiver + Gemini reply (Unity sends base64 WAV, we forward to Gemini and play reply)
import dotenv from "dotenv";
import { WebSocketServer , WebSocket } from "ws";
import { GoogleGenAI, Modality, Type} from "@google/genai";
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
const functionCallLog = []; // store function-calling events

const FoodFunctionDeclaration = {
  name: 'get_thai_food',
  description: 'Gets thai food.',
  parameters: {
    type: Type.OBJECT,
    properties: {
      foodname: {
        type: Type.STRING,
        description: 'thaifood name',
      },
    },
    required: ['foodname'],
  },
};

const ListAsiaFunctionDeclaration = {
  name: 'List_Asia_Countries',
  description: 'List  Asia countries.',
  parameters: {
    type: Type.OBJECT,
    properties: {
      // ชื่อตัวแปรที่จะรับค่าเป็น Array
      AsiaCountries: { 
        type: Type.ARRAY,  // <--- 1. กำหนด Type เป็น ARRAY
        description: "Countries in  Asia", 
        items: {           // <--- 2. ต้องระบุว่าข้างใน Array เป็นอะไร
          type: Type.STRING // บอกว่าเป็น List ของ "ข้อความ"
        }
      }, 
    },
    required: ['AsiaCountries'],
  },
};

const WebCamControl = {
  name: 'WebCam_Control',
  description: 'Control the webcam device.',
  parameters: {
    type: Type.OBJECT,
    properties: {
      action: {
        type: Type.STRING,
        description: 'Action to perform on the webcam (e.g., "start", "stop").', 
      },
    },
    required: ['action'],
  },
};


function logFunctionCalls(message) {
  const calls = [];

  const toolCalls = message?.toolCall?.functionCalls;
  if (Array.isArray(toolCalls)) {
    calls.push(...toolCalls);
  }

  const parts = message?.serverContent?.modelTurn?.parts || [];
  for (const part of parts) {
    if (part?.functionCall) {
      calls.push(part.functionCall);
    }
  }

  if (!calls.length) {
    return;
  }

  for (const call of calls) {
    const argString = safeStringifyArgs(call?.args);
    const idLabel = call?.id ? ` id=${call.id}` : "";
    const nameLabel = call?.name ? ` name=${call.name}` : "";
    console.log(`[function_call]${idLabel}${nameLabel} args=${argString}`);
  }
}

function safeStringifyArgs(args) {
  try {
    return JSON.stringify(args ?? {});
  } catch (err) {
    return `[unserializable args: ${err?.message ?? err}]`;
  }
}

// Send function responses so the model can use tool outputs to reply and forward JSON to the requesting client
async function handleFunctionCalls(session, message, onFunctionCallEntry) {
  const toolCalls = message?.toolCall?.functionCalls;
  if (!Array.isArray(toolCalls) || toolCalls.length === 0) {
    return;
  }

  for (const call of toolCalls) {
    // For now, echo back the args so the model can ground its answer.
    const response = call?.args ?? {};
    const argString = safeStringifyArgs(response);
    console.log(`[function_response] id=${call?.id ?? "n/a"} name=${call?.name ?? "n/a"} response=${argString}`);
    const entry = {
      id: call?.id ?? null,
      name: call?.name ?? null,
      args: call?.args ?? {},
      response,
      ts: Date.now(),
    };
    functionCallLog.push(entry);
    console.log(`[function_call_log] id=${entry.id ?? "n/a"} name=${entry.name ?? "n/a"} args=${safeStringifyArgs(entry.args)}`);
    broadcast({ type: "function_call_log", entry });
    if (typeof onFunctionCallEntry === "function") {
      try {
        onFunctionCallEntry(entry);
      } catch (err) {
        console.error("Failed to forward function_call entry to client:", err?.message ?? err);
      }
    }
    await session.sendToolResponse({
      functionResponses: [{
        id: call?.id,
        name: call?.name,
        response,
      }],
    });
  }
}



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
        systemInstruction: "You are a helpful assistant and answer in a friendly tone response in English If a request can use a function call, then use one. There are two function calls: FoodFunctionDeclaration (use when someone wants to find Thai food) and ListAsiaFunctionDeclaration (use when someone wants to find countries in Asia). After getting the result from the function call, read it out loud for questions that use a function call..",
        temperature: 0.4,
      tools: [{
      functionDeclarations: [FoodFunctionDeclaration, ListAsiaFunctionDeclaration, WebCamControl]
    }],
      },
      callbacks: {
        onopen: () => console.log("Gemini session opened"),
        onmessage: async (message) => {
          // Reply to tool calls immediately so the model can continue
          await handleFunctionCalls(session, message, (entry) => {
            if (ws.readyState === WebSocket.OPEN) {
              ws.send(JSON.stringify({ type: "function_call_result", entry }));
            }
          });
          // Log any tool/function calls for debugging
          logFunctionCalls(message);
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
      console.log("Sending ai_response", {
        hasAudio: !!result.audio,
        byteLength: result.audio ? Buffer.from(result.audio, "base64").length : 0,
        sampleRate: result.sampleRate || 24000,
        hasText: !!result.text,
      });
      ws.send(JSON.stringify(response));
      if (result.audio) {
        console.log("Broadcasting Gemini audio", {
          sampleRate: result.sampleRate || 24000,
          byteLength: Buffer.from(result.audio, "base64").length,
          hasText: !!result.text,
        });
        // Broadcast Gemini audio to all connected clients
        broadcast({ type: "audio_broadcast", audio: result.audio, sampleRate: result.sampleRate || 24000 });
        // Play Gemini audio response on the server side for monitoring
        // playAudio(result.audio, result.sampleRate || 24000);
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
