using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AwesomeCopilot.Evals.AgentInvokers;

/// <summary>
/// Invokes the arch.agent.md (Senior Cloud Architect) agent using an OpenAI-compatible chat client.
/// Supports GitHub Models (https://models.inference.ai.azure.com) and GitHub Copilot API endpoints.
/// </summary>
public sealed class ArchAgentInvoker
{
    /// <summary>
    /// Default GitHub Models endpoint (OpenAI-compatible).
    /// Set GITHUB_MODELS_ENDPOINT to override (e.g. for GitHub Copilot API).
    /// </summary>
    public static readonly string DefaultEndpoint =
        Environment.GetEnvironmentVariable("GITHUB_MODELS_ENDPOINT")
        ?? "https://models.inference.ai.azure.com";

    /// <summary>
    /// Default model to use when invoking the agent.
    /// Set GITHUB_MODELS_MODEL to override.
    /// </summary>
    public static readonly string DefaultModel =
        Environment.GetEnvironmentVariable("GITHUB_MODELS_MODEL")
        ?? "gpt-4o";

    private readonly IChatClient _chatClient;
    private readonly string _systemPrompt;

    /// <summary>
    /// Core constraint from arch.agent.md: the agent must NOT generate any code.
    /// </summary>
    public const string NoCodeConstraint =
        "NO CODE GENERATION: You should NOT generate any code. " +
        "Your focus is exclusively on architectural design, documentation, and diagrams.";

    /// <summary>
    /// Initialises the invoker with a pre-configured <see cref="IChatClient"/> and system prompt.
    /// </summary>
    public ArchAgentInvoker(IChatClient chatClient, string systemPrompt)
    {
        _chatClient = chatClient;
        _systemPrompt = systemPrompt;
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> using the given API key and optional endpoint/model overrides.
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
    /// Creates an <see cref="ArchAgentInvoker"/> from environment variables.
    /// Requires <c>GITHUB_TOKEN</c> to be set.
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
                "Set it to a GitHub personal access token or GitHub Copilot token.");

        var chatClient = CreateChatClient(apiKey);
        var prompt = systemPrompt ?? LoadSystemPromptFromAgentFile();

        return new ArchAgentInvoker(chatClient, prompt);
    }

    /// <summary>
    /// Invokes the arch agent with the provided user message and returns the raw <see cref="ChatResponse"/>.
    /// </summary>
    /// <param name="userMessage">The user's architecture request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The agent's <see cref="ChatResponse"/>.</returns>
    public async Task<(IReadOnlyList<ChatMessage> Messages, ChatResponse Response)> InvokeAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, _systemPrompt),
            new ChatMessage(ChatRole.User, userMessage),
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

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
