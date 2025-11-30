#!/bin/bash
################################################################################
# Netloy Post-Publish Script for Linux/macOS
################################################################################
# This script demonstrates using macros in two ways:
#   1. String replacement: ${MACRO_NAME} - replaced by Netloy before execution
#   2. Environment variables: $MACRO_NAME - accessed during execution
################################################################################

set -e  # Exit on error

echo ""
echo "============================================================================"
echo "POST-PUBLISH SCRIPT - Linux/macOS"
echo "============================================================================"
echo ""

################################################################################
# METHOD 1: String Replacement (values replaced by Netloy before execution)
################################################################################
echo "[INFO] Application Information (String Replacement):"
echo "  App Name     : ${APP_FRIENDLY_NAME}"
echo "  App ID       : ${APP_ID}"
echo "  Version      : ${APP_VERSION}"
echo "  Package Type : ${PACKAGE_TYPE}"
echo "  Runtime      : ${PACKAGE_ARCH}"
echo ""

################################################################################
# METHOD 2: Environment Variables (values accessed during execution)
################################################################################
echo "[INFO] Build Information (Environment Variables):"
echo "  Publisher    : $PUBLISHER_NAME"
echo "  Executable   : $APP_EXEC_NAME"
echo "  Output Dir   : $PUBLISH_OUTPUT_DIRECTORY"
echo "  Config Dir   : $CONF_FILE_DIRECTORY"
echo ""

################################################################################
# Example: Copy Additional Files
################################################################################
echo "[INFO] Copying additional files..."

# Using environment variable for dynamic path
TARGET_DIR="$PUBLISH_OUTPUT_DIRECTORY"

if [ -d "$CONF_FILE_DIRECTORY/Resources" ]; then
    echo "  Source: $CONF_FILE_DIRECTORY/Resources"
    echo "  Target: $TARGET_DIR"

    if cp -r "$CONF_FILE_DIRECTORY/Resources/"* "$TARGET_DIR/" 2>/dev/null; then
        echo "  [SUCCESS] Resources copied successfully"
    else
        echo "  [WARNING] Failed to copy resources"
    fi
else
    echo "  [SKIP] Resources folder not found"
fi
echo ""

################################################################################
# Example: Create Version File
################################################################################
echo "[INFO] Creating version information file..."

# Mix both methods: string replacement for static content
cat > "$TARGET_DIR/version.txt" << EOF
Application: ${APP_FRIENDLY_NAME}
Version: $APP_VERSION
Build Date: $(date '+%Y-%m-%d %H:%M:%S')
Package Type: $PACKAGE_TYPE
Runtime: $PACKAGE_ARCH
Publisher: $PUBLISHER_NAME
EOF

echo "  [SUCCESS] Version file created: $TARGET_DIR/version.txt"
echo ""

################################################################################
# Example: Set Executable Permissions
################################################################################
echo "[INFO] Setting executable permissions..."

if [ -f "$TARGET_DIR/$APP_EXEC_NAME" ]; then
    chmod +x "$TARGET_DIR/$APP_EXEC_NAME"
    echo "  [SUCCESS] Permissions set for: $APP_EXEC_NAME"
else
    echo "  [WARNING] Executable not found: $APP_EXEC_NAME"
fi
echo ""

################################################################################
# Example: Conditional Processing by Platform
################################################################################
echo "[INFO] Performing platform-specific tasks..."

case "$PACKAGE_ARCH" in
    linux-x64)
        echo "  [INFO] Detected Linux x64 architecture"
        echo "  [ACTION] Optimizing for x64 platform"
        ;;
    linux-arm64)
        echo "  [INFO] Detected Linux ARM64 architecture"
        echo "  [ACTION] Optimizing for ARM64 platform"
        ;;
    osx-x64)
        echo "  [INFO] Detected macOS x64 (Intel)"
        echo "  [ACTION] Optimizing for Intel Macs"
        ;;
    osx-arm64)
        echo "  [INFO] Detected macOS ARM64 (Apple Silicon)"
        echo "  [ACTION] Optimizing for Apple Silicon"
        ;;
    *)
        echo "  [INFO] Runtime: $PACKAGE_ARCH"
        ;;
esac
echo ""

################################################################################
# Example: Create README file
################################################################################
echo "[INFO] Creating README file..."

cat > "$TARGET_DIR/README.txt" << EOF
${APP_FRIENDLY_NAME}
============================================

Version: $APP_VERSION
Runtime: $PACKAGE_ARCH

Installation:
1. Extract all files to a folder
2. Run ./$APP_EXEC_NAME

For more information, visit:
${PUBLISHER_LINK_URL}

Copyright $PUBLISHER_COPYRIGHT
EOF

echo "  [SUCCESS] README file created: $TARGET_DIR/README.txt"
echo ""

################################################################################
# Example: Verify Output
################################################################################
echo "[INFO] Verifying build output..."

if [ -f "$TARGET_DIR/$APP_EXEC_NAME" ]; then
    echo "  [SUCCESS] Main executable found: $APP_EXEC_NAME"

    # Check if executable
    if [ -x "$TARGET_DIR/$APP_EXEC_NAME" ]; then
        echo "  [SUCCESS] Executable has correct permissions"
    else
        echo "  [WARNING] Executable may not have execute permissions"
    fi
else
    echo "  [ERROR] Main executable not found: $APP_EXEC_NAME"
    exit 1
fi

FILE_COUNT=$(find "$TARGET_DIR" -type f | wc -l)
echo "  [INFO] Total files in output: $FILE_COUNT"
echo ""

################################################################################
# Completion
################################################################################
echo "============================================================================"
echo "POST-PUBLISH SCRIPT COMPLETED SUCCESSFULLY"
echo "============================================================================"
echo "  Output Location: $PUBLISH_OUTPUT_DIRECTORY"
echo "  Package: ${APP_FRIENDLY_NAME} v$APP_VERSION"
echo "  Runtime: $PACKAGE_ARCH"
echo "============================================================================"
echo ""

exit 0
