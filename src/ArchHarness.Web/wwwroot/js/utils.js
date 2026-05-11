export function escapeHtml(text) {
  return String(text ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

// Security boundary: sanitizeHtmlFragment is the only approved path for injecting rendered HTML into the DOM.
// Every rich-content write must go through sanitizeHtmlFragment or setSanitizedHtml so sink review stays centralized.
// Strips hostile tags and URI schemes to guard against XSS from locally-rendered server content.
export function sanitizeHtmlFragment(html) {
  const parser = new DOMParser();
  const doc = parser.parseFromString(html || "", "text/html");
  doc.querySelectorAll("script,iframe,object,embed,form,base,meta,svg,math,use,link[rel=import]").forEach(el => el.remove());
  doc.querySelectorAll("*").forEach(el => {
    for (const attr of Array.from(el.attributes)) {
      const name = attr.name.toLowerCase();
      const trimmedValue = attr.value.trimStart().toLowerCase();
      const isUnsafeUri = trimmedValue.startsWith("javascript:")
        || trimmedValue.startsWith("data:")
        || trimmedValue.startsWith("vbscript:");
      const isUnsafeSrcSet = name === "srcset"
        && (trimmedValue.includes("data:") || trimmedValue.includes("javascript:") || trimmedValue.includes("vbscript:"));
      if (name.startsWith("on")
        || name === "style"
        || name === "formaction"
        || name === "xlink:href"
        || (name === "data" && (el.tagName === "OBJECT" || el.tagName === "EMBED"))
        || ((name === "href" || name === "src" || name === "action" || name === "poster" || name === "background") && isUnsafeUri)
        || isUnsafeSrcSet) {
        el.removeAttribute(attr.name);
      }
    }
  });
  const fragment = document.createDocumentFragment();
  while (doc.body.firstChild) {
    fragment.append(doc.body.firstChild);
  }
  return fragment;
}

export function setSanitizedHtml(element, html) {
  element.replaceChildren(sanitizeHtmlFragment(html));
}

export function timeAgo(value) {
  if (!value) return "";
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  const secs = Math.floor((Date.now() - date.getTime()) / 1000);
  if (secs < 60) return `${secs}s ago`;
  const mins = Math.floor(secs / 60);
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;
  const months = Math.floor(days / 30);
  if (months < 12) return `${months}mo ago`;
  return `${Math.floor(months / 12)}y ago`;
}

export function runDateFromId(runId) {
  if (!runId || runId.length < 13) return null;
  const year = runId.slice(0, 4);
  const month = runId.slice(4, 6);
  const day = runId.slice(6, 8);
  const hour = runId.slice(9, 11);
  const minute = runId.slice(11, 13);
  return new Date(`${year}-${month}-${day}T${hour}:${minute}:00`);
}

export function readEventField(entry, field) {
  if (!entry) {
    return null;
  }

  const pascalCase = field.charAt(0).toUpperCase() + field.slice(1);
  return entry[field] ?? entry[pascalCase] ?? null;
}

export function formatTimestamp(value) {
  return value
    ? new Date(value).toLocaleString([], { hour: "2-digit", minute: "2-digit", month: "short", day: "numeric" })
    : "Pending";
}

export function formatRunTimestamp(runId) {
  if (!runId || runId.length < 13) {
    return runId || "Unknown";
  }

  const year = runId.slice(0, 4);
  const month = runId.slice(4, 6);
  const day = runId.slice(6, 8);
  const hour = runId.slice(9, 11);
  const minute = runId.slice(11, 13);
  return `${year}-${month}-${day} ${hour}:${minute}`;
}

export function summarizeWorkspacePath(path) {
  const normalized = String(path || "").replaceAll("\\", "/").replace(/\/$/, "");
  if (!normalized) {
    return "No workspace path";
  }

  const segments = normalized.split("/").filter(Boolean);
  return segments.length <= 3 ? normalized : `.../${segments.slice(-3).join("/")}`;
}

export function equalIgnoringCase(left, right) {
  return String(left || "").localeCompare(String(right || ""), undefined, { sensitivity: "accent" }) === 0;
}

export function populateSelect(select, values) {
  select.replaceChildren();
  values.forEach(value => {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = value;
    select.append(option);
  });
}

export function setSelectValue(select, value) {
  if (!value) {
    return;
  }

  const option = Array.from(select.options).find(candidate => candidate.value === value);
  if (option) {
    select.value = value;
  }
}

export function getSelectDisplayLabel(select) {
  const selectedOption = Array.from(select.options).find(option => option.value === select.value) || select.options[0] || null;
  return selectedOption?.textContent || selectedOption?.value || "Select";
}
