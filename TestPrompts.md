# ChemVerify Test Prompts

These prompts are used to validate the behavior of the ChemVerify verification engine.

They represent a mixture of:

- AI-generated synthetic text
- Literature-style procedures
- Conceptual chemistry narratives
- Intentionally flawed procedures

The prompts help evaluate ChemVerify's ability to detect:

- Logical inconsistencies
- Malformed scientific statements
- Missing experimental parameters
- Domain plausibility issues
- Citation integrity

> Some prompts intentionally contain errors or missing information.
> These errors are intentional and are used to verify that ChemVerify validators behave correctly.

---

## Test Prompt 1 — Carbonyl Reduction Literature Summary

**Category:** Scientific narrative (no procedure)

**Purpose:** Tests:

- Citation extraction
- Malformed chemical tokens
- Missing reagent identifiers
- Temperature claims without numeric values

**Prompt:**

> The reduction of carbonyl compounds to their corresponding alcohols represents a cornerstone of organic synthesis, with sodium borohydride () remaining the benchmark reagent for this transformation due to its mildness, safety, and high degree of chemoselectivity (Ward & Rhee, 1989; Lamm et al., 2013).
>
> While is inherently capable of reducing both aldehydes and ketones, literature precedents demonstrate that exceptional selectivity for aldehydes over ketones can be achieved by modulating reaction conditions such as temperature and solvent composition.
>
> Typically, these reductions are conducted in protic media—most commonly methanol or ethanol—which assist in activating the carbonyl group and dissolving the borohydride salt.
>
> Kinetic studies have established the following reactivity hierarchy:
>
> aliphatic aldehydes > aromatic aldehydes > ketones > conjugated enones
>
> For instance, performing the reaction at low temperatures (e.g., °C) in a mixed solvent system of alcohol and dichloromethane allows selective reduction of aldehydes in the presence of ketones.
>
> Recent methodologies also use NaBH₄ supported on wet silica gel, enabling rapid solvent-free reductions.

**References:**

- Lamm, V. et al. *Beilstein Journal of Organic Chemistry* (2013)
- Robinson, R. K.; De Jesus, K. *Journal of Chemical Education* (1996)
- Ward, D. E.; Rhee, C. K. *Canadian Journal of Chemistry* (1989)
- Zeynizadeh, B.; Behyar, T. *Journal of the Brazilian Chemical Society* (2005)

---

## Test Prompt 2 — Multistep Synthesis of Benzocaine

**Category:** Multi-step organic synthesis

**Purpose:** Tests:

- Multistep procedure extraction
- Reagent quantity tracking
- Oxidation chemistry
- Esterification logic
- Nitro reduction sequence

**Prompt:**

### Step 1 — Oxidation of p-Nitrotoluene

> To a mixture of p-nitrotoluene (1.37 g, 10 mmol) and Na₂CO₃ (0.5 g) in 40 mL of water, KMnO₄ (4.74 g, 30 mmol) is added.
>
> The mixture is refluxed for 2 hours until the purple color disappears and MnO₂ precipitates.
>
> After filtration and acidification with HCl, p-nitrobenzoic acid is isolated.
>
> Reference: Vogel's Practical Organic Chemistry.

### Step 2 — Fischer Esterification

> p-Nitrobenzoic acid (1.67 g, 10 mmol) is dissolved in ethanol (20 mL).
>
> Concentrated H₂SO₄ (2 mL) is added and the mixture is refluxed for 1.5 hours.
>
> The mixture is cooled and poured into ice water.
>
> Neutralization with NaHCO₃ precipitates ethyl 4-nitrobenzoate.

### Step 3 — Nitro Reduction

> Ethyl 4-nitrobenzoate (1.95 g, 10 mmol) is treated with SnCl₂·2H₂O (11.2 g, 50 mmol) in ethanol (20 mL).
>
> The mixture is heated to 70 °C for 30 minutes.
>
> After cooling, the solution is adjusted to pH 8–9 using NaOH.
>
> Extraction with ethyl acetate yields benzocaine.

---

## Test Prompt 3 — Grignard Addition

**Category:** Moisture-sensitive organometallic reaction

**Purpose:** Tests:

- Anhydrous condition detection
- Reagent preparation
- Quench step identification
- Reaction workup logic

**Prompt:**

### Step 1 — Grignard Formation

> To a flame-dried flask containing magnesium turnings (0.27 g, 11 mmol) and iodine, a solution of (2-bromoethyl)benzene (1.85 g, 10 mmol) in anhydrous diethyl ether (15 mL) is added dropwise.
>
> The mixture is stirred for 1 hour until magnesium is consumed.

### Step 2 — Carbonyl Addition

> The reaction is cooled to 0 °C.
>
> Acetaldehyde (0.44 g, 10 mmol) in ether (5 mL) is added slowly.
>
> The mixture warms to room temperature and is stirred for 2 hours.

### Step 3 — Quench and Workup

> The reaction is quenched with saturated ammonium chloride solution.
>
> The organic phase is separated and washed with brine.
>
> Drying over MgSO₄ followed by evaporation yields the crude product.
>
> Purification by chromatography affords 4-phenyl-2-butanol.
>
> Yield: 75–82 %

---

## Test Prompt 4 — Conceptual Synthesis Narrative

**Category:** Conceptual synthesis discussion

**Purpose:** Tests:

- Scientific narrative parsing
- Structural reasoning extraction
- Absence of explicit procedure

**Prompt:**

> This example describes a six-step asymmetric synthesis of (+)-Hyperaspine.
>
> The synthetic strategy emphasizes early stereochemical installation followed by a ring-forming cyclization that rapidly generates molecular complexity.
>
> Functional groups introduced early serve as latent reactivity handles, enabling later transformations.
>
> The route minimizes protecting groups by relying on chemoselective reactions.
>
> Late-stage transformations focus on oxidation state adjustment and final functionalization.

---

## Test Prompt 5 — Literature Procedure Example

**Category:** Patent-style synthesis text

**Purpose:** Tests:

- Chemical name extraction
- Yield parsing
- Solvent detection
- Reaction duration

**Prompt:**

> A solution of ((1S,2S)-1-{[(4'-methoxymethyl-biphenyl-4-yl)-(2-pyridin-2-yl-cyclopropanecarbonyl)-amino]-methyl}-2-methyl-butyl)-carbamic acid tert-butyl ester (25 mg, 0.045 mmol) in dichloromethane (4 mL) was treated with HCl in dioxane (4 N, 0.5 mL).
>
> The mixture was stirred at room temperature for 12 hours.
>
> The reaction mixture was concentrated to dryness to afford
>
> (1R,2R)-2-pyridin-2-yl-cyclopropanecarboxylic acid ((2S,3S)-2-amino-3-methylpentyl)-(4'-methoxymethyl-biphenyl-4-yl)-amide
>
> (18 mg, 95% yield) as a white solid.

---

## Test Prompt 6 — Contradictory Yield Reporting

**Category:** Internal consistency error

**Purpose:** Tests:

- `YieldMassConsistencyValidator`
- `NumericContradictionValidator`

**Prompt:**

> The reaction produced the desired product in 82 % yield (0.82 g) after purification.
>
> In a subsequent section of the report the isolated yield was described as 15 % (0.15 g) under identical reaction conditions.

---

## Test Prompt 7 — Missing Solvent

**Category:** Incomplete procedure

**Purpose:** Tests:

- `MissingSolventValidator`
- `IncompleteScientificClaimValidator`

**Prompt:**

> To a stirred solution of benzaldehyde (1.06 g, 10 mmol) was added sodium borohydride (0.38 g, 10 mmol).
>
> The mixture was stirred for 30 minutes at room temperature.
>
> After completion the reaction mixture was quenched with water and extracted with ethyl acetate.

---

## Test Prompt 8 — Incompatible Reagents

**Category:** Chemistry plausibility

**Purpose:** Tests:

- `IncompatibleReagentSolventValidator`

**Prompt:**

> Lithium aluminum hydride (0.5 g, 13 mmol) was dissolved in methanol (20 mL) at room temperature.
>
> The reaction mixture was stirred for 10 minutes prior to substrate addition.

---

## Test Prompt 9 — Impossible Stoichiometry

**Category:** Stoichiometric inconsistency

**Purpose:** Tests:

- `EquivalentsConsistencyValidator`

**Prompt:**

> A reaction mixture containing benzaldehyde (1 mmol) and sodium borohydride (0.05 mmol) was stirred for 1 hour.
>
> The reagent was described as 10 equivalents relative to the aldehyde.

---

## Test Prompt 10 — Temperature Conflict

**Category:** Logical contradiction

**Purpose:** Tests:

- `NumericContradictionValidator`

**Prompt:**

> The reaction mixture was maintained at 0 °C for 2 hours.
>
> Later in the same description the mixture was described as being heated to reflux during the same 2-hour period.

---

## Test Prompt 11 — Missing Temperature

**Category:** Incomplete measurement

**Purpose:** Tests:

- `MissingTemperatureWhenImpliedValidator`

**Prompt:**

> The mixture was heated under reflux for 3 hours.
>
> After cooling the solution was stirred at °C for an additional 30 minutes.

---

## Test Prompt 12 — Placeholder Token

**Category:** Text integrity

**Purpose:** Tests:

- `PlaceholderTokenValidator`
- `MalformedChemicalTokenValidator`

**Prompt:**

> The reaction was performed using [REAGENT_NAME] (2.0 g) and [SOLVENT] (20 mL) under standard conditions.
>
> The product was isolated after purification.

---

## Recommended Usage

These prompts are intended for:

- Validator regression testing
- Extraction benchmarking
- UI demonstration
- Example report generation
- CI stability testing

Each prompt should produce deterministic verification output.

---

## Future Prompt Categories

Future test prompts should include:

- Conflicting reaction times
- Ambiguous reagent identity
- Unsupported scientific claims
- Malformed citations
- Unrealistic reaction conditions
- Impossible thermodynamic outcomes

Each prompt should define:

1. Category
2. Purpose
3. Expected validator behavior