# Agents

`solo` is one developer. `trio` is the orchestrator, developer, and security reviewer. `full` adds an architect and a FinOps explainer before the developer. Prompts are embedded from `Prompts/`.

The Orchestrator and Developer use the strong model. The reviewer uses the cheap model, or the strong model when the cheap id is unset. Add an agent by implementing `IAgent` and registering it. Do not edit Host or Core.
