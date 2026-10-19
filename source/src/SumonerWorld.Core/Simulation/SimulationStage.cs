namespace SumonerWorld.Core.Simulation;

/// <summary>
/// The fixed order of work inside one tick (TDD §2.2). Systems run stage by stage; the order never depends on registration.
/// </summary>
public enum SimulationStage
{
    /// <summary>Apply validated player inputs and intents.</summary>
    ApplyInputs = 0,

    /// <summary>Apply finished async job results in job-id order.</summary>
    ApplyJobResults = 1,

    /// <summary>Creature perception.</summary>
    Perception = 2,

    /// <summary>Utility goal selection.</summary>
    AiDecision = 3,

    /// <summary>Behaviour trees emit AI intents.</summary>
    Behaviour = 4,

    /// <summary>Movement and collision.</summary>
    Movement = 5,

    /// <summary>Combat and magic resolution.</summary>
    Combat = 6,

    /// <summary>Status effects and death.</summary>
    StatusEffects = 7,

    /// <summary>Bonding, economy and crafting transactions.</summary>
    Transactions = 8,

    /// <summary>Ecosystem and abstract population steps.</summary>
    Ecosystem = 9,

    /// <summary>Event dispatch to listeners outside the simulation.</summary>
    EventDispatch = 10,

    /// <summary>Build the network snapshot.</summary>
    Replication = 11,

    /// <summary>Copy dirty state for the persistence thread.</summary>
    Persistence = 12,
}
