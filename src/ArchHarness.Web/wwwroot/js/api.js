export async function requestJson(url, options) {
  const response = await fetch(url, options);
  if (response.status === 204) {
    return null;
  }

  if (response.status === 202) {
    const text = await response.text();
    return text ? JSON.parse(text) : null;
  }

  if (!response.ok) {
    const contentType = response.headers.get("content-type") || "";
    let errorData = null;
    let text = "";

    if (contentType.includes("application/json")) {
      errorData = await response.json();
      text = errorData?.error || errorData?.title || JSON.stringify(errorData);
    } else {
      text = await response.text();
    }

    const error = new Error(text || `Request failed with status ${response.status}`);
    error.status = response.status;
    error.data = errorData;
    throw error;
  }

  return response.json();
}

export async function requestEventStream(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    const contentType = response.headers.get("content-type") || "";
    let errorData = null;
    let text = "";

    if (contentType.includes("application/json")) {
      errorData = await response.json();
      text = errorData?.error || errorData?.title || JSON.stringify(errorData);
    } else {
      text = await response.text();
    }

    const error = new Error(text || `Request failed with status ${response.status}`);
    error.status = response.status;
    error.data = errorData;
    throw error;
  }

  if (!response.body) {
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  const processBlock = block => {
    const normalized = block.replaceAll("\r", "");
    if (!normalized.trim()) {
      return;
    }

    let eventName = "message";
    const dataLines = [];
    normalized.split("\n").forEach(line => {
      if (!line || line.startsWith(":")) {
        return;
      }

      if (line.startsWith("event:")) {
        eventName = line.slice("event:".length).trim() || "message";
        return;
      }

      if (line.startsWith("data:")) {
        dataLines.push(line.slice("data:".length).trimStart());
      }
    });

    let data = null;
    const serialized = dataLines.join("\n");
    if (serialized) {
      try {
        data = JSON.parse(serialized);
      } catch {
        data = serialized;
      }
    }

    options?.onEvent?.({ event: eventName, data });
  };

  const flushBuffer = finalChunk => {
    let delimiterIndex = buffer.indexOf("\n\n");
    while (delimiterIndex >= 0) {
      processBlock(buffer.slice(0, delimiterIndex));
      buffer = buffer.slice(delimiterIndex + 2);
      delimiterIndex = buffer.indexOf("\n\n");
    }

    if (finalChunk && buffer.trim()) {
      processBlock(buffer);
      buffer = "";
    }
  };

  while (true) {
    const { value, done } = await reader.read();
    if (done) {
      buffer += decoder.decode();
      flushBuffer(true);
      break;
    }

    buffer += decoder.decode(value, { stream: true });
    flushBuffer(false);
  }
}

export function fetchPlanningSession(sessionId, workspacePath) {
  const url = `/api/planning-sessions/${encodeURIComponent(sessionId)}?workspacePath=${encodeURIComponent(workspacePath)}`;
  return requestJson(url);
}

export function postPlanningFollowUp(sessionId, { workspacePath, text, attachments, relatedRunId }) {
  return requestJson(`/api/planning-sessions/${encodeURIComponent(sessionId)}/messages`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      workspacePath,
      role: "user",
      kind: "follow-up",
      text: text ?? "",
      authorAgent: null,
      relatedRunId: relatedRunId ?? null,
      attachments: Array.isArray(attachments) && attachments.length > 0 ? attachments : null
    })
  });
}
