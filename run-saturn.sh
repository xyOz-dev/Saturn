#!/bin/bash

# Check if API key is already set
if [ -z "$OPENROUTER_API_KEY" ]; then
    echo "🪐 Saturn - Your personal swarm of employees"
    echo "=========================================="
    echo ""
    echo "No OpenRouter API key found in environment."
    echo ""
    echo "To get an API key:"
    echo "1. Go to https://openrouter.ai/"
    echo "2. Sign up or log in"
    echo "3. Get your API key from the dashboard"
    echo ""
    read -p "Enter your OpenRouter API key: " -s api_key
    echo ""
    
    if [ -z "$api_key" ]; then
        echo "❌ No API key provided. Exiting."
        exit 1
    fi
    
    export OPENROUTER_API_KEY="$api_key"
    echo "✅ API key set for this session"
    echo ""
fi

echo "🚀 Launching Saturn..."
echo ""

# Run Saturn
cd /Users/jlee/projects/_tools_/Saturn
dotnet run --project Saturn
