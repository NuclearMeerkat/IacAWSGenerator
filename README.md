# IacAgent

Command-line tool that generates AWS Terraform from a prompt. Agents propose the code. Fixed gates accept or reject it. The tool never runs `terraform apply` or `terraform destroy`.

`generate` runs the trio profile: an orchestrator, a developer, and a security reviewer. OpenRouter is the default provider. A run uses a local model only when you pass `--endpoint`. The host retries while gates fail, then writes `run.json` and, on failure, `escalation.md`.

## Prerequisites

- .NET SDK 10
- Terraform and Checkov on `PATH` for `doctor`
- `OPENROUTER_API_KEY` when you stay on OpenRouter
- Infracost is reported by `doctor` and is optional until budget checks exist

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
| `--max-retries` | `IACAGENT_MAX_RETRIES` | Repair limit. Default `3`. |
| `--budget` | `IACAGENT_BUDGET` | Maximum estimated cost in USD per month. Omit it to skip Infracost. |
| `--auto-approve` | `IACAGENT_AUTO_APPROVE` | Skip interactive questions. |
| `--verbose` | `IACAGENT_VERBOSE` | Debug logs. |

The API key is only `OPENROUTER_API_KEY`. It is not read from the JSON file or from a flag.

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

Exit codes: `0` success, `10` gate failure, `11` budget exceeded, `12` needs a human, `20` configuration or a missing prerequisite, `1` unexpected error.

## Spike

`spikes/IacAgent.Spike` is the Phase 0 probe and is not part of `IacAgent.sln`. It can call OpenRouter and Ollama in one run. The `iacagent` command does not.
