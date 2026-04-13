# Architecture and Execution Flow

## Relevant files

| Path | Why it matters |
| --- | --- |
| `src\ArchHarness.App\Program.cs` | Shared dependency-injection composition root. |
| `src\ArchHarness.App\Core\Contracts.cs` | Run contracts such as `RunRequest`, `ExecutionPlan`, `ClarificationSpec`, and review models. |
| `src\ArchHarness.App\Core\OrchestratorRuntime.cs` | Entry point for starting or resuming runs. |
| `src\ArchHarness.App\Core\OrchestratedRunProcessor.cs` | Main run pipeline: planning, execution, review loop, verification, and finalization. |
| `src\ArchHarness.App\Core\ArchitectureReviewLoop.cs` | Iterative remediation loop for style, security, and architecture findings. |
| `src\ArchHarness.App\Core\RunVerificationWorkflow.cs` | Post-execution verification and bounded remediation workflow. |

## Composition root

`src\ArchHarness.App\Program.cs` is the runtime composition root used by both the console and web hosts. It registers:

- Copilot services: model resolution, session management, client wrappers, governance hooks, event streams, and usage logging.
- Agent roles: Orchestration, Planning, FrontendDeveloper, BackendDeveloper, Build, CodingStyle, Security, Architecture, and WikiDoc.
- Runtime services: execution-plan parsing, review loop orchestration, completion validation, verification command execution, WikiDoc workflow services, setup summary generation, and preflight validation.
- Persistence and workspace services: run store, run-state store, run-history catalog, user-scoped settings/projects/providers, and workspace adapters.

The console and web hosts add only their host-specific bridges on top of this shared runtime.

## Core contracts

The runtime is driven by a small set of durable contracts in `src\ArchHarness.App\Core\Contracts.cs`:

| Contract | Purpose |
| --- | --- |
| `RunRequest` | Input to a run. Carries task prompt, workspace path/mode, workflow, model overrides, build command, permission mode, review-loop overrides, architecture-loop flags, project identity, and planning handoff source. |
| `ExecutionPlanStep` | One delegated task for a specific agent. Steps support explicit dependencies, language scope, and `ParallelGroup` batching. |
| `ExecutionPlan` | Ordered step list plus `IterationStrategy` and completion criteria. |
| `ClarificationSpec` | Durable task contract covering scope, assumptions, acceptance criteria, likely touchpoints, and verification commands. |
| `PlanApproval` | User decision for a generated plan (`approved`, `regenerate`, or `canceled`). |
| `PersistedRunState` | Checkpoint used for pause/resume and for handing a planning run into a later implementation run. |

## Persisted phases and statuses

Run state separates phase from status:

| Type | Values from code | Meaning |
| --- | --- | --- |
| Phase | `clarification`, `plan-approval`, `planning`, `handoff-ready`, `executing-plan`, `architecture-loop`, `finalizing` | Where the processor is inside the pipeline. |
| Status | `idle`, `starting`, `resuming`, `running`, `pausing`, `paused`, `canceling`, `completed`, `incomplete`, `canceled`, `stopped`, `failed` | Current execution outcome from the host/runtime perspective. |

`PersistedRunState.CanResume` remains true for everything except `completed` and `canceled`.

## Standard run lifecycle

The normal run path is `IOrchestratorRuntime.RunAsync` or `ResumeAsync`, which delegates to `OrchestratedRunProcessor.ExecuteAsync`:

1. **Normalize and prepare the request.** Workflow defaults are applied before work begins.
2. **Create the run directory and choose a workspace adapter.** Run artifacts go under `<workspace>\.agent-harness\runs\<runId>\`.
3. **Resolve build verification behavior.** `BuildCommandInference.Select` prefers discovered `.sln` files, then non-test `.csproj` files, favors `src\`, ignores `bin\` and `obj\`, and can inject a discovered target into a user-supplied `dotnet build` command that does not already name one.
4. **Clarify and plan.** The processor can generate a `ClarificationSpec`, ask follow-up questions through host bridges, and persist both JSON and Markdown versions of the spec.
5. **Parse and validate the execution plan.** `ExecutionPlanParser` accepts JSON inside markdown code fences, extracts the first JSON object, and can repair some truncated JSON before schema validation.
6. **Normalize plan ordering.** Review steps are moved behind implementation/build work, and default CodingStyle/Security/Architecture review steps can be injected when the model omits them.
7. **Pause for plan approval when required.** Approval decisions are persisted to `PlanApproval.json` and can come from the console host or the web API.
8. **Execute the plan.** Steps dispatch to the target agents and honor `DependsOnStepIds` and `ParallelGroup`.
9. **Run review/remediation loops.** The processor invokes the architecture loop when review agents are enabled and high-severity findings remain.
10. **Verify and validate completion.** Verification commands run, remediation can be attempted, and the completion validator writes durable evidence.
11. **Finalize artifacts and summary.** The run writes final markdown/json summaries, terminal run state, and event logs.

## Planning-only and handoff runs

The `planning` workflow stops after clarification, plan generation, and plan approval. Instead of executing implementation steps immediately, it persists a resumable state with phase `handoff-ready`. The web host exposes `POST /api/runs/{runId}/handoff`, which creates a new run using the approved artifacts and records the original planning run in `RunRequest.PlanningSourceRunId`.

This is the repo's explicit separation point between "produce an approved plan" and "apply the plan."

## Review-loop mechanics

`ArchitectureReviewLoop` is the remediation engine used after an initial implementation pass and also by the dedicated `architecture-loop` workflow.

### Agent participation

`ReviewLoopAgentSelection` controls which review agents are active:

| Agent | Default in committed config | Effect |
| --- | --- | --- |
| `CodingStyle` | Enabled | Runs direct style enforcement against the latest diff or file set. |
| `Security` | Enabled | Produces security findings and required actions; high-severity findings block completion when enabled. |
| `Architecture` | Enabled | Produces architecture findings and required actions; high-severity findings block completion when enabled. |

### Loop behavior

- The loop continues while review is required, the max iteration count has not been reached, and enabled architecture/security reviews still report high-severity findings.
- In standard runs, the loop works from touched files and the latest diff.
- In `architecture-loop` mode, the loop expands its scope to the workspace file set for the configured language filters.
- If two consecutive iterations produce identical architecture and security finding fingerprints, the loop marks the run with `blocked:no-progress-identical-findings` and stops instead of spinning forever.

## Verification and completion validation

`RunVerificationWorkflow` performs the post-execution "prove it works" pass:

- Verification commands come from `ClarificationSpec.VerificationCommands`.
- If a build-related completion criterion exists and the run has a `BuildCommand`, the workflow injects a synthetic "Build validation" command when the spec did not already define one.
- The workflow performs up to **3** attempts when executable verification commands exist. If there are no commands, it still runs one validation pass to produce a completion result.
- Between failed attempts, it builds a remediation prompt and re-invokes the frontend and/or backend developer agents based on which agent types appeared in the plan. If no implementation agent appears, it falls back to the backend developer agent.

`CompletionCriteriaSupport` contains the built-in deterministic checks for:

- Build criteria
- No high-severity security findings
- No high-severity architecture findings
- Coding style enforcement completion
- WikiDoc-specific criteria

The final validator writes both `CompletionValidation.json` and `CompletionValidation.md`, including criteria, evidence, attempts, and an implementation assessment.

## Governance and audit trail

The runtime adds explicit safety and audit controls around tool execution:

- `CopilotGovernancePolicy` denies obviously destructive operations before the underlying tool runs.
- Orchestration and build agents exclude `edit_file` by default through `AgentToolPolicyProvider`.
- Session lifecycle events, agent delta streams, and structured run events are all persisted so a completed run can be reconstructed later from the artifact directory.

## See also

- [Storage, Run Artifacts, and State](Storage-Run-Artifacts-and-State.md)
- [Configuration and Models](Configuration-and-Models.md)
- [WikiDoc Workflow](WikiDoc-Workflow.md)
