namespace ChemVerify.Core.Procedure;

/// <summary>
/// The contextual state of a procedure at a single step boundary. Immutable —
/// the engine produces a new instance for each step.
/// </summary>
public sealed record ProcedureState
{
    /// <summary>Step index this snapshot describes (-1 = pre-procedure baseline).</summary>
    public int StepIndex { get; init; } = -1;

    /// <summary>Solvents currently active in the reaction vessel (canonical entity keys).</summary>
    public IReadOnlyList<string> CurrentSolvents { get; init; } = [];

    /// <summary>Current atmosphere — "nitrogen", "argon", "hydrogen", "air", or null.</summary>
    public string? CurrentAtmosphere { get; init; }

    /// <summary>Current temperature in °C (null if unknown).</summary>
    public double? CurrentTemperatureCelsius { get; init; }

    /// <summary>Symbolic temperature label when a numeric value is unavailable ("rt", "reflux", "ice_bath").</summary>
    public string? CurrentSymbolicTemperature { get; init; }

    /// <summary>Current pressure (atm). Null if unspecified — assumed atmospheric.</summary>
    public double? CurrentPressureAtm { get; init; }

    /// <summary>Description of the current vessel ("flask", "open beaker", "schlenk", etc.) or null.</summary>
    public string? CurrentVessel { get; init; }

    /// <summary>The reaction phase ("setup", "reaction", "workup", "purification", "complete").</summary>
    public ReactionPhase CurrentPhase { get; init; } = ReactionPhase.Setup;

    /// <summary>True when system is sealed/pressurized (autoclave, sealed tube, balloon at &gt;1 atm).</summary>
    public bool IsSealedSystem { get; init; }

    /// <summary>True when an inert atmosphere is currently established.</summary>
    public bool IsUnderInertAtmosphere { get; init; }

    /// <summary>True when conditions are explicitly dry/anhydrous and no aqueous medium is present.</summary>
    public bool IsDryEnvironment { get; init; }

    /// <summary>True when the system has been concentrated to dryness (residue only).</summary>
    public bool IsConcentratedToDryness { get; init; }

    /// <summary>Reagents currently present in the vessel (canonical entity keys).</summary>
    public IReadOnlyList<string> CurrentReagents { get; init; } = [];

    /// <summary>Free-form contextual flags ("refluxing", "cooling", "heating", "stirring", etc.).</summary>
    public IReadOnlySet<string> CurrentFlags { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Heuristically classified operation type for this step. Used by validators to
    /// suppress cross-operation comparisons (e.g. distillation BP vs reaction temp).
    /// </summary>
    public OperationType CurrentOperation { get; init; } = OperationType.Reaction;
}

/// <summary>
/// Lightweight operation classification of a single procedural step. Heuristic;
/// not derived from a full parser. Used by validators to avoid cross-operation
/// false positives (sequential durations, distillation temperatures, etc.).
/// </summary>
public enum OperationType
{
    Reaction,
    Heating,
    Reflux,
    Cooling,
    Quench,
    Extraction,
    Wash,
    Drying,
    Concentration,
    Distillation,
    Purification,
    Analysis,
    ProductProperty,
    Monitoring,
    Isolation
}

/// <summary>
/// The high-level phase of a procedure step. Order is significant — earlier
/// phases appear before later ones in a typical procedure and the engine uses
/// monotonic ordering to detect anomalies.
/// </summary>
public enum ReactionPhase
{
    Setup       = 0,
    Reaction    = 10,
    Heating     = 20,
    Cooling     = 30,
    Quench      = 40,
    Extraction  = 50,
    Workup      = 60,
    Purification = 70,
    Analysis    = 80,
    Complete    = 90
}

/// <summary>
/// Canonical procedural-context flag names recorded on
/// <see cref="ProcedureState.CurrentFlags"/>. Keeping them as constants
/// prevents typo drift between the state engine and downstream suppressors.
/// </summary>
public static class ProcedureStateFlags
{
    public const string Refluxing       = "refluxing";
    public const string Heating         = "heating";
    public const string Cooling         = "cooling";
    public const string ThermalChange   = "thermal_change";
    public const string Microwave       = "microwave";
    public const string OilBath         = "oil_bath";
    public const string Autoclave       = "autoclave";
    public const string PressureVessel  = "pressure_vessel";
    public const string SealedTube      = "sealed_tube";
    public const string ElevatedPressure = "elevated_pressure";
    public const string InertPressure   = "inert_pressure";
    public const string PostQuench      = "post_quench";
    public const string AnalyticalContext = "analytical_context";
    public const string VacuumDistillation = "vacuum_distillation";
    public const string ProductProperty = "product_property";
}

/// <summary>
/// Pair of states bracketing a single step plus a snippet of the step text.
/// </summary>
public sealed record StateSnapshot(
    int StepIndex,
    int StepStartOffset,
    int StepEndOffset,
    string StepText,
    ProcedureState BeforeState,
    ProcedureState AfterState,
    IReadOnlyList<StateTransition> Transitions);

/// <summary>
/// A discrete change between two consecutive procedure states. Used by
/// validators and reports to explain reasoning.
/// </summary>
public sealed record StateTransition(
    int StepIndex,
    StateTransitionKind Kind,
    string Description,
    string? PreviousValue,
    string? NewValue);

/// <summary>The category of a state transition.</summary>
public enum StateTransitionKind
{
    SolventAdded,
    SolventRemoved,
    AtmosphereChanged,
    TemperatureChanged,
    VesselChanged,
    PhaseChanged,
    ReagentAdded,
    DrynessChanged,
    SealedChanged,
    PressureChanged,
    FlagAdded,
    FlagCleared
}
