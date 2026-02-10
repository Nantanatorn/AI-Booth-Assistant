const fetch = require("node-fetch"); // You might need to install node-fetch if not available, or use http module.
// Since this is a simple script, I'll use the native http module to avoid dependencies if possible,
// but node-fetch is easier. Let's assume standard node environment.
// Actually, modern Node has fetch built-in (v18+). If not, I'll use http.
// Let's use a simple http request.

const http = require("http");

const fs = require("fs");
const path = require("path");

// Directory containing the PDF files
const productDir = path.join(__dirname, "../Product");

// Get all PDF files from the Product directory
const getFiles = () => {
  try {
    const allFiles = fs.readdirSync(productDir);
    return allFiles
      .filter((file) => file.toLowerCase().endsWith(".pdf"))
      .map((file) => path.join(productDir, file));
  } catch (err) {
    console.error("❌ Error reading Product directory:", err.message);
    process.exit(1);
  }
};

const files = getFiles();

const seedFile = (filePath) => {
  return new Promise((resolve, reject) => {
    const data = JSON.stringify({ filePath });

    const options = {
      hostname: "localhost",
      port: 3000, // Make sure this matches your server port
      path: "/api/seed-pdf",
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Content-Length": Buffer.byteLength(data),
      },
    };

    const req = http.request(options, (res) => {
      let responseBody = "";

      res.on("data", (chunk) => {
        responseBody += chunk;
      });

      res.on("end", () => {
        console.log(`\n📄 File: ${path.basename(filePath)}`);
        console.log(`Status: ${res.statusCode}`);
        // console.log("Response:", responseBody); // Uncomment to see full response
        if (res.statusCode >= 200 && res.statusCode < 300) {
          console.log("✅ Success");
        } else {
          console.log("❌ Failed");
        }
        resolve();
      });
    });

    req.on("error", (error) => {
      console.error(`❌ Error processing ${filePath}:`, error.message);
      resolve(); // Continue to next file even if error
    });

    req.write(data);
    req.end();
  });
};

async function processFiles() {
  console.log(
    `🚀 Starting batch seed for ${files.length} files from ${productDir}...`
  );

  for (const file of files) {
    await seedFile(file);
  }

  console.log("\n✨ Batch processing completed.");
}

processFiles();
