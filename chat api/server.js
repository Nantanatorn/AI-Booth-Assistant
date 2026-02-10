require("dotenv").config();
const express = require("express");
const mongoose = require("mongoose");
const cors = require("cors");
const { GoogleGenerativeAIEmbeddings } = require("@langchain/google-genai");

const app = express();
const PORT = process.env.PORT1 || 3000;
const MONGO_URL = process.env.MONGO_URL;

// --- Middleware ---
app.use(express.json());
app.use(cors());

// Request Logger
app.use((req, res, next) => {
  console.log(`[${new Date().toISOString()}] ${req.method} ${req.url}`);
  next();
});

// --- MongoDB Connection ---
if (!MONGO_URL) {
  console.error("❌ Missing MONGO_URL in .env");
  process.exit(1);
}

mongoose
  .connect(MONGO_URL)
  .then(() => console.log("✅ MongoDB Connected"))
  .catch((err) => console.error("❌ MongoDB Connection Error:", err));

// --- Embeddings Setup ---
const embeddings = new GoogleGenerativeAIEmbeddings({
  apiKey: process.env.GEMINI_API_KEY,
  modelName: "models/gemini-embedding-001",
  taskType: "RETRIEVAL_DOCUMENT",
});

// --- Mongoose Schema ---
// --- Mongoose Schema ---
const ProductSchema = new mongoose.Schema({
  productName: { type: String, required: true },
  warranty: { type: String, default: "N/A" },
  price: { type: String, default: "N/A" },
  description: { type: String },
  embedding: { type: [Number], required: true },
  createdAt: { type: Date, default: Date.now },
});

const Product = mongoose.model("Product", ProductSchema, "product");

const KnowledgeSchema = new mongoose.Schema({
  source: { type: String, required: true },
  topic: { type: String, required: true },
  content: { type: String, required: true },
  embedding: { type: [Number], required: true },
  createdAt: { type: Date, default: Date.now },
});

const Knowledge = mongoose.model("Knowledge", KnowledgeSchema, "Exzy Products");

// --- Routes ---
app.get("/", (req, res) => {
  res.send("API is running...");
});

// Add new Product data with Embedding
app.post("/api/products", async (req, res) => {
  try {
    const { productName, warranty, price, description } = req.body;
    if (!productName) {
      return res.status(400).json({ error: "productName is required" });
    }

    // Create text for embedding (include description if available)
    const textToEmbed = `Topic/Product: ${productName}, Description: ${
      description || ""
    }, Price: ${price || "N/A"}, Warranty: ${warranty || "N/A"}`;

    // Generate embedding
    const vector = await embeddings.embedQuery(textToEmbed);

    const newProduct = new Product({
      productName,
      warranty: warranty || "N/A",
      price: price || "N/A",
      description,
      embedding: vector,
    });
    await newProduct.save();

    res
      .status(201)
      .json({ message: "Data added successfully", data: newProduct });
  } catch (err) {
    console.error("Error adding data:", err);
    res.status(500).json({ error: "Failed to add data", details: err.message });
  }
});

// Get all Products
app.get("/api/getproducts", async (req, res) => {
  try {
    const products = await Product.find();
    res.json(products);
  } catch (err) {
    res
      .status(500)
      .json({ error: "Failed to fetch products", details: err.message });
  }
});

// Default products for seeding
const defaultProducts = [
  {
    productName: "สินค้า A",
    warranty: "รับประกัน 2 ปี เงื่อนไข: ต้องเก็บใบเสร็จไว้",
    price: "50,000 บาท",
    description: "สินค้าคุณภาพสูง เหมาะสำหรับ...",
  },
  {
    productName: "สินค้า B",
    warranty: "รับประกัน 1 ปี เงื่อนไข: ไม่คุ้มครองอุบัติเหตุ",
    price: "30,000 บาท",
  },
  {
    productName: "สินค้า C",
    warranty: "รับประกัน 3 ปี เงื่อนไข: บริการซ่อมถึงบ้านฟรี",
    price: "80,000 บาท",
  },
];

// Seed default products
app.post("/api/seed", async (req, res) => {
  try {
    console.log("🌱 Seeding default products...");
    const results = [];

    for (const p of defaultProducts) {
      // Check if exists to avoid duplicates (optional, based on name)
      const exists = await Product.findOne({ productName: p.productName });
      if (exists) {
        console.log(`Skipping ${p.productName} (already exists)`);
        continue;
      }

      const textToEmbed = `Topic/Product: ${p.productName}, Description: ${
        p.description || ""
      }, Price: ${p.price || "N/A"}, Warranty: ${p.warranty || "N/A"}`;
      const vector = await embeddings.embedQuery(textToEmbed);

      const newProduct = new Product({
        productName: p.productName,
        warranty: p.warranty || "N/A",
        price: p.price || "N/A",
        description: p.description,
        embedding: vector,
      });
      await newProduct.save();
      results.push(newProduct);
      console.log(`✅ Added ${p.productName}`);
    }

    res.json({ message: "Seeding completed", added: results });
  } catch (err) {
    console.error("Seeding error:", err);
    res.status(500).json({ error: "Seeding failed", details: err.message });
  }
});

// --- PDF Seeding ---
const fs = require("fs");
const pdf = require("pdf-parse");
const path = require("path");
const { GoogleGenAI } = require("@google/genai");

const genAI = new GoogleGenAI({ apiKey: process.env.GEMINI_API_KEY });

app.post("/api/seed-pdf", async (req, res) => {
  try {
    const { filePath } = req.body;
    if (!filePath) {
      return res.status(400).json({ error: "filePath is required" });
    }

    const fileName = path.basename(filePath);
    console.log(`📄 Reading PDF from: ${filePath} (Source: ${fileName})`);
    const dataBuffer = fs.readFileSync(filePath);
    const data = await pdf(dataBuffer);
    const pdfText = data.text;

    console.log("🤖 Extracting knowledge with Gemini...");
    // For @google/genai SDK (new version)
    // It does not use getGenerativeModel. It uses client.models.generateContent directly.

    // Updated Prompt for Knowledge/Article Extraction
    const prompt = `
      You are an expert content parser.
      The provided text is an educational article or informational content.
      Your task is to extract **ALL** information from the text into structured "Topics" and "Content".

      Rules:
      1. **NO SUMMARIZATION**: You must preserve all details, examples, statistics, and explanations. Do not shorten the content.
      2. **Identify Topics**: Group the content by its natural headings or main concepts (e.g., "What is Interactive Whiteboard?", "5 Features of Hikvision").
      3. **Comprehensive Content**: For each topic, include the *entire* relevant text from the source. If there are bullet points, lists, or technical specs, include them all in the "content" field.
      4. **FIX THAI TEXT**: The input text comes from a PDF and may have missing vowels or tones (e.g., "กระดานจฉยะ" instead of "กระดานอัจฉริยะ"). **YOU MUST CORRECT THESE SPELLING ERRORS** based on context to make it readable Thai.
      5. **Output Format**: Return ONLY a JSON array of objects with these keys:
         - "topic": The Topic Title.
         - "content": The full, detailed content for that topic.
      
      Text to analyze:
      ${pdfText}
    `;

    const result = await genAI.models.generateContent({
      model: "gemini-2.5-flash-preview-09-2025",
      contents: [{ parts: [{ text: prompt }] }],
    });

    // The response structure in the new SDK might be different.
    // Usually result.response.text() works if it mimics the old one, but let's check.
    // In @google/genai, result is the response object directly or has .text()?
    // Let's assume result.text() or result.candidates[0]...
    // Actually, checking docs/patterns: result.text() is common helper.
    // If not, we might need result.candidates[0].content.parts[0].text.
    // Let's try the standard helper first.
    // Robust extraction for @google/genai SDK
    let jsonString = "";

    // Debug: Log the keys to see what we got
    // console.log("Gemini Result Keys:", Object.keys(result));

    if (result.response && typeof result.response.text === "function") {
      // Standard pattern for some versions
      jsonString = result.response.text();
    } else if (result.candidates && result.candidates.length > 0) {
      // Direct access to candidates
      const candidate = result.candidates[0];
      if (
        candidate.content &&
        candidate.content.parts &&
        candidate.content.parts.length > 0
      ) {
        jsonString = candidate.content.parts[0].text;
      }
    } else if (typeof result.text === "function") {
      // Fallback if it exists
      jsonString = result.text();
    }

    if (!jsonString) {
      console.log("Debug Result Structure:", JSON.stringify(result, null, 2));
      throw new Error(
        "Could not extract text from Gemini response. Check logs for structure.",
      );
    }

    // Sanitize JSON string: escape control characters that might break JSON.parse
    // This regex replaces unescaped control characters (newlines, tabs, etc.) within strings
    // Note: This is a basic sanitizer. For complex cases, a proper parser is better,
    // but Gemini usually returns valid JSON if prompted correctly.
    // The issue is often unescaped newlines in the content field.

    // First, try to find the JSON array brackets to isolate the JSON
    const firstBracket = jsonString.indexOf("[");
    const lastBracket = jsonString.lastIndexOf("]");

    if (firstBracket !== -1 && lastBracket !== -1) {
      jsonString = jsonString.substring(firstBracket, lastBracket + 1);
    }

    // Attempt to clean up common bad control characters if simple parse fails
    let items;
    try {
      jsonString = jsonString.replace(/```json|```/g, "").trim();
      items = JSON.parse(jsonString);
    } catch (e) {
      console.log("⚠️ JSON Parse failed, attempting to sanitize...");
      // Replace newlines that are NOT part of the JSON structure
      // This is tricky. A safer bet is to ask Gemini to be strict or use a library.
      // For now, let's try to escape unescaped newlines.
      // Or better, just log it and fail so we can see what's wrong.
      console.log("Raw JSON String:", jsonString);

      // Try a simple fix: remove control characters that are not \n, \r, \t
      // jsonString = jsonString.replace(/[\x00-\x1F\x7F-\x9F]/g, "");

      throw new Error(`JSON Parse Error: ${e.message}`);
    }

    console.log(`📦 Found ${items.length} topics. Saving to Knowledge DB...`);
    const results = [];

    for (const item of items) {
      // Validate content
      if (!item.content || item.content.trim() === "") {
        console.log(
          `⚠️ Skipping topic "${item.topic || "Unknown"}" due to empty content.`,
        );
        continue;
      }

      // Check for duplicates in Knowledge collection
      const exists = await Knowledge.findOne({ topic: item.topic });
      if (exists) {
        console.log(`Skipping ${item.topic} (already exists)`);
        continue;
      }

      // Embed the Topic + Content
      const textToEmbed = `Topic: ${item.topic}\nContent: ${item.content}`;
      const vector = await embeddings.embedQuery(textToEmbed);

      const newKnowledge = new Knowledge({
        source: fileName,
        topic: item.topic,
        content: item.content,
        embedding: vector,
      });
      await newKnowledge.save();
      results.push(newKnowledge);
      console.log(`✅ Added Knowledge: ${item.topic}`);
    }

    res.json({ message: "Knowledge Seeding completed", added: results });
  } catch (err) {
    console.error("PDF Seeding error:", err);
    res.status(500).json({ error: "PDF Seeding failed", details: err.message });
  }
});

// Get all Knowledge/Articles (from smart_interactive_whiteboard collection)
app.get("/api/knowledge", async (req, res) => {
  try {
    // Exclude embedding field using projection (-embedding)
    const knowledge = await Knowledge.find().select("-embedding");
    res.json(knowledge);
  } catch (err) {
    res
      .status(500)
      .json({ error: "Failed to fetch knowledge", details: err.message });
  }
});

// --- Start Server ---
app.listen(PORT, () => {
  console.log(`Server running on port ${PORT}`);
});
