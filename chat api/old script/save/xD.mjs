import { GoogleGenAI, Modality, Type } from "@google/genai";

import dotenv from "dotenv";

import { WebSocketServer, WebSocket } from "ws";

import { MongoDBAtlasVectorSearch } from "@langchain/mongodb";

import { GoogleGenerativeAIEmbeddings } from "@langchain/google-genai";

import mongoose from "mongoose";

import prompt from "../prompt2.js";

dotenv.config();
// Imports removed (ytdl, express, http, cors)

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

// ✅ ใช้โมเดลเดิมตามที่คุณต้องการ

const model = "gemini-2.5-flash-native-audio-preview-12-2025";

const prompt1 = prompt;

const PORT = process.env.PORT || 3100;

// ✅ NEW: ตัวแปรเก็บประวัติการสนทนาใน RAM (Key = SessionID, Value = { history: [], lastActive: Date })
const sessionStore = new Map();
const SESSION_TIMEOUT = 1000 * 60 * 60 * 24; // 24 Hours

function updateSessionActivity(sessionId) {
  if (sessionStore.has(sessionId)) {
    const sessionData = sessionStore.get(sessionId);
    sessionData.lastActive = Date.now();
    sessionStore.set(sessionId, sessionData);
  }
}

// Check for expired sessions every 1 minute
setInterval(() => {
  const now = Date.now();
  for (const [sessionId, data] of sessionStore.entries()) {
    if (now - data.lastActive > SESSION_TIMEOUT) {
      console.log(`🧹 Cleaning up expired session: ${sessionId}`);
      sessionStore.delete(sessionId);
    }
  }
}, 60 * 1000);

// Setup Express & HTTP Server
// Express & Proxy logic removed

// Bind WebSocket to the HTTP Server
const wss = new WebSocketServer({ port: PORT, path: "/audio" });

const clients = new Set();

const functionCallLog = [];

let vectorStore;

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
    modelName: "models/gemini-embedding-001",
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

// const WebCamControl = {
//   name: "WebCam_Control",

//   description: "Control the webcam device.",

//   parameters: {
//     type: Type.OBJECT,

//     properties: {
//       action: {
//         type: Type.STRING,

//         description:
//           'Action to perform on the webcam (e.g., "cam-start", "cam-stop").',
//       },
//     },

//     required: ["action"],
//   },
// };

// const GameControl = {
//   name: "Game_Control",

//   description: "Control the game device.",

//   parameters: {
//     type: Type.OBJECT,

//     properties: {
//       action: {
//         type: Type.STRING,

//         description:
//           'Action to perform on the game (e.g., "tapgame-start", "tapgame-stop").',
//       },
//     },

//     required: ["action"],
//   },
// };

const SearchKnowledgeTool = {
  name: "search_knowledge",
  description: "Search for information about Exzy co.ltd products.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      query: { type: Type.STRING, description: "The search query." },
      source: {
        type: Type.STRING,

        description:
          "Optional: Filter by specific source file if known (e.g., 'Visitar - Visitor Management System.pdf').",
      },
    },

    required: ["query"],
  },
};

const ListProductsTool = {
  name: "list_products",
  description: "List all available products from the knowledge base.",
  parameters: {
    type: Type.OBJECT,
    properties: {},
  },
};

const ReturnUserTextTool = {
  name: "return_user_text",
  description:
    "Returns the text that the user said. Use this to confirm what you heard.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      text: { type: Type.STRING, description: "The text spoken by the user." },
    },
    required: ["text"],
  },
};

const ProductCardTool = {
  name: "product_card",
  description: "Show card Carousel.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      action: {
        type: Type.STRING,
        description:
          'Action to perform on the product card (e.g., "show", "hide").',
      },
    },
    required: ["action"],
  },
};

const ShowProductTool = {
  name: "show_product",
  description: "Show a specific product card (e.g., Meet in touch, Co Desk).",
  parameters: {
    type: Type.OBJECT,
    properties: {
      product: {
        type: Type.STRING,
        description: "The name of the product to show.",
        enum: [
          "Meet in touch",
          "Co Desk",
          "Visitar",
          "Smart Locker",
          "W+ app",
          "Meeting pod",
          "Access Control",
        ],
      },
    },
    required: ["product"],
  },
};

const config = {
  responseModalities: [Modality.AUDIO],

  systemInstruction: prompt1,

  tools: [
    {
      functionDeclarations: [
        SearchKnowledgeTool,
        ListProductsTool,
        ReturnUserTextTool,
        ProductCardTool,
        ShowProductTool,
      ],
    },
  ],

  // ✅ แก้ไข: เปิดใช้งาน Input Audio Transcription (แปลงเสียง User เป็น Text)
  // inputAudioTranscription: {}, // Disabled by user request

  outputAudioTranscription: {},

  realtimeInputConfig: {
    automaticActivityDetection: { disabled: true }, // ปิด VAD อัตโนมัติ เพื่อทำ PTT
  },

  temperature: 0.7,
};

function safeStringifyArgs(args) {
  try {
    return JSON.stringify(args ?? {});
  } catch (err) {
    return `[unserializable args: ${err?.message ?? err}]`;
  }
}

function logFunctionCalls(message) {
  const calls = [];

  const toolCalls = message?.toolCall?.functionCalls;

  if (Array.isArray(toolCalls)) calls.push(...toolCalls);

  const parts = message?.serverContent?.modelTurn?.parts || [];

  for (const part of parts) {
    if (part?.functionCall) calls.push(part.functionCall);
  }

  if (!calls.length) return;

  for (const call of calls) {
    const argString = safeStringifyArgs(call?.args);

    // console.log(`[function_call] id=${call?.id} name=${call?.name} args=${argString}`);
  }
}

async function handleFunctionCalls(session, message, onFunctionCallEntry) {
  const toolCalls = [];

  if (message?.toolCall?.functionCalls) {
    toolCalls.push(...message.toolCall.functionCalls);
  }

  const parts = message?.serverContent?.modelTurn?.parts || [];

  for (const part of parts) {
    if (part.functionCall) {
      toolCalls.push(part.functionCall);
    }
  }

  if (toolCalls.length === 0) return;

  for (const call of toolCalls) {
    const args = call?.args ?? {};

    let response = args;

    const argString = safeStringifyArgs(args);

    console.log(
      `[function_response] id=${call?.id ?? "n/a"} name=${
        call?.name ?? "n/a"
      } response=${argString}`,
    );

    let result = "No information found.";

    let searchResults = [];

    let isRagTool = false;

    if (call.name === "search_knowledge") {
      isRagTool = true;

      const SEARCH_LIMIT = 15;

      const filter = args.source ? { source: { $eq: args.source } } : undefined;

      searchResults = await vectorStore.similaritySearchWithScore(
        args.query,

        SEARCH_LIMIT,

        filter,
      );
    } else if (call.name === "list_products") {
      try {
        const collection = mongoose.connection.db.collection("Exzy Products");
        const distinctSources = await collection.distinct("source");

        // Filter out unwanted items
        const products = distinctSources.filter(
          (s) => s !== "ช่องทางการติดต่อ Exzy Company Limited",
        );

        if (products.length > 0) {
          result = "Available Products:\n- " + products.join("\n- ");
        } else {
          result = "No products found in database.";
        }
      } catch (err) {
        console.error("List Products Error:", err);
        result = "Failed to retrieve product list.";
      }
      response = { result };
    }

    if (isRagTool) {
      if (searchResults.length > 0) {
        const validResults = searchResults

          .filter(([doc, score]) => score >= 0.5)

          .map(([doc, score]) => {
            const topic =
              doc.pageContent || doc.metadata.topic || "Unknown Topic";

            const content = doc.metadata.content || "";

            const source = doc.metadata.source || "Unknown Source";

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
        console.error(
          "Failed to forward function_call entry:",

          err?.message ?? err,
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

wss.on("connection", (ws, req) => {
  // ✅ 1. ดึง Session ID จาก URL Parameter
  // ตัวอย่าง: ws://localhost:3100/audio?sessionId=player1
  const urlParams = new URLSearchParams(req.url.split("?")[1]);
  const sessionId = urlParams.get("sessionId") || `guest_${Date.now()}`;

  console.log(`🔌 Client Connected: ${sessionId}`);
  clients.add(ws);

  // ✅ 2. เตรียม History ใน RAM
  if (!sessionStore.has(sessionId)) {
    sessionStore.set(sessionId, { history: [], lastActive: Date.now() });
  } else {
    updateSessionActivity(sessionId);
  }

  // ตัวแปรพักข้อความ AI (เพราะ AI ส่งมาทีละคำ เราต้องรอให้จบประโยคค่อยเซฟ)
  let currentModelText = "";

  let session;

  const sessionReady = ai.live
    .connect({
      model,
      config,
      callbacks: {
        onopen: async () => {
          console.log("✅ Gemini session opened (WS)");
        },

        onmessage: async (message) => {
          // const rawLog = JSON.stringify(message).substring(0, 500);

          // console.log(`📩 Gemini Raw Message: ${rawLog}...`);

          let audioBase64 = null;

          let sampleRate = 24000;

          if (message.data) audioBase64 = message.data;

          const parts = message.serverContent?.modelTurn?.parts || [];

          for (const p of parts) {
            if (p.inlineData && p.inlineData.mimeType.startsWith("audio/")) {
              audioBase64 = p.inlineData.data;
            }

            if (p.text) {
              // console.log("🗣️ Gemini Text:", p.text);
            }
          }

          // ✅ Helper Function: Clean AI Response
          function cleanAIResponse(text) {
            if (!text) return text;
            // Remove "Ctrl46", "Ctrl 46" and other variations, case-insensitive
            return text.replace(/Ctrl\s*46/gi, "").trim();
          }

          const content = message.serverContent;

          // ✅ Capture Output Audio Transcription (เสียง AI -> Text)

          if (
            content &&
            content.outputTranscription &&
            content.outputTranscription.text
          ) {
            let text = content.outputTranscription.text;

            // 🔥 APPLY FILTER HERE
            text = cleanAIResponse(text);

            console.log("📝 Output Transcription:", text);

            if (ws.readyState === WebSocket.OPEN) {
              ws.send(
                JSON.stringify({ type: "audio_transcription", text: text }),
              );
            }
          }

          // ✅ NEW: Capture Input Audio Transcription (เสียง User -> Text)
          /* 
          if (
            content &&
            content.inputTranscription &&
            content.inputTranscription.text
          ) {
             // ... Code removed/commented out to prevent double triggers ...
             // We now rely on 'return_user_text' tool.
          }
          */

          if (audioBase64) {
            ws.send(
              JSON.stringify({
                type: "audio_broadcast",
                audio: audioBase64,
                sampleRate,
              }),
            );
          }

          // ✅ Accumulate Model Text
          if (content?.outputTranscription?.text) {
            // 🔥 APPLY FILTER HERE TOO
            currentModelText += cleanAIResponse(
              content.outputTranscription.text,
            );
          }

          // Handle Function Calls

          await handleFunctionCalls(session, message, (entry) => {
            if (ws.readyState === WebSocket.OPEN) {
              console.log(
                `[Server] Sending direct: type=function_call_result, action=${
                  entry.response?.action || entry.args?.action
                }`,
              );

              // ✅ Handle return_user_text specific logic (Echo & Save)
              if (entry.name === "return_user_text" && entry.args?.text) {
                const userText = entry.args.text;
                console.log(`🎤 [return_user_text] Echoing: "${userText}"`); // <--- NEW LOG
                ws.send(
                  JSON.stringify({
                    type: "input_audio_transcription", // Reuse existing client type
                    text: userText,
                  }),
                );

                // Save to RAM
                const sessionData = sessionStore.get(sessionId);
                if (sessionData) {
                  sessionData.history.push({
                    role: "user",
                    parts: [{ text: userText }],
                  });
                  updateSessionActivity(sessionId);
                }
              }

              ws.send(JSON.stringify({ type: "function_call_result", entry }));
            }
          });

          logFunctionCalls(message);

          if (message?.serverContent?.turnComplete) {
            console.log("🏁 Gemini finished turn.");
            // ✅ Save Model Turn to RAM
            if (currentModelText.trim()) {
              const sessionData = sessionStore.get(sessionId);
              if (sessionData) {
                sessionData.history.push({
                  role: "model",
                  parts: [{ text: currentModelText }],
                });
                updateSessionActivity(sessionId);
              }
              currentModelText = ""; // Reset Buffer
            }
          }
        },

        onerror: (e) => {
          console.error("❌ Gemini session error:", JSON.stringify(e, null, 2));
          // Force client to reconnect
          if (ws.readyState === WebSocket.OPEN) ws.close();
        },

        onclose: (event) => {
          console.log(
            `⚠️ Gemini session closed (WS). Code: ${event?.code}, Reason: ${event?.reason}`,
          );
          // Force client to reconnect
          if (ws.readyState === WebSocket.OPEN) ws.close();
        },
      },
    })

    .then((s) => {
      session = s;

      console.log("✅ Gemini Session Ready");

      // ✅ 3. Resumption Logic: ส่ง History จาก RAM กลับไปให้ AI (Move here to ensure session exists)
      const sessionData = sessionStore.get(sessionId);
      const rawHistory = sessionData ? sessionData.history : [];

      if (rawHistory.length > 0) {
        // 🛠️ Sanitize History: Merge consecutive turns with the same role
        const sanitizedHistory = [];
        let lastTurn = null;

        for (const turn of rawHistory) {
          if (lastTurn && lastTurn.role === turn.role) {
            // Merge parts into the previous turn
            lastTurn.parts.push(...turn.parts);
          } else {
            // New turn (Deep copy to avoid reference issues)
            lastTurn = JSON.parse(JSON.stringify(turn));
            sanitizedHistory.push(lastTurn);
          }
        }

        console.log(
          `🔄 Resuming session for ${sessionId} (Original: ${rawHistory.length}, Sanitized: ${sanitizedHistory.length} turns)`,
        );
        console.log(
          "DEBUG Sanitized History:",
          JSON.stringify(sanitizedHistory, null, 2),
        );

        const lastTurnWasUser =
          sanitizedHistory.length > 0 &&
          sanitizedHistory[sanitizedHistory.length - 1].role === "user";

        console.log(
          `Resuming session. Last turn was ${
            lastTurnWasUser ? "user" : "model"
          }. turnComplete=${lastTurnWasUser}`,
        );

        s.sendClientContent({
          turns: sanitizedHistory,
          turnComplete: lastTurnWasUser,
        });
      }
    })

    .catch((err) => {
      console.error("Failed to create Gemini session:", err);

      ws.close();
    });

  ws.on("message", async (raw) => {
    try {
      const payload = JSON.parse(raw.toString());

      // ✅ 1. Activity Start (User กดปุ่ม)

      if (payload.type === "activity_start") {
        console.log("▶️ User started speaking (PTT Pressed)");

        // ถ้า User พูดแทรก ให้เซฟสิ่งที่ AI พูดค้างไว้ลง RAM ก่อนตัดบท
        if (currentModelText.trim()) {
          const sessionData = sessionStore.get(sessionId);
          if (sessionData) {
            sessionData.history.push({
              role: "model",
              parts: [{ text: currentModelText }],
            });
          }
          currentModelText = "";
        }

        updateSessionActivity(sessionId);

        const liveSession = session || (await sessionReady);

        if (liveSession) {
          liveSession.sendRealtimeInput({ activityStart: {} });
        }
      }

      // ✅ 2. Audio Stream (Streaming เสียง)
      else if (payload.type === "audio_stream" && payload.data) {
        const liveSession = session || (await sessionReady);

        if (liveSession) {
          const rate = payload.sampleRate || 24000;

          liveSession.sendRealtimeInput({
            audio: {
              data: payload.data,

              mimeType: `audio/pcm;rate=${rate}`,
            },
          });
        }
      }

      // ✅ 3. Activity End (User ปล่อยปุ่ม)
      else if (
        payload.type === "activity_end" ||
        payload.type === "turn_complete"
      ) {
        const liveSession = session || (await sessionReady);

        if (liveSession) {
          console.log("⏹️ User stopped speaking. Sending activityEnd...");

          liveSession.sendRealtimeInput({ activityEnd: {} });
        }
      }

      // ✅ 4. Text Input (FAQ System)
      else if (payload.type === "text_input" && payload.text) {
        console.log(`📩 Received Text Input: "${payload.text}"`);

        const sessionData = sessionStore.get(sessionId);
        let currentHistory = [];

        if (sessionData) {
          // Add to RAM
          sessionData.history.push({
            role: "user",
            parts: [{ text: payload.text }],
          });
          updateSessionActivity(sessionId);
          currentHistory = sessionData.history;
        } else {
          currentHistory = [{ role: "user", parts: [{ text: payload.text }] }];
          sessionStore.set(sessionId, {
            history: currentHistory,
            lastActive: Date.now(),
          });
        }

        const liveSession = session || (await sessionReady);
        if (liveSession) {
          // Send updated history to define context and trigger response
          liveSession.sendClientContent({
            turns: [{ role: "user", parts: [{ text: payload.text }] }],
            turnComplete: true,
          });
        }
      }

      // Legacy play_video using ytdl removed
    } catch (err) {
      console.error("Message handling failed:", err);
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

function broadcast(obj) {
  if (obj.type !== "audio_broadcast") {
    // console.log(`[Server] Broadcasting: type=${obj.type}`);
  }

  const msg = JSON.stringify(obj);

  for (const client of clients) {
    if (client.readyState === WebSocket.OPEN) {
      client.send(msg);
    }
  }
}

initializeRAG();
