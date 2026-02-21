from dataclasses import dataclass


@dataclass
class FrontendAgent:
    model: str

    def plan(self, task_prompt, context_files):
        target_files = context_files[:5]
        return {
            "task": task_prompt,
            "components": ["feature-shell", "state-handler", "validation"],
            "filesToTouch": target_files,
            "acceptanceCriteria": ["Implements requested behavior", "Handles edge cases", "Maintains accessibility"],
            "edgeCases": ["Empty data state", "Validation error paths"],
        }


@dataclass
class BuilderAgent:
    model: str

    def implement(self, plan, review_actions=None):
        return {
            "implementedFromPlan": True,
            "filesTouched": plan.get("filesToTouch", []),
            "appliedActions": review_actions or [],
            "notes": "Apply repo-specific implementation updates according to plan and review actions.",
        }


@dataclass
class ArchitectureAgent:
    model: str

    def review(self, diff_text, files_touched, applied_actions=None):
        findings = []
        if "TODO" in diff_text and "Remove TODO markers and complete implementation details." not in (applied_actions or []):
            findings.append(
                {
                    "severity": "high",
                    "rule": "Structural quality",
                    "file": files_touched[0] if files_touched else None,
                    "line": None,
                    "symbol": None,
                    "rationale": "TODO markers indicate unfinished implementation.",
                }
            )
        required_actions = [
            "Remove TODO markers and complete implementation details."
        ] if findings else []
        return {"findings": findings, "requiredActions": required_actions}
