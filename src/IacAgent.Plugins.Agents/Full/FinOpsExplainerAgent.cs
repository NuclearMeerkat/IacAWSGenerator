using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Full;

/// <summary>Cheap-model cost notes for the full profile.</summary>
internal sealed class FinOpsExplainerAgent(IModelRouter router, IEnumerable<IRedactionRule> rules)
    : AdviceAgent(router, rules, "finops-explainer", "FinOps.md", ModelTier.Cheap);
