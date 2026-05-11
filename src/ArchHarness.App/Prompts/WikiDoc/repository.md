Inspect the repository and write thorough, multi-page wiki documentation suitable for Azure DevOps wiki.

Steps:
1. Read and analyze the repository thoroughly using available read tools.
   Examine project files, source code, configuration, scripts, READMEs, and directory structure.
2. Write multiple documentation pages to the output directory at {{OutputTarget}}.
   Create sub-pages as individual .md files, one per significant topic you discover.
   Thoroughly document the solution and any other aspects that an operator, developer,
   or new team member would need to understand.
   Each sub-page should be a deep-dive into its topic with concrete facts from the source code.
   You may create as many or as few sub-pages as the repository warrants.
3. Write Home.md as an INDEX page:
   - Repository title and a concise summary paragraph.
   - A table of contents with relative links to every sub-page you wrote (e.g., [Architecture](Architecture.md)).
   - Do NOT put substantive documentation in Home.md; it is an index only.
4. After writing ALL documentation files, return ONLY a JSON summary index:
{
  "repositoryName": "string",
  "summary": "string",
  "pages": ["string"],
  "concepts": [{"name": "string", "summary": "string"}]
}

Rules:
- Do not ask follow-up questions.
- Write ALL documentation files using file-write tools BEFORE returning the JSON.
- `pages` lists every .md filename you wrote (including Home.md), e.g. ["Home.md", "Architecture.md", "Getting-Started.md"].
- `concepts` should capture reusable cross-repository ideas, bounded to 8 items maximum.
- Prefer concrete facts from the repository over generic advice.
- If the repository is sparse, say that explicitly rather than inventing details; still write at least Home.md.
- Use relative links between pages so the wiki works when published to Azure DevOps.
- The final response text must be ONLY the JSON summary object.

ScanRoot: {{ScanRoot}}
RepositoryRoot: {{RepositoryRoot}}
RepositoryRelativePath: {{RepositoryRelativePath}}
RepositoryDisplayName: {{RepositoryDisplayName}}
OutputTarget: {{OutputTarget}}
