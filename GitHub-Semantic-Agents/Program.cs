using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;
using DotNetEnv;
using System.Linq;

// STEP 1: Build the Semantic Kernel with your LLM (OpenAI or Ollama)
var builder = Kernel.CreateBuilder();
Env.Load();
builder.AddOpenAIChatCompletion(
    modelId: "gpt-5-nano-2025-08-07",
    apiKey: Env.GetString("OPENAI_API_KEY"));
var kernel = builder.Build();

// STEP 2: Connect to an MCP server using stdio transport
await using McpClient mcpClient = await McpClient.CreateAsync(
    new StdioClientTransport(new()
    {
        Name = "GitHub",
        Command = "npx",
        Arguments = new[] { "-y", "@modelcontextprotocol/server-github" }
    })
);

// STEP 3: Retrieve all MCP tools exposed by the server
var tools = await mcpClient.ListToolsAsync();

// STEP 4: Create a separate kernel for GitHub agent with MCP tools
var githubKernel = kernel.Clone();
githubKernel.Plugins.AddFromFunctions(
    "GitHub",
    tools.Select(t => t.AsKernelFunction())
);

// STEP 5: Define agents for agent-to-agent collaboration
const string OrchestratorAgentName = "Orchestrator";
const string GithubAgentName = "GitHubSpecialist";

var orchestratorAgent = new ChatCompletionAgent
{
    Name = OrchestratorAgentName,
    Instructions =
        """
        You coordinate with the GitHubSpecialist to answer user questions about GitHub repositories.
        You do NOT have access to GitHub tools - only the GitHubSpecialist does.
        
        FIRST MESSAGE: When you receive a user question about a GitHub repository, respond with: "Asking GitHubSpecialist to [specific action like 'fetch the latest commits from repo X' or 'get information about repo Y']"
        
        WAIT for the GitHubSpecialist to retrieve and share the data with you.
        
        FINAL MESSAGE: After the GitHubSpecialist provides the data, analyze it and provide a brief answer (2-3 sentences) in this format: "From what the GitHubSpecialist provided, I can deduce that [your concise answer based on the data]. Do you want me to do something else?"
        
        IMPORTANT: You can only work with information that GitHubSpecialist shares with you in the conversation.
        """,
    Kernel = kernel,  // Orchestrator has NO GitHub tools
    Arguments = new KernelArguments(
        new OpenAIPromptExecutionSettings
        {
        })
};

var githubAgent = new ChatCompletionAgent
{
    Name = GithubAgentName,
    Instructions =
        """
        You are the ONLY agent with access to GitHub MCP tools through the `GitHub` plugin.
        
        When the Orchestrator asks you to fetch data:
        1. Use the appropriate MCP tool (list_commits, get_file_contents, search_repositories, etc.)
        2. Extract the key information from the tool results
        3. Respond with: "Returned data successfully. Here's what I found: [provide a clear summary of the data including relevant details like commit messages, file contents, repo description, etc.]"
        
        IMPORTANT: Include the actual data in your response so the Orchestrator can see it!
        
        If the tool fails, respond with: "Failed to retrieve data: [error message]"
        """,
    Kernel = githubKernel,  // Only GitHubSpecialist has GitHub tools
    Arguments = new KernelArguments(
        new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        })
};

// STEP 6: Create the agent chat with selection and termination strategies
// Create a selection strategy: Orchestrator -> GitHub -> Orchestrator
var selectionFunction = AgentGroupChat.CreatePromptFunctionForStrategy(
    $$$"""
    Determine which agent should respond next based on the conversation history.
    
    Rules:
    - If the last message is from the user, choose {{{OrchestratorAgentName}}} to announce the action.
    - If the last message is from {{{OrchestratorAgentName}}} and contains "Asking GitHubSpecialist", choose {{{GithubAgentName}}} to execute.
    - If the last message is from {{{GithubAgentName}}}, choose {{{OrchestratorAgentName}}} to provide the final answer.
    
    Respond with ONLY the agent name, nothing else.
    
    Last message: {{$lastmessage}}
    """,
    safeParameterNames: "lastmessage");

var terminationFunction = AgentGroupChat.CreatePromptFunctionForStrategy(
    $$$"""
    Check if the conversation should end. Return 'true' if the last message is from {{{OrchestratorAgentName}}} and contains "Do you want me to do something else?", otherwise 'false'.
    
    Last message: {{$lastmessage}}
    """,
    safeParameterNames: "lastmessage");

var chat = new AgentGroupChat(orchestratorAgent, githubAgent)
{
    ExecutionSettings = new AgentGroupChatSettings
    {
        SelectionStrategy = new KernelFunctionSelectionStrategy(selectionFunction, kernel)
        {
            InitialAgent = orchestratorAgent, // Start with Orchestrator to announce action
            ResultParser = (result) => result.GetValue<string>()?.Trim() ?? OrchestratorAgentName,
            HistoryVariableName = "lastmessage"
        },
        TerminationStrategy = new KernelFunctionTerminationStrategy(terminationFunction, kernel)
        {
            Agents = [orchestratorAgent],  // Only check orchestrator's final message
            ResultParser = (result) => result.GetValue<string>()?.Contains("true", StringComparison.OrdinalIgnoreCase) ?? false,
            HistoryVariableName = "lastmessage",
            MaximumIterations = 3  // Orchestrator -> GitHub -> Orchestrator
        }
    }
};

Console.WriteLine("\nMulti-agent MCP chat ready!");
Console.WriteLine("Type 'exit' to quit, 'reset' to clear history, or ask questions about GitHub repositories.\n");

// STEP 7: Run the interactive chat loop
bool isRunning = true;

while (isRunning)
{
    Console.Write("You > ");
    string? userInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(userInput))
    {
        continue;
    }

    userInput = userInput.Trim();

    if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        isRunning = false;
        Console.WriteLine("Goodbye!");
        break;
    }

    if (userInput.Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        await chat.ResetAsync();
        Console.WriteLine("[Chat history cleared]\n");
        continue;
    }

    // Add user message to chat
    chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, userInput));

    Console.WriteLine();

    try
    {
        // Run the agent chat
        await foreach (var message in chat.InvokeAsync())
        {
            if (string.IsNullOrWhiteSpace(message.Content))
            {
                continue;
            }

            var author = string.IsNullOrWhiteSpace(message.AuthorName) ? "Agent" : message.AuthorName;
            // Only show messages from the Orchestrator to the user
            if (author == OrchestratorAgentName)
            {
                Console.WriteLine($"[{author}] {message.Content}\n");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}\n");
    }

    // Reset chat history after each question to prevent accumulation and token issues
    await chat.ResetAsync();

    // Reset the completion flag for next turn
    chat.IsComplete = false;
}
