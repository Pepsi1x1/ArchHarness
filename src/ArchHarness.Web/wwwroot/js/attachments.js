import { state, elements } from './state.js';
import { renderComposerState } from './composer.js';

const MAX_ATTACHMENT_BYTES = 8 * 1024 * 1024; // 8 MiB per image
const MAX_ATTACHMENT_COUNT = 6;
const ACCEPTED_IMAGE_PREFIX = "image/";

function randomId() {
  if (globalThis.crypto?.randomUUID) {
    return globalThis.crypto.randomUUID();
  }
  return `att-${Date.now()}-${Math.random().toString(16).slice(2, 10)}`;
}

function readFileAsBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error || new Error("Failed to read file."));
    reader.onload = () => {
      const result = reader.result || "";
      const comma = typeof result === "string" ? result.indexOf(",") : -1;
      resolve(comma >= 0 ? result.slice(comma + 1) : "");
    };
    reader.readAsDataURL(file);
  });
}

export function getComposerAttachments() {
  if (!Array.isArray(state.composerAttachments)) {
    state.composerAttachments = [];
  }
  return state.composerAttachments;
}

export function clearComposerAttachments() {
  state.composerAttachments = [];
  renderAttachments();
}

export function removeComposerAttachment(id) {
  const list = getComposerAttachments();
  state.composerAttachments = list.filter(item => item.id !== id);
  renderAttachments();
  renderComposerState();
}

export async function addComposerFiles(files) {
  if (!files || files.length === 0) {
    return;
  }

  const current = getComposerAttachments();
  let remaining = MAX_ATTACHMENT_COUNT - current.length;
  if (remaining <= 0) {
    return;
  }

  const next = [...current];
  for (const file of Array.from(files)) {
    if (remaining <= 0) {
      break;
    }
    if (!file || typeof file !== "object") {
      continue;
    }
    const mimeType = file.type || "";
    if (!mimeType.startsWith(ACCEPTED_IMAGE_PREFIX)) {
      continue;
    }
    if (typeof file.size === "number" && file.size > MAX_ATTACHMENT_BYTES) {
      continue;
    }

    let dataBase64 = "";
    try {
      dataBase64 = await readFileAsBase64(file);
    } catch (error) {
      console.warn("Failed to read attachment", error);
      continue;
    }
    if (!dataBase64) {
      continue;
    }

    next.push({
      id: randomId(),
      kind: "image",
      mimeType,
      fileName: file.name || "image",
      sizeBytes: typeof file.size === "number" ? file.size : 0,
      dataBase64,
      previewUrl: `data:${mimeType};base64,${dataBase64}`
    });
    remaining -= 1;
  }

  state.composerAttachments = next;
  renderAttachments();
  renderComposerState();
}

export function collectSubmissionAttachments() {
  return getComposerAttachments().map(({ id, kind, mimeType, fileName, sizeBytes, dataBase64 }) => ({
    id, kind, mimeType, fileName, sizeBytes, dataBase64
  }));
}

export function renderAttachments() {
  const container = elements.promptAttachments;
  if (!container) {
    return;
  }

  const list = getComposerAttachments();
  container.innerHTML = "";

  if (list.length === 0) {
    container.classList.add("hidden");
    return;
  }
  container.classList.remove("hidden");

  list.forEach(attachment => {
    const chip = document.createElement("div");
    chip.className = "attachment-chip";
    chip.dataset.attachmentId = attachment.id;

    if (attachment.previewUrl) {
      const img = document.createElement("img");
      img.className = "attachment-chip-thumb";
      img.alt = attachment.fileName;
      img.src = attachment.previewUrl;
      chip.append(img);
    }

    const label = document.createElement("span");
    label.className = "attachment-chip-label";
    label.textContent = attachment.fileName;
    chip.append(label);

    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "attachment-chip-remove";
    remove.setAttribute("aria-label", `Remove ${attachment.fileName}`);
    remove.textContent = "✕";
    remove.addEventListener("click", () => removeComposerAttachment(attachment.id));
    chip.append(remove);

    container.append(chip);
  });
}

export function initAttachments() {
  const fileInput = elements.promptAttachmentInput;
  const attachButton = elements.promptAttachmentButton;
  const textarea = elements.taskPrompt;

  if (fileInput) {
    fileInput.addEventListener("change", async (event) => {
      const target = event.currentTarget;
      await addComposerFiles(target.files);
      target.value = "";
    });
  }

  if (attachButton && fileInput) {
    attachButton.addEventListener("click", () => fileInput.click());
  }

  if (textarea) {
    textarea.addEventListener("paste", async (event) => {
      const items = event.clipboardData?.items;
      if (!items || items.length === 0) {
        return;
      }
      const files = [];
      for (const item of items) {
        if (item.kind === "file") {
          const file = item.getAsFile();
          if (file && file.type && file.type.startsWith(ACCEPTED_IMAGE_PREFIX)) {
            files.push(file);
          }
        }
      }
      if (files.length > 0) {
        event.preventDefault();
        await addComposerFiles(files);
      }
    });

    textarea.addEventListener("dragover", (event) => {
      if (event.dataTransfer && Array.from(event.dataTransfer.types || []).includes("Files")) {
        event.preventDefault();
        textarea.classList.add("is-drop-target");
      }
    });

    textarea.addEventListener("dragleave", () => {
      textarea.classList.remove("is-drop-target");
    });

    textarea.addEventListener("drop", async (event) => {
      if (!event.dataTransfer || !event.dataTransfer.files || event.dataTransfer.files.length === 0) {
        return;
      }
      event.preventDefault();
      textarea.classList.remove("is-drop-target");
      await addComposerFiles(event.dataTransfer.files);
    });
  }

  renderAttachments();
}
