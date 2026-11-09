# run.json

Every `iacagent generate` run writes `run.json` in the output directory, including failed runs.

```json
{
  "schemaVersion": 1,
  "runId": "guid",
  "startedAtUtc": "2026-10-26T13:00:00.000Z",
  "durationMs": 0,
  "profile": "trio",
  "prompt": "text",
  "models": { "strong": "id", "cheap": "id", "provider": "openrouter" },
  "tokens": { "input": 0, "output": 0 },
  "iterations": [
    { "index": 1, "gates": [ { "name": "validate", "passed": true, "issueCount": 0 } ], "tokens": { "input": 0, "output": 0 } }
  ],
  "assumptions": [ "text" ],
  "outcome": "Succeeded"
}
```

| Field | Meaning |
|---|---|
| schemaVersion | Always `1` for this document. |
| runId | A new GUID for the run. |
| startedAtUtc | UTC start time in ISO-8601. |
| durationMs | Elapsed time until `run.json` is written. |
| profile | The profile name that was requested. |
| prompt | The user prompt. |
| models.strong | Strong-tier model id from configuration. Empty when unset. |
| models.cheap | Cheap-tier model id from configuration. Empty when unset. |
| models.provider | `openrouter` when no endpoint is set. `local` when `--endpoint` or `IACAGENT_ENDPOINT` is set. |
| tokens | Sum of iteration tokens. Phase 1 writes zeros because it does not call a model. |
| iterations | One entry per agent-and-gate pass. `gates` is empty until gates are registered. |
| assumptions | Notes collected from agents. |
| outcome | `Succeeded`, `GateFailed`, `BudgetExceeded`, `NeedsHuman`, or `ConfigError`. `BudgetExceeded` means the Infracost estimate is above `--budget`. A token limit stops as `GateFailed` unless every remaining issue is that budget rule. The readable explanation is in `escalation.md`. |

Secret values are not written. The OpenRouter API key is read only from `OPENROUTER_API_KEY` and is not a field in this file.
