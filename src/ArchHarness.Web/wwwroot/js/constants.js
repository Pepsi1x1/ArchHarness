export const STORAGE_KEY = "archharness.web.shell-state";
export const IDLE_INTERACTION_POLL_MS = 5000;
export const ACTIVE_INTERACTION_POLL_MS = 400;
export const STREAM_RENDER_DELAY_MS = 140;
export const DEFAULT_STREAM_EMPTY_MESSAGE = "Start a run from the composer to stream orchestrator, agent, and subagent output here.";
export const REVIEW_PROVIDER_NAME_MAX_LENGTH = 128;
export const REVIEW_FILTER_MAX_LENGTH = 200;
export const REVIEW_PULL_REQUEST_ID_MAX_LENGTH = 20;
export const PAT_STORAGE_MODE_PROTECTED = 0;
export const PAT_STORAGE_MODE_PLAINTEXT = 1;
export const GITHUB_AUTH_MODE_NONE = 0;
export const GITHUB_AUTH_MODE_PAT = 1;
export const GITHUB_AUTH_MODE_OAUTH = 2;

export const WORKFLOWS = Object.freeze({
  AUTO: "auto",
  PLANNING: "planning",
  ARCHITECTURE_LOOP: "architecture-loop",
  WIKIDOC: "wikidoc"
});

export const REVIEW_LOOP_DEFAULT_SELECTION = Object.freeze({
  codingStyleEnabled: true,
  securityEnabled: true,
  architectureEnabled: true
});

export const REVIEW_LOOP_AGENT_OPTIONS = Object.freeze([
  { key: "codingStyleEnabled", label: "Coding Style" },
  { key: "securityEnabled", label: "Security" },
  { key: "architectureEnabled", label: "Architecture" }
]);

export const RUN_STATUSES = Object.freeze({
  IDLE: "idle",
  STARTING: "starting",
  RESUMING: "resuming",
  RUNNING: "running",
  PAUSING: "pausing",
  PAUSED: "paused",
  CANCELING: "canceling",
  COMPLETED: "completed",
  CANCELED: "canceled",
  STOPPED: "stopped",
  FAILED: "failed"
});

export const MAIN_PANEL_VIEWS = Object.freeze({
  STREAM: "stream",
  BRANCH_CHANGES: "branch-changes"
});

export const STREAM_CONNECTION_STATES = Object.freeze({
  IDLE: RUN_STATUSES.IDLE,
  RECONNECTING: "reconnecting"
});

export const LEGACY_AUTOFILL_PROMPTS = new Set([
  "Implement requested change",
  "Run coding style, security, and architecture review loop for the existing workspace and apply required remediation."
]);

export const ROLE_LABELS = {
  conversation: "Conversation",
  orchestration: "Orchestration",
  planning: "Planning",
  frontendDeveloper: "Frontend Developer",
  backendDeveloper: "Backend Developer",
  build: "Build",
  codingStyle: "Coding Style",
  security: "Security",
  architecture: "Architecture"
};
