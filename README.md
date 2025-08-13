<div align="center">

# 🪐

### *Your personal swarm of employees, without the salary.*

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![OpenRouter](https://img.shields.io/badge/OpenRouter-Powered-00A8E1?style=for-the-badge)](https://openrouter.ai/)
[![GitHub Stars](https://img.shields.io/github/stars/xyOz-dev/Saturn?style=for-the-badge&color=yellow)](https://github.com/xyOz-dev/Saturn/stargazers)

</div>

---

<details>
<summary><b>📋 Prerequisites</b></summary>

- **.NET 8.0 SDK** or later
- **Git** (Saturn requires a Git repository)
- **OpenRouter API Key** ([Get one here](https://openrouter.ai/))

</details>


## 📦 Installation

### **Install as .NET Global Tool** *(Recommended)*

```bash
# Install from NuGet
dotnet tool install --global SaturnAgent

# Or install from local package
dotnet tool install --global --add-source ./nupkg SaturnAgent
```

---

## 🚀 Quick Start

### 1️⃣ **Set up your API key**

```bash
# Windows (Command Prompt)
setx OPENROUTER_API_KEY your-api-key-here

# Windows (PowerShell)
$env:OPENROUTER_API_KEY = "your-api-key-here"

# macOS/Linux
export OPENROUTER_API_KEY="your-api-key-here"
```

### 2️⃣ **Launch Saturn**

```bash
# If installed as global tool
saturn

# If running from source
dotnet run --project Saturn
```

---
<details>
<summary><b>🏗️ Build Instructions</b></summary>

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

# Run tests (if available)
dotnet test
```

</details>

</div>
