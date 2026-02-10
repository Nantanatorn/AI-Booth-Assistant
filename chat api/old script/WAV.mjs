// Text prompt -> Gemini audio reply; saves each turn as WAV
import { GoogleGenAI, Modality, Type} from "@google/genai";
import dotenv from "dotenv";
import readline from "node:readline";
import Speaker from "speaker";
import { WebSocket } from "ws";

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
// Keep the audio-native model as requested
const model = "gemini-2.5-flash-native-audio-preview-09-2025";


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
  description: 'List Asia Countries.',
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

const config = {
  responseModalities: [Modality.AUDIO],
  systemInstruction: "You are a helpful assistant and answer in a friendly tone response in English If a request can use a function call, then use one. There are two function calls: FoodFunctionDeclaration (use when someone wants to find Thai food) and ListAsiaFunctionDeclaration (use when someone wants to find countries in Asia). After getting the result from the function call, read it out loud for questions that use a function call..",
  tools: [{
      functionDeclarations: [FoodFunctionDeclaration, ListAsiaFunctionDeclaration, WebCamControl]
    }],
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
    // For now, echo back the args so the model can ground its answer.
    const response = call?.args ?? {};
    const argString = safeStringifyArgs(response);
    console.log(`[function_response] id=${call?.id ?? "n/a"} name=${call?.name ?? "n/a"} response=${argString}`);
    await session.sendToolResponse({
      functionResponses: [{
        id: call?.id,
        name: call?.name,
        response,
      }],
    });
  }
}

async function live() {
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
      if (message.serverContent && message.serverContent.turnComplete) {
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
        await handleFunctionCalls(session, message);
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

    const turns = await handleTurn();

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
