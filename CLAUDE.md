# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Development Commands

### Building the Project
```bash
# Build in Release mode (default)
dotnet build -c Release

# Build in Debug mode
dotnet build -c Debug

# Clean build
dotnet clean && dotnet build

# Build with PowerShell script (Windows)
.\build.ps1
.\build.ps1 -Configuration Debug
.\build.ps1 -Clean
```

### Running Tests
```bash
# Run all tests
dotnet test Saturn.Tests/Saturn.Tests.csproj

# Run tests with detailed output
dotnet test Saturn.Tests/Saturn.Tests.csproj --verbosity normal

# Run specific test
dotnet test --filter "FullyQualifiedName~TestClassName"
```

### Creating NuGet Package
```bash
# Create NuGet package
dotnet pack -c Release

# Package will be created in ./nupkg directory
```

### Installing Locally as Global Tool
```bash
# Install from local package
dotnet tool install --global --add-source ./nupkg SaturnAgent

# Uninstall if needed
dotnet tool uninstall --global SaturnAgent

# Run the tool
saturn
```

## Architecture Overview

Saturn is a CLI-based coding assistant with multi-agent orchestration capabilities powered by LLM providers (OpenRouter, Anthropic).

### Core Components

1. **Agent System** (`Agents/`)
   - `AgentBase`: Abstract base class for all agents
   - `AgentManager`: Manages multiple agents and orchestration
   - `AgentConfiguration`: Stores agent settings and preferences
   - Mode system for different agent behaviors

2. **Tool System** (`Tools/`)
   - `ITool`: Interface for all tools
   - `ToolBase`: Base class providing common tool functionality
   - `ToolRegistry`: Auto-registers and manages available tools
   - Tools include: file operations (Read, Write, ApplyDiff), search (Glob, Grep), command execution, web fetching
   - Multi-agent tools for agent orchestration

3. **Provider System** (`Providers/`)
   - `ILLMProvider` and `ILLMClient`: Interfaces for LLM provider abstraction
   - `ProviderFactory`: Creates appropriate provider based on configuration
   - Supports OpenRouter and Anthropic providers
   - Token management and authentication handling

4. **UI System** (`UI/`)
   - Terminal.Gui-based interface for cross-platform console UI
   - `ChatInterface`: Main chat interaction window
   - Various dialogs for configuration and settings
   - Markdown rendering support

5. **Data Persistence** (`Data/`)
   - SQLite database for chat history
   - `ChatHistoryRepository`: Manages chat sessions and messages
   - Encrypted storage for sensitive data

### Key Design Patterns

- **Factory Pattern**: Used in ProviderFactory for creating LLM clients
- **Registry Pattern**: ToolRegistry auto-discovers and registers tools via reflection
- **Repository Pattern**: ChatHistoryRepository for data access
- **Adapter Pattern**: OpenRouterToolAdapter for tool integration

### Environment Requirements

- **Git Repository**: Saturn requires operating within a Git repository
- **API Keys**: Requires either OPENROUTER_API_KEY or Anthropic authentication
- **.NET 8.0 SDK**: Required for building and running

### Testing Approach

- **Framework**: xUnit with FluentAssertions
- **Structure**: Tests mirror source structure in Saturn.Tests/
- **Mocking**: Custom mock implementations for HTTP handlers and providers
- **Integration Tests**: Available for provider integration testing

### Important Files and Directories

- `Program.cs`: Entry point, handles initialization and provider selection
- `Configuration/`: Configuration management and persistence
- `Core/GitManager.cs`: Git repository validation and operations
- `.github/workflows/`: CI/CD pipelines for building, testing, and releasing
- `build.ps1`: PowerShell build script with full build pipeline

### Development Tips

- Always ensure you're in a Git repository before running Saturn
- Use the build scripts for consistent builds and packaging
- Tool implementations should inherit from ToolBase for consistent behavior
- New providers should implement ILLMProvider and ILLMClient interfaces
- UI dialogs should follow the Terminal.Gui patterns established in existing dialogs

### Known Issues and Fixes

#### Anthropic Provider Authentication
The Anthropic provider requires a specific system prompt prefix for Claude Code authentication:
- The system prompt must start with: "You are Claude Code, Anthropic's official CLI for Claude."
- This is automatically prepended in `MessageConverter.cs` for Anthropic API calls
- System prompts are wrapped in JSON for caching; `ExtractTextContent` in `AgentBase.cs` handles extraction