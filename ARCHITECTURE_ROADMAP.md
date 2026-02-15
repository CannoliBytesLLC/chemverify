# ChemVerify Architecture Roadmap
Purpose: Define the long-term development path and prevent feature drift.

ChemVerify is a deterministic scientific verification engine.

It does NOT generate knowledge.
It evaluates knowledge produced by humans or AI.

The system answers a single question:

    "Should a scientist trust this output?"

The product evolves in layered capability stages.
Each stage depends on the reliability of the previous one.

We NEVER skip layers.

---

# Core Philosophy

ChemVerify separates two roles:

Producer → generates statements (AI, human, paper, SOP)
Verifier → evaluates statements (ChemVerify)

ChemVerify must remain:
- deterministic
- reproducible
- explainable
- model-agnostic

If a feature reduces determinism or traceability, it is rejected.

---

# Capability Ladder

---

## Phase 1 — Claim Extraction (FOUNDATION)
Goal: Convert arbitrary scientific text into structured claims.

The system must reliably detect what was stated before evaluating truth.

### Responsibilities
- Identify quantities
- Identify units
- Identify yields
- Identify temperatures
- Identify times
- Identify citations (DOI / references)
- Identify chemical tokens
- Track source locations
- Produce stable artifact hash

### Output Guarantee
Given the same text → identical claims → identical hash

### Acceptance Criteria
- Deterministic output
- No chemistry reasoning
- No correctness judgement
- Only structured interpretation

### Why This Exists
You cannot verify a claim you failed to interpret correctly.

---

## Phase 2 — Internal Consistency (LOGICAL VALIDATION)
Goal: Determine if the text contradicts itself.

This layer knows logic, not chemistry.

### Examples
- 82% yield vs 15% yield
- 5 min vs 2 hours for same step
- 10 mL vs 20 mL same reagent
- malformed DOI
- incomplete measurement
- missing number before unit
- dangling scientific notation
- mixed citation traceability

### Output Meaning
"Does the document agree with itself?"

### Acceptance Criteria
- No external knowledge required
- Purely logical validation
- Deterministic scoring

---

## Phase 3 — Text Integrity (SCIENTIFIC QUALITY SIGNALS)
Goal: Detect signs the text was poorly generated.

This is NOT chemistry correctness.
This detects *hallucination fingerprints*.

### Examples
- empty chemical parentheses
- lone °C
- broken LaTeX fragments
- unsupported comparative claims
- mixed citation styles
- incomplete scientific statements

### Output Meaning
"Does this look like reliable scientific writing?"

---

## Phase 4 — Domain Plausibility (CHEMISTRY AWARE)
Goal: Evaluate whether reality could agree with the text.

This is the first chemistry-aware layer.

### Examples
- impossible stoichiometry
- reagent incompatibility
- thermodynamic impossibility
- conservation violations
- impossible workup sequence

### Output Meaning
"Could this procedure physically work?"

NOTE:
This phase depends on Phases 1-3 being trusted.
Never implement before extraction stability.

---

## Phase 5 — External Verification (KNOWLEDGE CROSS-CHECK)
Goal: Compare claims to known scientific knowledge.

### Examples
- DOI existence
- literature consistency
- known reaction feasibility
- known yield ranges

### Output Meaning
"Does the scientific community agree?"

This phase may use external APIs but must preserve auditability.

---

# Scoring Philosophy

ChemVerify produces a **risk score**, not a truth score.

Truth requires experimentation.
Risk predicts reliability.

Risk increases when:
- contradictions exist
- traceability is weak
- structure is malformed
- chemistry is implausible

---

# Non-Goals

ChemVerify will NOT:
- predict reaction outcomes
- replace a chemist
- generate protocols
- recommend reagents
- act as a chatbot

Those belong to AI systems — not verification systems.

---

# Development Rule

Before adding a feature, ask:

Does this improve scientific trust evaluation?

If NO → reject
If MAYBE → postpone
If YES → implement in correct phase

---

# Current Progress

Completed:
- Phase 1 extraction framework
- Phase 2 contradiction detection
- Phase 3 text integrity signals (partial)

Next Target:
Expand Phase 2 coverage before Phase 4 begins.

---

# Final Product Vision

A lab, company, or regulator can paste an AI-generated procedure and receive:

- structured audit artifact
- reproducible risk score
- explanation of concerns
- questions requiring human review

ChemVerify does not replace judgement.
It directs attention.