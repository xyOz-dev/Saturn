<div align="center">

# 🪐

### *Your personal swarm of employees, without the salary.*

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![OpenRouter](https://img.shields.io/badge/OpenRouter-Supported-00A8E1?style=for-the-badge)](https://openrouter.ai/)
[![LM Studio](https://img.shields.io/badge/LM_Studio-Supported-4E1F9D?style=for-the-badge)](https://lmstudio.ai/)
[![GitHub Stars](https://img.shields.io/github/stars/xyOz-dev/Saturn?style=for-the-badge&color=yellow)](https://github.com/xyOz-dev/Saturn/stargazers)

</div>

---

> **Warning:** Some hotkeys may not work on Unix systems, we are looking into a fix. This issue is caused by the UI framework (Terminal.Gui).

## What is Saturn?

Saturn is an AI coding agent that runs entirely in your terminal. You describe what you want done in plain English, and Saturn reads your code, makes edits, runs commands, and reports back, all inside the Git repository you point it at.

Under the hood it connects to [OpenRouter](https://openrouter.ai/), so you can use models from Anthropic, OpenAI, Google and others with a single API key — or to a local [LM Studio](https://lmstudio.ai/) server, so you can run entirely offline on your own hardware. Providers can be switched at any time from the Agent menu without restarting. It is built on .NET 8 and Terminal.Gui, and installs as a standard .NET global tool.

## Features

- **Terminal chat interface** with streaming responses and markdown rendering
- **A full tool suite** the agent uses on your behalf: read, write and diff files, grep and glob searches, shell command execution, and web fetch
- **Command approval**, so the agent asks before running shell commands
- **Multi-agent orchestration**: the main agent can spawn sub-agents, hand tasks off to them, check their status and collect results, letting it work on several parts of a task in parallel
- **Multiple providers**: OpenRouter for cloud models, LM Studio for local ones, hot-swappable from within the UI
- **Modes and user rules** to customize the agent's behavior per task or globally
- **Persistent chat history** stored locally in SQLite, so you can reload previous sessions
- **Git-aware**: Saturn requires a Git repository, so every change it makes can be reviewed and reverted with normal Git workflow

## Prerequisites

- **.NET 8.0 SDK** or later
- **Git**. Saturn only operates inside a Git repository. If you start it elsewhere it will offer to initialize one.
- **One LLM provider:**
  - An **OpenRouter API key** ([get one here](https://openrouter.ai/)), or
  - **LM Studio** ([download](https://lmstudio.ai/)) with its local server running and a model downloaded

## Installation

Install as a .NET global tool (recommended):

```bash
# Install from NuGet
dotnet tool install --global SaturnAgent

# Or install from a local package
dotnet tool install --global --add-source ./nupkg SaturnAgent
```

## Quick Start

### 1. Set up a provider

**Option A — OpenRouter (cloud models):**

```bash
# Windows (Command Prompt)
setx OPENROUTER_API_KEY your-api-key-here

# Windows (PowerShell)
$env:OPENROUTER_API_KEY = "your-api-key-here"

# macOS/Linux
export OPENROUTER_API_KEY="your-api-key-here"
```

**Option B — LM Studio (local models):**

1. In LM Studio, download a model and start the local server (Developer tab → Start Server).
2. Tell Saturn to use it:

```bash
# Windows (Command Prompt)
setx SATURN_PROVIDER lmstudio

# Windows (PowerShell)
$env:SATURN_PROVIDER = "lmstudio"

# macOS/Linux
export SATURN_PROVIDER=lmstudio
```

Saturn connects to `http://localhost:1234/v1` by default; set `LMSTUDIO_BASE_URL` if your server runs elsewhere. If no model is configured, Saturn picks the first loaded model automatically.

You can also switch providers later from inside Saturn via **Agent → Provider...** — the choice is remembered between runs, and each provider remembers its own model. Note that the `SATURN_PROVIDER` environment variable, when set, always wins at launch — so if you set it permanently (e.g. with `setx`) it will override the in-app choice on the next start. Clear it (`setx SATURN_PROVIDER ""` or unset it in your shell profile) if you prefer to control the provider from the UI.

> **Note on local models:** Saturn drives everything through tool calls, so use a model with solid tool-calling support (e.g. Qwen 2.5 Coder, Llama 3.1+, Mistral). Small models may produce malformed tool calls or loop. Sub-agent parallelism is also limited by the fact that a local server generates for one request at a time.

### 2. Launch Saturn

From inside the Git repository you want to work on:

```bash
# If installed as a global tool
saturn

# If running from source
dotnet run --project Saturn
```

### What to expect

On first launch Saturn checks that you are in a Git repository (and offers to create one if not), then opens the chat interface. From there the workflow is simple:

1. Type a request, for example: *"Find where user sessions are validated and add a unit test covering expired sessions."*
2. Saturn streams its reasoning and starts using tools: searching the codebase, reading files, writing changes.
3. If it needs to run a shell command (build, test, etc.) it asks for your approval first.
4. When it finishes, review the changes with `git diff` like any other work in your repo.

Your conversation is saved automatically and can be reloaded from the chat menu in a later session. Model selection, temperature and other settings are configurable from within the UI and persist between runs.

## Building from Source

```bash
# Clone repository
git clone https://github.com/xyOz-dev/Saturn.git
cd Saturn

# Restore dependencies
dotnet restore

# Build in Release mode
dotnet build -c Release

# Create NuGet package
dotnet pack -c Release

# Run tests
dotnet test
```
