using System.ClientModel;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AwesomeCopilot.Evals.AgentInvokers;

/// <summary>
/// Invokes the arch.agent.md (Senior Cloud Architect) agent using the official GitHub Copilot SDK.
/// The AI-judge client used by quality evaluators is created separately via <see cref="CreateChatClient"/>.
/// </summary>
public sealed class ArchAgentInvoker
{
    /// <summary>
    /// GitHub Models endpoint used by the AI-judge chat client.
    /// Set <c>GITHUB_MODELS_ENDPOINT</c> to override (e.g. for the GitHub Copilot API).
    /// </summary>
    public static readonly string DefaultEndpoint =
        Environment.GetEnvironmentVariable("GITHUB_MODELS_ENDPOINT")
        ?? "https://models.inference.ai.azure.com";

    /// <summary>
    /// Model identifier used both for Copilot sessions and the AI-judge client.
    /// Set <c>GITHUB_MODELS_MODEL</c> to override.
    /// </summary>
    public static readonly string DefaultModel =
        Environment.GetEnvironmentVariable("GITHUB_MODELS_MODEL")
        ?? "gpt-4o";

    private readonly CopilotClientOptions _clientOptions;
    private readonly string _model;
    private readonly string _systemPrompt;

    /// <summary>
    /// Core constraint from arch.agent.md: the agent must NOT generate any code.
    /// </summary>
    public const string NoCodeConstraint =
        "NO CODE GENERATION: You should NOT generate any code. " +
        "Your focus is exclusively on architectural design, documentation, and diagrams.";

    /// <summary>
    /// Initialises the invoker with the given <see cref="CopilotClientOptions"/>, system prompt,
    /// and model identifier.
    /// </summary>
    public ArchAgentInvoker(CopilotClientOptions clientOptions, string systemPrompt, string model)
    {
        _clientOptions = clientOptions;
        _systemPrompt = systemPrompt;
        _model = model;
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> for use by AI-judge quality evaluators.
    /// Uses the GitHub Models endpoint (OpenAI-compatible).
    /// </summary>
    /// <param name="apiKey">The API key (e.g. GitHub personal access token).</param>
    /// <param name="endpoint">Endpoint URI. Defaults to <see cref="DefaultEndpoint"/>.</param>
    /// <param name="model">Model identifier. Defaults to <see cref="DefaultModel"/>.</param>
    /// <returns>A configured <see cref="IChatClient"/>.</returns>
    public static IChatClient CreateChatClient(string apiKey, string? endpoint = null, string? model = null)
    {
        var resolvedEndpoint = endpoint ?? DefaultEndpoint;
        var resolvedModel = model ?? DefaultModel;

        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(resolvedEndpoint) });

        return openAIClient.GetChatClient(resolvedModel).AsIChatClient();
    }

    /// <summary>
    /// Creates an <see cref="ArchAgentInvoker"/> from environment variables using the
    /// official GitHub Copilot SDK. Requires <c>GITHUB_TOKEN</c> to be set.
    /// </summary>
    /// <param name="systemPrompt">
    /// The system prompt to use. When <c>null</c>, the prompt is loaded from <c>agents/arch.agent.md</c>.
    /// </param>
    /// <returns>A configured <see cref="ArchAgentInvoker"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the <c>GITHUB_TOKEN</c> environment variable is not set.
    /// </exception>
    public static ArchAgentInvoker CreateFromEnvironment(string? systemPrompt = null)
    {
        var apiKey = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? throw new InvalidOperationException(
                "GITHUB_TOKEN environment variable is not set. " +
                "Set it to a GitHub personal access token with GitHub Copilot access.");

        var options = new CopilotClientOptions { GitHubToken = apiKey };
        var prompt = systemPrompt ?? LoadSystemPromptFromAgentFile();

        return new ArchAgentInvoker(options, prompt, DefaultModel);
    }

    /// <summary>
    /// Invokes the arch agent via the GitHub Copilot SDK and returns the response alongside
    /// the reconstructed conversation history for use by downstream evaluators.
    /// </summary>
    /// <param name="userMessage">The user's architecture request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A tuple of the conversation messages (system + user) and the agent's
    /// <see cref="ChatResponse"/>, compatible with <c>Microsoft.Extensions.AI.Evaluation</c> evaluators.
    /// </returns>
    public async Task<(IReadOnlyList<ChatMessage> Messages, ChatResponse Response)> InvokeAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        await using var client = new CopilotClient(_clientOptions);
        await client.StartAsync(cancellationToken);

        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = _model,
            SystemMessage = new SystemMessageConfig { Content = _systemPrompt },
            // ApproveAll is safe here: arch.agent.md is a documentation-only agent that does
            // not invoke any tools, so no tool calls will be requested during evaluation.
            OnPermissionRequest = PermissionHandler.ApproveAll,
        }, cancellationToken);

        var assistantMessage = await session.SendAndWaitAsync(
            new MessageOptions { Prompt = userMessage },
            timeout: null,
            cancellationToken: cancellationToken);

        if (assistantMessage is null)
            throw new InvalidOperationException(
                "The Copilot agent returned no response. " +
                "Verify that GITHUB_TOKEN has the required Copilot access and the endpoint is reachable.");

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, _systemPrompt),
            new ChatMessage(ChatRole.User, userMessage),
        };

        var response = new ChatResponse(
            new ChatMessage(ChatRole.Assistant, assistantMessage.Data?.Content ?? string.Empty));

        return (messages, response);
    }

    /// <summary>
    /// Loads the system prompt from the <c>agents/arch.agent.md</c> file in the repository,
    /// stripping any YAML front matter before returning the content.
    /// </summary>
    /// <param name="agentFilePath">
    /// Explicit path to the agent file. When <c>null</c>, the file is located by walking up
    /// from <see cref="AppContext.BaseDirectory"/> until <c>agents/arch.agent.md</c> is found.
    /// </param>
    /// <returns>The system prompt extracted from the agent file.</returns>
    public static string LoadSystemPromptFromAgentFile(string? agentFilePath = null)
    {
        var path = agentFilePath ?? FindAgentFilePath();
        var content = File.ReadAllText(path);
        return StripFrontMatter(content);
    }

    /// <summary>
    /// Walks up the directory tree from <see cref="AppContext.BaseDirectory"/> to locate
    /// <c>agents/arch.agent.md</c>.
    /// </summary>
    private static string FindAgentFilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "agents", "arch.agent.md");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not find agents/arch.agent.md. " +
            "Ensure the test is run from within the repository.");
    }

    /// <summary>
    /// Removes YAML front matter (delimited by <c>---</c> lines) from the beginning of
    /// <paramref name="content"/> and returns the remaining text.
    /// </summary>
    private static string StripFrontMatter(string content)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        if (lines[0].Trim() != "---")
            return content;

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
                return string.Join("\n", lines.Skip(i + 1)).TrimStart('\n');
        }

        return content;
    }
}
