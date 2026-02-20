# Chemical Verification Engine (ChemVerify)

**A deterministic validation layer for AI-generated chemistry.**

ChemVerify evaluates whether a written chemical procedure is **internally consistent, sufficiently specified, and computationally interpretable** before experimental use.

Unlike predictive chemistry models, ChemVerify does **not** attempt to predict yields or outcomes.  
Instead, it analyzes the *text itself* — extracting quantitative and structural claims and verifying cross-consistency under configurable policy profiles.

---

## Status

**Prototype / reference implementation — actively developed**

Current focus:

- deterministic verification pipeline  
- extensible validator architecture  
- enterprise-style reproducibility and auditability  

---

## What “Chemical Verification” Means

Modern language models can produce chemistry text that appears plausible but is not experimentally coherent:

- missing critical conditions  
- conflicting parameters  
- incompatible reagent environments  
- merged experimental regimes  
- underspecified procedures  

**Chemical verification** is the computational evaluation of whether a written procedure forms a consistent experimental scenario.

ChemVerify treats scientific text as a **structured system of constraints**, not prose.

---

## What ChemVerify Does

Given an input passage (for example an AI-generated synthesis), the engine produces a structured verification report containing:

- **Overall verdict**
- **Severity classification**
- **Risk drivers**
- **Validated vs. questionable claims**
- **Cross-consistency analysis**
- **Evidence spans (traceable to source text)**
- **Policy profile used**
- **Deterministic provenance metadata**

Outputs are intentionally deterministic and reproducible.

---

## Architecture Overview

```

Input Text
↓
Claim Extraction
↓
Canonicalization
↓
Validator Pipeline
↓
Risk Scoring
↓
Structured Report (JSON / SARIF)

````

### Key Principles

- Deterministic execution
- Policy-driven validation
- Reproducible outputs
- Evidence-linked findings
- Extensible rule system

---

## Quick Start

### Prerequisites

- .NET SDK (**.NET 8 recommended**)

### Run API locally

```bash
git clone https://github.com/CannoliBytesLLC/chemverify.git
cd ChemVerify.API
dotnet restore
dotnet run --project ChemVerify.API.csproj
````

---

## CLI Usage

ChemVerify includes a command-line interface suitable for local analysis and CI workflows.

```bash
dotnet run --project ChemVerify.Cli -- analyze input.txt
```

### Options

```
--profile <name>     Policy profile (Default, PharmaStrict, etc.)
--format json|sarif  Output format
--out <file>         Write output to file
```

### Exit Codes

| Code | Meaning                  |
| ---- | ------------------------ |
| 0    | Low risk / OK            |
| 1    | Medium risk              |
| 2    | High risk                |
| 3    | Execution or input error |

---

## Running CLI Tests

The repository includes a deterministic CLI test suite:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestCli.ps1
```

This validates:

* exit code contract
* JSON output structure
* SARIF generation
* deterministic behavior

---

## Policy Profiles

Verification behavior is controlled through **policy profiles**, configurable via JSON.

Profiles define:

* enabled validators
* excluded validators
* severity weights
* retry behavior
* verification strictness

### Resolution Order

1. JSON configuration
2. Built-in defaults
3. Safe fallback profile

---

## Extending ChemVerify — Adding Validators

ChemVerify is designed to be extended through validators.

A validator checks one scientific consistency rule.

Examples:

* contradictory temperatures
* missing solvent conditions
* malformed chemical tokens
* incompatible reagent environments

---

### Step 1 — Create a Validator

Create a class implementing `IValidator`:

```csharp
public sealed class ExampleValidator : IValidator
{
    public string Name => "ExampleValidator";

    public IEnumerable<ValidationFinding> Validate(
        ValidationContext context)
    {
        if (/* rule violated */)
        {
            yield return new ValidationFinding(
                validatorName: Name,
                message: "Example inconsistency detected.");
        }
    }
}
```

---

### Step 2 — Add Metadata (Recommended)

```csharp
[ValidatorMetadata(
    Id = "CHEM001",
    Kind = FindingKind.Contradiction,
    DefaultWeight = 0.25)]
```

Metadata enables:

* consistent scoring
* SARIF rule mapping
* audit traceability

---

### Step 3 — Build

Validators are auto-discovered at startup.

No manual registration required.

---

### Step 4 — Test

Add tests under:

```
ChemVerify.Tests/
```

Include:

* positive case (violation detected)
* negative case (valid input)

---

## Design Goals

ChemVerify aims to become:

* a validation layer for AI-assisted science
* a deterministic audit component
* CI-integratable scientific verification tooling
* an extensible research infrastructure platform

---

## Non-Goals

ChemVerify does **not**:

* predict reaction outcomes
* replace experimental validation
* perform quantum or kinetic simulation

It evaluates **textual scientific coherence only**.

---

## Contributing

Contributions are welcome. Suggested areas:

* new validators
* additional policy profiles
* extraction improvements
* performance optimization
* domain-specific rule packs

---

## Why This Exists

AI can generate chemistry faster than humans can verify it.

ChemVerify explores how deterministic software can restore **verification, traceability, and safety** to AI-assisted scientific workflows.
