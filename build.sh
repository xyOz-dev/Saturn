#!/usr/bin/env bash

# Build script for Saturn CLI Tool
# Usage: ./build.sh [options]
# Options:
#   -c, --configuration  Build configuration (Debug/Release) [default: Release]
#   -v, --version       Package version override
#   -n, --no-pack       Skip creating NuGet package
#   --clean             Clean build output before building
#   -h, --help          Show this help message

set -e

# Default values
CONFIGURATION="Release"
VERSION=""
NO_PACK=false
CLEAN=false

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[0;33m'
NC='\033[0m' # No Color

# Functions
print_header() {
    echo ""
    echo -e "${CYAN}========================================${NC}"
    echo -e "${CYAN} $1${NC}"
    echo -e "${CYAN}========================================${NC}"
    echo ""
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_help() {
    echo "Saturn Build Script"
    echo ""
    echo "Usage: $0 [options]"
    echo ""
    echo "Options:"
    echo "  -c, --configuration  Build configuration (Debug/Release) [default: Release]"
    echo "  -v, --version       Package version override"
    echo "  -n, --no-pack       Skip creating NuGet package"
    echo "  --clean             Clean build output before building"
    echo "  -h, --help          Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0                          # Build in Release mode"
    echo "  $0 -c Debug                 # Build in Debug mode"
    echo "  $0 -v 1.0.1                 # Build with specific version"
    echo "  $0 --clean                  # Clean and build"
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--configuration)
            CONFIGURATION="$2"
            if [[ "$CONFIGURATION" != "Debug" && "$CONFIGURATION" != "Release" ]]; then
                print_error "Invalid configuration: $CONFIGURATION. Must be Debug or Release."
                exit 1
            fi
            shift 2
            ;;
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        -n|--no-pack)
            NO_PACK=true
            shift
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        -h|--help)
            print_help
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            print_help
            exit 1
            ;;
    esac
done

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_PATH="$SCRIPT_DIR"
CSPROJ_PATH="$PROJECT_PATH/Saturn.csproj"

# Check for .NET 8 SDK (required for Saturn)
if [ -d "/opt/homebrew/opt/dotnet@8/libexec" ]; then
    # Use .NET 8 if available via Homebrew
    export PATH="/opt/homebrew/opt/dotnet@8/libexec:$PATH"
    print_success "Using .NET 8 SDK from Homebrew"
fi

# Verify .NET SDK version
DOTNET_VERSION=$(dotnet --version 2>/dev/null || echo "not found")
if [[ "$DOTNET_VERSION" == "not found" ]]; then
    print_error ".NET SDK not found. Please install .NET 8 SDK."
    exit 1
elif [[ ! "$DOTNET_VERSION" =~ ^8\. ]]; then
    print_error "Saturn requires .NET 8 SDK. Found: $DOTNET_VERSION"
    echo "Please install .NET 8 SDK via: brew install dotnet@8"
    exit 1
fi

# Check if project exists
if [ ! -f "$CSPROJ_PATH" ]; then
    print_error "Project file not found: $CSPROJ_PATH"
    exit 1
fi

# Clean if requested
if [ "$CLEAN" = true ]; then
    print_header "Cleaning Solution"
    
    for folder in bin obj nupkg; do
        path="$PROJECT_PATH/$folder"
        if [ -d "$path" ]; then
            rm -rf "$path"
            print_success "Removed $folder"
        fi
    done
fi

# Restore dependencies
print_header "Restoring Dependencies"
dotnet restore "$CSPROJ_PATH"
print_success "Dependencies restored"

# Build
print_header "Building Saturn ($CONFIGURATION)"
BUILD_ARGS="build \"$CSPROJ_PATH\" -c $CONFIGURATION --no-restore"

if [ -n "$VERSION" ]; then
    BUILD_ARGS="$BUILD_ARGS -p:Version=$VERSION"
fi

eval dotnet $BUILD_ARGS
print_success "Build completed"

# Run tests if they exist
TEST_PROJECTS=$(find "$SCRIPT_DIR" -name "*Tests.csproj" 2>/dev/null)
if [ -n "$TEST_PROJECTS" ]; then
    print_header "Running Tests"
    for test_project in $TEST_PROJECTS; do
        echo "Testing: $(basename "$test_project")"
        dotnet test "$test_project" -c "$CONFIGURATION" --no-build
    done
    print_success "All tests passed"
fi

# Pack NuGet package
if [ "$NO_PACK" = false ]; then
    print_header "Creating NuGet Package"
    
    PACK_ARGS="pack \"$CSPROJ_PATH\" -c $CONFIGURATION --no-build"
    
    if [ -n "$VERSION" ]; then
        PACK_ARGS="$PACK_ARGS -p:Version=$VERSION"
    fi
    
    eval dotnet $PACK_ARGS
    
    NUPKG_PATH="$PROJECT_PATH/nupkg"
    if [ -d "$NUPKG_PATH" ]; then
        for package in "$NUPKG_PATH"/*.nupkg; do
            if [ -f "$package" ]; then
                size=$(du -k "$package" | cut -f1)
                print_success "Created package: $(basename "$package")"
                echo "  Size: ${size} KB"
            fi
        done
    fi
fi

print_header "Build Completed Successfully!"

# Instructions for publishing
if [ "$NO_PACK" = false ]; then
    echo ""
    echo -e "${YELLOW}To test the tool locally:${NC}"
    echo "  dotnet tool install --global --add-source ./nupkg SaturnAgent"
    echo ""
    echo -e "${YELLOW}To publish to NuGet.org:${NC}"
    echo "  dotnet nuget push ./Saturn/nupkg/*.nupkg -k YOUR_API_KEY -s https://api.nuget.org/v3/index.json"
fi