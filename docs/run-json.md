# run.json

Every `iacagent generate` run writes `run.json` in the output directory, including failed runs.

```json
{
  "schemaVersion": 2,
  "runId": "guid",
  "startedAtUtc": "2026-10-26T13:00:00.000Z",
  "durationMs": 0,
  "profile": "trio",
  "prompt": "text",
  "security": "baseline",
  "models": { "strong": "id", "cheap": "id", "provider": "openrouter" },
  "tokens": { "input": 0, "output": 0 },
  "iterations": [
    { "index": 1, "gates": [ { "name": "validate", "passed": true, "issueCount": 0, "blockingCount": 0 } ], "tokens": { "input": 0, "output": 0 } }
  ],
  "files": [ "main.tf" ],
  "assumptions": [ "text" ],
  "outcome": "Succeeded",
  "error": null
}
```

| Field | Meaning |
|---|---|
| schemaVersion | Always `2` for this document. |
| runId | A new GUID for the run. |
| startedAtUtc | UTC start time in ISO-8601. |
| durationMs | Elapsed time until `run.json` is written. |
| profile | The profile name that was requested. |
| prompt | The user prompt, with secrets redacted. |
| security | `baseline` or `strict`. `baseline` blocks only the host's short check list. |
| models.strong | Strong-tier model id from configuration. Empty when unset. |
| models.cheap | Cheap-tier model id that the run used. When the cheap id is unset, this is the strong model id. |
| models.provider | `openrouter` when no endpoint is set. `local` when `--endpoint` or `IACAGENT_ENDPOINT` is set. |
| tokens | Sum of iteration tokens. Phase 1 writes zeros because it does not call a model. |
| iterations | One entry per agent-and-gate pass. `issueCount` is every finding. `blockingCount` is the findings that can fail the gate. |
| files | Relative paths written by this run. The next run into the same output directory deletes only these paths. |
| assumptions | Notes collected from agents. |
| outcome | `Succeeded`, `GateFailed`, `BudgetExceeded`, `NeedsHuman`, `ConfigError`, `Error`, or `Cancelled`. `BudgetExceeded` means the Infracost estimate is above `--budget`. A token limit stops as `GateFailed` unless every remaining issue is that budget rule. `Error` and `Cancelled` are written when the run throws or the caller cancels, and the exception still propagates. The readable explanation of a gate stop is in `escalation.md`. |
| error | First line of the exception message when the outcome is `Error` or `Cancelled`. Omitted otherwise. Secrets are redacted. |

Secret values are not written. The OpenRouter API key is read only from `OPENROUTER_API_KEY` and is not a field in this file.
