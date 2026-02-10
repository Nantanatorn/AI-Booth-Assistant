# AI Booth Assistant 🤖

The **AI Booth Assistant** project is designed to help answer questions and provide information about products or services at exhibition booths. It features a collaboration between **Unity Connection** for the Avatar display and a **Node.js API** that connects to **Google Gemini** and **MongoDB** to process responses from a Knowledge Base.

## 🛠️ Tech Stack

- **Game Engine**: Unity (C#)
- **Backend Runtime**: Node.js
- **Framework**: Express.js
- **Database**: MongoDB (Mongoose)
- **AI Model**: Google Gemini (DeepMind)
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

- `AI Unity/`: Unity Project Source Code (Client)
- `chat api/`: Backend API Source Code (Server)
- `Build/`: Directory for built files (if any)

---

## 🚀 Installation & Setup

### 1. Backend Setup (API)

Prerequisites: [Node.js](https://nodejs.org/) and [MongoDB](https://www.mongodb.com/).

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
   PORT=3000
   PORT1=3100
   MONGO_URL=mongodb://localhost:27017/your_database_name
   GEMINI_API_KEY=your_gemini_api_key
   ```
4. Run Server:
   - **For API & Upload PDF:**
     1. Run the main server first:
        ```bash
        npm start
        ```
     2. Open another Terminal to run the PDF upload script:
        ```bash
        cd "api-call"
        node seed_request.js
        ```
        > **Note:** For PDF uploading, `server.js` (npm start) must be running.

   - **For AI Chat:**
     ```bash
     node xD.js
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
2. Open the Main Scene by navigating to **Project** > **Scenes** > **Hand-Game** > **HandGame**.
3. Check the API URL settings in Scripts (e.g., `StageManager.cs` or `Loopchat.cs`) to ensure they point to the running server (e.g., `http://localhost:3000`).
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
