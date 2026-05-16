# ChemVerify Architecture Roadmap

Purpose: Define the long-term development path and prevent feature drift.

ChemVerify is a deterministic scientific verification and governance engine.

It does NOT generate scientific knowledge.
It evaluates scientific knowledge produced by:

* humans
* AI systems
* papers
* SOPs
* ELNs
* automated workflows

The system answers a single question:

> "Should a scientist trust this output?"

The product evolves in layered capability stages.
Each stage depends on the reliability of the previous one.

We NEVER skip layers.

---

# Core Philosophy

ChemVerify separates two roles:

Producer → generates statements
Verifier → evaluates statements

ChemVerify must remain:

* deterministic
* reproducible
* explainable
* evidence-linked
* model-agnostic
* audit-friendly

If a feature reduces determinism, traceability, or reproducibility, it is rejected.

---

# High-Level Architecture

```text
Scientific Text
        ↓
Claim Extraction
        ↓
Canonicalization
        ↓
Procedural State Modeling
        ↓
Operation Segmentation
        ↓
Validator Pipeline
        ↓
Governance Processing
        ↓
Risk Aggregation
        ↓
Structured Audit Artifact
```

---

# Capability Ladder

Each phase exists for a specific reason.

Higher phases depend on lower phases being trusted first.

---

# Phase 1 — Claim Extraction (FOUNDATION)

Goal:
Convert arbitrary scientific text into structured claims.

The system must reliably detect what was stated before evaluating correctness.

## Responsibilities

* identify quantities
* identify units
* identify temperatures
* identify durations
* identify concentrations
* identify yields
* identify citations
* identify chemical tokens
* identify source spans
* identify operation cues
* generate stable artifact hashes

## Output Guarantee

Given identical text:

* identical claims
* identical evidence spans
* identical hashes
* identical extraction artifacts

## Acceptance Criteria

* deterministic output
* no chemistry reasoning
* no plausibility judgement
* only structured interpretation

## Why This Exists

You cannot verify a claim you failed to interpret correctly.

---

# Phase 2 — Internal Consistency (LOGICAL VALIDATION)

Goal:
Determine whether the document contradicts itself.

This layer understands:

* logic
* structure
* consistency

It does NOT require chemistry knowledge.

## Examples

* contradictory temperatures
* contradictory durations
* malformed DOI
* missing number before unit
* dangling scientific notation
* inconsistent citations
* impossible formatting structure
* incomplete scientific claims

## Output Meaning

> "Does the document agree with itself?"

## Acceptance Criteria

* deterministic validation
* no external chemistry knowledge
* purely logical evaluation

---

# Phase 2.5 — Procedural State Modeling (OPERATIONAL CONTEXT)

Goal:
Convert scientific procedures from flat text into structured operational flows.

Scientific procedures are not ordinary prose.
They are implicit process graphs.

Validators become unreliable without operational context.

## Responsibilities

* procedural phase tracking
* operation segmentation
* operation-scoped condition binding
* reagent lifecycle tracking
* reaction/workup separation
* analytical-context isolation
* sequential-operation awareness
* product-property isolation

## Supported Operation Types

Examples include:

* Reaction
* Heating
* Reflux
* Cooling
* Quench
* Extraction
* Washing
* Drying
* Concentration
* Distillation
* Purification
* Analysis
* ProductProperty
* Monitoring
* Isolation

## Examples

* reflux belongs only to local heating scope
* bp/mp/NMR are product-property contexts
* workup water does not invalidate prior dry reaction
* "then heated 20 h" is sequential, not contradictory
* analytical notation is not reaction-state data
* vacuum distillation temperatures are not solvent boiling violations

## Output Meaning

> "What operational context does each claim belong to?"

## Acceptance Criteria

* deterministic segmentation
* operation-local state tracking
* reproducible procedural interpretation
* no mechanistic chemistry inference

## Why This Exists

Most validator false positives originate from context leakage rather than chemistry logic.

Operational scoping is required before meaningful plausibility reasoning becomes possible.

---

# Phase 3 — Text Integrity (SCIENTIFIC QUALITY SIGNALS)

Goal:
Detect signs the scientific text itself may be unreliable.

This layer does NOT determine chemistry correctness.
It detects:

* malformed scientific communication
* hallucination fingerprints
* structural instability

## Examples

* broken LaTeX
* lone °C
* malformed scientific notation
* unsupported comparative statements
* incomplete measurements
* mixed citation styles
* dangling parentheses
* malformed chemical tokens

## Output Meaning

> "Does this resemble reliable scientific writing?"

## Acceptance Criteria

* deterministic pattern analysis
* no external knowledge required
* reproducible quality signals

---

# Phase 4 — Domain Plausibility (CHEMISTRY AWARE)

Goal:
Evaluate whether physical reality could plausibly agree with the procedure.

This is the first chemistry-aware layer.

This phase depends on:

* stable extraction
* trusted operational scoping
* validated consistency logic

## Examples

* impossible reflux conditions
* reagent incompatibility
* impossible workup sequences
* atmosphere inconsistencies
* impossible solvent-temperature relationships
* thermodynamic impossibilities
* conservation violations
* physically implausible conditions

## Output Meaning

> "Could this procedure physically work?"

## Acceptance Criteria

* deterministic chemistry-aware reasoning
* evidence-linked findings
* operation-aware validation
* reproducible risk scoring

## Important Constraint

ChemVerify does NOT attempt:

* route optimization
* reaction prediction
* autonomous synthesis planning

This phase evaluates plausibility, not synthetic creativity.

---

# Phase 5 — External Verification (KNOWLEDGE CROSS-CHECK)

Goal:
Compare extracted claims against external scientific knowledge.

This phase introduces:

* external references
* literature cross-checking
* DOI verification
* known-condition comparison

while preserving:

* auditability
* provenance
* reproducibility

## Examples

* DOI existence
* literature consistency
* known reaction feasibility
* known solvent ranges
* literature-supported yields
* known reagent incompatibilities

## Output Meaning

> "Does the scientific community agree?"

## Important Constraint

External verification must NEVER:

* reduce determinism
* hide provenance
* obscure evidence sources

Every external claim must remain traceable.

---

# Governance Layer

ChemVerify separates:

* raw findings
* adjusted findings
* suppressed findings
* severity classification
* aggregated risk

Validators emit raw findings.

Governance processing determines:

* how findings contribute to risk
* whether findings are downgraded
* whether findings are suppressed
* how findings are clustered
* how findings affect audit reporting

## Governance Responsibilities

* false-positive reduction
* deterministic suppression
* severity weighting
* risk aggregation
* finding clustering
* audit reporting
* policy enforcement

## Design Principle

Governance layers must NEVER destroy traceability.

Suppressed findings remain auditable.

---

# Precision Audit Infrastructure

ChemVerify includes corpus-scale validator auditing infrastructure.

The system can:

* sample findings
* classify likely false positives
* benchmark validator behavior
* analyze corpus-level failure distributions
* support benchmark-driven tuning

## Why This Exists

Validators should evolve through:

* measurable precision analysis
* reproducible benchmark evidence
* structured audit workflows

NOT:

* ad-hoc rule additions

---

# Scoring Philosophy

ChemVerify produces:

> risk scores

NOT:

> truth scores

Truth requires experimentation.

Risk estimates:

* reliability
* coherence
* plausibility
* traceability
* procedural stability

## Risk Increases When

* contradictions exist
* traceability is weak
* procedural context is ambiguous
* chemistry is implausible
* operational states conflict
* findings corroborate each other
* high-severity issues appear

---

# Non-Goals

ChemVerify will NOT:

* predict reaction outcomes
* replace experimental validation
* replace chemists
* generate protocols
* recommend reagents
* optimize syntheses
* act as a chatbot
* hallucinate chemistry knowledge

Those belong to generative AI systems — not verification systems.

---

# Development Rule

Before adding a feature, ask:

> "Does this improve scientific trust evaluation?"

If:

* NO → reject
* MAYBE → postpone
* YES → implement in the correct phase

---

# Current Progress

## Completed / Active

### Phase 1

* deterministic extraction framework
* structured claim modeling
* evidence tracking

### Phase 2

* contradiction detection
* internal consistency validation
* logical validation infrastructure

### Phase 2.5

* procedural phase tracking
* operation-aware segmentation
* reagent lifecycle handling
* operation-scoped condition binding
* analytical-context isolation
* sequential-operation awareness

### Phase 3 (partial)

* scientific integrity signals
* malformed scientific notation detection
* hallucination-style structural detection

### Phase 4 (early)

* solvent plausibility validation
* reagent compatibility validation
* atmosphere consistency
* procedural plausibility checks

### Governance Layer

* severity-aware risk scoring
* deterministic suppressors
* finding clustering
* precision auditing
* benchmark-driven tuning

---

# Current Technical State

Recent benchmark runs on 100k+ Pistachio-derived procedure paragraphs demonstrate:

* deterministic execution
* corpus-scale validation
* operation-aware reasoning
* large-scale precision tuning
* benchmark-driven false-positive reduction

Representative improvements:

| Validator                     |       Before |       After |
| ----------------------------- | -----------: | ----------: |
| SolventTemperatureValidator   | 43,197 fails | 2,169 fails |
| NumericContradictionValidator |  8,317 fails | 3,324 fails |
| ProceduralOrderingValidator   |  8,989 fails |   416 fails |

These reductions were achieved through:

* procedural state modeling
* operation segmentation
* analytical-context isolation
* sequential-operation awareness
* reagent lifecycle handling

NOT through:

* LLM postprocessing
* probabilistic filtering
* non-deterministic heuristics

---

# Final Product Vision

A scientist, lab, company, or regulator can submit a procedure and receive:

* structured audit artifact
* reproducible risk assessment
* evidence-linked findings
* governance-style reporting
* deterministic traceability
* validator explanations
* human-review guidance

ChemVerify does not replace scientific judgement.

It directs scientific attention.
