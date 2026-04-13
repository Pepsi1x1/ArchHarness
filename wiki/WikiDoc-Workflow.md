# WikiDoc Workflow

## Relevant files

| Path | Why it matters |
| --- | --- |
| `src\ArchHarness.App\Agents\WikiDocAgent.cs` | Defines the WikiDoc system prompt, repository prompt, megawiki synthesis prompt, and JSON-only completion contract. |
| `src\ArchHarness.App\Core\WikiDocWorkflow.cs` | Main repository discovery, parallel execution, fallback handling, megawiki synthesis, and validation logic. |
| `src\ArchHarness.App\Core\WikiDocRepositoryDiscoverer.cs` | Recursive Git repository discovery rules. |
| `src\ArchHarness.App\Core\WikiDocOutputResolver.cs` | Repository-local `wiki\` resolution, rename rules, and fallback creation. |
| `src\ArchHarness.App\Core\WikiDocContracts.cs` | Durable report, output, and concept contracts. |
| `src\ArchHarness.Web\wwwroot\js\wikidoc-screen.js` | Dedicated UI flow for launching and streaming WikiDoc runs. |

WikiDoc is a first-class workflow inside ArchHarness. It is not a post-processing script: it uses the same runtime, event stream, artifact store, and model-resolution system as standard coding runs.

## Entry points

WikiDoc can be started from two places:

1. **Console CLI** via `wikidoc <scanRoot> [projectName] [modelOverrides]`
2. **Web/Electron UI** via the dedicated WikiDoc screen, which submits a normal run using `workflow: "wikidoc"`

The workflow uses the scan root as its workspace path and always runs with `workspaceMode: "existing-folder"`.

## Repository discovery rules

`WikiDocRepositoryDiscoverer` walks the scan root recursively and returns every directory that has a `.git` marker. Important behavior:

- the scan root itself is included when it is a Git repository
- results are ordered by relative path
- repository display names use the scan-relative path when possible
- a filesystem-safe key is generated for fallback output paths

The discovery walk skips:

- `.agent-harness`
- `.git`
- `.vs`
- `bin`
- `obj`
- `node_modules`
- reparse points

This keeps WikiDoc from recursing into transient build output, nested Git metadata, or symlinked content.

## Output resolution rules

`WikiDocOutputResolver` decides where documentation is written for each repository:

| Situation | Result |
| --- | --- |
| `wiki\` already exists as a directory | Write directly into that folder. |
| `wiki` already exists as a file | Use a deterministic fallback folder under the run directory and record the reason. |
| `docs\`, `doc\`, or `documentation\` exists and contains only documentation-safe file types | Rename that folder to `wiki\` and write there. |
| No usable local docs folder exists | Create `wiki\` under the repository root. |
| Local `wiki\` creation fails | Use a deterministic fallback folder under the run directory and record the reason. |

The "documentation-safe" rename check allows markdown and documentation-adjacent assets such as `.md`, `.markdown`, `.mdx`, `.txt`, `.json`, `.yaml`, `.yml`, `.png`, `.jpg`, `.jpeg`, `.gif`, `.svg`, `.webp`, `.pdf`, and `.drawio`.

Repository fallbacks are written under:

`<runDirectory>\wikidoc-fallback\<repositorySafeKey>\wiki\`

When a fallback is used, the workflow records the requested local path, the actual fallback path, and a machine-readable reason code in `WikiDocFallbacks.json`.

## Agent contract

`WikiDocAgent` imposes a stricter contract than most other agents:

- it must write all markdown pages via tools before it returns
- `Home.md` must be an index page only
- the response must be raw JSON, not markdown or commentary
- the JSON payload must include repository name, summary, page list, and up to 8 concept seeds

The repository currently does **not** contain `Prompts\WikiDoc\system.md`, `repository.md`, or `megawiki.md`. Because those files are absent, `WikiDocAgent` is currently using its embedded fallback prompt text from `WikiDocAgent.cs`.

`WikiDocAgent` also normalizes the returned JSON:

- blank page names are discarded
- duplicate pages are removed
- concept seeds are trimmed, deduplicated by name, and capped at 8

## Repository execution model

`WikiDocWorkflow.ExecuteAsync` runs one documentation pass per discovered repository and collects the results into a run-wide report.

Important execution details:

- repository documentation runs in parallel using `Parallel.ForEachAsync`
- parallelism is controlled by persisted global settings via `WikiDocParallelism`
- the committed default parallelism comes from `AgentsOptions` and is `4`
- each repository run gets a stable documentation session key such as `wikidoc-root` or `wikidoc-src_service`

If the agent fails to materialize `Home.md`, the workflow writes a minimal fallback `Home.md` so the repository still has a readable landing page and the run report remains consistent.

## Megawiki synthesis

After all repository passes complete, the workflow generates an aggregate wiki rooted at:

`<scanRoot>\megawiki\wiki\`

The aggregate output contains:

- `Home.md` for the overall index
- `concepts\*.md` for cross-repository concepts

Megawiki synthesis uses `WikiDocAgent.SynthesizeMegaWikiAsync`, which:

- runs with the orchestration role override
- receives a JSON payload containing per-repository home paths, summaries, and concept seeds
- returns a JSON manifest containing concept slugs only

If the agent returns concept slugs but does not actually write the concept pages, the workflow backfills placeholder concept files so the output tree is still complete. The same fallback behavior exists for missing aggregate `Home.md`.

If the scan-root `megawiki\wiki\` path cannot be created, the aggregate output falls back to:

`<runDirectory>\wikidoc-fallback\megawiki\wiki\`

## Validation and run reporting

At the end of a WikiDoc run, the workflow writes:

| File | Contents |
| --- | --- |
| `WikiDocReport.json` | Discovered repository count, per-repository output roots, aggregate output paths, summaries, concepts, and fallback records. |
| `WikiDocFallbacks.json` | Explicit fallback ledger for repository and megawiki outputs. |
| `CompletionValidation.json` / `CompletionValidation.md` | Built-in validation outcome for repository discovery, per-repo home pages, megawiki output, and concept pages. |

The built-in validation checks whether the workflow:

1. discovered at least one Git repository
2. wrote one `Home.md` per discovered repository
3. synthesized megawiki output
4. synthesized concept pages

## Operational implications

For operators, WikiDoc has a few important characteristics:

- it will happily document multiple repositories in one pass when pointed at a higher-level scan root
- it prefers repository-local `wiki\` folders so outputs stay close to source
- it records every fallback explicitly instead of silently redirecting output
- it produces a second aggregate knowledge base (`megawiki`) for shared concepts across repositories

## See also

- [Hosts and User Interfaces](Hosts-and-User-Interfaces.md)
- [Storage, Run Artifacts, and State](Storage-and-Run-Artifacts-and-State.md)
- [Configuration and Models](Configuration-and-Models.md)
