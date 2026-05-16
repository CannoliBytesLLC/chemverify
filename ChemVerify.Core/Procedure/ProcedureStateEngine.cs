using System.Text.RegularExpressions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Chemistry;
using ChemVerify.Core.Services;

namespace ChemVerify.Core.Procedure;

/// <summary>
/// Pure deterministic engine that derives a sequence of <see cref="StateSnapshot"/>
/// values for a procedure by replaying extracted claims and step text.
/// </summary>
/// <remarks>
/// The engine is stateless and side-effect free — call <see cref="Build"/> with
/// the analyzed text and the already-extracted claims; it returns one snapshot
/// per <see cref="TextStep"/>. No I/O, no AI, no caching.
/// </remarks>
public static class ProcedureStateEngine
{
    private static readonly Regex OpenVesselRegex = new(
        @"\b(open\s+(?:flask|beaker|vessel|to\s+air)|exposed\s+to\s+air|opened?\s+to\s+the\s+air|open[- ]?air)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SealedVesselRegex = new(
        @"\b(sealed\s+(?:tube|vial|flask|vessel)|autoclave|pressure\s+(?:tube|vessel|reactor)|"
        + @"(?:under\s+)?(?:\d+\s*(?:atm|bar|psi)|elevated\s+pressure))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConcentrationToDrynessRegex = new(
        @"\b(concentrat(?:ed?|ing)\s+(?:to\s+dryness|in\s+vacuo\s+to\s+dryness|under\s+(?:reduced\s+pressure\s+)?to\s+dryness)|"
        + @"evaporat(?:ed?|ing)\s+to\s+dryness|"
        + @"removed?\s+(?:the\s+)?solvent\s+(?:in\s+vacuo|under\s+reduced\s+pressure)|"
        + @"taken?\s+to\s+dryness)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AddSolventRegex = new(
        @"\b(dissolv(?:ed?|ing)\s+in|added?\s+(?:to\s+)?(?:a\s+solution\s+of\s+)?[^.]{0,40}?\bin\b|taken?\s+up\s+in|redissolv(?:ed?|ing)\s+in)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WorkupCueRegex = new(
        @"\b(quench(?:ed|ing)?|work[- ]?up|workup|extract(?:ed|ion|ing)?|wash(?:ed|ing)?|partition(?:ed|ing)?|separate(?:d|ing)?|"
        + @"separatory\s+funnel|sep(?:arating)?\s+funnel|brine|aqueous\s+layer|organic\s+layer)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PurificationCueRegex = new(
        @"\b(chromatograph\w+|column\s+chromatography|recrystalli[sz]\w+|trituration|prep(?:arative)?\s*HPLC|"
        + @"flash\s+(?:column|chromatography)|TLC|silica\s+gel\s+column)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RefluxRegex = new(
        @"\b(reflux(?:ed|ing)?|at\s+reflux|under\s+reflux)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CoolHeatRegex = new(
        @"\b(cool(?:ed|ing)?|warm(?:ed|ing)?|heat(?:ed|ing)?|chilled?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HeatingCueRegex = new(
        @"\b(heat(?:ed|ing)?|warm(?:ed|ing)?|raised\s+to|brought\s+to)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CoolingCueRegex = new(
        @"\b(cool(?:ed|ing)?\s+(?:to|down)|chilled?(?:\s+to)?|allowed\s+to\s+cool|let\s+cool|brought\s+to\s+(?:rt|room\s+temperature)|ice\s+bath)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QuenchCueRegex = new(
        @"\b(quench(?:ed|ing)?|added?\s+(?:dropwise\s+)?(?:to|with)\s+(?:saturated\s+)?(?:aq(?:ueous)?\.?\s+)?(?:NH4Cl|water|brine|sat(?:\.|urated)?\s+\w+)\s+(?:solution|sol\.|to\s+quench)?|reaction\s+was\s+quenched|stopped\s+the\s+reaction)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExtractionCueRegex = new(
        @"\b(extract(?:ed|ion|ing)?\s+(?:with|using)|partition(?:ed|ing)?\s+between|separatory\s+funnel|sep(?:arating)?\s+funnel|aqueous\s+layer|organic\s+layer|combined\s+organic\s+(?:layers|extracts))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnalysisCueRegex = new(
        @"\b(NMR|HPLC|LCMS|LC[- ]?MS|GC[- ]?MS|HRMS|mass\s+spec|IR\s+spectrum|UV[- ]?Vis|melting\s+point|m\.?p\.?\s*[:=]|TLC\s+(?:showed|indicated|monitor))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Analytical / product-property cues that indicate the temperature/value
    // is a measurement, not a reaction condition: "mp 165-175 °C",
    // "bp 91-103 °C / 5 mm Hg", "m/e 256", "MS (ESI)", "1H NMR", etc.
    private static readonly Regex ProductPropertyCueRegex = new(
        @"\b(m\.?\s?p\.?|melting\s+point|b\.?\s?p\.?|boiling\s+point|m/e|m/z|\[M\+H\]|\[M\-H\]|HRMS|HR-?MS|MS\s*[\(:]|LC[- ]?MS|GC[- ]?MS|1H\s*NMR|13C\s*NMR|NMR\s*[\(:]|IR\s*[\(:])\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Distillation under reduced pressure — "bp 91-103 °C / 5 mm Hg",
    // "distilled at 0.5 mmHg", "120 °C at 5 Torr".
    private static readonly Regex VacuumDistillationRegex = new(
        @"\b(b\.?\s?p\.?|boiling\s+point|distill(?:ed|ing|ation)?)\b[^.]{0,40}?(\d+(?:\.\d+)?\s*(?:mm\s*Hg|mmHg|Torr|mbar|Pa|kPa))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Cues that terminate active reaction heating/reflux scope. Once any of
    // these appear, lingering "refluxing"/"heating" flags must clear so a
    // later analytical or workup step is not treated as still-refluxing.
    private static readonly Regex ScopeTerminatorRegex = new(
        @"\b(cool(?:ed|ing)?(?:\s+(?:to|down|in))?|chilled?|allowed\s+to\s+cool|let\s+cool|"
        + @"filter(?:ed|ing|ation)?|filtrate|wash(?:ed|ing)?|extract(?:ed|ion|ing)?|partition(?:ed|ing)?|"
        + @"dr(?:ied|ying)\s+(?:over|with)|concentrat(?:ed|ing)?\s+(?:in\s+vacuo|under\s+reduced\s+pressure|to\s+dryness)?|"
        + @"evaporat(?:ed|ing)?|chromatograph(?:y|ed|ing)?|column|recrystalli[sz]\w+|"
        + @"isolat(?:ed|ing)?|yield\w*|afford(?:ed|ing|s)?|obtained?\s+as|gave\b)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Heating/pressure apparatus context cues — used to populate state flags so
    // suppressors can recognise valid closed/elevated-pressure setups.
    private static readonly Regex MicrowaveRegex = new(
        @"\b(microwave|μW|uW|mw\s+irradiation|microwave\s+(?:reactor|vial|irradiation))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OilBathRegex = new(
        @"\b(oil[\s-]?bath|silicone\s+bath|sand\s+bath|heating\s+mantle)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AutoclaveRegex = new(
        @"\b(autoclave|parr\s+(?:reactor|bomb|shaker)|high[- ]pressure\s+(?:reactor|vessel)|bomb\s+reactor)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PressureVesselRegex = new(
        @"\b(pressure\s+(?:tube|vessel|reactor|bottle)|sealed\s+tube|sealed\s+vial|thick[- ]walled\s+(?:tube|vial)|ace\s+pressure\s+tube)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ElevatedPressureRegex = new(
        @"\b((?:\d+(?:\.\d+)?)\s*(?:atm|bar|psi|MPa|kPa)|under\s+(?:elevated|high)\s+pressure|pressuri[sz]ed)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InertPressureRegex = new(
        @"\b(under\s+(?:H2|hydrogen|N2|nitrogen|Ar|argon|CO|CO2)\s+(?:atmosphere\s+)?at\s+\d+\s*(?:atm|bar|psi)|"
        + @"H2\s+balloon|hydrogen\s+balloon|under\s+\d+\s*(?:atm|bar|psi)\s+of\s+(?:H2|hydrogen|N2|nitrogen|Ar|argon))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Build per-step state snapshots for the analyzed text. Returns an empty
    /// list when the text yields no steps.
    /// </summary>
    public static IReadOnlyList<StateSnapshot> Build(string text, IReadOnlyList<ExtractedClaim> claims)
    {
        if (string.IsNullOrEmpty(text)) return [];

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        if (steps.Count == 0) return [];

        ILookup<int, ExtractedClaim> claimsByStep = claims
            .Where(c => c.StepIndex.HasValue)
            .ToLookup(c => c.StepIndex!.Value);

        List<StateSnapshot> snapshots = new(steps.Count);
        ProcedureState state = new();

        foreach (TextStep step in steps)
        {
            string stepText = text[step.StartOffset..step.EndOffset];
            ProcedureState before = state;
            (ProcedureState after, IReadOnlyList<StateTransition> transitions) =
                Apply(before, step.Index, stepText, claimsByStep[step.Index]);

            snapshots.Add(new StateSnapshot(
                step.Index, step.StartOffset, step.EndOffset, stepText,
                before, after, transitions));
            state = after;
        }

        return snapshots;
    }

    private static (ProcedureState After, IReadOnlyList<StateTransition> Transitions) Apply(
        ProcedureState before,
        int stepIndex,
        string stepText,
        IEnumerable<ExtractedClaim> stepClaims)
    {
        List<StateTransition> transitions = [];

        // Snapshot mutable working copy
        List<string> solvents = [.. before.CurrentSolvents];
        List<string> reagents = [.. before.CurrentReagents];
        HashSet<string> flags = new(before.CurrentFlags, StringComparer.OrdinalIgnoreCase);
        string? atmosphere = before.CurrentAtmosphere;
        double? tempC = before.CurrentTemperatureCelsius;
        string? symbolicTemp = before.CurrentSymbolicTemperature;
        double? pressure = before.CurrentPressureAtm;
        string? vessel = before.CurrentVessel;
        ReactionPhase phase = before.CurrentPhase;
        bool sealedSystem = before.IsSealedSystem;
        bool inert = before.IsUnderInertAtmosphere;
        bool dry = before.IsDryEnvironment;
        bool drynessConcentrated = before.IsConcentratedToDryness;
        bool tempSetThisStep = false;

        // Step-scoped flags do not carry over from the previous step (they
        // describe the current step's text only, not state of the vessel).
        flags.Remove(ProcedureStateFlags.AnalyticalContext);
        flags.Remove(ProcedureStateFlags.VacuumDistillation);
        flags.Remove(ProcedureStateFlags.ProductProperty);

        // ── Apply claims for this step ─────────────────────────────────
        foreach (ExtractedClaim claim in stepClaims)
        {
            switch (claim.ClaimType)
            {
                case ClaimType.SolventMention:
                    string solv = (claim.EntityKey ?? claim.NormalizedValue ?? claim.RawText).ToLowerInvariant();
                    if (!solvents.Contains(solv, StringComparer.OrdinalIgnoreCase))
                    {
                        solvents.Add(solv);
                        transitions.Add(new StateTransition(stepIndex,
                            StateTransitionKind.SolventAdded, $"Solvent introduced: {solv}", null, solv));
                    }
                    break;

                case ClaimType.ReagentMention:
                    string rg = (claim.EntityKey ?? claim.NormalizedValue ?? claim.RawText).ToLowerInvariant();
                    if (!reagents.Contains(rg, StringComparer.OrdinalIgnoreCase))
                    {
                        reagents.Add(rg);
                        transitions.Add(new StateTransition(stepIndex,
                            StateTransitionKind.ReagentAdded, $"Reagent added: {rg}", null, rg));
                    }
                    break;

                case ClaimType.AtmosphereCondition:
                    string atm = claim.NormalizedValue ?? "nitrogen";
                    bool nowInert = atm is "nitrogen" or "argon";
                    if (!string.Equals(atm, atmosphere, StringComparison.OrdinalIgnoreCase))
                    {
                        transitions.Add(new StateTransition(stepIndex,
                            StateTransitionKind.AtmosphereChanged,
                            $"Atmosphere changed to {atm}", atmosphere, atm));
                        atmosphere = atm;
                    }
                    if (nowInert != inert)
                    {
                        inert = nowInert;
                    }
                    if (atm == "air") dry = false;
                    break;

                case ClaimType.DrynessCondition:
                    if (!dry)
                    {
                        dry = true;
                        transitions.Add(new StateTransition(stepIndex,
                            StateTransitionKind.DrynessChanged, "Dry/anhydrous conditions established", "wet", "dry"));
                    }
                    break;

                case ClaimType.SymbolicTemperature:
                    string sym = claim.NormalizedValue ?? "rt";
                    if (!string.Equals(sym, symbolicTemp, StringComparison.OrdinalIgnoreCase))
                    {
                        transitions.Add(new StateTransition(stepIndex,
                            StateTransitionKind.TemperatureChanged,
                            $"Symbolic temperature: {sym}", symbolicTemp, sym));
                        symbolicTemp = sym;
                    }
                    if (sym == "rt") { tempC = 25.0; tempSetThisStep = true; }
                    else if (sym == "ice_bath") { tempC = 0.0; tempSetThisStep = true; }
                    // reflux is resolved against active solvents at validation time
                    break;

                case ClaimType.NumericWithUnit:
                    if (claim.Unit is "°C" or "C" && double.TryParse(claim.NormalizedValue, out double c))
                    {
                        if (tempC != c)
                        {
                            transitions.Add(new StateTransition(stepIndex,
                                StateTransitionKind.TemperatureChanged,
                                $"Temperature set to {c} °C",
                                tempC?.ToString("0.#"), c.ToString("0.#")));
                            tempC = c;
                            symbolicTemp = null;
                        }
                        tempSetThisStep = true;
                    }
                    else if (claim.Unit is "K" && double.TryParse(claim.NormalizedValue, out double k))
                    {
                        double conv = k - 273.15;
                        tempC = conv;
                        tempSetThisStep = true;
                        transitions.Add(new StateTransition(stepIndex,
                            StateTransitionKind.TemperatureChanged,
                            $"Temperature set to {conv:0.#} °C ({k} K)", null, conv.ToString("0.#")));
                    }
                    else if (claim.Unit is "atm" && double.TryParse(claim.NormalizedValue, out double atmP))
                    {
                        pressure = atmP;
                        if (atmP > 1.05) sealedSystem = true;
                        transitions.Add(new StateTransition(stepIndex,
                            StateTransitionKind.PressureChanged, $"Pressure: {atmP} atm", null, atmP.ToString("0.##")));
                    }
                    break;
            }
        }

        // ── Free-text cues that the claim layer doesn't model ─────────
        if (OpenVesselRegex.IsMatch(stepText))
        {
            const string openVessel = "open";
            if (!string.Equals(vessel, openVessel, StringComparison.OrdinalIgnoreCase))
            {
                transitions.Add(new StateTransition(stepIndex,
                    StateTransitionKind.VesselChanged, "Vessel opened to air", vessel, openVessel));
                vessel = openVessel;
            }
            // Air exposure breaks inert blanket
            if (inert)
            {
                inert = false;
                transitions.Add(new StateTransition(stepIndex,
                    StateTransitionKind.AtmosphereChanged, "Inert atmosphere broken (open to air)", atmosphere, "air"));
                atmosphere = "air";
            }
        }
        else if (SealedVesselRegex.IsMatch(stepText) && !sealedSystem)
        {
            sealedSystem = true;
            transitions.Add(new StateTransition(stepIndex,
                StateTransitionKind.SealedChanged, "Sealed/pressurized system", "open", "sealed"));
        }

        if (ConcentrationToDrynessRegex.IsMatch(stepText))
        {
            if (solvents.Count > 0)
            {
                transitions.Add(new StateTransition(stepIndex,
                    StateTransitionKind.SolventRemoved,
                    $"Concentrated to dryness — removed: {string.Join(", ", solvents)}", string.Join(",", solvents), null));
                solvents.Clear();
            }
            if (!drynessConcentrated)
            {
                drynessConcentrated = true;
                transitions.Add(new StateTransition(stepIndex,
                    StateTransitionKind.FlagAdded, "concentrated_to_dryness", null, "concentrated_to_dryness"));
            }
        }

        // Analytical / product-property cues: classify the step so the
        // solvent-temperature validator skips numeric values that describe
        // measurements (mp, bp/mmHg, m/e, NMR, MS) rather than reaction
        // conditions. Also clear any inherited reflux/heating scope.
        bool analyticalHere = ProductPropertyCueRegex.IsMatch(stepText)
            || AnalysisCueRegex.IsMatch(stepText);
        bool vacuumHere = VacuumDistillationRegex.IsMatch(stepText);
        if (analyticalHere)
        {
            flags.Add(ProcedureStateFlags.AnalyticalContext);
            flags.Add(ProcedureStateFlags.ProductProperty);
            // Distillation/ProductProperty contexts must not inherit reaction
            // temperatures: a "bp 119 C at 1 mmHg" line should not pass the
            // reaction's prior 80 C forward, and the step's own measured value
            // must not be treated as a reaction condition.
            tempC = null;
            symbolicTemp = null;
        }
        if (vacuumHere)
        {
            flags.Add(ProcedureStateFlags.VacuumDistillation);
            tempC = null;
        }

        // Scope terminator: cool/filter/wash/dry/extract/concentrate/
        // chromatograph/isolate/yield/afford end an active reaction-heating
        // scope so subsequent cues are not interpreted as still-refluxing.
        bool terminatorHere = ScopeTerminatorRegex.IsMatch(stepText)
            || analyticalHere
            || vacuumHere;
        if (terminatorHere)
        {
            if (flags.Contains(ProcedureStateFlags.Refluxing))
            {
                flags.Remove(ProcedureStateFlags.Refluxing);
                transitions.Add(new StateTransition(stepIndex,
                    StateTransitionKind.FlagCleared, "Reflux scope ended", "refluxing", null));
            }
            if (flags.Contains(ProcedureStateFlags.Heating))
            {
                flags.Remove(ProcedureStateFlags.Heating);
            }
            if (string.Equals(symbolicTemp, "reflux", StringComparison.OrdinalIgnoreCase))
            {
                symbolicTemp = null;
            }
        }

        if (RefluxRegex.IsMatch(stepText) && !terminatorHere)
        {
            flags.Add(ProcedureStateFlags.Refluxing);
            // Bare "heated to reflux" with no numeric temp this step must not
            // inherit a stale temperature (e.g. RT 25 °C carried from a prior
            // "stir at room temperature overnight" clause).
            if (!tempSetThisStep)
            {
                tempC = null;
            }
            symbolicTemp ??= "reflux";
        }
        if (CoolHeatRegex.IsMatch(stepText))
        {
            flags.Add(ProcedureStateFlags.ThermalChange);
        }

        // ── Apparatus / pressure cues ────────────────────────────────
        bool addedPressureContext = false;
        if (MicrowaveRegex.IsMatch(stepText))
        {
            flags.Add(ProcedureStateFlags.Microwave);
            // microwave reactors are inherently sealed pressure vessels
            sealedSystem = true;
            addedPressureContext = true;
        }
        if (OilBathRegex.IsMatch(stepText))
        {
            flags.Add(ProcedureStateFlags.OilBath);
        }
        if (AutoclaveRegex.IsMatch(stepText))
        {
            flags.Add(ProcedureStateFlags.Autoclave);
            sealedSystem = true;
            addedPressureContext = true;
        }
        if (PressureVesselRegex.IsMatch(stepText))
        {
            flags.Add(ProcedureStateFlags.PressureVessel);
            sealedSystem = true;
            addedPressureContext = true;
        }
        if (ElevatedPressureRegex.IsMatch(stepText))
        {
            flags.Add(ProcedureStateFlags.ElevatedPressure);
            sealedSystem = true;
            addedPressureContext = true;
        }
        if (InertPressureRegex.IsMatch(stepText))
        {
            flags.Add(ProcedureStateFlags.InertPressure);
            sealedSystem = true;
            inert = true;
            addedPressureContext = true;
        }
        if (addedPressureContext)
        {
            transitions.Add(new StateTransition(stepIndex,
                StateTransitionKind.SealedChanged,
                "Sealed/pressurized apparatus context detected", null, "sealed"));
        }

        // Phase progression — explicit phase modeling. Order matters: more
        // specific cues (purification, analysis) win over generic ones; later
        // phases never regress to earlier ones except via Heating <-> Cooling
        // which can interleave during a reaction.
        ReactionPhase newPhase = phase;
        bool quenchHere = QuenchCueRegex.IsMatch(stepText);
        bool extractionHere = ExtractionCueRegex.IsMatch(stepText);
        bool heatingHere = HeatingCueRegex.IsMatch(stepText) || flags.Contains(ProcedureStateFlags.Refluxing);
        bool coolingHere = CoolingCueRegex.IsMatch(stepText);

        if (AnalysisCueRegex.IsMatch(stepText) && phase >= ReactionPhase.Workup)
        {
            newPhase = ReactionPhase.Analysis;
        }
        else if (PurificationCueRegex.IsMatch(stepText))
        {
            newPhase = ReactionPhase.Purification;
        }
        else if (extractionHere && phase < ReactionPhase.Purification)
        {
            newPhase = ReactionPhase.Extraction;
        }
        else if (quenchHere && phase < ReactionPhase.Extraction)
        {
            newPhase = ReactionPhase.Quench;
        }
        else if (WorkupCueRegex.IsMatch(stepText) && phase < ReactionPhase.Workup)
        {
            // Workup is the umbrella phase between Quench/Extraction and Purification.
            newPhase = ReactionPhase.Workup;
        }
        else if (coolingHere && phase >= ReactionPhase.Reaction && phase < ReactionPhase.Quench)
        {
            newPhase = ReactionPhase.Cooling;
            flags.Add(ProcedureStateFlags.Cooling);
        }
        else if (heatingHere && phase >= ReactionPhase.Reaction && phase < ReactionPhase.Quench)
        {
            newPhase = ReactionPhase.Heating;
            flags.Add(ProcedureStateFlags.Heating);
        }
        else if (phase == ReactionPhase.Setup && (reagents.Count > 0 || solvents.Count > 0))
        {
            newPhase = ReactionPhase.Reaction;
        }

        // Once we have transitioned past Quench at any prior step, propagate
        // post_quench so phase-aware suppressors (e.g. dryness vs. aqueous
        // workup) can recognise the regime change.
        if (phase >= ReactionPhase.Quench || newPhase >= ReactionPhase.Quench)
        {
            flags.Add(ProcedureStateFlags.PostQuench);
        }

        if (newPhase != phase)
        {
            transitions.Add(new StateTransition(stepIndex,
                StateTransitionKind.PhaseChanged,
                $"Phase: {phase} -> {newPhase}", phase.ToString(), newPhase.ToString()));
            phase = newPhase;
        }

        // Once we add a solvent again after concentration, dryness flag clears
        if (drynessConcentrated && solvents.Count > 0 && AddSolventRegex.IsMatch(stepText))
        {
            drynessConcentrated = false;
        }

        OperationType operation = ClassifyOperation(stepText, phase, flags, vacuumHere, analyticalHere);

        return (new ProcedureState
        {
            StepIndex = stepIndex,
            CurrentSolvents = solvents,
            CurrentAtmosphere = atmosphere,
            CurrentTemperatureCelsius = tempC,
            CurrentSymbolicTemperature = symbolicTemp,
            CurrentPressureAtm = pressure,
            CurrentVessel = vessel,
            CurrentPhase = phase,
            IsSealedSystem = sealedSystem,
            IsUnderInertAtmosphere = inert,
            IsDryEnvironment = dry,
            IsConcentratedToDryness = drynessConcentrated,
            CurrentReagents = reagents,
            CurrentFlags = flags,
            CurrentOperation = operation
        }, transitions);
    }

    // Lightweight operation-type classifier. Order matters: more specific
    // terminal/measurement contexts win over general reaction cues.
    private static readonly Regex DistillationCueRegex = new(
        @"\b(distill(?:ed|ing|ation)?|short[- ]path|kugelrohr|fractional\s+distillation)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DryingCueRegex = new(
        @"\b(dr(?:ied|ying)\s+(?:over|with|under)|MgSO4|Na2SO4|molecular\s+sieves|in\s+(?:a\s+)?desiccator|over\s+(?:anhydrous\s+)?(?:sodium\s+sulfate|magnesium\s+sulfate))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WashCueRegex = new(
        @"\b(wash(?:ed|ing)?\s+(?:with|using)|brine\s+wash)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MonitoringCueRegex = new(
        @"\b(TLC|monitor(?:ed|ing)?\s+by|followed\s+by\s+TLC|reaction\s+progress)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IsolationCueRegex = new(
        @"\b(isolat(?:ed|ing|ion)?|afford(?:ed|ing|s)?|obtained?\s+as|gave\b|yield\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static OperationType ClassifyOperation(
        string stepText, ReactionPhase phase, IReadOnlySet<string> flags,
        bool vacuumHere, bool analyticalHere)
    {
        // Terminal/measurement contexts first
        if (analyticalHere && !vacuumHere && !DistillationCueRegex.IsMatch(stepText))
        {
            // Pure analytical (NMR/MS/mp without distillation cues)
            return ProductPropertyCueRegex.IsMatch(stepText)
                ? OperationType.ProductProperty
                : OperationType.Analysis;
        }
        if (vacuumHere || DistillationCueRegex.IsMatch(stepText))
        {
            return OperationType.Distillation;
        }
        if (MonitoringCueRegex.IsMatch(stepText))
        {
            return OperationType.Monitoring;
        }
        if (PurificationCueRegex.IsMatch(stepText))
        {
            return OperationType.Purification;
        }
        if (ExtractionCueRegex.IsMatch(stepText))
        {
            return OperationType.Extraction;
        }
        if (WashCueRegex.IsMatch(stepText))
        {
            return OperationType.Wash;
        }
        if (DryingCueRegex.IsMatch(stepText))
        {
            return OperationType.Drying;
        }
        if (QuenchCueRegex.IsMatch(stepText))
        {
            return OperationType.Quench;
        }
        if (ConcentrationToDrynessRegex.IsMatch(stepText))
        {
            return OperationType.Concentration;
        }
        if (RefluxRegex.IsMatch(stepText) || flags.Contains(ProcedureStateFlags.Refluxing))
        {
            return OperationType.Reflux;
        }
        if (CoolingCueRegex.IsMatch(stepText))
        {
            return OperationType.Cooling;
        }
        if (HeatingCueRegex.IsMatch(stepText))
        {
            return OperationType.Heating;
        }
        if (IsolationCueRegex.IsMatch(stepText) && phase >= ReactionPhase.Workup)
        {
            return OperationType.Isolation;
        }
        return OperationType.Reaction;
    }
}
