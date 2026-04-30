# AwesomeCopilot.Evals

A .NET 10 xUnit evaluation project for testing GitHub Copilot agent behaviour using **Microsoft.Extensions.AI.Evaluation**.

## Overview

This project provides a **walking skeleton** for evaluating `agents/arch.agent.md` (Senior Cloud Architect) to verify it follows its defined constraints — specifically that it produces architecture documentation and Mermaid diagrams but **never generates code**.

It establishes the pattern for future agent evaluations across the `awesome-copilot` repository.

## Project Structure

```
src/AwesomeCopilot.Evals/
├── AwesomeCopilot.Evals.csproj   # Project file (.NET 10, xUnit)
├── README.md                      # This file
├── AgentInvokers/
│   └── ArchAgentInvoker.cs        # Invokes arch.agent.md via OpenAI-compatible API
├── TestCases/
│   └── ArchAgentTestCase.cs       # Evaluation tests for arch.agent.md
└── Results/                       # Evaluation result output directory (gitignored)
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A `GITHUB_TOKEN` environment variable with a GitHub personal access token (for live tests)

## Running the Tests

```bash
# Build
dotnet build src/AwesomeCopilot.Evals/

# Run (without GITHUB_TOKEN — all tests are skipped gracefully)
dotnet test src/AwesomeCopilot.Evals/

# Run with live API access
GITHUB_TOKEN=your_token dotnet test src/AwesomeCopilot.Evals/
```

## Configuration

| Environment Variable     | Default                                  | Description                                    |
|--------------------------|------------------------------------------|------------------------------------------------|
| `GITHUB_TOKEN`           | *(required for live tests)*              | GitHub personal access token                   |
| `GITHUB_MODELS_ENDPOINT` | `https://models.inference.ai.azure.com`  | OpenAI-compatible API endpoint                 |
| `GITHUB_MODELS_MODEL`    | `gpt-4o`                                 | Model to use for agent invocation and judging  |

## What Is Evaluated

### arch.agent.md Constraints

| Test | Description | Requires API |
|------|-------------|:---:|
| No code blocks | Response must not contain code blocks in programming languages | ✅ |
| Mermaid diagrams | Response must contain at least one ` ```mermaid ` block | ✅ |
| Coherence | AI-judged coherence score ≥ 3/5 (CoherenceEvaluator) | ✅ |
| Relevance | AI-judged relevance score ≥ 3/5 (RelevanceEvaluator) | ✅ |

### Evaluators Used

Built-in evaluators from `Microsoft.Extensions.AI.Evaluation.Quality`:

- **`CoherenceEvaluator`** — measures how logically consistent and well-structured the response is
- **`RelevanceEvaluator`** — measures how directly the response addresses the user's request

## Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Extensions.AI` | 10.5.0 | `IChatClient` abstraction |
| `Microsoft.Extensions.AI.Evaluation` | 10.5.0 | Core evaluation types |
| `Microsoft.Extensions.AI.Evaluation.Quality` | 10.5.0 | Built-in quality evaluators |
| `Microsoft.Extensions.AI.OpenAI` | 10.5.0 | OpenAI / GitHub Models client |
| `xunit` | 2.9.3 | Test framework |
| `Xunit.SkippableFact` | 1.5.61 | Conditional test skipping |

## Extending the Evaluations

To add evaluations for a new agent:

1. Add a new invoker in `AgentInvokers/` (e.g. `MyAgentInvoker.cs`)
2. Add a new test class in `TestCases/` (e.g. `MyAgentTestCase.cs`)
3. Use built-in evaluators from `Microsoft.Extensions.AI.Evaluation.Quality`
4. Follow the same skip pattern for graceful degradation without credentials

## GitHub Models API

This project uses the [GitHub Models API](https://docs.github.com/en/github-models) as its OpenAI-compatible endpoint. The API is accessed using a GitHub personal access token (`GITHUB_TOKEN`).

To use a different endpoint (e.g. Azure OpenAI, GitHub Copilot API):

```bash
export GITHUB_MODELS_ENDPOINT=https://your-endpoint.openai.azure.com
export GITHUB_TOKEN=your-api-key
```
