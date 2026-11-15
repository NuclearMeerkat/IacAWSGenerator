# IacAgent

Command-line tool that generates AWS Terraform from a prompt. Agents propose the code. Fixed gates accept or reject it. The tool never runs `terraform apply` or `terraform destroy`.

`generate` runs the trio profile: an orchestrator, a developer, and a security reviewer. OpenRouter is the default provider. A run uses a local model only when you pass `--endpoint`. The host retries while gates fail, then writes `run.json` and, on failure, `escalation.md`.

## Prerequisites

- .NET SDK 10
- Terraform and Checkov on `PATH`. `generate` and `doctor` need both.
- Infracost on `PATH` when you pass `--budget`. `doctor` reports it either way.
- `OPENROUTER_API_KEY` when you stay on OpenRouter. A local model needs `--endpoint` instead.

A pip install of Checkov on Windows puts `checkov.cmd` in the Python `Scripts` directory. That directory has to be on `PATH`.

## Configuration

Command flags override environment variables, which override `iacagent.json` in the current directory.

| Flag | Environment variable | Meaning |
|---|---|---|
| `--profile` | `IACAGENT_PROFILE` | `solo`, `trio`, or `full`. Default `trio`. |
| `--out` | `IACAGENT_OUT` | Output directory. Default `./out`. |
| `--model-strong` | `IACAGENT_MODEL_STRONG` | Strong-tier model id. |
| `--model-cheap` | `IACAGENT_MODEL_CHEAP` | Cheap-tier model id. |
| `--endpoint` | `IACAGENT_ENDPOINT` | Local model server. Omit it to use OpenRouter. |
| `--max-retries` | `IACAGENT_MAX_RETRIES` | Repair limit. Uses the profile value, usually `3`, when omitted. |
| `--max-tokens` | `IACAGENT_MAX_TOKENS` | Token limit for the run. Uses the profile value when omitted. |
| `--security` | `IACAGENT_SECURITY` | Checkov policy: `baseline` or `strict`. Default `baseline`. |
| `--temperature` | `IACAGENT_TEMPERATURE` | Model temperature from `0` to `2`, or `none`. Default `0.2`. |
| `--budget` | `IACAGENT_BUDGET` | Maximum estimated cost in USD per month. Omit it to skip Infracost. |
| `--auto-approve` | `IACAGENT_AUTO_APPROVE` | Skip interactive questions. |
| `--verbose` | `IACAGENT_VERBOSE` | Debug logs. |

The API key is only `OPENROUTER_API_KEY`. It is not read from the JSON file or from a flag.

`pricing` in `iacagent.json` is USD per one million tokens, keyed by the exact model id. A run uses it when the provider does not report a cost. A model missing from the table leaves `costUsd` out of `run.json`. A negative or missing rate is a configuration error (exit code 20).

```json
{
  "pricing": {
    "provider/model": {
      "inputPerMillion": 0.2,
      "outputPerMillion": 0.8
    }
  }
}
```

## Commands

```text
dotnet run --project src/IacAgent.Cli -- doctor
dotnet run --project src/IacAgent.Cli -- generate "API Gateway with a Lambda" --profile trio --out ./out
```

`doctor` exits with code `20` when Terraform, Checkov, or model access is missing, and it prints a fix for each problem. `generate` writes `run.json`. The schema is in [docs/run-json.md](docs/run-json.md).

```text
dotnet run --project src/IacAgent.Cli -- review --plan plan.json
```

`review` redacts the plan, runs Checkov on the Terraform next to it (`--source` overrides that directory), and asks the cheap model to explain the findings. A secret in the plan is replaced with `[redacted]` before it reaches the model or the console.

```text
dotnet run --project src/IacAgent.Cli -- bench --suite benchmarks/hello-suite.json
```

`bench` runs every combination in the suite, one at a time, and writes `results.csv`. It exits `0` when the suite finishes, including when individual runs fail. `--resume <folder>` continues a suite and skips a combination that already has `run.json`. An invalid suite file exits `20`.

A task may set `expect`: a list of groups of resource types. The final run meets it when every group contains at least one type that appears in `run.json` `resources.types`. `results.csv` then has `intentMet` (`true`, `false`, or empty when `expect` is omitted) and `missingGroups` (the failing groups, joined with `|`). This column does not change the gate outcome.

`maxRetries`, for example `[0, 3]`, is another combination. It is a folder after the security level and a `maxRetries` column. Each value is the repair limit for that run. When the list is omitted, the column is empty and the profile's own limit is used. `0` records the first attempt, before any repair. `runTimeoutMinutes` defaults to `15`. A run that exceeds it is `Cancelled` with the error `timed out after N minutes`, `run.json` is still written, and the suite continues.

Exit codes: `0` success, `10` gate failure, `11` budget exceeded, `12` needs a human, `20` configuration or a missing prerequisite, `1` unexpected error.

## Extending

Register the new type from `IacAgent.Cli`. Do not edit Host or Core for these.

| To add | Do this | Do not edit |
|---|---|---|
| An agent | Implement `IAgent`, add its prompt file, and register it. | Host, Core |
| A gate | Implement `IGate` with a unique `Order` and register it. | Host, Core, other gates |
| A profile | Implement `IProfileDefinition` and register it. Select it with `--profile <name>`. | Host, Core, `ProfileCatalog` |
| A model provider | Add a plugin whose `AddXxx` method registers an `IModelRouter`. | Host, Core, other plugins |
| A redaction rule | Implement `IRedactionRule` and register it. | Host, Core |

## Spike

`spikes/IacAgent.Spike` is the Phase 0 probe and is not part of `IacAgent.sln`. It can call OpenRouter and Ollama in one run. The `iacagent` command does not.
