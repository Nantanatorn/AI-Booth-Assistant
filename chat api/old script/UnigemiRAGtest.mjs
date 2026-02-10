// WebSocket audio receiver + Gemini reply (Unity sends base64 WAV, we forward to Gemini and play reply)
import dotenv from "dotenv";
import { WebSocketServer, WebSocket } from "ws";
import { GoogleGenAI, Modality, Type } from "@google/genai";
import Speaker from "speaker";
import { MongoDBAtlasVectorSearch } from "@langchain/mongodb";
import { GoogleGenerativeAIEmbeddings } from "@langchain/google-genai";
import mongoose from "mongoose";

dotenv.config();

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

const PORT = process.env.PORT || 3100;
// Using the User-confirmed Native Audio Preview Model
const MODEL = "gemini-2.5-flash-native-audio-preview-09-2025";

const ai = new GoogleGenAI({ apiKey: GEMINI_API_KEY });

const wss = new WebSocketServer({ port: PORT, path: "/audio" });
const clients = new Set();
const functionCallLog = []; // store function-calling events

let vectorStore; // Unified vector store for Atlas

// --- Mongoose Schema ---
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

  const collection = mongoose.connection.db.collection("Exzy Products");

  vectorStore = new MongoDBAtlasVectorSearch(embeddings, {
    collection: collection,
    indexName: "smart_index",
    textKey: "topic",
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
        description: 'Action to perform on the webcam (e.g., "start", "stop").',
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
  description: "Search for information about Exzy co.ltd products.",
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

const prompt = `Your name is Assy. You are an intelligent assistant, 
designed for use in an exhibition to answer questions from interested parties about the company, its products, benefits, and general information of Exzy Co., Ltd.
Rules:
1. If the user asks you to say 'Connected', just say 'Connected' clearly and DO NOT use any tools.
2. For other questions about the company, ALWAYS use the 'search_knowledge' tool to find the answer.
3. Answer concisely but comprehensively.
4. If no information is found via tools, explicitly say 'No information found'.
5. Rely ONLY on data from the database for tool-based queries; DO NOT fabricate information.
6. ALWAYS read the answer out loud clearly.
7. Use the 'WebCam_Control' tool to control the webcam device.
8. Use the 'Game_Control' tool to control the game device.
9. ALWAYS sent Action to control the webcam device or game device.`;

const config = {
  responseModalities: [Modality.AUDIO],
  systemInstruction: prompt,
  tools: [
    {
      functionDeclarations: [WebCamControl, SearchKnowledgeTool, GameControl],
    },
  ],
};

function safeStringifyArgs(args) {
  try {
    return JSON.stringify(args ?? {});
  } catch (err) {
    return `[unserializable args: ${err?.message ?? err}]`;
  }
}

async function handleFunctionCalls(session, message, onFunctionCallEntry) {
  const toolCalls = [];

  // 1. Check Legacy Top-level
  if (message?.toolCall?.functionCalls) {
    toolCalls.push(...message.toolCall.functionCalls);
  }

  // 2. Check Content Parts (Fix for Silence Issue)
  const parts = message?.serverContent?.modelTurn?.parts || [];
  for (const part of parts) {
    if (part.functionCall) {
      toolCalls.push(part.functionCall);
    }
  }

  if (toolCalls.length === 0) {
    return;
  }

  console.log(`⚡ Processing ${toolCalls.length} Function Call(s)`);

  for (const call of toolCalls) {
    const args = call?.args ?? {};
    let response = args;

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
      searchResults = await vectorStore.similaritySearchWithScore(
        args.query,
        3
      );
    }

    if (isRagTool) {
      if (searchResults.length > 0) {
        const validResults = searchResults
          .filter(([doc, score]) => score >= 0.5)
          .map(([doc, score]) => {
            const topic = doc.pageContent || doc.metadata.topic || "Unknown";
            const content = doc.metadata.content || "";
            const source = doc.metadata.source || "Unknown";
            console.log(`Topic: ${topic}`);
            return `Topic: ${topic}\nContent: ${content}\nSource: ${source}`;
          });

        if (validResults.length > 0) {
          result = validResults.join("\n\n---\n\n");
        } else {
          result = "No relevant information found in the knowledge base.";
        }
      } else {
        result = "No information found.";
      }
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
        console.error("Failed to forward function_call entry:", err);
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
  const sessionReady = ai.live
    .connect({
      model: MODEL,
      config,
      callbacks: {
        onopen: () => {
          console.log("✅ Gemini session opened (Callback)");
        },
        onmessage: async (message) => {
          // console.log("📩 Received message from Gemini:", JSON.stringify(message).substring(0, 200) + "...");

          let audioBase64 = null;
          let sampleRate = 24000;

          // Check for top-level data (Legacy/Convenience)
          if (message.data) {
            // console.log("🔹 Found audio in message.data");
            audioBase64 = message.data;
          }

          const parts = message.serverContent?.modelTurn?.parts || [];
          if (parts.length > 0) {
            // console.log(`🔹 Message has ${parts.length} parts`);
          }

          for (const p of parts) {
            if (p.inlineData && p.inlineData.mimeType.startsWith("audio/")) {
              // console.log("🔹 Found audio in inlineData");
              audioBase64 = p.inlineData.data;
            }
            if (p.text) {
              console.log("🗣️ Gemini Text:", p.text);
            }
          }

          if (audioBase64) {
            console.log(
              `📤 Sending audio chunk to Unity: ${audioBase64.length} bytes`
            );
            ws.send(
              JSON.stringify({
                type: "audio_broadcast",
                audio: audioBase64,
                sampleRate: sampleRate,
              })
            );
          } else {
            // console.log("⚠️ No audio data in this turn");
          }

          await handleFunctionCalls(session, message, (entry) => {
            if (ws.readyState === WebSocket.OPEN) {
              ws.send(JSON.stringify({ type: "function_call_result", entry }));
            }
          });

          if (message?.serverContent?.turnComplete) {
            console.log("🏁 Gemini finished turn.");
          }
        },
        onerror: (e) =>
          console.error("❌ Gemini session error:", e?.message ?? e),
        onclose: () => console.log("🔴 Gemini session closed"),
      },
    })
    .then((s) => {
      session = s;
      console.log("✅ Gemini Session Ready (Promise Resolved)");
      session.sendRealtimeInput({ activityStart: {} });
      setTimeout(() => {
        console.log("👋 Sending Welcome Message Trigger...");
        try {
          session.sendRealtimeInput({
            clientContent: {
              turns: [
                {
                  role: "user",
                  parts: [
                    {
                      text: "The user has connected. Please say 'Connected' to confirm that you are ready.",
                    },
                  ],
                },
              ],
              turnComplete: true,
            },
          });
          console.log("➡️ Welcome Message Sent");
        } catch (err) {
          console.error("❌ Failed to send Welcome Message:", err);
        }
      }, 1000); // Wait 1s to ensure connection stability
    });

  ws.on("message", async (raw) => {
    try {
      const payload = JSON.parse(raw.toString());

      if (payload.type === "turn_complete") {
        console.log("Creating Turn Complete Signal...");
        const liveSession = session || (await sessionReady);
        if (liveSession) {
          console.log("Sending turnComplete to Gemini...");
          liveSession.sendRealtimeInput({
            // clientContent: { turnComplete: true },
            activityEnd: {},
          });

          console.log("✅ Sent turnComplete signal to Gemini");
        } else {
          console.error("❌ Live Session not ready for turnComplete");
        }
        return;
      }

      if (payload.type === "audio_stream" && payload.data) {
        const liveSession = session || (await sessionReady);
        if (!liveSession) {
          // console.warn("⚠️ Received audio_stream but session not ready");
          return;
        }

        // HARDCODE: Force 24kHz to match Unity's fixed output
        const sampleRate = 24000;
        console.log(`🎤 Received Stream Chunk (Force Rate: ${sampleRate})`);

        // FIX: Use mediaChunks with explicit Rate and Base64 Data
        liveSession.sendRealtimeInput({
          mediaChunks: [
            {
              mimeType: `audio/pcm;rate=${sampleRate}`,
              data: payload.data,
            },
          ],
        });
        return;
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

await initializeRAG();

function broadcast(obj) {
  const msg = JSON.stringify(obj);
  for (const client of clients) {
    if (client.readyState === WebSocket.OPEN) {
      client.send(msg);
    }
  }
}
