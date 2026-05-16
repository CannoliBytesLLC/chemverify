# Chemical Verification Engine (ChemVerify)

**A deterministic scientific governance and verification engine for chemistry procedures.**

ChemVerify evaluates whether written chemical procedures are:

* internally consistent
* procedurally coherent
* sufficiently specified
* computationally interpretable

before experimental use.

Unlike predictive chemistry systems, ChemVerify does **not** attempt to predict reaction outcomes, optimize yields, or simulate chemistry.

Instead, it treats scientific procedures as structured operational systems and evaluates the *text itself* through deterministic validation and cross-consistency analysis.

---

# Status

**Active prototype / research infrastructure**

Current development focus:

* deterministic scientific validation
* operation-aware procedural reasoning
* validator precision auditing
* governance-style reporting
* large-scale corpus benchmarking
* reproducible scientific QA workflows

---

# What “Chemical Verification” Means

Modern language models can generate chemistry text that appears plausible while still containing:

* conflicting conditions
* merged procedural regimes
* impossible reaction states
* missing operational details
* incompatible reagent environments
* invalid sequencing
* underspecified workups
* analytical/reaction-state confusion

ChemVerify evaluates whether a written procedure forms a coherent experimental scenario.

The engine treats chemistry procedures as:

> **structured operational constraint systems rather than ordinary prose.**

---

# Current Capabilities

ChemVerify currently supports:

* deterministic scientific validation
* operation-scoped procedural analysis
* procedural phase tracking
* severity-aware risk scoring
* clustered finding aggregation
* validator precision auditing
* evidence-linked findings
* configurable policy profiles
* governance-style audit reporting
* large-scale corpus benchmarking

The engine has been benchmarked against **100,000+ Pistachio-derived chemistry procedure paragraphs** using deterministic execution and corpus-scale validator analytics.

---

# Benchmarking

Recent benchmark runs on a 100k Pistachio-derived procedure corpus:

* ~9 second execution time
* deterministic outputs
* 500+ automated tests
* operation-aware procedural segmentation
* phase-aware validation
* corpus-scale validator auditing

Example validator tuning improvements:

| Validator                     |       Before |       After |
| ----------------------------- | -----------: | ----------: |
| SolventTemperatureValidator   | 43,197 fails | 2,169 fails |
| NumericContradictionValidator |  8,317 fails | 3,324 fails |
| ProceduralOrderingValidator   |  8,989 fails |   416 fails |

False-positive reductions were achieved through:

* operation-scoped validation
* procedural phase tracking
* sequential-operation awareness
* analytical-context suppression
* product-property isolation
* reagent lifecycle modeling
* workup-aware validation logic

---

# Procedural Reasoning

ChemVerify models chemistry procedures as structured operational flows rather than flat text.

Supported procedural contexts include:

* reaction
* heating
* reflux
* cooling
* quench
* extraction
* washing
* drying
* concentration
* distillation
* purification
* analysis
* product characterization
* monitoring

This enables validators to distinguish:

* reaction temperatures vs product boiling points
* sequential operations vs contradictory claims
* analytical notation vs reaction-state data
* workup environments vs reactive environments
* isolated product properties vs active reaction conditions

---

# Deterministic Governance Architecture

ChemVerify is designed as deterministic scientific governance infrastructure.

Core properties:

* reproducible outputs
* explainable findings
* evidence-linked validation
* configurable policy profiles
* audit-friendly reporting
* deterministic execution
* CI/CD compatibility
* corpus-scale benchmarking support

The validation layer intentionally avoids LLM dependence.

---

# What ChemVerify Produces

Given an input procedure, ChemVerify generates structured findings including:

* overall risk assessment
* severity classification
* validator findings
* evidence-linked spans
* procedural context
* clustered issue summaries
* governance metadata
* deterministic provenance data

Supported output formats include:

* JSON
* SARIF

---

# Architecture Overview

```text
Input Procedure
        ↓
Claim Extraction
        ↓
Canonicalization
        ↓
Procedure State Modeling
        ↓
Operation Segmentation
        ↓
Validator Pipeline
        ↓
Governance Processing
        ↓
Risk Scoring
        ↓
Structured Report (JSON / SARIF)
```

---

# Key Design Principles

* deterministic execution
* explainable validation
* evidence traceability
* reproducible outputs
* extensible validator architecture
* policy-driven governance
* corpus-scale performance

---

# Quick Start

## Prerequisites

* .NET SDK (**.NET 8 recommended**)

---

## Clone Repository

```bash
git clone https://github.com/CannoliBytesLLC/chemverify.git
cd chemverify
```

---

## Run API

```bash
dotnet restore
dotnet run --project ChemVerify.API
```

---

# CLI Usage

ChemVerify includes a command-line interface suitable for:

* local analysis
* CI workflows
* corpus benchmarking
* validator auditing
* governance pipelines

Example:

```bash
dotnet run --project ChemVerify.Cli -- analyze input.txt
```

---

## CLI Options

```text
--profile <name>     Policy profile
--format json|sarif  Output format
--out <file>         Output file
```

---

## Exit Codes

| Code | Meaning                    |
| ---- | -------------------------- |
| 0    | Low risk / acceptable      |
| 1    | Medium risk                |
| 2    | High risk                  |
| 3    | Execution or input failure |

---

# Precision Audit Tooling

ChemVerify includes validator precision auditing utilities for corpus-scale analysis.

Audit workflows support:

* validator failure sampling
* false-positive analysis
* structured metadata export
* CSV/JSON audit generation
* benchmark-driven validator tuning
* corpus-level analytics

Example audit targets:

* sequential-operation ambiguity
* analytical notation conflicts
* reagent lifecycle issues
* scope leakage
* product-property confusion
* extraction artifacts

---

# Policy Profiles

Verification behavior is controlled through configurable policy profiles.

Profiles define:

* enabled validators
* excluded validators
* severity weighting
* strictness configuration
* governance behavior
* reporting configuration

Resolution order:

1. JSON configuration
2. Built-in defaults
3. Safe fallback profile

---

# Extending ChemVerify

ChemVerify is designed around extensible validators.

A validator represents one deterministic scientific consistency rule.

Example validator categories:

* contradictory temperatures
* missing solvents
* incompatible reagent environments
* impossible reflux conditions
* invalid procedural sequencing
* malformed scientific notation
* mass/yield inconsistencies
* atmosphere inconsistencies

---

## Creating a Validator

```csharp
public sealed class ExampleValidator : IValidator
{
    public string Name => "ExampleValidator";

    public IEnumerable<ValidationFinding> Validate(
        ValidationContext context)
    {
        if (/* inconsistency detected */)
        {
            yield return new ValidationFinding(
                validatorName: Name,
                message: "Example inconsistency detected.");
        }
    }
}
```

---

## Validator Metadata

```csharp
[ValidatorMetadata(
    Id = "CHEM001",
    Kind = FindingKind.Contradiction,
    DefaultWeight = 0.25)]
```

Metadata supports:

* severity scoring
* SARIF rule mapping
* governance reporting
* audit traceability
* benchmark analytics

---

# Testing

ChemVerify includes deterministic automated testing for:

* validator behavior
* CLI behavior
* JSON structure
* SARIF generation
* governance processing
* regression prevention
* operation-scoped validation

Run tests:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestCli.ps1
```

Current suite:

* 500+ automated tests

---

# Design Goals

ChemVerify aims to become:

* deterministic scientific governance infrastructure
* AI-assisted chemistry QA tooling
* corpus-scale procedural analysis infrastructure
* CI-integratable scientific validation tooling
* extensible scientific audit infrastructure
* benchmark tooling for AI-generated chemistry

---

# Non-Goals

ChemVerify does **not**:

* predict reaction outcomes
* replace experimental validation
* perform quantum simulation
* optimize synthetic routes
* autonomously design chemistry

It evaluates:

> **textual scientific coherence and procedural consistency only.**

---

# Why This Exists

AI systems can generate chemistry faster than humans can reliably verify it.

ChemVerify explores how deterministic software can restore:

* verification
* traceability
* reproducibility
* auditability
* procedural consistency
* governance

to AI-assisted scientific workflows.

---

# Repository

[ChemVerify GitHub](https://github.com/CannoliBytesLLC/chemverify?utm_source=chatgpt.com)
