#!/bin/bash

echo "🪐 Saturn Setup Wizard"
echo "====================="
echo ""
echo "This will set up Saturn and save your API key permanently."
echo ""
echo "To get an OpenRouter API key:"
echo "1. Go to https://openrouter.ai/"
echo "2. Sign up or log in"
echo "3. Get your API key from the dashboard"
echo ""

read -p "Enter your OpenRouter API key: " -s api_key
echo ""

if [ -z "$api_key" ]; then
    echo "❌ No API key provided. Setup cancelled."
    exit 1
fi

# Add to .zshrc
echo "" >> ~/.zshrc
echo "# Saturn - OpenRouter API Key" >> ~/.zshrc
echo "export OPENROUTER_API_KEY=\"$api_key\"" >> ~/.zshrc

echo "✅ API key saved to ~/.zshrc"
echo ""

# Set for current session
export OPENROUTER_API_KEY="$api_key"

# Optional: Install as global tool
echo "Would you like to install Saturn as a global .NET tool?"
echo "This will allow you to run 'saturn' from anywhere."
read -p "Install globally? (y/n): " install_global

if [ "$install_global" = "y" ] || [ "$install_global" = "Y" ]; then
    echo ""
    echo "📦 Installing Saturn as global tool..."
    cd /Users/jlee/projects/_tools_/Saturn
    dotnet pack -c Release
    dotnet tool install --global --add-source ./nupkg SaturnAgent
    echo "✅ Saturn installed globally! You can now run 'saturn' from anywhere."
else
    echo "⏭️  Skipping global installation."
fi

echo ""
echo "🎉 Setup complete!"
echo ""
echo "To run Saturn:"
echo "  - If installed globally: just type 'saturn'"
echo "  - Otherwise: run './run-saturn.sh' from the Saturn directory"
echo ""
echo "Note: You'll need to restart your terminal or run 'source ~/.zshrc'"
echo "for the API key to be available in new sessions."
