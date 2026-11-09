using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Full;

/// <summary>Strong-model architecture notes for the full profile.</summary>
internal sealed class ArchitectAgent(IModelRouter router, IEnumerable<IRedactionRule> rules)
    : AdviceAgent(router, rules, "architect", "Architect.md", ModelTier.Strong);
