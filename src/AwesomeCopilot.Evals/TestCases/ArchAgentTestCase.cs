using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Xunit;
using AwesomeCopilot.Evals.AgentInvokers;

namespace AwesomeCopilot.Evals.TestCases;

/// <summary>
/// Evaluation tests for the arch.agent.md (Senior Cloud Architect) agent.
///
/// These tests verify that the agent:
///   1. Produces architecture-only output (no code blocks).
///   2. Includes Mermaid diagrams in its response.
///   3. Produces coherent, relevant responses (AI-judged — requires GITHUB_TOKEN).
///
/// Tests are skipped automatically when GITHUB_TOKEN is not set, allowing the project
/// to build and run in CI without requiring live credentials.
/// </summary>
public class ArchAgentTestCase
{
    // -------------------------------------------------------------------------
    // Test prompt: ask the arch agent to design a simple system
    // -------------------------------------------------------------------------
    private const string TestUserPrompt =
        "Design the architecture for a simple REST API todo application. " +
        "Include a system context diagram, component diagram, and deployment diagram " +
        "using Mermaid syntax. Do not generate any code.";

    // -------------------------------------------------------------------------
    // Architecture-only constraint tests (no AI judge required)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that the agent response does not contain fenced code blocks
    /// in programming languages (the core constraint of arch.agent.md).
    /// Mermaid diagram blocks (```mermaid) are explicitly permitted.
    /// </summary>
    [SkippableFact(DisplayName = "Arch agent response must not contain code blocks")]
    public async Task ArchAgent_Response_ShouldNotContainCodeBlocks()
    {
        SkipIfNoApiKey();

        var invoker = ArchAgentInvoker.CreateFromEnvironment();
        var (_, response) = await invoker.InvokeAsync(TestUserPrompt);

        var responseText = response.Text ?? string.Empty;

        AssertNoCodeBlocks(responseText);
    }

    /// <summary>
    /// Verifies that the agent response contains at least one Mermaid diagram block,
    /// as required by arch.agent.md.
    /// </summary>
    [SkippableFact(DisplayName = "Arch agent response must contain Mermaid diagrams")]
    public async Task ArchAgent_Response_ShouldContainMermaidDiagrams()
    {
        SkipIfNoApiKey();

        var invoker = ArchAgentInvoker.CreateFromEnvironment();
        var (_, response) = await invoker.InvokeAsync(TestUserPrompt);

        var responseText = response.Text ?? string.Empty;

        Assert.Contains("```mermaid", responseText, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // AI-judged quality evaluations (require GITHUB_TOKEN + evaluator model)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Evaluates the coherence of the arch agent's response using
    /// <see cref="CoherenceEvaluator"/> from Microsoft.Extensions.AI.Evaluation.Quality.
    /// A coherent response scores >= 3 out of 5.
    /// </summary>
    [SkippableFact(DisplayName = "Arch agent response coherence score should be acceptable")]
    public async Task ArchAgent_Response_ShouldBeCoherent()
    {
        SkipIfNoApiKey();

        var invoker = ArchAgentInvoker.CreateFromEnvironment();
        var (messages, response) = await invoker.InvokeAsync(TestUserPrompt);

        var judgeClient = CreateJudgeChatClient();
        var chatConfig = new ChatConfiguration(judgeClient);

        var evaluator = new CoherenceEvaluator();
        var result = await evaluator.EvaluateAsync(messages, response, chatConfig);

        var coherenceMetric = result.Get<NumericMetric>(CoherenceEvaluator.CoherenceMetricName);

        Assert.NotNull(coherenceMetric.Value);
        Assert.True(
            coherenceMetric.Value >= 3,
            $"Expected coherence score >= 3 but got {coherenceMetric.Value}. " +
            $"Reason: {coherenceMetric.Reason}");
    }

    /// <summary>
    /// Evaluates the relevance of the arch agent's response using
    /// <see cref="RelevanceEvaluator"/> from Microsoft.Extensions.AI.Evaluation.Quality.
    /// A relevant response scores >= 3 out of 5.
    /// </summary>
    [SkippableFact(DisplayName = "Arch agent response relevance score should be acceptable")]
    public async Task ArchAgent_Response_ShouldBeRelevant()
    {
        SkipIfNoApiKey();

        var invoker = ArchAgentInvoker.CreateFromEnvironment();
        var (messages, response) = await invoker.InvokeAsync(TestUserPrompt);

        var judgeClient = CreateJudgeChatClient();
        var chatConfig = new ChatConfiguration(judgeClient);

        var evaluator = new RelevanceEvaluator();
        var result = await evaluator.EvaluateAsync(messages, response, chatConfig);

        var relevanceMetric = result.Get<NumericMetric>(RelevanceEvaluator.RelevanceMetricName);

        Assert.NotNull(relevanceMetric.Value);
        Assert.True(
            relevanceMetric.Value >= 3,
            $"Expected relevance score >= 3 but got {relevanceMetric.Value}. " +
            $"Reason: {relevanceMetric.Reason}");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the AI judge client used by quality evaluators.
    /// Uses the same GitHub Models / Copilot API endpoint as the agent itself.
    /// </summary>
    private static IChatClient CreateJudgeChatClient()
    {
        var apiKey = Environment.GetEnvironmentVariable("GITHUB_TOKEN")!;
        return ArchAgentInvoker.CreateChatClient(apiKey);
    }

    /// <summary>
    /// Asserts that the response text does not contain fenced code blocks in programming
    /// languages. Mermaid blocks (```mermaid) are explicitly permitted.
    /// </summary>
    private static void AssertNoCodeBlocks(string responseText)
    {
        // Collect all fenced code-block language identifiers (e.g. ```csharp, ```c#, ```json5)
        // anchored at the start of each line so inline backticks are not mismatched.
        var codeBlockLanguages = new System.Text.RegularExpressions.Regex(
            @"^```([a-zA-Z][a-zA-Z0-9+#\-]*)",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        var matches = codeBlockLanguages.Matches(responseText);

        var disallowedLanguages = matches
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Where(lang => lang != "mermaid")
            .Distinct()
            .ToList();

        Assert.True(
            disallowedLanguages.Count == 0,
            $"Arch agent response contained code blocks in disallowed languages: " +
            $"{string.Join(", ", disallowedLanguages)}. " +
            $"The arch.agent.md constraint forbids code generation.");
    }

    /// <summary>
    /// Skips the current test if the <c>GITHUB_TOKEN</c> environment variable is not set.
    /// This allows the evaluation tests to be gracefully skipped in environments without
    /// credentials, while running fully in CI when credentials are available.
    /// </summary>
    private static void SkipIfNoApiKey()
    {
        var apiKey = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        Skip.If(
            string.IsNullOrWhiteSpace(apiKey),
            "GITHUB_TOKEN is not set. Set GITHUB_TOKEN to a GitHub personal access token " +
            "to run this test against a live AI endpoint.");
    }
}
