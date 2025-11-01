#!/bin/bash

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Project paths and files
SOLUTION_FILE="Paycheck4.sln"
PROJECT_FILE="src/Paycheck4.Console/Paycheck4.Console.csproj"
PUBLISH_BASE_DIR="src/Paycheck4.Console/bin"
DEPLOY_BASE_DIR="/opt/paycheck4"
EXECUTABLE_NAME="Paycheck4.Console"

# Build configuration
CONFIGURATION="Debug"
RUNTIME="linux-arm64"
FRAMEWORK="net8.0"
VERBOSITY="minimal"
OUTPUT_DIR=""

# Build flags
SELF_CONTAINED=false
CLEAN=false
RESTORE=false
PUBLISH=false

# Deployment configuration
DEPLOY=false
DEPLOY_HOST=""
DEPLOY_USER=""
DEPLOY_PERMISSIONS="755"

# Command templates
DOTNET_BUILD_CMD="dotnet build"
DOTNET_PUBLISH_CMD="dotnet publish"
DOTNET_CLEAN_CMD="dotnet clean"
DOTNET_RESTORE_CMD="dotnet restore"
DEPLOY_PASSWORD="Memp1859"
PSCP_CMD="pscp -pw $DEPLOY_PASSWORD"
PLINK_CMD="plink -pw $DEPLOY_PASSWORD"

# Remote commands
REMOTE_MKDIR_CMD="sudo mkdir -p"
REMOTE_CHOWN_CMD="sudo chown"
REMOTE_CHMOD_CMD="sudo chmod"

# Help message
show_help() {
    echo "Usage: build.sh [options]"
    echo ""
    echo "Options:"
    echo "  -h, --help                 Show this help message"
    echo "  -c, --configuration        Set build configuration (Debug|Release) [default: Debug]"
    echo "  -r, --runtime              Set runtime identifier [default: linux-arm64]"
    echo "  -s, --self-contained       Build as self-contained application"
    echo "  -p, --publish              Publish the application instead of building"
    echo "  -o, --output              Set the output directory for publish"
    echo "  -d, --deploy              Deploy to remote host after build/publish"
    echo "  --host                    Remote host for deployment (e.g., 192.168.68.69)"
    echo "  --user                    Remote username for deployment"
    echo "  --clean                    Clean before building"
    echo "  --restore                  Restore dependencies before building"
    echo "  -v, --verbosity           Set verbosity level (quiet|minimal|normal|detailed|diagnostic) [default: minimal]"
    echo ""
    echo "Examples:"
    echo "  ./build.sh                                    # Debug build"
    echo "  ./build.sh -c Release                        # Release build"
    echo "  ./build.sh -p -c Release -s                  # Publish self-contained release"
    echo "  ./build.sh -p -o ./publish -c Release -s     # Publish to specific directory"
    echo "  ./build.sh -p -d --host 192.168.68.69 --user kcondict   # Publish and deploy"
    echo ""
    exit 0
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -h|--help)
            show_help
            ;;
        -c|--configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        -r|--runtime)
            RUNTIME="$2"
            shift 2
            ;;
        -s|--self-contained)
            SELF_CONTAINED=true
            shift
            ;;
        -p|--publish)
            PUBLISH=true
            shift
            ;;
        -o|--output)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        -d|--deploy)
            DEPLOY=true
            shift
            ;;
        --host)
            DEPLOY_HOST="$2"
            shift 2
            ;;
        --user)
            DEPLOY_USER="$2"
            shift 2
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --restore)
            RESTORE=true
            shift
            ;;
        -v|--verbosity)
            VERBOSITY="$2"
            shift 2
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            show_help
            ;;
    esac
done

# Validate configuration
if [[ "$CONFIGURATION" != "Debug" && "$CONFIGURATION" != "Release" ]]; then
    echo -e "${RED}Invalid configuration: $CONFIGURATION${NC}"
    exit 1
fi

# Command construction
if [ "$PUBLISH" = true ]; then
    CMD="$DOTNET_PUBLISH_CMD $PROJECT_FILE"
    CMD="$CMD -c $CONFIGURATION"
    CMD="$CMD -v $VERBOSITY"
    
    if [ "$SELF_CONTAINED" = true ]; then
        CMD="$CMD -r $RUNTIME --self-contained"
    fi
    
    if [ ! -z "$OUTPUT_DIR" ]; then
        CMD="$CMD -o $OUTPUT_DIR"
    fi
else
    CMD="$DOTNET_BUILD_CMD $SOLUTION_FILE"
    CMD="$CMD -c $CONFIGURATION"
    CMD="$CMD -v $VERBOSITY"
    
    if [ "$SELF_CONTAINED" = true ]; then
        CMD="$CMD -r $RUNTIME --self-contained"
    fi
fi

# Clean if requested
if [ "$CLEAN" = true ]; then
    echo -e "${YELLOW}Cleaning solution...${NC}"
    $DOTNET_CLEAN_CMD $SOLUTION_FILE -c $CONFIGURATION
    if [ $? -ne 0 ]; then
        echo -e "${RED}Clean failed${NC}"
        exit 1
    fi
fi

# Restore if requested
if [ "$RESTORE" = true ]; then
    echo -e "${YELLOW}Restoring dependencies...${NC}"
    $DOTNET_RESTORE_CMD $SOLUTION_FILE
    if [ $? -ne 0 ]; then
        echo -e "${RED}Restore failed${NC}"
        exit 1
    fi
fi

# Execute command
if [ "$PUBLISH" = true ]; then
    echo -e "${YELLOW}Publishing application...${NC}"
else
    echo -e "${YELLOW}Building solution...${NC}"
fi
echo "Command: $CMD"
eval $CMD

# Handle deployment if requested
if [ "$DEPLOY" = true ]; then
    if [ -z "$DEPLOY_HOST" ] || [ -z "$DEPLOY_USER" ]; then
        echo -e "${RED}Error: Deployment requires both --host and --user parameters${NC}"
        exit 1
    fi

    echo -e "${YELLOW}Deploying to $DEPLOY_HOST...${NC}"
    
    # Create directory structure and set permissions
    REMOTE_SETUP="$REMOTE_MKDIR_CMD $DEPLOY_BASE_DIR && $REMOTE_CHOWN_CMD $DEPLOY_USER:$DEPLOY_USER $DEPLOY_BASE_DIR && $REMOTE_CHMOD_CMD $DEPLOY_PERMISSIONS $DEPLOY_BASE_DIR"
    echo "Setting up remote directory..."
    echo "$DEPLOY_PASSWORD" | $PLINK_CMD "$DEPLOY_USER@$DEPLOY_HOST" "$REMOTE_SETUP"
    if [ $? -ne 0 ]; then
        echo -e "${RED}Failed to set up remote directory${NC}"
        exit 1
    fi

    # Determine publish directory using variables
    PUBLISH_DIR="$PUBLISH_BASE_DIR/$CONFIGURATION/$FRAMEWORK/$RUNTIME/publish"
    if [ ! -z "$OUTPUT_DIR" ]; then
        PUBLISH_DIR="$OUTPUT_DIR"
    fi

    # Convert Windows path to PSCP format if needed
    PSCP_PATH=$(echo "$PUBLISH_DIR" | sed 's/\\/\//g')

    echo "Copying files..."
    # Use PSCP with password
    for file in "$PSCP_PATH"/*; do
        echo "Copying $(basename "$file")..."
        $PSCP_CMD "$file" "$DEPLOY_USER@$DEPLOY_HOST:$DEPLOY_BASE_DIR/"
        if [ $? -ne 0 ]; then
            echo -e "${RED}Failed to copy $(basename "$file")${NC}"
            exit 1
        fi
    done

    # Set execute permissions for the application
    echo "$DEPLOY_PASSWORD" | $PLINK_CMD "$DEPLOY_USER@$DEPLOY_HOST" "$REMOTE_CHMOD_CMD +x $DEPLOY_BASE_DIR/$EXECUTABLE_NAME"
    if [ $? -ne 0 ]; then
        echo -e "${RED}Failed to set execute permissions${NC}"
        exit 1
    fi

    echo -e "${GREEN}Deployment completed successfully${NC}"
fi

# Check build result
if [ $? -eq 0 ]; then
    echo -e "${GREEN}Build completed successfully${NC}"
    exit 0
else
    echo -e "${RED}Build failed${NC}"
    exit 1
fi