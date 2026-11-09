namespace IacAgent.Core;

/// <summary>Where an agent sits in the generate flow.</summary>
public enum AgentRole
{
    /// <summary>Turns the prompt into requirements once.</summary>
    Orchestrator,

    /// <summary>Adds design or cost notes once, before Terraform is written.</summary>
    Advisor,

    /// <summary>Writes Terraform and repairs gate failures.</summary>
    Developer,

    /// <summary>Comments on low-severity findings after the gates pass.</summary>
    Reviewer,
}
