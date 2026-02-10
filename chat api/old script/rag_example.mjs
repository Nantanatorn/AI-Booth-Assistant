import {
  ChatGoogleGenerativeAI,
  GoogleGenerativeAIEmbeddings,
} from "@langchain/google-genai";
import dotenv from "dotenv";
import { RecursiveCharacterTextSplitter } from "@langchain/textsplitters";
import { Document } from "@langchain/core/documents";
import { ChatPromptTemplate } from "@langchain/core/prompts";
import { StringOutputParser } from "@langchain/core/output_parsers";
import {
  RunnableSequence,
  RunnablePassthrough,
} from "@langchain/core/runnables";

// --- ✅ จุดที่แก้: ใช้ MemoryVectorStore แทน HNSWLib ---
// ตัวนี้ไม่ต้องใช้ community ครับ มากับ core/langchain เลย
// import { MemoryVectorStore } from "@langchain/vectorstores/memory";
import { MemoryVectorStore } from "@langchain/classic/vectorstores/memory";
// ----------------------------------------------------

dotenv.config();
const GOOGLE_API_KEY = process.env.GEMINI_API_KEY;
const MODEL = "gemini-2.5-flash";
async function runRAG() {
  console.log("--- เริ่มต้นระบบ RAG (No Community / In-Memory) ---");

  // 1. เตรียมข้อมูล
  const gameData = [
    "บอสตัวแรกชื่อ Fire Golem อาศัยอยู่ในภูเขาไฟทางทิศเหนือ",
    "จุดอ่อนของ Fire Golem คือธาตุน้ำแข็งและการโจมตีที่ดวงตา",
    "ดาบ Excalibur ซ่อนอยู่ใต้ทะเลสาบ ต้องดำน้ำลงไป 50 เมตร",
  ];
  const docs = gameData.map((text) => new Document({ pageContent: text }));
  const splitter = new RecursiveCharacterTextSplitter({
    chunkSize: 1000,
    chunkOverlap: 200,
  });
  const splitDocs = await splitter.splitDocuments(docs);

  // 2. สร้าง Vector Store (เปลี่ยนเป็น Memory)
  console.log("...กำลัง Embed ข้อมูลลง RAM...");
  const vectorStore = await MemoryVectorStore.fromDocuments(
    splitDocs,
    new GoogleGenerativeAIEmbeddings({
      apiKey: GOOGLE_API_KEY,
      modelName: "text-embedding-004",
    })
  );

  const retriever = vectorStore.asRetriever({ k: 2 });

  // 3. เตรียมโมเดล
  const llm = new ChatGoogleGenerativeAI({
    model: MODEL,
    apiKey: GOOGLE_API_KEY,
    temperature: 0.3,
  });

  const prompt = ChatPromptTemplate.fromTemplate(`
    ตอบคำถามจากข้อมูลนี้: {context}
    คำถาม: {question}
  `);

  const formatDocumentsAsString = (documents) =>
    documents.map((doc) => doc.pageContent).join("\n\n");

  const ragChain = RunnableSequence.from([
    {
      context: retriever.pipe(formatDocumentsAsString),
      question: new RunnablePassthrough(),
    },
    prompt,
    llm,
    new StringOutputParser(),
  ]);

  // 4. ทดสอบ
  const ans = await ragChain.invoke("จะจัดการFire golemได้ยังไง?");
  console.log(`\nA: ${ans}`);
}

runRAG().catch(console.error);
