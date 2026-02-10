const mongoose = require("mongoose");
require("dotenv").config();

const MONGO_URL = process.env.MONGO_URL;

if (!MONGO_URL) {
  console.error("❌ Missing MONGO_URL in .env");
  process.exit(1);
}

const KnowledgeSchema = new mongoose.Schema({
  topic: { type: String, required: true },
  content: { type: String, required: true },
  embedding: { type: [Number], required: true },
  createdAt: { type: Date, default: Date.now },
});

const Knowledge = mongoose.model("Knowledge", KnowledgeSchema, "knowledge");

async function verifyKnowledge() {
  console.log("Connecting to MongoDB...");
  try {
    await mongoose.connect(MONGO_URL);
    console.log("✅ MongoDB Connected");

    const count = await Knowledge.countDocuments();
    console.log(`\n📊 Total Knowledge Documents: ${count}`);

    if (count > 0) {
      const docs = await Knowledge.find().limit(5);
      console.log("\n🔎 Sample Documents:");
      docs.forEach((doc, index) => {
        console.log(`\n[${index + 1}] Topic: ${doc.topic}`);
        console.log(`    Content Preview: ${doc.content.substring(0, 100)}...`);
      });
    } else {
      console.log(
        "\n⚠️ No knowledge data found. Please run the seeding API first."
      );
    }
  } catch (err) {
    console.error("❌ Error:", err);
  } finally {
    await mongoose.disconnect();
    console.log("\n👋 Disconnected");
  }
}

verifyKnowledge();
