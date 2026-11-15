# Agents

Turns a prompt into Terraform. `solo` is one developer. `trio` adds an orchestrator and a security reviewer. `full` adds an architect and a FinOps explainer before the developer.

Key types: `OrchestratorAgent`, `DeveloperAgent`, `SecurityReviewerAgent`, `SoloDeveloperAgent`, `ArchitectAgent`, `FinOpsExplainerAgent`. Each prompt is an embedded file under `Prompts/`.

The orchestrator, developer, solo agent, and architect use the strong model. The reviewer and FinOps explainer use the cheap model, or the strong model when that id is unset.

Add an agent by implementing `IAgent`, adding its prompt file, and registering it from `IacAgent.Cli`. Do not edit Host or Core.
