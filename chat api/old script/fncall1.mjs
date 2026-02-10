import { GoogleGenAI, Type } from '@google/genai';
import dotenv from 'dotenv';
dotenv.config();
// Configure the client
const GEMINI_API_KEY = process.env.GEMINI_API_KEY;
if (!GEMINI_API_KEY) {
  throw new Error('Missing GEMINI_API_KEY in environment');
}

const ai = new GoogleGenAI({ apiKey: GEMINI_API_KEY });

// Define the function declaration for the model
const weatherFunctionDeclaration = {
  name: 'get_current_temperature',
  description: 'Gets the current temperature for a given location.',
  parameters: {
    type: Type.OBJECT,
    properties: {
      location: {
        type: Type.STRING,
        description: 'The city name, e.g. San Francisco',
      },
    },
    required: ['location'],
  },
};
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
  description: 'List  Asia countries.',
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

// Send request with function declarations
const response = await ai.models.generateContent({
  model: 'gemini-2.5-flash',
  contents: "Which  is a thai food Pad Thai or Sushi? ",
  config: {
    tools: [{
      functionDeclarations: [weatherFunctionDeclaration, FoodFunctionDeclaration, ListAsiaFunctionDeclaration]
    }],
  },
});

// Check for function calls in the response
if (response.functionCalls && response.functionCalls.length > 0) {
  const functionCall = response.functionCalls[0]; // Assuming one function call
  console.log(`Function to call: ${functionCall.name}`);
  console.log(`Arguments: ${JSON.stringify(functionCall.args)}`);
  // In a real app, you would call your actual function here:
  // const result = await getCurrentTemperature(functionCall.args);
} else {
  console.log("No function call found in the response.");
  console.log(response.text);
}
