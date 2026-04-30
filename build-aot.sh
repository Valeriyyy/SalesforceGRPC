#!/bin/bash
# Script to build and test the AOT (Ahead-of-Time) compiled SalesforceGRPC application

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
BUILD_CONFIG="${1:-Release}"
RUNTIME="${2:-}"
PUBLISH_DIR="bin/${BUILD_CONFIG}/net10.0/publish"

echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}  SalesforceGRPC AOT Build Script${NC}"
echo -e "${BLUE}========================================${NC}"
echo "Build Configuration: $BUILD_CONFIG"
echo "Runtime: ${RUNTIME:-native (no runtime)}"
echo ""

# Step 1: Clean previous builds
echo -e "${YELLOW}[1/6] Cleaning previous builds...${NC}"
rm -rf bin obj
cd SalesforceGrpc
rm -rf bin obj
cd ..
cd Database
rm -rf bin obj
cd ..
echo -e "${GREEN}✓ Clean complete${NC}"
echo ""

# Step 2: Restore dependencies
echo -e "${YELLOW}[2/6] Restoring NuGet packages...${NC}"
dotnet restore
echo -e "${GREEN}✓ Restore complete${NC}"
echo ""

# Step 3: Build project
echo -e "${YELLOW}[3/6] Building project...${NC}"
dotnet build SalesforceGrpc/SalesforceGrpc.csproj -c $BUILD_CONFIG
echo -e "${GREEN}✓ Build complete${NC}"
echo ""

# Step 4: Run AOT compilation
echo -e "${YELLOW}[4/6] Publishing as AOT...${NC}"

if [ -z "$RUNTIME" ]; then
    # No runtime - pure native executable
    dotnet publish SalesforceGrpc/SalesforceGrpc.csproj \
        -c $BUILD_CONFIG \
        -o SalesforceGrpc/$PUBLISH_DIR \
        -p:PublishAot=true \
        -p:StripSymbols=false \
        --no-self-contained
    echo -e "${GREEN}✓ Native AOT build complete${NC}"
else
    # Self-contained with runtime
    dotnet publish SalesforceGrpc/SalesforceGrpc.csproj \
        -c $BUILD_CONFIG \
        -o SalesforceGrpc/$PUBLISH_DIR \
        -p:PublishAot=true \
        -p:StripSymbols=false \
        -r $RUNTIME \
        --self-contained
    echo -e "${GREEN}✓ Self-contained AOT build complete${NC}"
fi
echo ""

# Step 5: Check output size
echo -e "${YELLOW}[5/6] Analyzing output...${NC}"
EXECUTABLE="SalesforceGrpc/$PUBLISH_DIR/SalesforceGrpc"
if [ -f "$EXECUTABLE" ]; then
    SIZE=$(du -h "$EXECUTABLE" | cut -f1)
    echo "Executable size: $SIZE"
    echo -e "${GREEN}✓ Executable ready: $EXECUTABLE${NC}"
else
    echo -e "${RED}✗ Executable not found at $EXECUTABLE${NC}"
    exit 1
fi
echo ""

# Step 6: Summary
echo -e "${YELLOW}[6/6] Build Summary${NC}"
echo -e "${GREEN}✓ AOT Compilation Successful!${NC}"
echo ""
echo "Output Directory: SalesforceGrpc/$PUBLISH_DIR/"
echo "Executable: $EXECUTABLE"
echo "Size: $SIZE"
echo ""
echo -e "${BLUE}Next Steps:${NC}"
echo "1. Test the executable:"
echo "   $EXECUTABLE"
echo ""
echo "2. Run in Docker:"
echo "   docker build -f Dockerfile.aot -t salesforcegrpc:aot ."
echo ""
echo "3. Check for AOT warnings:"
echo "   dotnet publish SalesforceGrpc/SalesforceGrpc.csproj -c $BUILD_CONFIG 2>&1 | grep -i warning"
echo ""
echo -e "${GREEN}Build complete!${NC}"

