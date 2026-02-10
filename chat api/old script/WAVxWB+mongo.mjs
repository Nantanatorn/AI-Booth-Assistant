// Text prompt -> Gemini audio reply; saves each turn as WAV
import { GoogleGenAI, Modality, Type } from "@google/genai";
import dotenv from "dotenv";
import readline from "node:readline";
import Speaker from "speaker";
import { WebSocketServer, WebSocket } from "ws";
import { MongoDBAtlasVectorSearch } from "@langchain/mongodb";
import { GoogleGenerativeAIEmbeddings } from "@langchain/google-genai";
import mongoose from "mongoose";
import prompt from "./prompt.js";

dotenv.config();

// Ensure @google/genai has a WebSocket implementation in Node
if (!globalThis.WebSocket) {
  globalThis.WebSocket = WebSocket;
}

const GEMINI_API_KEY = process.env.GEMINI_API_KEY;
const MONGO_URL = process.env.MONGO_URL;

if (!GEMINI_API_KEY) {
  console.error("Missing GEMINI_API_KEY in environment");
  process.exit(1);
}
if (!MONGO_URL) {
  console.error("Missing MONGO_URL in environment");
  process.exit(1);
}

const ai = new GoogleGenAI({ apiKey: GEMINI_API_KEY });
// Keep the audio-native model as requested
const model = "gemini-2.5-flash-native-audio-preview-09-2025";
const prompt1 = prompt;
const PORT = process.env.PORT || 3100;

const wss = new WebSocketServer({ port: PORT, path: "/audio" });
const clients = new Set();
const functionCallLog = []; // store function-calling events for broadcasting

let vectorStore; // Unified vector store for Atlas

// --- Mongoose Schema (Must match server.js) ---
const KnowledgeSchema = new mongoose.Schema({
  source: { type: String, required: true },
  topic: { type: String, required: true },
  content: { type: String, required: true },
  embedding: { type: [Number], required: true },
  createdAt: { type: Date, default: Date.now },
});

const Knowledge = mongoose.model("Knowledge", KnowledgeSchema, "Exzy Products");

async function initializeRAG() {
  console.log("Connecting to MongoDB...");
  try {
    await mongoose.connect(MONGO_URL);
    console.log("✅ MongoDB Connected");
  } catch (err) {
    console.error("❌ MongoDB Connection Error:", err);
    process.exit(1);
  }

  console.log("Initializing MongoDB Atlas Vector Search...");

  const embeddings = new GoogleGenerativeAIEmbeddings({
    apiKey: GEMINI_API_KEY,
    modelName: "text-embedding-004",
  });

  // Connect to the specific collection
  const collection = mongoose.connection.db.collection("Exzy Products");

  // Initialize Atlas Vector Search
  vectorStore = new MongoDBAtlasVectorSearch(embeddings, {
    collection: collection,
    indexName: "smart_index", // Ensure you created this index in Atlas!
    textKey: "topic", // Search primarily on topic, but embedding covers content too
    embeddingKey: "embedding",
  });

  console.log("MongoDB Atlas Vector Search Ready.");
}

// --- Tool Definitions ---
const WebCamControl = {
  name: "WebCam_Control",
  description: "Control the webcam device.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      action: {
        type: Type.STRING,
        description:
          'Action to perform on the webcam (e.g., "cam-start", "cam-stop").',
      },
    },
    required: ["action"],
  },
};

const GameControl = {
  name: "Game_Control",
  description: "Control the game device.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      action: {
        type: Type.STRING,
        description:
          'Action to perform on the game (e.g., "tapgame-start", "tapgame-stop").',
      },
    },
    required: ["action"],
  },
};

const SearchKnowledgeTool = {
  name: "search_knowledge",
  description: "Search for  information about Exzy co.ltd products.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      query: {
        type: Type.STRING,
        description: "The search query.",
      },
    },
    required: ["query"],
  },
};

const config = {
  responseModalities: [Modality.AUDIO],
  systemInstruction: prompt,
  tools: [
    {
      functionDeclarations: [WebCamControl, SearchKnowledgeTool, GameControl],
    },
  ],
  outputAudioTranscription: {},
};

// Debug log when the model triggers a function call/tool call
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
    // console.log(`[function_call]${idLabel}${nameLabel} args=${argString}`);
  }
}

function safeStringifyArgs(args) {
  try {
    return JSON.stringify(args ?? {});
  } catch (err) {
    return `[unserializable args: ${err?.message ?? err}]`;
  }
}

// Send function responses so the model can use tool outputs to reply and broadcast/log
async function handleFunctionCalls(session, message, onFunctionCallEntry) {
  const toolCalls = message?.toolCall?.functionCalls;
  if (!Array.isArray(toolCalls) || toolCalls.length === 0) {
    return;
  }

  for (const call of toolCalls) {
    // For now, echo back the args so the model can ground its answer.
    const args = call?.args ?? {};
    let response = args; // Default response is just the args (for non-RAG tools)

    const argString = safeStringifyArgs(args);
    console.log(
      `[function_response] id=${call?.id ?? "n/a"} name=${
        call?.name ?? "n/a"
      } response=${argString}`
    );

    let result = "No information found.";
    let searchResults = [];
    let isRagTool = false;

    if (call.name === "search_knowledge") {
      isRagTool = true;
      // console.log(`🔍 Searching Knowledge for: ${args.query}`);
      // Use the unified vectorStore for all searches
      searchResults = await vectorStore.similaritySearchWithScore(
        args.query,
        3
      );
    }

    if (isRagTool) {
      if (searchResults.length > 0) {
        // console.log(`📊 Found ${searchResults.length} results`);
        if (searchResults.length > 0) {
          // console.log(
          //   "DEBUG: First result doc:",
          //   JSON.stringify(searchResults[0][0], null, 2)
          // );
        }

        const validResults = searchResults
          .filter(([doc, score]) => score >= 0.5)
          .map(([doc, score]) => {
            const topic =
              doc.pageContent || doc.metadata.topic || "Unknown Topic";
            const content = doc.metadata.content || "";
            const source = doc.metadata.source || "Unknown Source";

            console.log(`Topic: ${topic}`);
            console.log(`Content: ${content}`);
            console.log("--------------------------------------------------");

            return `Topic: ${topic}\nContent: ${content}\nSource: ${source}`;
          });

        if (validResults.length > 0) {
          result = validResults.join("\n\n---\n\n");
          // console.log(`📄 Found: ${result.substring(0, 100)}...`);
        } else {
          // console.log("❌ Score too low.");
          result = "No relevant information found in the knowledge base.";
        }
      } else {
        // console.log("❌ No results found.");
        result = "No information found.";
      }
      // For RAG tools, the response MUST be the result object
      response = { result };
    }

    const entry = {
      id: call?.id ?? null,
      name: call?.name ?? null,
      args: call?.args ?? {},
      response,
      ts: Date.now(),
    };
    functionCallLog.push(entry);
    broadcast({ type: "function_call_log", entry });
    if (typeof onFunctionCallEntry === "function") {
      try {
        onFunctionCallEntry(entry);
      } catch (err) {
        console.error(
          "Failed to forward function_call entry:",
          err?.message ?? err
        );
      }
    }
    await session.sendToolResponse({
      functionResponses: [
        {
          id: call?.id,
          name: call?.name,
          response,
        },
      ],
    });
  }
}

wss.on("connection", (ws) => {
  console.log("WS client connected");
  clients.add(ws);

  let session;
  let bufferedMessages = [];
  let pendingResolve = null;

  const collectTurnsFromQueue = () =>
    new Promise((resolve) => {
      const idx = bufferedMessages.findIndex(
        (m) => m?.serverContent?.turnComplete
      );
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
      model,
      config,
      callbacks: {
        onopen: () => console.log("Gemini session opened (WS)"),
        onmessage: async (message) => {
          await handleFunctionCalls(session, message, (entry) => {
            if (ws.readyState === WebSocket.OPEN) {
              console.log(
                `[Server] Sending direct: type=function_call_result, action=${
                  entry.response?.action || entry.args?.action
                }`
              );
              ws.send(JSON.stringify({ type: "function_call_result", entry }));
            }
          });

          // ✅ Capture Output Audio Transcription
          // Check for camelCase OR snake_case key in serverContent
          const content = message.serverContent;
          if (
            content &&
            content.outputTranscription &&
            content.outputTranscription.text
          ) {
            const text = content.outputTranscription.text;
            console.log("📝 Output Transcription:", text);

            if (ws.readyState === WebSocket.OPEN) {
              ws.send(
                JSON.stringify({ type: "audio_transcription", text: text })
              );
            }
          }

          logFunctionCalls(message);
          bufferedMessages.push(message);
          if (message?.serverContent?.turnComplete && pendingResolve) {
            const msgs = bufferedMessages;
            bufferedMessages = [];
            pendingResolve(msgs);
          }
        },
        onerror: (e) =>
          console.error("Gemini session error (WS):", e?.message ?? e),
        onclose: () => console.log("Gemini session closed (WS)"),
      },
    })
    .then((s) => (session = s))
    .catch((err) => {
      console.error("Failed to create Gemini session for WS client:", err);
      ws.close();
    });

  ws.on("message", async (raw) => {
    try {
      const payload = JSON.parse(raw.toString());
      if (payload.type !== "audio" || !payload.data) return;

      const { pcmBase64, sampleRate, channels } = parseWavToPcm(payload.data);
      console.log(`Received audio from WS: ${sampleRate} Hz, ${channels} ch`);

      const liveSession = session || (await sessionReady);
      if (!liveSession) throw new Error("Gemini session not ready");

      const result = await handleAudio(
        liveSession,
        pcmBase64,
        sampleRate,
        collectTurnsFromQueue
      );
      const response = { type: "ai_response", ...result };
      console.log(`[Server] Sending direct: type=${response.type}`);
      ws.send(JSON.stringify(response));
      if (result.audio) {
        // Play audio locally on the server
        playAudio(
          Buffer.from(result.audio, "base64"),
          result.sampleRate || 24000
        );

        broadcast({
          type: "audio_broadcast",
          audio: result.audio,
          sampleRate: result.sampleRate || 24000,
        });
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

async function live() {
  await initializeRAG();
  const responseQueue = [];

  async function waitMessage() {
    let message;
    while (!message) {
      message = responseQueue.shift();
      if (!message) {
        await new Promise((resolve) => setTimeout(resolve, 100));
      }
    }
    return message;
  }

  async function handleTurn() {
    const turns = [];
    let done = false;
    while (!done) {
      const message = await waitMessage();
      turns.push(message);
      const hasToolCall =
        message?.toolCall?.functionCalls?.length > 0 ||
        message?.serverContent?.modelTurn?.parts?.some((p) => p.functionCall);

      if (hasToolCall) {
        console.log("⚡ Received Tool Call message");
        done = true;
      } else if (message.serverContent && message.serverContent.turnComplete) {
        done = true;
      }
    }
    return turns;
  }

  const session = await ai.live.connect({
    model,
    callbacks: {
      onopen: () => console.log("✅ Session opened"),
      onmessage: async (message) => {
        await handleFunctionCalls(session, message, (entry) => {
          broadcast({ type: "function_call_result", entry });
        });
        logFunctionCalls(message);
        responseQueue.push(message);
      },
      onerror: (e) => {
        console.error("Error:", e.message);
      },
      onclose: (e) => {
        console.log("🔻 Session closed:", e.reason);
      },
    },
    config,
  });

  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
  });

  const askUser = (query) =>
    new Promise((resolve) => {
      rl.question(query, (answer) => resolve(answer));
    });

  let turnIndex = 1;
  console.log("💬 พิมพ์ข้อความคุยกับ AI ได้เลย (พิมพ์ 'exit' เพื่อออก)");

  let running = true;
  while (running) {
    const userInput = await askUser("คุณ: ");

    if (!userInput.trim()) {
      continue;
    }

    if (userInput.toLowerCase() === "exit") {
      running = false;
      break;
    }

    // Send text input to the model
    session.sendRealtimeInput({
      text: userInput,
    });

    console.log("🤖 กำลังรอคำตอบ (audio)...");

    let turns = await handleTurn();

    // Check if the turn involved a tool call
    let toolCallMsg = turns.find((msg) => {
      return (
        msg?.toolCall?.functionCalls?.length > 0 ||
        msg?.serverContent?.modelTurn?.parts?.some((p) => p.functionCall)
      );
    });

    // If it was a tool call, the handleFunctionCalls (in onmessage) has already triggered.
    // We just need to wait for the NEXT turn which will contain the audio.
    while (toolCallMsg) {
      console.log("🤖 Model called a tool, waiting for the result audio...");

      const nextTurns = await handleTurn();
      turns = turns.concat(nextTurns);

      // Check if the new turn is ALSO a tool call (chained calls)
      toolCallMsg = nextTurns.find((msg) => {
        return (
          msg?.toolCall?.functionCalls?.length > 0 ||
          msg?.serverContent?.modelTurn?.parts?.some((p) => p.functionCall)
        );
      });
    }

    // Combine returned audio
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

    if (combinedAudio.length === 0) {
      console.log("⚠ ไม่มี audio กลับมาจากโมเดลในรอบนี้");
      continue;
    }

    const audioBuffer = new Int16Array(combinedAudio);
    const audioBase64 = Buffer.from(audioBuffer.buffer).toString("base64");
    // play locally if needed
    // playAudio(Buffer.from(audioBuffer.buffer), 24000);
    // broadcast to Unity/WS clients
    broadcast({
      type: "audio_broadcast",
      audio: audioBase64,
      sampleRate: 24000,
    });
  }

  rl.close();
  session.close();
}

function parseWavToPcm(base64) {
  const buf = Buffer.from(base64, "base64");
  if (buf.length < 44) throw new Error("Wave data too short");
  const audioFormat = buf.readUInt16LE(20);
  const channels = buf.readUInt16LE(22);
  const sampleRate = buf.readUInt32LE(24);
  if (audioFormat !== 1) throw new Error("Only PCM WAV supported");
  if (channels !== 1 && channels !== 2)
    throw new Error("Only mono/stereo supported");
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

async function handleAudio(
  session,
  pcmBase64,
  sampleRate,
  collectTurnsFromQueue
) {
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
    response.sampleRate = 24000;
  }

  return response;
}

function broadcast(obj) {
  if (obj.type !== "audio_broadcast") {
    console.log(`[Server] Broadcasting: type=${obj.type}`);
  }
  const msg = JSON.stringify(obj);
  for (const client of clients) {
    if (client.readyState === WebSocket.OPEN) {
      client.send(msg);
    }
  }
}

async function main() {
  await live().catch((e) => console.error("got error", e));
}

main();

function playAudio(pcmBuffer, sampleRate) {
  const speaker = new Speaker({
    channels: 1,
    bitDepth: 16,
    sampleRate,
    signed: true,
  });
  speaker.write(pcmBuffer);
  speaker.end();
}
