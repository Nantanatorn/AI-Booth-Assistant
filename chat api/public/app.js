const chatBox = document.getElementById("chat");
const input = document.getElementById("input");
const sendBtn = document.getElementById("send-btn");

// ต่อ WebSocket มาที่ Node.js (ซึ่ง proxy ไป Gemini)
const ws = new WebSocket(`ws://${window.location.host}/chat`);

ws.onopen = () => {
  appendSystem("Connecting to Gemini Live...");
};

ws.onmessage = (event) => {
  let obj;
  try {
    obj = JSON.parse(event.data);
  } catch {
    appendAI(event.data);
    return;
  }

  if (obj.type === "system") {
    appendSystem(obj.text);
  } else if (obj.type === "ai_chunk") {
    appendAI(obj.text);
  }
};

ws.onclose = () => {
  appendSystem("🔌 Disconnected from server");
};

ws.onerror = (err) => {
  console.error("WS error", err);
  appendSystem("WS error (ดู console)");
};

sendBtn.onclick = sendMessage;
input.addEventListener("keydown", (e) => {
  if (e.key === "Enter") {
    sendMessage();
  }
});

function sendMessage() {
  const text = input.value.trim();
  if (!text || ws.readyState !== WebSocket.OPEN) return;

  appendUser(text);

  ws.send(
    JSON.stringify({
      type: "user_message",
      text,
    })
  );

  input.value = "";
}

function appendUser(text) {
  const row = document.createElement("div");
  row.className = "msg-row user";

  const div = document.createElement("div");
  div.className = "msg msg-user";
  div.textContent = text;

  row.appendChild(div);
  chatBox.appendChild(row);
  chatBox.scrollTop = chatBox.scrollHeight;
}

function appendAI(text) {
  const row = document.createElement("div");
  row.className = "msg-row ai";

  const div = document.createElement("div");
  div.className = "msg msg-ai";
  div.textContent = text;

  row.appendChild(div);
  chatBox.appendChild(row);
  chatBox.scrollTop = chatBox.scrollHeight;
}

function appendSystem(text) {
  const div = document.createElement("div");
  div.className = "msg-system";
  div.textContent = text;
  chatBox.appendChild(div);
  chatBox.scrollTop = chatBox.scrollHeight;
}
