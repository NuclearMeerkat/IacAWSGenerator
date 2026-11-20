# AGENTS.md — IacAgent

Instructions for the AI coding assistant working on this repository. Read this whole file before writing code. Follow it literally. When something is unclear or missing, ask the user instead of guessing.

The file has three parts:

- **Sections 1–9: the product and the rules.** These stay true for every task.
- **Section 10: the current state.** What already exists and works.
- **Section 11: the plan.** The next steps, in order. Work through them one at a time.

---

## 1. What we are building

`IacAgent` is an open-source .NET 10 command-line tool that generates AWS infrastructure code (Terraform) from a text prompt using LLM agents, and only accepts the result if fixed, deterministic checks ("gates") pass.

Core idea: **the LLM proposes, the gates decide.** Agents write and repair code. Gates (formatting, validation, security scan, cost check) are ordinary tools that no agent setup can skip.

This is also a master's thesis project. The research question is:

> When the gates are fixed and cannot be bypassed, does a multi-agent setup (trio, full) produce better Terraform than a single agent (solo), and at what cost in tokens, money, and time?

To answer it, the tool records data about every run (`run.json`) and will get a benchmark runner (section 11, step 12) that runs a suite of prompts across profiles, models, and temperatures. "Better" means: passes the gates, has no blocking security findings, stays within budget, and does **not over-engineer** (no resources the prompt did not ask for).

### Decisions already made (do not change without asking)

| Topic | Decision |
|---|---|
| Language / platform | C# 14, .NET 10 |
| IaC tool | Terraform. OpenTofu is an optional extra later. |
| Cloud | AWS only. Do NOT build multi-cloud abstractions. |
| Model access | OpenRouter is the default provider. Local models (Ollama or any OpenAI-compatible server) also work. |
| LLM abstraction | `Microsoft.Extensions.AI` (`IChatClient`). Do NOT use Semantic Kernel. |
| Agent orchestration | Our own small orchestrator. Do NOT add an agent framework unless the user asks. |
| CLI | `System.CommandLine` + `Spectre.Console` |
| Native AOT | Optional goal, not required. Do not break the normal build for it. See section 9. |
| Tools used by gates | `terraform`, `checkov`, `infracost` (all free) |
| Safety | The tool never runs `terraform apply` or `terraform destroy`. |
| Security policy | Checkov findings are **blocking** or **advisory**. The host-owned policy decides, not Checkov's severity (free Checkov leaves severity empty). Levels: `baseline` (default, a short list of blocking checks) and `strict` (every failed check blocks except a skip list). See section 7. |
| Over-engineering | A blocking check must be fixable by changing an attribute of an existing resource. Checks that can only be fixed by adding infrastructure (VPC, KMS key, code signing, DLQ, X-Ray) are advisory under `baseline`. |
| Temperature | Configurable per run and recorded in `run.json`. Default `0.2`. It is also a benchmark variable. |

---

## 2. Hard rules

You MUST:

1. Keep agents and gates separate. Agents are pluggable; gates belong to the host and cannot be removed by any profile or user setting.
2. Return gate failures to the Developer agent as **structured errors** (file, line, rule id, message), never as a raw text dump. Send the Developer **only blocking issues**.
3. Put every external effect (running a process, calling a model, reading the environment) behind an interface so it can be faked in tests.
4. Write a unit test for every agent, gate, and parser you add or change. Agents are tested with a fake `IChatClient`.
5. Run `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes` after every change set and fix failures before moving on.
6. Keep each step small and finish it: one plan step at a time, working and tested before the next.
7. Read secrets only from environment variables. Redact them from prompts, logs, `run.json`, `escalation.md`, and `review.md`.
8. Verify NuGet package names and current stable versions before adding a dependency. Do not invent package names or versions.
9. Verify every Checkov check ID you put in code against the installed Checkov (`checkov --list --framework terraform`). Do not write check IDs from memory.

You MUST NOT:

1. Run or implement `terraform apply` / `terraform destroy`, or require real cloud credentials for any gate.
2. Add new dependencies (NuGet packages or external tools) without asking the user first. The packages in section 4 are pre-approved.
3. Create abstractions for clouds, IaC tools, or plugin systems beyond the interfaces in section 5 and the extension points in section 2A.
4. Pass whole Terraform projects between agents when a diff or the failing files are enough (see section 7).
5. Let the retry loop run without limits.
6. Rewrite or reformat files you were not asked to change.
7. Hard-code model names. They come from configuration.
8. Let generated code weaken a gate. Inline `checkov:skip` comments written by a model do not suppress a blocking check.

---

## 2A. Clean code and extensibility

The architecture must be clean, easy to read, and easy to extend. Customization by a developer is a core feature of this project. Follow these rules in every file.

### The test for good architecture

**Extend by adding, not editing.** Adding a new agent, gate, model provider, profile, or redaction rule MUST be possible by writing one new class and one DI registration line, without editing `IacAgent.Host` or `IacAgent.Core`. If a new feature of one of these kinds forces you to edit the host, stop and tell the user: the design has a flaw to fix first.

### The only extension points

| Extension point | Interface | Where it lives | Registered how |
|---|---|---|---|
| Agent | `IAgent` | `Plugins.Agents` or a new plugin project | `services.AddSingleton<IAgent, MyAgent>()` or the plugin's `AddXxx` method |
| Gate | `IGate` | `Plugins.Gates` or a new plugin project | `services.AddSingleton<IGate, MyGate>()` |
| Profile | `IProfileDefinition` | any project | `services.AddSingleton<IProfileDefinition, MyProfile>()` |
| Model provider | `IModelRouter` | `Plugins.Models` | `AddXxx` method of that plugin |
| Redaction rule | `IRedactionRule` | `Core` interface, any project implements | `services.AddSingleton<IRedactionRule, MyRule>()` |

`ProfileCatalog` collects all registered `IProfileDefinition` instances by name. It never contains a hard-coded list of profiles.

### Plugin structure

1. Each plugin project exposes exactly one public entry point: an extension method `AddXxx(this IServiceCollection services, IConfiguration configuration)`. Everything else in the project is `internal` unless another project must use it.
2. `IacAgent.Cli` is the composition root: it calls the `AddXxx` methods. It is the only place that knows which plugins exist.
3. Plugin settings are strongly typed `XxxOptions` classes, bound from configuration and validated at startup (fail fast with exit code 20 and a clear message).

### Code rules

1. **Single responsibility.** One class, one reason to change. If you need "and" to describe a class, split it.
2. **Small methods.** A method usually fits on one screen (about 30 lines). Extract well-named helpers when it grows.
3. **Names reveal intent.** Full words, no abbreviations except well-known ones (`Id`, `Url`). Methods are verbs, types are nouns, booleans read as questions (`IsValid`, `HasIssues`).
4. **Comments explain why, not what.** Do not restate the code. Public types get short XML doc comments.
5. **Dependency injection only.** Constructor injection. No static mutable state, no service locator, no `new` for services that have an interface. No optional constructor parameters that exist only for tests.
6. **Immutability by default.** Use `record` types and `init` properties for data. Mutable state stays inside a class and is not shared.
7. **No magic values.** Constants, enums, or options instead of repeated strings and numbers (tool names, exit codes, file names, gate names).
8. **Expected failures are results, not exceptions.** A failed gate is a `GateResult`. Exceptions are for unexpected situations. Never swallow an exception silently. Always pass `CancellationToken` through async calls.
9. **Async all the way.** Never use `.Result` or `.Wait()`.
10. **Composition over inheritance.** Avoid base classes unless two or more real subclasses share real logic.
11. **Rule of three.** Do not extract a shared abstraction until the same code appears three times.
12. **`Core` has no logic.** Only interfaces, records, enums, and constants.
13. **Smallest public surface.** Default to `internal`; make something `public` only when another project needs it.
14. **Folders by feature,** not by technical type, inside each project (for example `Checkov/`, `Validate/`).

### Keep it simple: over-engineering is a defect

Over-engineering is as bad as messy code. Extensibility applies ONLY to the five extension points above. Everywhere else, choose the simplest design that works and is easy to read.

Before adding any new type, interface, layer, or setting, ask: "What gets worse or harder to read if I do not add this?" If the answer is "nothing", do not add it.

Do NOT:

- Add an interface with one implementation that is neither an extension point, a core interface (section 5), nor needed to fake something in tests.
- Add abstract base classes with one subclass, or generic classes with one concrete use.
- Add classes that only forward calls to another class.
- Add factories of factories, builders for simple objects, or layers "for later".
- Use design patterns for their own sake (Strategy, Visitor, Mediator, CQRS, event buses, repositories) unless the user asks for one.
- Add settings for things nobody asked to configure.
- Use reflection, assembly scanning, or other "magic" to register or find things. Register services explicitly.

### Easy to navigate

1. One public type per file; the file name equals the type name.
2. Predictable names that show the role: `CheckovGate`, `CheckovOutputParser`, `TrioProfile`, `CheckovOptions`.
3. Keep folders shallow: at most 3 levels inside a project.
4. No dumping-ground names: no `Utils`, `Helpers`, `Common`, `Manager`, or `Misc`.
5. Aim for files under about 300 lines. Split by responsibility when a file grows beyond that.
6. Entry methods read like a short summary of the steps, each step a well-named method.
7. Each plugin folder has a short `README.md` (5 to 10 lines): what the plugin does, its key types, and how to extend it.

### Tooling rules

- `Directory.Build.props`: `Nullable` enabled, `TreatWarningsAsErrors` true, latest analyzers.
- A committed `.editorconfig`; `dotnet format --verify-no-changes` must pass.
- Tests are named `Method_Scenario_ExpectedResult` and mirror the source folder structure.

### Extension recipes (keep these working and documented in README.md)

| To add... | Do this | Files you must NOT edit |
|---|---|---|
| A new agent | Implement `IAgent`, add its prompt file, register it | Host, Core |
| A new gate | Implement `IGate` with a unique `Order`, register it | Host, Core, other gates |
| A new profile | Implement `IProfileDefinition`, register it; select with `--profile <name>` | Host, Core, `ProfileCatalog` |
| A new model provider | Add a plugin project with an `AddXxx` method that registers an `IModelRouter` | Host, Core, other plugins |
| A new secret-redaction rule | Implement `IRedactionRule`, register it | Host, Core |

`RepairLoopTests.RunAsync_FakeProfileAndGate_RunThroughHost` proves these recipes work. Keep it passing.

---

## 3. Repository layout

```
src/
  IacAgent.Core/               interfaces, records, enums, constants; only Microsoft.Extensions.AI.Abstractions
  IacAgent.Host/               pipeline runner, repair loop, profile catalog, run recorder, doctor, plan review, redaction rules
  IacAgent.Plugins.Agents/     orchestrator, developer, security reviewer, solo, architect, FinOps agents + Prompts/*.md
  IacAgent.Plugins.Models/     OpenRouter, Ollama, and OpenAI-compatible model router
  IacAgent.Plugins.Terraform/  process runner
  IacAgent.Plugins.Gates/      Validate/ (fmt + init + validate), Checkov/, Infracost/
  IacAgent.Cli/                commands, settings loading, DI setup, console output
tests/
  IacAgent.Core.Tests/
  IacAgent.Host.Tests/
  IacAgent.Plugins.Agents.Tests/
  IacAgent.Plugins.Gates.Tests/
  fixtures/                    small Terraform projects
spikes/IacAgent.Spike/         Phase 0 probe; not part of the solution
docs/
  run-json.md                  schema of run.json
  aot-notes.md                 Native AOT result
  observations.md              findings from real runs (for the thesis)
AGENTS.md
README.md
```

Dependency direction: `Cli` → `Host` → `Core`; plugins → `Core`. `Core` depends on nothing of ours. Plugins never reference each other.

---

## 4. Pre-approved packages

Use the latest stable versions (verify on NuGet):

- `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Options`
- `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.Abstractions`
- `Microsoft.Extensions.AI.OpenAI` (OpenRouter and any OpenAI-compatible endpoint, with a custom base URL)
- `OllamaSharp`
- `System.CommandLine`
- `Spectre.Console`
- Tests: `xunit` (currently 2.9.x), `Verify.Xunit` for snapshot tests

OpenRouter base URL: `https://openrouter.ai/api/v1`. API key from env var `OPENROUTER_API_KEY`.

---

## 5. Core contracts (as they are now)

`IacAgent.Core` contains these types. Section 11 changes some of them; each such change is listed in its step.

```csharp
public enum ModelTier { Strong, Cheap }
public enum AgentRole { Orchestrator, Advisor, Developer, Reviewer } // the host runs agents by role, in this order

public interface IAgent
{
    string Name { get; }
    AgentRole Role { get; }
    Task<AgentResult> RunAsync(AgentContext context, CancellationToken ct);
}

public sealed record AgentContext(string Prompt, bool AutoApprove, string? RequirementsJson = null,
    Artifact? Artifact = null, IReadOnlyList<GateResult>? GateResults = null);

public sealed record AgentResult(IReadOnlyList<string> Assumptions, string? RequirementsJson = null,
    IReadOnlyList<GeneratedFile>? Files = null, string? Review = null, TokenCount? Tokens = null,
    bool Failed = false, string? FailureMessage = null, bool NeedsHuman = false);

public interface IGate
{
    string Name { get; }
    int Order { get; } // gates run in ascending order
    Task<GateResult> CheckAsync(Artifact artifact, CancellationToken ct);
}

public interface IModelRouter
{
    IChatClient GetClient(ModelTier tier);
    string GetModelId(ModelTier tier); // includes the strong-model fallback for the cheap tier
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, CancellationToken ct);
}

public interface IRunRecorder
{
    void RecordIteration(IterationRecord record);
    Task WriteAsync(string outputDirectory, RunOutcome outcome, CancellationToken ct);
}

public interface IUserInteraction { Task<string?> AskAsync(string question, CancellationToken ct); } // null = must not prompt
public interface IProfileDefinition { string Name { get; } PipelineProfile Build(IServiceProvider services); }
public interface IRedactionRule { string Redact(string text); }

public sealed record Artifact(string Directory, IReadOnlyList<GeneratedFile> Files);
public sealed record GeneratedFile(string RelativePath, string Content);
public sealed record GateIssue(string File, int? Line, string RuleId, string Severity, string Message);
public sealed record GateResult(string GateName, bool Passed, IReadOnlyList<GateIssue> Issues);
public sealed record GateSummary(string Name, bool Passed, int IssueCount);
public sealed record IterationRecord(int Index, IReadOnlyList<GateSummary> Gates, TokenCount Tokens);
public sealed record TokenCount(int Input, int Output);
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
public sealed record LoopPolicy(int MaxRepairIterations = 3, int? MaxTotalTokens = null);
public sealed record PipelineProfile(string Name, IReadOnlyList<IAgent> Agents, LoopPolicy Loop); // gates are NOT part of the profile
public enum RunOutcome { Succeeded, GateFailed, BudgetExceeded, NeedsHuman, ConfigError }
public static class KnownGateRules { public const string BudgetExceeded = "budget-exceeded"; }
```

Gates are registered with DI as `IEnumerable<IGate>` and executed by the host. A profile cannot add, remove, or reorder them.

---

## 6. CLI

Executable name: `iacagent`.

| Command | Status | Behavior |
|---|---|---|
| `iacagent generate "<prompt>"` | done | Runs the profile pipeline, writes Terraform files to `--out` (default `./out`). |
| `iacagent doctor` | done | Checks `terraform`, `checkov`, `infracost`, the API key, and the model endpoint. Prints a fix for each problem. Exit code 20 if something required is missing. |
| `iacagent review --plan plan.json [--source dir]` | done | Reads `terraform show -json` output, redacts secrets, runs Checkov on the source, asks the cheap model to explain findings. |
| `iacagent bench --suite <file>` | plan step 12 | Runs a benchmark suite and writes one CSV row per run. |

`generate` options (existing): `--profile solo|trio|full` (default `trio`), `--out <dir>`, `--model-strong <id>`, `--model-cheap <id>`, `--endpoint <url>`, `--budget <usd-per-month>`, `--max-retries <n>`, `--auto-approve`, `--verbose`.

`generate` options added by the plan: `--security baseline|strict` (step 5), `--temperature <0..2>` (step 7), `--max-tokens <n>` (step 2).

Ollama is detected when the endpoint port is `11434` or the host name contains `ollama` (`ModelRouter.IsOllama`).

Configuration precedence: CLI flags > environment variables (`IACAGENT_*`) > `iacagent.json` in the current directory. The API key is read only from `OPENROUTER_API_KEY`.

Exit codes: `0` success, `10` gate failure after retries, `11` budget exceeded, `12` needs human decision, `20` configuration or missing prerequisite, `1` unexpected error.

Interactive questions (Spectre.Console prompts) MUST be skipped when `--auto-approve` is set or when input is not a terminal.

---

## 7. How the pipeline works

### Flow for `generate`

1. Load config and select the profile.
2. **Orchestrator** agents turn the prompt into structured requirements (JSON). They may ask clarifying questions unless `--auto-approve` is set or input is not a terminal. Each answer is sent back, and the model rewrites the requirements. An answer that asks for advice gets at most three sentences and one confirming question. The dialogue stops when nothing is open, every answer in a round is empty, prompting is impossible, or after 3 follow-up rounds. The last round tells the model to choose defaults. If an orchestrator fails, the run stops with `GateFailed` and `escalation.md`.
3. **Advisor** agents (full profile: Architect, FinOps) add design notes once.
4. **Developer** produces Terraform files as JSON: `{"files":[{"path":"main.tf","content":"..."}]}`. Parse strictly; if the JSON is invalid, ask the model once to fix the format, then fail the iteration.
5. Files are written to the working directory. On a repair, returned files are merged over the previous set by path.
6. The host runs all gates in `Order`:
   1. `validate` (10): `terraform fmt -check -recursive`, `terraform init -backend=false -input=false`, `terraform validate -json`
   2. `checkov` (20): `checkov -d . --framework terraform -o json --quiet`
   3. `infracost` (30): only when `--budget` is set; compares the monthly estimate with the budget
7. If every gate passes, the **Reviewer** agent (trio/full) summarizes the advisory findings into `review.md`. It does not decide pass or fail.
8. If a gate fails and iterations remain, send the Developer **only**: the blocking issues and the content of the files that contain them. Then go to step 5.
9. Stop when: gates pass, the repair limit is reached, the same set of blocking issues appears twice in a row, or the token limit is hit. On stop without success, write `escalation.md` listing the remaining issues and the last attempt.
10. Always write `run.json`, also when an exception or cancellation ends the run (plan step 1).

### Security policy (target design, plan step 5)

- Checkov always scans everything. Every failed check is returned as a `GateIssue`. The policy marks each issue `IsBlocking` true or false.
- `baseline` (default): an issue blocks only if its check ID is in `CheckovOptions.BlockingChecks`. The default list contains only checks that are a clear security hole **and** can be fixed by changing an attribute of an existing resource: admin ports (SSH, RDP, database ports) open to `0.0.0.0/0`, IAM policies with `*:*`, public S3 ACLs or policies, unencrypted storage where encryption is a flag, publicly accessible databases.
- `strict`: every failed check blocks unless its ID is in `CheckovOptions.StrictSkipChecks`.
- A check that Checkov reports in `skipped_checks` (an inline `checkov:skip` comment) is treated as failed and blocking if the policy would block it.
- Advisory issues go to the Reviewer and `review.md`. They are never sent to the Developer and never change the outcome.

### Profiles

| Profile | Agents |
|---|---|
| `solo` | One Developer agent with a combined prompt (requirements and security hints) |
| `trio` | Orchestrator, Developer, SecurityReviewer (default) |
| `full` | Orchestrator, Architect, FinOps explainer, Developer, SecurityReviewer |

Each profile is an `IProfileDefinition` class. `ProfileCatalog` finds registered definitions by name.

### Model tiers

- `Strong`: Orchestrator, Developer, Solo, Architect.
- `Cheap`: SecurityReviewer, FinOps, plan review. Falls back to the strong model when unset.

### Context rules (important for cost)

- Pass the Developer the full requirements JSON and, on repairs, only the failing files, the blocking issues, and a short list of the other files and the resources they declare.
- Never send `*.tfstate` files or secret values to a model. Redaction runs before every model call that includes file content.

### Agent prompts

- One markdown file per agent under `IacAgent.Plugins.Agents/Prompts/`, embedded as a resource.
- Each prompt states: role, input format, exact output format, and a short list of Terraform-for-AWS rules (least-privilege IAM, no `0.0.0.0/0` on admin ports, encryption on where it is a flag, tags on resources, pinned provider versions, no resources the requirements do not name).
- Keep prompts short and specific. No API keys, model names, or tool paths in prompts.

### run.json

Written to the output directory on every run. The schema is documented in `docs/run-json.md`; keep that file in sync with every change to `RunDocuments.cs`. Plan steps 5–7 add fields and bump `schemaVersion` to `2`.

---

## 8. Testing rules

- **Agents:** tested with a fake `IChatClient` that returns scripted responses. No network, no API key. One test class per agent.
- **Gates:** tested with a fake `IProcessRunner` and saved tool outputs. Tests that need the real `terraform` or `checkov` binary are marked `[Trait("Category","Integration")]` and skipped when the binary is missing. With xunit 2.9 use a small `FactAttribute` subclass that sets `Skip` in its constructor when the tool is not on `PATH`.
- **Parsers:** `terraform validate -json`, Checkov JSON, and Infracost JSON each have their own test class using saved sample outputs under a `Samples/` folder in the test project.
- **Host:** tested with fake agents and fake gates: retry limit, repeated-issue stop, token limit, escalation file, `run.json` content, error handling.
- **Snapshots:** use `Verify` for the `run.json` shape and generated file lists.
- **Fixtures:** `tests/fixtures/` has `valid/`, `invalid-syntax/`, and `insecure/` projects.
- A step is not done until `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes` pass.

---

## 9. Native AOT (optional)

The Phase 0 attempt is in `docs/aot-notes.md` (blocked by a missing C++ linker). Do not change the design to force AOT. Keep using `System.Text.Json` source-generated contexts for our own types (`RunJsonContext`).

---

## 10. Current state

Steps 0–12 of section 11 are done; step 13 (Orchestrator clarification dialogue) is open. `generate`, `doctor`, `review`, and `bench` are on `main`. 105 tests pass, the build has no warnings, and `dotnet format` is clean.

Working:

- Profiles `solo`, `trio`, and `full`. Gates are validate, Checkov (`baseline` or `strict`), and Infracost when `--budget` is set.
- `run.json` schema version 2, including a crash or cancellation. Cost comes from `iacagent.json` `pricing` when the provider does not report it.
- The benchmark suite is `benchmarks/hello-suite.json`. The static-website task was replaced with a private versioned bucket, because a public website fails baseline checks that a model can only silence by adding CloudFront.
- The intent-check spike recommends no intent gate. See `docs/intent-check-spike.md`.

Still open:

- Native AOT is blocked by a missing C++ linker. See `docs/aot-notes.md`.
- The price table uses the OpenRouter headline for DeepSeek V4.1 Flash ($0.0507 input and $0.2028 output per million tokens on 15 Nov 2026) and does not price cached input separately.
- A live `bench` run is required before the thesis uses the numbers. Every task has a budget, so Infracost must be logged in.

---

## 11. The plan

### How to work through the plan

- Do the steps in order. Do not start a step before the previous one is done.
- Before each step, write a short plan (3 to 7 lines) in the chat.
- After each step: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, then one commit with a clear message (`fix:`, `feat:`, `refactor:`, `test:`, `docs:`).
- Report in 2 to 3 lines what works and what is not done.
- Reread your diff before committing and remove anything not needed.
- When a step says **Ask the user**, stop and ask before writing code for that part.
- When you change `run.json`, update `docs/run-json.md` in the same commit.
- When you change a flag, update `README.md` in the same commit.

---

### Step 0 — Housekeeping

**Goal:** required docs are tracked; the baseline is green.

1. Remove the `docs/` line from `.gitignore`. Commit `docs/run-json.md`, `docs/aot-notes.md`, `docs/observations.md`.
2. Run build, test, and format. Record the test count in the commit message.

**Done when:** `git ls-files docs` lists the three files.

---

### Step 1 — `run.json` is always written

**Goal:** every run, including crashes and Ctrl+C, leaves a `run.json`. Benchmarks must not lose data.

**Core change:** add `RunOutcome.Error` and `RunOutcome.Cancelled`.

1. In `PipelineRunner.RunAsync`, wrap everything after the recorder is created in `try/catch`:
   - `OperationCanceledException` when the caller's token is cancelled → outcome `Cancelled`.
   - Any other exception → outcome `Error`. Log it with `LogError`, then rethrow after writing.
   - Write `run.json` with `CancellationToken.None` so a cancelled run can still write the file.
2. Add `error` (string, nullable, redacted, first line of the exception message) to `RunDocument`.
3. `ExitCodes.For`: `Error` → `1`, `Cancelled` → `1`.
4. Do not add a retry library. The OpenAI client already retries transient HTTP errors; verify this in the `OpenAI` package docs and write the result in one sentence in `docs/observations.md`. OllamaSharp is local and needs no retry.

**Tests (Host):** an agent that throws → `run.json` exists with outcome `Error` and a redacted `error`; a cancelled token → outcome `Cancelled`; the exception still reaches the caller.

**Done when:** killing a run with Ctrl+C leaves `run.json`.

---

### Step 2 — Split the repair loop and fix its bugs

**Goal:** `PipelineRunner` reads like a summary; the loop's state lives in one place; three loop bugs are fixed.

1. Move the developer-and-gates loop out of `PipelineRunner` into `Pipeline/RepairLoop.cs` (internal). Keep loop state in one private record, for example `RepairState(IReadOnlyList<GeneratedFile> Files, IReadOnlyList<GateResult> LastResults, string? PreviousSignature, int Repairs, TokenCount Tokens)`. Each step of the loop is a named method: `AskDeveloperAsync`, `WriteAndCheckAsync`, `RecordIteration`, `DecideNext`. Target: no method over about 30 lines, no file over 300 lines.
2. **Bug: a bad Developer reply hides the real issues.** When the Developer returns no files on a repair, keep the previous gate results and add the `developer-output` issue to them. The next repair must still see the real gate issues and the same failing files.
3. **Bug: the repeated-issue stop rarely fires.** `IssueSignature` must use only `gate | ruleId | file` (no line, no message). After step 5 it must use blocking issues only.
4. **Bug: two retry limits.** Make `RunSettings.MaxRepairIterations` nullable (null when no layer sets it). The effective limit is the setting when set, otherwise `profile.Loop.MaxRepairIterations`. Do the same for a new `--max-tokens` / `IACAGENT_MAX_TOKENS` / `maxTokens` setting that overrides `profile.Loop.MaxTotalTokens`. Pass the effective `LoopPolicy` in `GenerateRequest` instead of the bare number.
5. Remove the optional constructor parameters of `PipelineRunner` (`IModelRouter? router = null`, `IEnumerable<IRedactionRule>? rules = null`). Make them required and give the tests fakes.

**Tests (Host):** developer fails on a repair → the next context still contains the original gate issues; same issues on different lines → stops as repeated; `--max-retries` overrides the profile; profile value used when unset; `--max-tokens` stops the loop.

**Done when:** `PipelineRunner.cs` and `RepairLoop.cs` are each under 300 lines and all loop tests pass.

---

### Step 3 — A fresh workspace per run

**Goal:** a run never sees files from an older run; reruns are reproducible.

1. Each run works in `Path.Combine(Path.GetTempPath(), "iacagent", runId)`. Gates run there. `Artifact.Directory` is the workspace.
2. At the end of the run (success or failure), copy to `--out`: the generated files, `run.json`, `escalation.md`, `review.md`. Do not copy `.terraform/` or `.terraform.lock.hcl`.
3. Before copying, delete from `--out` only the generated files listed in the previous `run.json` there (new field `files`: list of relative paths written by that run). Never delete other files.
4. Delete the workspace after copying, unless `--verbose` is set (then log its path).
5. In `ProcessRunner`, when the command is `terraform` and `TF_PLUGIN_CACHE_DIR` is not set, set it for the child process to `%LOCALAPPDATA%/iacagent/plugin-cache` (create the folder). This stops provider downloads on every iteration.

**Tests (Host):** a stale `old.tf` in `--out` is not gated; a user file in `--out` that is not in the old `files` list survives; `files` is written to `run.json`.

**Done when:** two consecutive runs into the same `--out` give the same gate results for the same model output.

---

### Step 4 — Formatting does not cost a repair

**Goal:** a formatting problem is fixed deterministically, not by the LLM.

1. In `TerraformFmtValidateGate`, replace `fmt -check` with `terraform fmt -recursive` (which rewrites the files). A non-zero exit still means a syntax error and stays a blocking issue.
2. After the gates run, the host reloads the content of the generated files from the workspace, so repair context, `escalation.md`, and the copied output show the formatted code.
3. Add a sentence to `docs/observations.md`: formatting is a deterministic normalization; the gate still decides.

**Tests (Gates):** fmt exit 0 → no issue; fmt exit non-zero → one blocking `terraform-fmt` issue. **Tests (Host):** reloaded file content is what the next repair receives.

---

### Step 5 — Checkov policy: blocking vs advisory

**Goal:** real security holes block; hardening extras become advice; the Developer stops over-engineering.

**Core change:** `GateIssue` gets a last parameter `bool IsBlocking = true`. All existing gates keep their behavior (their issues are blocking).

1. **Options.** Add `Checkov/CheckovOptions.cs` in `Plugins.Gates`:
   - `SecurityLevel Level` (enum `Baseline`, `Strict`; default `Baseline`)
   - `IReadOnlyList<string> BlockingChecks` (default list in code, see 3)
   - `IReadOnlyList<string> StrictSkipChecks` (default empty)
   Bind it in `AddGates` from the `Checkov` configuration section. Validate at startup: an unknown level is a config error (exit 20).
2. **Configuration.** In `Application.BuildHost`, add `iacagent.json` to `builder.Configuration` (optional), then add an in-memory layer with `Checkov:Level` from the composed settings so the precedence stays CLI > env > file. Add `--security baseline|strict`, `IACAGENT_SECURITY`, and `security` in `iacagent.json`. `RunSettingsComposer.Validate` rejects other values.
3. **Default blocking list.** Run `checkov --list --framework terraform` and pick the AWS checks that are a clear security hole AND fixable by changing an attribute of an existing resource: SSH/RDP/database ports open to `0.0.0.0/0`, IAM `*:*` admin policies, public S3 ACLs and policies, unencrypted EBS/RDS/S3 where it is a flag, publicly accessible RDS. Put the list in `CheckovOptions` with one comment per ID naming the check. **Ask the user** to approve the final list before committing. Do not include: route authorization (`CKV_AWS_309`), port 80 open (`CKV_AWS_260`), Lambda VPC/DLQ/X-Ray/concurrency/code signing, KMS CMK on logs, log retention.
4. **Policy.** Add `Checkov/CheckovPolicy.cs` (internal): `IsBlocking(GateIssue issue)` using the level and lists. Remove `CheckovOutputParser.FailsGate` and the old `OpenAdminPortRules` set (it wrongly contains `CKV_AWS_23`).
5. **Suppressions.** `CheckovOutputParser` also reads `results.skipped_checks`. A skipped check that the policy would block becomes a blocking issue with the message "Suppressed with checkov:skip. Remove the suppression and fix the resource."
6. **Gate.** `CheckovGate` returns all issues with `IsBlocking` set; `Passed` is true when no issue is blocking.
7. **Agents.** `TerraformReply.UserMessage` sends only blocking issues. `SecurityReviewerAgent` reviews only non-blocking issues (replace the severity filter).
8. **Host.** Use `IsBlocking` in `FailingFiles`, `IssueSignature`, the reviewer trigger (replace `IsLowSeverity`), and `EscalationReport` (blocking under "Remaining issues", then one line with the advisory count).
9. **run.json.** `GateSummary` gets `BlockingCount`; `issueCount` stays the total. Add top-level `security` (`baseline` or `strict`). Bump `schemaVersion` to `2`.
10. Update `docs/observations.md` with the reason for this design (the history is already there).

**Tests (Gates):** baseline + unlisted failed check → passes, issue advisory; baseline + listed check → fails; strict → fails; strict + skip-listed → passes; skipped blocking check → fails with the suppression message; unknown level → config error. Use a saved real Checkov output from the hello Lambda under `Samples/`.
**Tests (Agents):** the Developer message contains no advisory issue; the reviewer receives only advisory issues.
**Tests (Host):** advisory-only results → `Succeeded`; signature ignores advisory issues.

**Done when:** the reference prompt with `--security baseline` produces a small design (no VPC, no KMS key, no code signing) and passes; with `--security strict` the advisory findings block.

---

### Step 6 — Measure over-engineering

**Goal:** `run.json` shows how big the design is, so profiles can be compared.

1. Add `Recording/TerraformResourceInventory.cs` in Host: reads the final generated `.tf` files and counts top-level `resource "<type>" "<name>"` blocks with a source-generated regex. Ignore lines inside `#`, `//`, and `/* */` comments. Keep it simple; do not write an HCL parser.
2. Add to `run.json`: `resources: { "count": 5, "types": { "aws_lambda_function": 1, ... } }` for the final attempt, and per iteration `blockingIssues` and `advisoryIssues` totals.

**Tests (Host):** inventory counts resources, ignores commented-out blocks and `data` blocks; `run.json` snapshot with `Verify`.

---

### Step 7 — Reproducibility data

**Goal:** every benchmark run can be explained and repeated.

1. **Temperature.** Add `--temperature`, `IACAGENT_TEMPERATURE`, `temperature` (number 0–2, default `0.2`). Pass it through `ModelConnection`; every agent sends `new ChatOptions { Temperature = ... }` instead of `options: null`. Some reasoning models reject temperature: allow the value `none`, which sends no temperature. Record `models.temperature` in `run.json`.
2. **Versions.** Record `toolVersion` (the CLI's `AssemblyInformationalVersion`; the .NET SDK adds the git commit, which also identifies the embedded prompts) and `tools: { "terraform": "...", "checkov": "..." }`. Read tool versions once per run through `IProcessRunner`, the same way `Doctor` does; move the shared version reading into one internal class used by both.
3. **Gate timing.** Measure each gate in the host. `GateSummary` gets `DurationMs` (Core change). Record it per gate per iteration.
4. **Cost.** Check whether OpenRouter's usage cost is available through `ChatResponse.Usage.AdditionalCounts` or `RawRepresentation`. If yes, record `costUsd` per iteration and in total. If not, **ask the user** whether to skip it or use a price table in configuration.

**Tests:** temperature reaches the fake `IChatClient`; `none` sends null; `run.json` snapshot contains the new fields.

---

### Step 8 — Better repair context

**Goal:** the Developer can fix cross-file errors without receiving the whole project.

1. **Core change:** add `IReadOnlyList<string>? OtherFiles` to `AgentContext`. On a repair the host fills it with one line per non-failing file: its path and the `type.name` of the resources it declares (from `TerraformResourceInventory` in Host; plugins cannot reference Host, so the host computes it). `TerraformReply.UserMessage` prints it as a short "Other files" section, without file content.
2. **Core change:** add `string? DesignNotes` to `AgentContext`. Advisor output goes there, not into `RequirementsJson`. The Developer message shows it as a separate "Design notes" section.

**Tests (Agents):** the repair message lists other files and resources but not their content; design notes are a separate section.

---

### Step 9 — Small fixes and docs

1. `TerraformValidateOutputParser`: keep the relative path (`modules/x/main.tf`), normalized to `/`, instead of `Path.GetFileName`.
2. Plugin `README.md` files: 5 to 10 lines each (what it does, key types, how to extend).
3. `README.md`: update prerequisites, the flag table (`--security`, `--temperature`, `--max-tokens`), and the extension recipes.

**Tests (Gates):** a diagnostic in a subfolder keeps its folder.

---

### Step 10 — Close the test gaps

1. One test class per agent: `OrchestratorAgentTests`, `DeveloperAgentTests`, `SecurityReviewerAgentTests`, `SoloDeveloperAgentTests`, `ArchitectAgentTests`, `FinOpsExplainerAgentTests`, in folders that mirror the source (`Trio/`, `Solo/`, `Full/`).
2. Split `GatesServiceCollectionExtensionsTests` into `Checkov/CheckovGateTests`, `Checkov/CheckovOutputParserTests`, `Validate/TerraformFmtValidateGateTests`, `Validate/TerraformValidateOutputParserTests`, `Infracost/InfracostGateTests`, `Infracost/InfracostOutputParserTests`. Keep one registration test.
3. Saved samples under `Samples/`: real `terraform validate -json` output (valid and invalid), real Checkov output, real Infracost output. Copy the spike samples as a start.
4. Fixtures: add `tests/fixtures/valid/` and `tests/fixtures/invalid-syntax/`.
5. Integration tests (`[Trait("Category","Integration")]`, skipped when the tool is missing): validate gate on `valid/` passes and on `invalid-syntax/` fails; Checkov gate on `insecure/` blocks.
6. Add `Verify.Xunit` and snapshot `run.json` (with run id, time, and duration scrubbed).

**Done when:** every agent, gate, and parser has its own test class.

---

### Step 11 — Spike: intent check (research only, no gate)

**Goal:** find out whether we can check that the code builds **what was asked**, not only that it is valid and safe. Write findings to `docs/intent-check-spike.md`. Do not add production code.

1. Can `terraform plan` run offline for `tests/fixtures/valid/` with fake credentials and the AWS provider settings `skip_credentials_validation`, `skip_requesting_account_id`, `skip_metadata_api_check`? Does it make network calls? Which resource types fail?
2. Can the plan JSON (`terraform show -json`) be checked with a few simple rules, such as "exactly one `aws_lambda_function` with runtime `python3.*`" and "one `aws_apigatewayv2_api`"? Compare Rego with OPA/conftest against plain C# checks. **Ask the user** before installing OPA or conftest.
3. Look at the IaC-Eval dataset (Kon et al., 2024): format, license, how many AWS tasks, whether its Rego intent rules could be reused for the benchmark suite.
4. Note the conflict with hard rule "no real cloud credentials" (fake credentials are not real ones) and recommend a decision for the user.

**Done when:** the doc answers the four questions and recommends yes or no for an intent gate.

---

### Step 12 — Benchmark runner

**Goal:** `iacagent bench` answers the research question from section 1.

1. **Suite file** (`benchmarks/*.json`):
   ```json
   {
     "name": "hello-suite",
     "repeat": 3,
     "profiles": ["solo", "trio", "full"],
     "models": [{ "strong": "id", "cheap": "id" }],
     "temperatures": [0, 0.5, 1.0],
     "security": ["baseline"],
     "tasks": [{ "id": "hello-lambda", "prompt": "API Gateway with a Lambda function in Python that returns hello", "budget": 10 }]
   }
   ```
2. **Runner.** Add `Benchmark/BenchmarkRunner.cs` in Host. For every combination of task × profile × model × temperature × security × repeat, call `PipelineRunner` with `AutoApprove = true` and the output directory `bench/<suite>/<timestamp>/<task>/<profile>/<model>/<temperature>/<security>/<repeat>`. Run sequentially (rate limits). A failed or crashed run is recorded and the suite continues.
3. **Resume.** Skip a combination whose `run.json` already exists, so an interrupted suite can continue with `--resume <folder>`.
4. **Results.** Write `results.csv` with one row per run: task, profile, strong model, cheap model, temperature, security, repeat, outcome, iterations, input tokens, output tokens, cost, duration, resource count, blocking issues, advisory issues. Write CSV by hand with proper quoting; do not add a CSV package.
5. **Example.** Add `benchmarks/hello-suite.json` with about five small tasks (hello Lambda, S3 static website, SQS + Lambda consumer, DynamoDB table with a Lambda, a VPC with a public and a private subnet).
6. **CLI.** `iacagent bench --suite <file> [--resume <folder>] [--endpoint <url>]`. Exit `0` when the suite finished, even if runs failed; `20` for an invalid suite file.

**Tests (Host):** fake profile and gate; the runner makes the expected number of runs, writes one CSV row per run, skips existing runs on resume, and continues after a run throws.

**Done when:** `iacagent bench --suite benchmarks/hello-suite.json` with `repeat: 1` finishes and `results.csv` has one row per combination.

---

### Step 13 — Orchestrator clarification dialogue

**Goal:** the user's answers change the requirements. When an answer is a question, a request for advice, or a change of direction, the Orchestrator replies with advice and asks a follow-up, until nothing is open or the round limit is reached.

**Bug being fixed:** `OrchestratorAgent` asks its questions and then uses the requirements from the **same** reply, which already contain the defaults. Answers are only appended to `assumptions`, which the Developer never sees. In a real run, "page number" and "seed 20 products" were ignored and "What is better in our case?" got no reply.

**Scope:** only `Plugins.Agents` (`Trio/OrchestratorAgent.cs`, `Prompts/Orchestrator.md`) and one line in `Cli/ConsoleUserInteraction.cs`. No Host or Core change. `--auto-approve`, non-interactive input, and `bench` must behave exactly as before: one model call, no questions.

1. **Reply format.** Extend the Orchestrator JSON with an optional `advice` string:
 `{"assumptions":[],"questions":[],"advice":"","needsHuman":false,"requirements":{...}}`.
2. **Prompt (`Orchestrator.md`).** Add an input section `Conversation so far` (each question and the user's answer) and these rules:
 - The user's answers override the defaults. Rewrite `requirements` and `assumptions` to match them.
 - If an answer asks a question or for advice, answer it in `advice` (at most 3 sentences, with one clear recommendation and the reason) and ask one short confirming question, for example "Use one Lambda for all three routes? Press Enter to accept."
 - If an answer changes direction ("use separate Lambdas", "add a POST endpoint"), update the requirements and ask only about what the change leaves open.
 - Never repeat a question that already has an answer.
 - When the input says `Final round: true`, return `questions: []` and choose defaults for anything still open.
3. **Dialogue loop (`OrchestratorAgent`).** Keep `RunAsync` a short summary; put each step in a named method (`AskModelAsync`, `AskUserAsync`, `BuildFollowUpPrompt`).
 - Round 0: the current call.
 - While the reply has questions, auto-approve is off, and rounds remain: ask each question (show `advice` before the first question of the round), collect the answers, then call the model again with the original prompt plus the full conversation. That reply replaces the previous requirements, assumptions, and questions.
 - Stop asking when the reply has no questions, every answer in a round is empty (the user accepts the defaults), or `AskAsync` returns `null` (cannot prompt).
 - Round limit: a `private const int MaxClarificationRounds = 3` (no new setting). The last follow-up call carries `Final round: true`; questions in that reply are recorded as `Assumed a default for: …` and not asked.
 - Every call goes through `ChatText.CompleteAsync` so the user's answers are redacted. An invalid JSON reply gets the existing one format retry, then the agent fails as today.
 - `Tokens` is the sum of all calls in the dialogue.
 - `needsHuman` is read from the **final** reply, with the current rule (`NeedsHuman` when auto-approve is on or a question stayed unanswered).
4. **Assumptions.** Take them from the final reply. Add one line per exchange in the form `User was asked "<question>" and answered "<answer>".` so `run.json` documents the dialogue. Drop the old `"<question> <answer>"` lines.
5. **Empty answer.** In `ConsoleUserInteraction`, use `new TextPrompt<string>(question).AllowEmpty()` so Enter means "accept the default". Return the trimmed text; an empty string is an answer, not `null`.
6. **Docs.** Update section 7, flow step 2, to describe the dialogue and the round limit. Add one sentence to `docs/observations.md`: the dialogue is bounded and only runs interactively, so benchmark runs are not affected. Update `README.md` if it describes the questions.

**Tests (Agents, `Trio/OrchestratorAgentTests`, fake `IChatClient` and fake `IUserInteraction`):**
- `RunAsync_AutoApprove_MakesOneCallAndAsksNothing`
- `RunAsync_UserAnswers_SecondCallContainsQuestionAndAnswer` and the final requirements come from the second reply
- `RunAsync_AllAnswersEmpty_MakesNoFollowUpCall`
- `RunAsync_ReplyWithAdvice_ShowsAdviceBeforeFollowUpQuestion`
- `RunAsync_ModelKeepsAsking_StopsAfterRoundLimit` (at most 1 + 3 calls, last request contains `Final round: true`, leftover questions become assumptions)
- `RunAsync_CannotPrompt_KeepsDefaultsAndMakesNoFollowUpCall`
- `RunAsync_Dialogue_SumsTokensOfAllCalls`
- `RunAsync_AnswerContainsSecret_RequestIsRedacted`
- `RunAsync_InvalidFollowUpJson_FailsAfterOneFormatRetry`

**Done when:** the sport-store prompt run interactively, with the answers "page number", "public", "What is better in our case?" (followed by Enter on the advice), and "yes, seed about 20 products", produces requirements and `main.tf` with page-number pagination and seeded items. The same prompt with `--auto-approve` still makes exactly one Orchestrator call.

---

### After the plan

Report to the user: what each step changed, the final test count, and the open decisions from steps 5, 7, and 11. Do not start new features without asking.
