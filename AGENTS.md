# AGENTS.md — IacAgent

Instructions for the AI coding assistant working on this repository. Read this whole file before writing code. Follow it literally. When something is unclear or missing, ask the user instead of guessing.

---

## 1. What we are building

`IacAgent` is an open-source .NET 10 command-line tool that generates AWS infrastructure code (Terraform) from a text prompt using LLM agents, and only accepts the result if fixed, deterministic checks ("gates") pass.

Core idea: **the LLM proposes, the gates decide.** Agents write and repair code. Gates (formatting, validation, security scan, cost check) are ordinary tools that no agent setup can skip.

This is also a master's thesis project. The tool must record data about every run (see FR8) so different agent setups can be compared later. The benchmark itself is NOT part of the current work.

### Decisions already made (do not change without asking)

| Topic | Decision |
|---|---|
| Language / platform | C# 14, .NET 10 |
| IaC tool | Terraform is the default. OpenTofu is an optional extra later (FR15). |
| Cloud | AWS only. Do NOT build multi-cloud abstractions. |
| Model access | OpenRouter is the default provider. Local models (Ollama or any OpenAI-compatible server) must also work from the MVP. |
| LLM abstraction | `Microsoft.Extensions.AI` (`IChatClient`). Do NOT use Semantic Kernel. |
| Agent orchestration | Our own small orchestrator. Do NOT add an agent framework unless the user asks. |
| CLI | `System.CommandLine` + `Spectre.Console` |
| Native AOT | Optional goal, not required. Do not break the normal build for it. See section 9. |
| Tools used by gates | `terraform`, `checkov`, later `infracost` (all free) |
| Safety | The tool never runs `terraform apply` or `terraform destroy`. |

---

## 2. Hard rules

You MUST:

1. Keep agents and gates separate. Agents are pluggable; gates belong to the host and cannot be removed by any profile or user setting.
2. Return gate failures to the Developer agent as **structured errors** (file, line, rule id, message), never as a raw text dump.
3. Put every external effect (running a process, calling a model, reading the environment) behind an interface so it can be faked in tests.
4. Write a unit test for every agent, gate, and parser you add. Agents are tested with a fake `IChatClient`.
5. Run `dotnet build` and `dotnet test` after every change set and fix failures before moving on.
6. Keep each step small and finish it: one phase item at a time, working and tested before the next.
7. Read secrets only from environment variables. Redact them from prompts, logs, and `run.json`.
8. Verify NuGet package names and current stable versions before adding a dependency. Do not invent package names or versions.

You MUST NOT:

1. Run or implement `terraform apply` / `terraform destroy`, or require cloud credentials for any gate.
2. Add new dependencies without asking the user first (the packages listed in section 4 are pre-approved).
3. Create abstractions for clouds, IaC tools, or plugin systems beyond the interfaces in section 5 and the extension points in section 2A.
4. Pass whole Terraform projects between agents when a diff or the failing files are enough (see section 7).
5. Let the retry loop run without limits.
6. Rewrite or reformat files you were not asked to change.
7. Hard-code model names. They come from configuration.

---

## 2A. Clean code and extensibility

The architecture must be clean, easy to read, and easy to extend. Customization by a developer is a core feature of this project. Follow these rules in every file.

### The test for good architecture

**Extend by adding, not editing.** Adding a new agent, gate, model provider, profile, or redaction rule MUST be possible by writing one new class and one DI registration line, without editing `IacAgent.Host` or `IacAgent.Core`. If a new feature of one of these kinds forces you to edit the host, stop and tell the user: the design has a flaw to fix first.

### The only extension points

Extensibility is limited to these seams. Do not add others without asking.

| Extension point | Interface | Where it lives | Registered how |
|---|---|---|---|
| Agent | `IAgent` | `Plugins.Agents` or a new plugin project | `services.AddSingleton<IAgent, MyAgent>()` or the plugin's `AddXxx` method |
| Gate | `IGate` | `Plugins.Gates` or a new plugin project | `services.AddSingleton<IGate, MyGate>()` |
| Profile | `IProfileDefinition` (`string Name`, `PipelineProfile Build(IServiceProvider)`) | any project | `services.AddSingleton<IProfileDefinition, MyProfile>()` |
| Model provider | `IModelRouter` | `Plugins.Models` | `AddXxx` method of that plugin |
| Redaction rule | `IRedactionRule` (finds and masks one kind of secret) | `Core` interface, any project implements | `services.AddSingleton<IRedactionRule, MyRule>()` |

`ProfileCatalog` collects all registered `IProfileDefinition` instances by name. It never contains a hard-coded list of profiles.

### Plugin structure

1. Each plugin project exposes exactly one public entry point: an extension method `AddXxx(this IServiceCollection services, IConfiguration configuration)`. Everything else in the project is `internal` unless another project must use it.
2. `IacAgent.Cli` is the composition root: it calls the `AddXxx` methods. It is the only place that knows which plugins exist.
3. Plugin settings are strongly typed `XxxOptions` classes, bound from configuration and validated at startup (fail fast with a clear message).

### Code rules

1. **Single responsibility.** One class, one reason to change. If you need "and" to describe a class, split it.
2. **Small methods.** A method usually fits on one screen (about 30 lines). Extract well-named helpers when it grows.
3. **Names reveal intent.** Full words, no abbreviations except well-known ones (`Id`, `Url`). Methods are verbs, types are nouns, booleans read as questions (`IsValid`, `HasIssues`).
4. **Comments explain why, not what.** Do not restate the code. Public types get short XML doc comments.
5. **Dependency injection only.** Constructor injection. No static mutable state, no service locator, no `new` for services that have an interface.
6. **Immutability by default.** Use `record` types and `init` properties for data. Mutable state stays inside a class and is not shared.
7. **No magic values.** Constants, enums, or options instead of repeated strings and numbers (tool names, exit codes, file names).
8. **Expected failures are results, not exceptions.** A failed gate is a `GateResult`. Exceptions are for unexpected situations. Never swallow an exception silently. Always pass `CancellationToken` through async calls.
9. **Async all the way.** Never use `.Result` or `.Wait()`.
10. **Composition over inheritance.** Avoid base classes unless two or more real subclasses share real logic.
11. **Rule of three.** Do not extract a shared abstraction until the same code appears three times.
12. **`Core` has no logic.** Only interfaces, records, and enums.
13. **Smallest public surface.** Default to `internal`; make something `public` only when another project needs it.
14. **Folders by feature,** not by technical type, inside each project (for example `Checkov/`, `Validate/`).

### Keep it simple: over-engineering is a defect

Over-engineering is as bad as messy code. Extensibility applies ONLY to the five extension points above. Everywhere else, choose the simplest design that works and is easy to read. If extensibility and simplicity conflict, keep the five extension points and choose simplicity for everything else.

Before adding any new type, interface, layer, or setting, ask: "What gets worse or harder to read if I do not add this?" If the answer is "nothing", do not add it.

Signs of over-engineering (do NOT do these):

- An interface with one implementation that is neither an extension point (section 2A), a core interface (section 5), nor needed to fake something in tests.
- Abstract base classes with one subclass; generic classes with one concrete use.
- Classes that only forward calls to another class.
- Factories of factories, builders for simple objects, layers "for later".
- Design patterns used for their own sake (Strategy, Visitor, Mediator, CQRS, event buses, repositories) unless the user asks for one.
- Settings for things nobody asked to configure.
- Reflection, assembly scanning, or other "magic" to register or find things. Register services explicitly.

Prefer plain and obvious code over clever code. A new developer should understand a method on first read.

### Easy to navigate

A reader must find the code for any feature within a minute.

1. One public type per file; the file name equals the type name.
2. Predictable names that show the role: `CheckovGate`, `CheckovOutputParser`, `TrioProfile`, `OpenRouterOptions`.
3. Keep folders shallow: at most 3 levels inside a project.
4. No dumping-ground names: no `Utils`, `Helpers`, `Common`, `Manager`, or `Misc`. Put code next to the feature that uses it.
5. Aim for files under about 300 lines. Split by responsibility when a file grows beyond that.
6. Entry methods read like a short summary of the steps, each step a well-named method (for example `GenerateAsync` calls `GatherRequirements`, `WriteFiles`, `RunGates`, `RepairOrStop`).
7. Each plugin folder has a short `README.md` (5 to 10 lines): what the plugin does, its key types, and how to extend it.

### Tooling rules (set up in Phase 1)

- `Directory.Build.props`: `Nullable` enabled, `TreatWarningsAsErrors` true, latest analyzers.
- A committed `.editorconfig`; `dotnet format --verify-no-changes` must pass.
- Tests are named `Method_Scenario_ExpectedResult` and mirror the source folder structure.

### Extension recipes (keep these working and documented in README.md)

| To add... | Do this | Files you must NOT edit |
|---|---|---|
| A new agent | Implement `IAgent`, add its prompt file, register it | Host, Core |
| A new gate (for example a custom policy check) | Implement `IGate` with a unique `Order`, register it | Host, Core, other gates |
| A new profile | Implement `IProfileDefinition`, register it; select with `--profile <name>` | Host, Core, `ProfileCatalog` |
| A new model provider | Add a plugin project with an `AddXxx` method that registers an `IModelRouter` | Host, Core, other plugins |
| A new secret-redaction rule | Implement `IRedactionRule`, register it | Host, Core |

Phase 2 includes one test that registers a fake agent, a fake gate, and a fake profile from the test project and runs them through the host. This test proves the recipes above work.

---

## 3. Repository layout

Create exactly this layout (one solution, `IacAgent.sln`):

```
src/
  IacAgent.Core/              interfaces and models only; no external packages except Microsoft.Extensions.AI.Abstractions
  IacAgent.Host/              profile runner, retry policy, gate pipeline, run recorder
  IacAgent.Plugins.Agents/    Orchestrator, Developer, SecurityReviewer agents + prompt files
  IacAgent.Plugins.Models/    OpenRouter and local-model client registration, model router
  IacAgent.Plugins.Terraform/ Terraform process wrapper, HCL error parsing, AWS prompt snippets
  IacAgent.Plugins.Gates/     fmt/validate gate, Checkov gate, (later) Infracost gate
  IacAgent.Cli/               commands, DI setup, console output
tests/
  IacAgent.Core.Tests/
  IacAgent.Host.Tests/
  IacAgent.Plugins.Agents.Tests/
  IacAgent.Plugins.Gates.Tests/
  fixtures/                   small Terraform projects: valid, invalid syntax, insecure (open security group)
docs/
  run-json.md                 schema of run.json
AGENTS.md
README.md
```

Dependency direction: `Cli` → `Host` → `Core`; plugins → `Core`. `Core` depends on nothing of ours. Plugins never reference each other.

---

## 4. Pre-approved packages

Use the latest stable versions (verify on NuGet):

- `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Logging`
- `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.Abstractions`
- `Microsoft.Extensions.AI.OpenAI` (used for OpenRouter and any OpenAI-compatible endpoint, with a custom base URL)
- `OllamaSharp` (local models via Ollama; implements `IChatClient`)
- `System.CommandLine`
- `Spectre.Console`
- Tests: `xunit`, `FluentAssertions` (or `xunit` asserts only), `Verify.Xunit` for snapshot tests

OpenRouter base URL: `https://openrouter.ai/api/v1`. API key from env var `OPENROUTER_API_KEY`.

---

## 5. Core interfaces (put these in `IacAgent.Core`)

Keep them close to this shape. Adjust names only if needed and tell the user.

```csharp
public enum ModelTier { Strong, Cheap }

public interface IAgent
{
    string Name { get; }
    Task<AgentResult> RunAsync(AgentContext context, CancellationToken ct);
}

public interface IGate
{
    string Name { get; }
    int Order { get; }                       // gates run in ascending order
    Task<GateResult> CheckAsync(Artifact artifact, CancellationToken ct);
}

public interface IModelRouter
{
    IChatClient GetClient(ModelTier tier);   // from Microsoft.Extensions.AI
}

public interface IProcessRunner              // wraps running terraform, checkov, infracost
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args,
                                 string workingDirectory, CancellationToken ct);
}

public interface IRunRecorder
{
    void RecordIteration(IterationRecord record);
    Task WriteAsync(string outputDirectory, RunOutcome outcome, CancellationToken ct);
}

public sealed record Artifact(string Directory, IReadOnlyList<GeneratedFile> Files);
public sealed record GeneratedFile(string RelativePath, string Content);

public sealed record GateIssue(string File, int? Line, string RuleId, string Severity, string Message);
public sealed record GateResult(string GateName, bool Passed, IReadOnlyList<GateIssue> Issues);

public sealed record LoopPolicy(int MaxRepairIterations = 3, int? MaxTotalTokens = null);

public sealed record PipelineProfile(
    string Name,
    IReadOnlyList<IAgent> Agents,            // differs per profile
    LoopPolicy Loop);                        // gates are NOT part of the profile

public enum RunOutcome { Succeeded, GateFailed, BudgetExceeded, NeedsHuman, ConfigError }

public interface IProfileDefinition          // one class per profile; see section 2A
{
    string Name { get; }
    PipelineProfile Build(IServiceProvider services);
}

public interface IRedactionRule              // masks one kind of secret in text before it reaches a model or a log
{
    string Redact(string text);
}
```

Gates are registered with DI as `IEnumerable<IGate>` and executed by the host. A profile cannot add, remove, or reorder them.

---

## 6. CLI

Executable name: `iacagent`.

| Command | Phase | Behavior |
|---|---|---|
| `iacagent generate "<prompt>"` | MVP | Runs the profile pipeline, writes Terraform files to `--out` (default `./out`). |
| `iacagent doctor` | MVP | Checks `terraform`, `checkov`, `infracost`, the API key, and the model endpoint. Prints a clear fix for each problem. Exit code 20 if something required is missing. |
| `iacagent review --plan plan.json` | Phase 5 | Reads `terraform show -json` output, redacts secrets, prints findings. |

`generate` options:

- `--profile solo|trio|full` (default `trio`)
- `--out <dir>`
- `--model-strong <id>` and `--model-cheap <id>` (OpenRouter model ids; also usable for local model names)
- `--endpoint <url>` use a local OpenAI-compatible server instead of OpenRouter (for Ollama use its native client when the URL points to Ollama; document how you detect it)
- `--budget <usd-per-month>` (Phase 3)
- `--max-retries <n>` (default 3)
- `--auto-approve` no interactive questions; the Orchestrator picks defaults, logs each assumption, or exits with an error
- `--verbose`

Configuration precedence: CLI flags > environment variables > `iacagent.json` in the current directory.

Exit codes: `0` success, `10` gate failure after retries, `11` budget exceeded, `12` needs human decision, `20` configuration or missing prerequisite, `1` unexpected error.

Interactive questions (Spectre.Console prompts) MUST be skipped when `--auto-approve` is set or when input is not a terminal.

---

## 7. How the pipeline works

### Flow for `generate`

1. Load config and select the profile.
2. **Orchestrator** turns the user prompt into structured requirements (JSON). It may ask the user clarifying questions (unless `--auto-approve`).
3. **Developer** produces Terraform files as JSON: `{"files":[{"path":"main.tf","content":"..."}]}`. Parse strictly; if the JSON is invalid, ask the model once to fix the format, then fail the iteration.
4. Write files to a working directory.
5. Host runs all gates in order:
   1. `terraform fmt -check -recursive`
   2. `terraform init -backend=false -input=false`
   3. `terraform validate -json`
   4. `checkov -d . --framework terraform -o json`
   5. (Phase 3) `infracost breakdown --path . --format json`, compared with `--budget`
6. If every gate passes, the **Security reviewer** (trio/full) summarizes the Checkov findings that remain at low severity and suggests improvements. It does not decide pass or fail.
7. If any gate fails and iterations remain, send the Developer **only**: the failing gate issues and the content of the files that contain issues. Then go to step 4.
8. Stop when: gates pass, `MaxRepairIterations` is reached, the same set of issues appears twice in a row, or the token limit is hit. On stop without success, write `escalation.md` listing the remaining issues and the last attempt.
9. Always write `run.json` (see below).

### Profiles

| Profile | Agents | Phase |
|---|---|---|
| `solo` | One Developer agent with a combined prompt (includes requirement gathering and security hints) | MVP |
| `trio` | Orchestrator, Developer, SecurityReviewer | MVP (default) |
| `full` | Orchestrator, Architect, FinOps explainer, SecurityReviewer, Developer | Phase 4 |

Each profile is an `IProfileDefinition` class (see section 2A) that builds its `PipelineProfile` in code. `ProfileCatalog` in `IacAgent.Host` finds registered definitions by name. The user selects a profile by name only.

### Model tiers

- `Strong`: Orchestrator and Developer.
- `Cheap`: SecurityReviewer and small summarization tasks.
Both come from configuration; either can point to a local model.

### Context rules (important for cost)

- Pass the Developer the full requirements JSON and, on retries, only the failing files and structured issues.
- Never send `*.tfstate` files or secret values to a model. A redaction step runs before every model call that includes file content.

### Agent prompts

- One markdown file per agent under `IacAgent.Plugins.Agents/Prompts/`, embedded as a resource.
- Each prompt states: role, input format, exact output format, and a short list of Terraform-for-AWS rules (least-privilege IAM, no `0.0.0.0/0` on admin ports, encryption on, tags on resources, pinned provider versions).
- Keep prompts short and specific. Do not put API keys, model names, or tool paths in prompts.

### run.json

Written to the output directory on every run (success or failure). Document the schema in `docs/run-json.md`. Required fields:

```json
{
  "schemaVersion": 1,
  "runId": "guid",
  "startedAtUtc": "ISO-8601",
  "durationMs": 0,
  "profile": "trio",
  "prompt": "text",
  "models": { "strong": "id", "cheap": "id", "provider": "openrouter|local" },
  "tokens": { "input": 0, "output": 0 },
  "iterations": [
    { "index": 1, "gates": [ { "name": "validate", "passed": true, "issueCount": 0 } ], "tokens": { "input": 0, "output": 0 } }
  ],
  "assumptions": [ "text" ],
  "outcome": "Succeeded"
}
```

---

## 8. Testing rules

- **Agents:** tested with a fake `IChatClient` that returns scripted responses. No network, no API key.
- **Gates:** tested against fixture projects in `tests/fixtures/`. Gate tests that need the real `terraform` or `checkov` binary are marked with a trait (`[Trait("Category","Integration")]`) and skipped when the binary is missing.
- **Host:** tested with fake agents and fake gates: retry limit, repeated-issue stop, token limit, escalation file, `run.json` content.
- **Parsers:** `terraform validate -json` and Checkov JSON parsing have their own unit tests with saved sample outputs.
- **Snapshots:** use `Verify` for generated file lists and `run.json` shape.
- A phase is not done until `dotnet build` and `dotnet test` pass.

---

## 9. Native AOT (optional)

- Goal, not requirement. Phase 0 only tests whether it works with our packages and records the result in `docs/aot-notes.md`.
- Do not change the code design to force AOT. If a library breaks AOT, keep the normal build and note it.
- Prefer `System.Text.Json` with source-generated contexts for our own types where it costs little; keep it that way.

---

## 10. Build order and acceptance criteria

Work through phases in order. Do not start a phase before the previous acceptance check passes. After each phase, summarize what you did and what to verify.

### Phase 0: spikes
- Small console project: call OpenRouter through `IChatClient` and call a local Ollama model through `IChatClient`; print both answers.
- Call `terraform validate` and `checkov` from C# through a process runner and print parsed output.
- Try `dotnet publish` with Native AOT; write the result in `docs/aot-notes.md`.
- **Accept:** both models answer; both tools run; AOT result documented.

### Phase 1: skeleton
- Solution and projects from section 3, DI host, interfaces from section 5, config loading, logging, `ProfileCatalog` with a stub profile, `doctor` command.
- **Accept:** `iacagent doctor` reports missing prerequisites with fixes; `iacagent generate "x" --profile trio` runs a stub pipeline end to end; tests pass.

### Phase 2: greenfield MVP
- Orchestrator, Developer, SecurityReviewer agents with prompts; model router for OpenRouter and local endpoint; process runner; fmt/validate gate and Checkov gate with structured parsing; retry loop with all stop conditions; `escalation.md`; `run.json`.
- **Accept:** `iacagent generate "API Gateway with a Lambda function in Python that returns hello"` writes Terraform that passes all gates (with a real model), and the same flow passes in tests with a fake model. Exit codes behave as in section 6.

### Phase 3: cost and budget
- Infracost gate, `--budget`, readable rejection report.
- **Accept:** an over-budget design exits with code 11 and a clear report.

### Phase 4: profiles
- `solo` and `full` profiles, `--auto-approve` behavior fully tested.
- **Accept:** the same prompt runs under all three profiles and produces comparable `run.json` files.

### Phase 5: brownfield review
- `review` command: reads plan JSON, redacts secrets, runs Checkov on the source, asks the model to explain findings and propose fixes.
- **Accept:** fixtures with seeded issues are reported; no secret value appears in any prompt or output.

(Benchmark runner and thesis data export come later and are not part of this file's scope.)

---

## 11. Working style

1. Before starting a phase item, write a short plan (3 to 7 lines) and then implement it.
2. Make small commits with clear messages (one concern each).
3. After each item: build, test, and report in 2 to 3 lines what works and what is not done.
4. If a requirement conflicts with another, or a library does not behave as expected, stop and ask the user with two or three concrete options.
5. Keep `README.md` current with: prerequisites (`terraform`, `checkov`, optional `infracost`, a model key or Ollama), install, and one working example command.
6. Prefer clear, boring code over clever code. Public types have short XML doc comments.
7. Do not add structure "for later". Build what the current phase needs, and keep it simple enough that later phases can extend it through the extension points.
8. Before you finish an item, reread your own diff and remove anything that is not needed.

---

## 12. Definition of done (for any task)

- Builds without warnings you introduced.
- All tests pass; new behavior has tests.
- No secrets in code, logs, or `run.json`.
- Exit codes and `--auto-approve` behavior match section 6.
- Code follows section 2A: no host edits needed to add an agent, gate, profile, model provider, or redaction rule; `dotnet format --verify-no-changes` passes.
- No unnecessary types, layers, or settings were added (see "Keep it simple" in section 2A), and the code for each feature is easy to find.
- The relevant phase acceptance check in section 10 is met or the gap is reported.
