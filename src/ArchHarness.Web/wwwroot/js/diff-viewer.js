export function getGitChangeStatusClass(status) {
  return String(status || "modified").toLowerCase().replaceAll(/[^a-z0-9]+/g, "-");
}

export function createGitDiffMessageView(message) {
  const empty = document.createElement("div");
  empty.className = "git-diff-empty";
  empty.textContent = message;
  return empty;
}

export function setGitDiffPreviewContent(previewElement, view) {
  previewElement.replaceChildren(view);
}

export function parseUnifiedDiff(diffText) {
  const lines = String(diffText || "").replaceAll("\r", "").split("\n");
  const sections = [];
  let currentFile = null;
  let currentHunk = null;

  const ensureFile = () => {
    if (!currentFile) {
      currentFile = {
        headerLines: [],
        hunks: []
      };
      sections.push(currentFile);
    }

    return currentFile;
  };

  lines.forEach(line => {
    if (line.startsWith("diff --git ")) {
      currentFile = {
        headerLines: [line],
        hunks: []
      };
      sections.push(currentFile);
      currentHunk = null;
      return;
    }

    if (line.startsWith("@@")) {
      const file = ensureFile();
      currentHunk = {
        header: line,
        lines: []
      };
      file.hunks.push(currentHunk);
      return;
    }

    if (currentHunk) {
      currentHunk.lines.push(line);
      return;
    }

    ensureFile().headerLines.push(line);
  });

  return sections;
}

function parseHunkHeader(header) {
  const match = /^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@/.exec(header || "");
  if (!match) {
    return { oldLine: 1, newLine: 1 };
  }

  return {
    oldLine: Number.parseInt(match[1], 10),
    newLine: Number.parseInt(match[3], 10)
  };
}

function createDiffRow(left, right, rowType) {
  return { left, right, rowType };
}

function createDiffSide(number, text, type) {
  return { number, text, type };
}

function createEmptyDiffSide() {
  return createDiffSide("", "", "empty");
}

function createMetaDiffRow(text) {
  return createDiffRow(
    createDiffSide("", text, "meta"),
    createDiffSide("", text, "meta"),
    "meta"
  );
}

function getPairRowType(deletions, additions) {
  if (deletions.length > 0 && additions.length > 0) {
    return "modify";
  }

  return deletions.length > 0 ? "delete" : "add";
}

function getDeletionType(additions) {
  return additions.length > 0 ? "modify-delete" : "delete";
}

function getAdditionType(deletions) {
  return deletions.length > 0 ? "modify-add" : "add";
}

function collectPrefixedLines(lines, startIndex, prefix) {
  const collected = [];
  let index = startIndex;
  while (index < lines.length && lines[index].startsWith(prefix)) {
    collected.push(lines[index]);
    index += 1;
  }

  return { collected, nextIndex: index };
}

function buildSideBySideRows(hunk) {
  const rows = [];
  const headerInfo = parseHunkHeader(hunk.header);
  let oldLine = headerInfo.oldLine;
  let newLine = headerInfo.newLine;
  let index = 0;

  const pushPairGroup = (deletions, additions) => {
    const rowType = getPairRowType(deletions, additions);
    const deletionType = getDeletionType(additions);
    const additionType = getAdditionType(deletions);
    const rowCount = Math.max(deletions.length, additions.length);
    for (let i = 0; i < rowCount; i += 1) {
      const deletion = deletions[i] || null;
      const addition = additions[i] || null;
      rows.push(createDiffRow(
        deletion
          ? createDiffSide(oldLine++, deletion.slice(1), deletionType)
          : createEmptyDiffSide(),
        addition
          ? createDiffSide(newLine++, addition.slice(1), additionType)
          : createEmptyDiffSide(),
        rowType
      ));
    }
  };

  while (index < hunk.lines.length) {
    const line = hunk.lines[index];
    if (line.startsWith("-")) {
      const deletionGroup = collectPrefixedLines(hunk.lines, index, "-");
      const additionGroup = collectPrefixedLines(hunk.lines, deletionGroup.nextIndex, "+");

      pushPairGroup(deletionGroup.collected, additionGroup.collected);
      index = additionGroup.nextIndex;
      continue;
    }

    if (line.startsWith("+")) {
      pushPairGroup([], [line]);
      index += 1;
      continue;
    }

    if (line.startsWith(" ")) {
      rows.push(createDiffRow(
        createDiffSide(oldLine++, line.slice(1), "context"),
        createDiffSide(newLine++, line.slice(1), "context"),
        "context"
      ));
      index += 1;
      continue;
    }

    rows.push(createMetaDiffRow(line));
    index += 1;
  }

  return rows;
}

function createDiffCell(side) {
  const cell = document.createElement("div");
  cell.className = `git-diff-cell git-diff-cell-${side.type}`;

  const lineNumber = document.createElement("span");
  lineNumber.className = "git-diff-line-number";
  lineNumber.textContent = side.number === "" ? "" : String(side.number);

  const content = document.createElement("span");
  content.className = "git-diff-line-content";
  content.textContent = side.text || "";

  cell.append(lineNumber, content);
  return cell;
}

export function createSideBySideDiffView(diffText) {
  const sections = parseUnifiedDiff(diffText);
  if (!sections.length || sections.every(section => section.hunks.length === 0)) {
    return createGitDiffMessageView(diffText || "No textual diff is available for the selected file.");
  }

  const container = document.createElement("div");
  container.className = "git-diff-side-by-side";

  sections.forEach(section => {
    const sectionEl = document.createElement("section");
    sectionEl.className = "git-diff-section";

    const headerLines = section.headerLines.filter(Boolean);
    if (headerLines.length > 0) {
      const header = document.createElement("div");
      header.className = "git-diff-section-header";
      header.textContent = headerLines[headerLines.length - 1];
      sectionEl.append(header);
    }

    section.hunks.forEach(hunk => {
      const hunkEl = document.createElement("div");
      hunkEl.className = "git-diff-hunk";

      const hunkHeader = document.createElement("div");
      hunkHeader.className = "git-diff-hunk-header";
      hunkHeader.textContent = hunk.header;
      hunkEl.append(hunkHeader);

      const rows = document.createElement("div");
      rows.className = "git-diff-rows";

      buildSideBySideRows(hunk).forEach(row => {
        const rowEl = document.createElement("div");
        rowEl.className = `git-diff-row git-diff-row-${row.rowType}`;
        rowEl.append(createDiffCell(row.left), createDiffCell(row.right));
        rows.append(rowEl);
      });

      hunkEl.append(rows);
      sectionEl.append(hunkEl);
    });

    container.append(sectionEl);
  });

  return container;
}
