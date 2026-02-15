# Chemical Verification Engine (ChemVerify)

A deterministic validation layer for **AI-generated chemistry**.

This project evaluates whether a generated chemical procedure is **internally consistent and sufficiently specified** before experimental use. Instead of predicting outcomes or yields, it analyzes the *description itself* by extracting quantitative claims (e.g., temperature, time, concentration, yield) and citations (e.g., DOI), then checking cross-consistency under a configurable policy profile.

> **Status:** Prototype / reference implementation in active development.

---

## What “Chemical Verification” Means

Modern language models can produce chemistry text that is plausible but not coherent: missing critical conditions, mixing incompatible constraints, or merging multiple experimental regimes into one narrative.

**Chemical verification** is the computational evaluation of whether a written chemical procedure forms a consistent, interpretable experimental scenario.

---

## What This Repo Does Today

Given an input passage (e.g., an AI-generated procedure), the engine produces a structured report:

- **Overall assessment** (verdict)
- **Risk score** (0–1)
- **Findings** (validated claims vs claims requiring more evidence)
- **Interpretation** (e.g., multi-scenario detection)
- **Extracted claims table** (raw text → normalized values → source locations)
- **Verification record** (run id, timestamp, policy profile)

The current system is intentionally deterministic and policy-driven so outputs are reproducible.

---

## Quick Start

### Prerequisites
- .NET SDK (recommended: .NET 8)

### Run locally
```bash
git clone https://github.com/CannoliBytesLLC/chemverify.git
cd ChemVerify.API
dotnet restore
dotnet run --project ChemVerify.API.csproj
