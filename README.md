# Semantic Kernel MCP Playground

This is a .NET console application demonstrating agent-to-agent (A2A) collaboration using Semantic Kernel, MCP (Model Context Protocol), and GitHub API integration.

## Features

- **Multi-Agent Chat**: Orchestrator coordinates with GitHubSpecialist for GitHub repository queries
- **MCP Integration**: Uses Model Context Protocol to connect to GitHub tools via stdio transport
- **Interactive Chat Loop**: Ask questions about GitHub repositories in real-time
- **Tool Isolation**: Proper A2A architecture with separate kernels for tool access

## Prerequisites

- .NET 9.0 SDK
- Node.js and npm (for running MCP server)
- OpenAI API key

## Setup

1. **Clone or navigate to the project directory**

2. **Install .NET dependencies**:
   ```bash
   dotnet restore
   ```

3. **Set up environment variables**:
   Create a `.env` file in the project root with:
   ```
   OPENAI_API_KEY=your_openai_api_key_here
   ```

4. **Run the application**:
   ```bash
   dotnet run
   ```

## Usage

The application starts an interactive chat where you can ask questions about GitHub repositories:

- Type questions like: "What is this repo about https://github.com/microsoft/semantic-kernel"
- Type `exit` to quit
- Type `reset` to clear chat history

The Orchestrator agent will coordinate with the GitHubSpecialist to fetch and analyze repository data.

## Architecture

- **Orchestrator**: Coordinates requests and provides final answers (no direct tool access)
- **GitHubSpecialist**: Handles GitHub API calls via MCP tools
- **MCP Server**: Runs `@modelcontextprotocol/server-github` for GitHub integration

## Technologies

- Semantic Kernel v1.66.0
- MCP v0.4.0-preview.2
- OpenAI GPT-5-nano
- .NET 9.0