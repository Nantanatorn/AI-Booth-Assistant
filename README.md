# AI Booth Assistant 🤖

The **AI Booth Assistant** project is designed to help answer questions and provide information about products or services at exhibition booths. It features a collaboration between **Unity** for the Avatar display and a **Node.js API** that connects to **Google Gemini** and **MongoDB** to process responses from a Knowledge Base.

## 🛠️ Tech Stack

- **Frontend**: Unity (C#)
- **Backend**: Node.js
- **Framework**: Express.js
- **Database**: MongoDB (Mongoose)
- **AI Model**: gemini-2.5-flash-native-audio-preview-12-2025
- **Libraries**: LangChain, Dotenv, PDF-Parse

## 🌟 Features

### 🖥️ Frontend (Unity)

- **AI Avatar**: An AI character that assists in answering questions and providing information about products/services at the exhibition.
- **Voice Interaction**: Supports Speech-to-Text for commands and Text-to-Speech for responses.
- **Auto Idle & Ratio Control**: Manages idle state and locks screen aspect ratio (9:16).
- **Media Controller**: Controls video playback and other media within the booth.

### ⚙️ Backend (Node.js)

- **LLM Integration**: Uses **Google Gemini** for natural language processing and response generation.
- **RAG (Retrieval-Augmented Generation)**: Retrieves information from the MongoDB database to answer questions based on specific product data or documents.
- **Embedding Search**: Uses Gemini Embedding for text vectorization and similarity search (Vector Search).
- **PDF Parsing**: Supports uploading PDF files to automatically extract and add information to the Knowledge Base.

---

## 📂 Project Structure

```text
AI Booth Assistant
├── .vscode
├── AI Unity (Client)
├── chat api (Server)
├── Build
├── .gitattributes
├── .gitignore
└── README.md
```

---

## 🚀 Installation & Setup

### Prerequisites

- [Unity](https://unity.com/)
- [Node.js](https://nodejs.org/)
- [MongoDB](https://www.mongodb.com/)

### 1. Backend Setup (API)

1. Open Terminal and navigate to the `chat api` folder:
   ```bash
   cd "chat api"
   ```
2. Install Dependencies:
   ```bash
   npm install
   ```
3. Create a `.env` file in the `chat api` folder and configure the following:
   ```env
   PORT= (Websocket PORT)
   PORT1= (PDF upload API PORT)
   MONGO_URL= (MongoDB URL)
   GEMINI_API_KEY= (Gemini API Key)
   ```
4. Run Server:
   - **For API & Upload PDF:**
     1. Place your PDF file into the `Product` folder.
     2. Run the main server:
        ```bash
        npm start
        ```
     3. Open another Terminal to run the PDF upload script:
        ```bash
        cd "api-call"
        node seed_request.js
        ```
        > **Note:** For PDF uploading, `server.js` (npm start) must be running.

   - **For AI Chat:**
     ```bash
     node AI-Booth.js
     ```

5. Setup MongoDB Vector Search Index
   - Create an Index named `smart_index` (or as defined in the code) in your Collection.
   - Use the following configuration:
     ```json
     {
       "fields": [
         {
           "numDimensions": 3072,
           "path": "embedding",
           "similarity": "cosine",
           "type": "vector"
         },
         {
           "path": "topic",
           "type": "filter"
         },
         {
           "path": "content",
           "type": "filter"
         },
         {
           "path": "source",
           "type": "filter"
         }
       ]
     }
     ```

### 2. Unity Setup (Client)

1. Open **Unity Hub** and add the project from the `AI Unity` folder.
2. Open the Main Scene by navigating to **Project** > **Scenes** > **AI-Booth** > **AI-Booth**.
3. Check the API URL settings in Scripts to ensure they point to the running server (e.g., `http://localhost:3000`).
4. Press **Play** to test.

## ❓ Troubleshooting

### ⚠️ Unity: Pink Shader Error or `ArgumentNullException`

If you encounter Shader errors, pink objects, or the following Error Log when opening the project:

```
ArgumentNullException: Value cannot be null.
Parameter name: shader
at Nobi.UiRoundedCorners.ImageWithRoundedCorners.Validate ()
```

Follow these steps to fix it:

1. Go to menu **Edit** > **Project Settings**.
2. Select **Graphics**.
3. Go to the **Always Included Shaders** section.
4. Click the plus (+) button to add a Shader.
5. Search for **"RoundedCorner"** and add it.
   - _Note:_ If you cannot find it, click the **Eye Icon** 👁️ in the search window to show hidden shaders.

## 📦 Build Backend to .exe (Optional)

To compile the Node.js backend into an executable file, it is recommended to bundle the code first using **esbuild** to handle dependencies correctly before using **pkg**.

### Step 1: Install Tools

Run these commands in your terminal (one-time setup):

```bash
npm install -g pkg
npm install esbuild --save-dev
```

### Step 2: Bundle Files

Use `esbuild` to bundle `AI-Booth.js`, `prompt2.js`, and other dependencies into a single file (`app_bundled.js`) to prevent errors:

```bash
npx esbuild AI-Booth.js --bundle --platform=node --target=node18 --outfile=app_bundled.js
```

_After running this, a new file `app_bundled.js` will be created._

### Step 3: Create .exe

Use `pkg` on the bundled file to generate the executable:

```bash
pkg app_bundled.js --targets node18-win-x64 --output Build/MyAssistant.exe
```

> **Note:**
>
> - You can also build `server.js` using the same method if needed (e.g., `npx esbuild server.js ...`).
> - Ensure the `.env` file is in the same folder as the `.exe` for it to work.
