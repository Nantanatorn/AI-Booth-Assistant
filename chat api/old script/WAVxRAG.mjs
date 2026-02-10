// Text prompt -> Gemini audio reply; saves each turn as WAV
import { GoogleGenAI, Modality, Type } from "@google/genai";
import dotenv from "dotenv";
import readline from "node:readline";
import Speaker from "speaker";
import { WebSocket } from "ws";
import { MemoryVectorStore } from "@langchain/classic/vectorstores/memory";
import { GoogleGenerativeAIEmbeddings } from "@langchain/google-genai";
import { Document } from "@langchain/core/documents";
import { MongoClient } from "mongodb";

dotenv.config();

// Ensure @google/genai has a WebSocket implementation in Node
if (!globalThis.WebSocket) {
  globalThis.WebSocket = WebSocket;
}

const GEMINI_API_KEY = process.env.GEMINI_API_KEY;
if (!GEMINI_API_KEY) {
  console.error("Missing GEMINI_API_KEY in environment");
  process.exit(1);
}

const ai = new GoogleGenAI({ apiKey: GEMINI_API_KEY });
// Use the specific model requested by the user
const model = "gemini-2.5-flash-native-audio-preview-09-2025";

// --- RAG Setup ---
// --- RAG Setup ---
let warrantyVectorStore;
let priceVectorStore;
let serviceVectorStore;

async function initializeRAG() {
  console.log("Initializing RAG Vector Stores...");
  const warrantyData = [
    "ประกันสินค้า A: รับประกัน 2 ปี เงื่อนไข: ต้องเก็บใบเสร็จไว้",
    "ประกันสินค้า B: รับประกัน 1 ปี เงื่อนไข: ไม่คุ้มครองอุบัติเหตุ",
    "ประกันสินค้า C: รับประกัน 3 ปี เงื่อนไข: บริการซ่อมถึงบ้านฟรี",
  ];
  const priceData = [
    "ราคาสินค้า A: 50,000 บาท",
    "ราคาสินค้า B: 30,000 บาท",
    "ราคาสินค้า C: 80,000 บาท",
  ];
  const serviceData = ["ศูนย์บริการ: เปิดทำการ จันทร์-ศุกร์ 09:00 - 18:00 น."];

  const docs = warrantyData.map((text) => new Document({ pageContent: text }));
  const priceDocs = priceData.map(
    (text) => new Document({ pageContent: text })
  );
  const serviceDocs = serviceData.map(
    (text) => new Document({ pageContent: text })
  );

  const embeddings = new GoogleGenerativeAIEmbeddings({
    apiKey: GEMINI_API_KEY,
    modelName: "text-embedding-004",
  });

  // Initialize separate stores
  warrantyVectorStore = await MemoryVectorStore.fromDocuments(docs, embeddings);
  priceVectorStore = await MemoryVectorStore.fromDocuments(
    priceDocs,
    embeddings
  );
  serviceVectorStore = await MemoryVectorStore.fromDocuments(
    serviceDocs,
    embeddings
  );

  console.log("RAG Vector Stores Ready.");
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

const config = {
  responseModalities: [Modality.AUDIO],
  systemInstruction:
    "You are a trustworthy assistant. Answer politely. If you need to access warranty data, call 'search_warranty' and use the retrieved information to answer. If you want to access price data, call 'search_price'. If you want to access service data, call 'search_service'. ALWAYS use the relevant tool whenever a related question is asked. IMPORTANT: When you get the result from a tool, YOU MUST READ the retrieved information out loud to the user clearly. ถ้าไม่มีข้อมูลในฐานข้อมูลให้บอกว่าไม่มีข้อมูล",
  tools: [
    {
      functionDeclarations: [
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

// Send function responses so the model can use tool outputs to reply
async function handleFunctionCalls(session, message) {
  const toolCalls = message?.toolCall?.functionCalls;
  if (!Array.isArray(toolCalls) || toolCalls.length === 0) {
    return;
  }

  for (const call of toolCalls) {
    const args = call?.args ?? {};
    const argString = safeStringifyArgs(args);
    console.log(
      `[function_response] id=${call?.id ?? "n/a"} name=${
        call?.name ?? "n/a"
      } response=${argString}`
    );

    let result = "No information found.";
    let searchResults = [];

    if (call.name === "search_warranty") {
      console.log(`🔍 Searching Warranty for: ${args.query}`);
      searchResults = await warrantyVectorStore.similaritySearchWithScore(
        args.query,
        1
      );
    } else if (call.name === "search_price") {
      console.log(`🔍 Searching Price for: ${args.query}`);
      searchResults = await priceVectorStore.similaritySearchWithScore(
        args.query,
        1
      );
    } else if (call.name === "search_service") {
      console.log(`🔍 Searching Service for: ${args.query}`);
      searchResults = await serviceVectorStore.similaritySearchWithScore(
        args.query,
        1
      );
    }

    if (searchResults.length > 0) {
      const [doc, score] = searchResults[0];
      console.log(`📊 Similarity Score: ${score}`);
      // Threshold: 0.7 (Increased to avoid false positives like J -> B)
      if (score >= 0.7) {
        result = doc.pageContent;
        console.log(`📄 Found: ${result}`);
      } else {
        console.log("❌ Score too low, returning 'No information found.'");
      }
    }

    await session.sendToolResponse({
      functionResponses: [
        {
          id: call?.id,
          name: call?.name,
          response: { result },
        },
      ],
    });
  }
}

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

      // Debug log
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

    while (toolCallMsg) {
      console.log(
        "🤖 Model called a tool, executing and waiting for next turn..."
      );
      await handleFunctionCalls(session, toolCallMsg);

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

    // Check for text content (debug)
    const textParts = turns
      .map((t) =>
        t?.serverContent?.modelTurn?.parts?.map((p) => p.text).join("")
      )
      .filter((t) => t);
    if (textParts.length > 0) {
      console.log("📝 Text response:", textParts.join(" "));
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
    playAudio(Buffer.from(audioBuffer.buffer), 24000);
  }

  rl.close();
  session.close();
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
