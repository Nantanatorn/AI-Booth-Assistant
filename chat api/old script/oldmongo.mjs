// Text prompt -> Gemini audio reply; saves each turn as WAV
import { GoogleGenAI, Modality, Type } from "@google/genai";
import dotenv from "dotenv";
import readline from "node:readline";
import Speaker from "speaker";
import { WebSocketServer, WebSocket } from "ws";
import { MongoDBAtlasVectorSearch } from "@langchain/mongodb";
import { GoogleGenerativeAIEmbeddings } from "@langchain/google-genai";
import mongoose from "mongoose";

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
const prompt =
  "You are a helpful assistant answering in a friendly tone. When asked about product warranty, price, or service hours, ALWAYS use the relevant tools. After retrieving the information, use it to answer the user's question CONCISELY. Do NOT read the raw search result verbatim. Only provide the specific information requested (e.g., if asked for price, only state the price). If the tool provides a list because the specific item was not found, search through that list yourself to answer. If you still cannot find the answer, say 'I do not know'.";
const PORT = process.env.PORT || 3100;

const wss = new WebSocketServer({ port: PORT, path: "/audio" });
const clients = new Set();
const functionCallLog = []; // store function-calling events for broadcasting

let vectorStore; // Unified vector store for Atlas

// Global variables for fallback data
let globalWarrantyData = [];
let globalPriceData = [];
let globalServiceData = [
  "ศูนย์บริการ: เปิดทำการ จันทร์-ศุกร์ 09:00 - 18:00 น.",
];

// --- Mongoose Schema (Must match server.js) ---
const ProductSchema = new mongoose.Schema({
  productName: { type: String, required: true },
  warranty: { type: String, required: true },
  price: { type: String, required: true },
  embedding: { type: [Number], required: true },
  createdAt: { type: Date, default: Date.now },
});
const KnowledgeSchema = new mongoose.Schema({
  source: { type: String, required: true },
  topic: { type: String, required: true },
  content: { type: String, required: true },
  embedding: { type: [Number], required: true },
  createdAt: { type: Date, default: Date.now },
});

const Product = mongoose.model("Product", ProductSchema, "product");
const Knowledge = mongoose.model(
  "Knowledge",
  KnowledgeSchema,
  "smart_interactive_whiteboard"
);

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

  // Fetch products ONLY for fallback data (not for vector store loading)
  const products = await Product.find({});
  console.log(`Fetched ${products.length} products for fallback data.`);

  // Prepare fallback data
  globalWarrantyData = products.map(
    (p) => `Product: ${p.productName}, Warranty: ${p.warranty}`
  );
  globalPriceData = products.map(
    (p) => `Product: ${p.productName}, Price: ${p.price}`
  );

  const embeddings = new GoogleGenerativeAIEmbeddings({
    apiKey: GEMINI_API_KEY,
    modelName: "text-embedding-004",
  });

  // Connect to the specific collection
  const collection = mongoose.connection.db.collection("product");

  // Initialize Atlas Vector Search
  vectorStore = new MongoDBAtlasVectorSearch(embeddings, {
    collection: collection,
    indexName: "product_index", // Ensure you created this index in Atlas!
    textKey: "productName", // We might need to adjust this if we want to search across multiple fields, but Atlas Search uses the embedding.
    embeddingKey: "embedding",
  });

  console.log("MongoDB Atlas Vector Search Ready.");
}

// --- Tool Definitions ---

const SearchWarrantyTool = {
  name: "search_warranty",
  description: "Search for warranty information.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      query: {
        type: Type.STRING,
        description: "The search query about warranty",
      },
    },
    required: ["query"],
  },
};

const SearchPriceTool = {
  name: "search_price",
  description: "Search for price information.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      query: {
        type: Type.STRING,
        description: "The search query about price.",
      },
    },
    required: ["query"],
  },
};
const SearchServiceTool = {
  name: "search_service",
  description: "Search for service information.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      query: {
        type: Type.STRING,
        description: "The search query about service.",
      },
    },
    required: ["query"],
  },
};

const FoodFunctionDeclaration = {
  name: "get_thai_food",
  description: "Gets thai food.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      foodname: {
        type: Type.STRING,
        description: "thaifood name",
      },
    },
    required: ["foodname"],
  },
};

const ListAsiaFunctionDeclaration = {
  name: "List_Asia_Countries",
  description: "List Asia Countries.",
  parameters: {
    type: Type.OBJECT,
    properties: {
      // ชื่อตัวแปรที่จะรับค่าเป็น Array
      AsiaCountries: {
        type: Type.ARRAY, // <--- 1. กำหนด Type เป็น ARRAY
        description: "Countries in  Asia",
        items: {
          // <--- 2. ต้องระบุว่าข้างใน Array เป็นอะไร
          type: Type.STRING, // บอกว่าเป็น List ของ "ข้อความ"
        },
      },
    },
    required: ["AsiaCountries"],
  },
};

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

const config = {
  responseModalities: [Modality.AUDIO],
  systemInstruction: prompt,
  tools: [
    {
      functionDeclarations: [
        FoodFunctionDeclaration,
        ListAsiaFunctionDeclaration,
        WebCamControl,
        SearchWarrantyTool,
        SearchPriceTool,
        SearchServiceTool,
      ],
    },
  ],
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

    if (call.name === "search_warranty") {
      isRagTool = true;
      console.log(`🔍 Searching Warranty for: ${args.query}`);
      // Use the unified vectorStore for all searches
      searchResults = await vectorStore.similaritySearchWithScore(
        args.query,
        3
      );
    } else if (call.name === "search_price") {
      isRagTool = true;
      console.log(`🔍 Searching Price for: ${args.query}`);
      searchResults = await vectorStore.similaritySearchWithScore(
        args.query,
        3
      );
    } else if (call.name === "search_service") {
      isRagTool = true;
      console.log(`🔍 Searching Service for: ${args.query}`);
      // Service info might not be in DB yet, but we search anyway.
      // If not found, fallback will handle it.
      searchResults = await vectorStore.similaritySearchWithScore(
        args.query,
        1
      );
    }

    if (isRagTool) {
      if (searchResults.length > 0) {
        console.log(`📊 Found ${searchResults.length} results`);
        if (searchResults.length > 0) {
          console.log(
            "DEBUG: First result doc:",
            JSON.stringify(searchResults[0][0], null, 2)
          );
        }
        const validResults = searchResults
          .filter(([doc, score]) => score >= 0.5)
          .filter(([doc, score]) => score >= 0.5)
          .map(([doc, score]) => {
            // Construct info based on what tool was called to save tokens/bandwidth
            const productName = doc.pageContent; // Always needed for context

            if (call.name === "search_price") {
              const price = doc.metadata.price || "N/A";
              return `Product: ${productName}, Price: ${price}`;
            } else if (call.name === "search_warranty") {
              const warranty = doc.metadata.warranty || "N/A";
              return `Product: ${productName}, Warranty: ${warranty}`;
            } else {
              // Default or Service: return everything available
              const price = doc.metadata.price || "N/A";
              const warranty = doc.metadata.warranty || "N/A";
              return `Product: ${productName}, Price: ${price}, Warranty: ${warranty}`;
            }
          });

        if (validResults.length > 0) {
          result = validResults.join("\n\n");
          console.log(`📄 Found: ${result}`);
        } else {
          console.log("❌ Score too low, returning ALL data for fallback.");
          if (call.name === "search_warranty") {
            result =
              "Specific info not found. Here is all warranty data:\n" +
              globalWarrantyData.join("\n");
          } else if (call.name === "search_price") {
            result =
              "Specific info not found. Here is all price data:\n" +
              globalPriceData.join("\n");
          } else if (call.name === "search_service") {
            result =
              "Specific info not found. Here is all service data:\n" +
              globalServiceData.join("\n");
          }
        }
      } else {
        // No results at all (rare with vector search unless empty), fallback anyway
        console.log("❌ No results found, returning ALL data for fallback.");
        if (call.name === "search_warranty") {
          result =
            "Specific info not found. Here is all warranty data:\n" +
            globalWarrantyData.join("\n");
        } else if (call.name === "search_price") {
          result =
            "Specific info not found. Here is all price data:\n" +
            globalPriceData.join("\n");
        } else if (call.name === "search_service") {
          result =
            "Specific info not found. Here is all service data:\n" +
            globalServiceData.join("\n");
        }
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
    console.log(
      `[function_call_log] id=${entry.id ?? "n/a"} name=${
        entry.name ?? "n/a"
      } args=${safeStringifyArgs(entry.args)}`
    );
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
              ws.send(JSON.stringify({ type: "function_call_result", entry }));
            }
          });
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
      ws.send(JSON.stringify(response));
      if (result.audio) {
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
