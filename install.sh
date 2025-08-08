#!/usr/bin/env bash
set -euo pipefail

# Script to download and install Perla CLI for Linux and macOS

REPO_OWNER="AngelMunoz"
REPO_NAME="Perla"
DEFAULT_INSTALL_DIR_BASE="$HOME/.local/share"
TOOL_NAME="Perla"
EXTRACTION_SUBDIR="perla" # Subdirectory inside install_dir where actual executable lives

# --- Helper Functions ---
log_info() {
    echo "[INFO] $1"
}

log_error() {
    echo "[ERROR] $1" >&2
}

# Check for required commands
check_command() {
    if ! command -v "$1" &> /dev/null; then
        log_error "Required command '$1' is not installed. Please install it and try again."
        exit 1
    fi
}

# --- Argument Parsing ---
INSTALL_VERSION=""
USE_LATEST=false
CUSTOM_DOWNLOAD_PATH=""
ADD_TO_PROFILE=true

usage() {
    echo "Usage: $0 [options]"
    echo ""
    echo "Options:"
    echo "  -v, --version VERSION   Specify a version to install (e.g., v0.1.0)."
    echo "  -l, --latest            Install the latest version (default if no version specified)."
    echo "  -p, --path PATH         Specify a custom download/installation path."
    echo "      --no-profile        Do not add Perla to the shell profile (PATH)."
    echo "  -h, --help              Show this help message."
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -v|--version)
            INSTALL_VERSION="$2"
            shift 2
            ;;
        -l|--latest)
            USE_LATEST=true
            shift
            ;;
        -p|--path)
            CUSTOM_DOWNLOAD_PATH="$2"
            shift 2
            ;;
        --no-profile)
            ADD_TO_PROFILE=false
            shift
            ;;
        -h|--help)
            usage
            ;;
        *)
            log_error "Unknown option: $1"
            usage
            exit 1
            ;;
    esac
done

# --- Pre-flight Checks ---
check_command "curl"
check_command "unzip"
# jq is preferred for GitHub API parsing
HAS_JQ=true
if ! command -v "jq" &> /dev/null; then
    log_info "jq command not found. Will attempt to parse GitHub API response with grep/sed, but this is less reliable. Installing jq is recommended."
    HAS_JQ=false
fi


# --- Platform Detection ---
os_type=""
os_arch=""

case "$(uname -s)" in
    Linux*)  os_type="linux" ;;
    Darwin*) os_type="osx" ;;
    *)
        log_error "Unsupported operating system: $(uname -s)"
        exit 1
        ;;
esac

case "$(uname -m)" in
    x86_64)  os_arch="x64" ;;
    arm64)   os_arch="arm64" ;;
    aarch64) os_arch="arm64" ;; # aarch64 is often reported for arm64
    *)
        log_error "Unsupported architecture: $(uname -m)"
        exit 1
        ;;
esac

selected_platform="${os_type}-${os_arch}"
log_info "Detected platform: $selected_platform"

# --- Determine Effective Install Directory ---
if [ -n "$CUSTOM_DOWNLOAD_PATH" ]; then
    effective_install_dir="$CUSTOM_DOWNLOAD_PATH"
else
    effective_install_dir="${DEFAULT_INSTALL_DIR_BASE}/${TOOL_NAME}"
fi

# Ensure the target directory exists, create if not
if [ ! -d "$effective_install_dir" ]; then
    log_info "Target directory '$effective_install_dir' does not exist. Creating it..."
    if mkdir -p "$effective_install_dir"; then
        log_info "Successfully created directory: $effective_install_dir"
    else
        log_error "Failed to create directory '$effective_install_dir'."
        exit 1
    fi
fi

# Resolve to absolute path
effective_install_dir="$(cd "$effective_install_dir" && pwd)"
log_info "Perla will be installed in: $effective_install_dir"

# --- Determine Release Tag ---
release_tag=""
if [ -n "$INSTALL_VERSION" ]; then
    release_tag="$INSTALL_VERSION"
    log_info "Using specified version: $release_tag"
else
    latest_release_url="https://api.github.com/repos/${REPO_OWNER}/${REPO_NAME}/releases/latest"
    log_info "Fetching latest release information..."
    response=$(curl -sL "$latest_release_url")
    if [ $? -ne 0 ]; then
        log_error "Failed to fetch latest release information from GitHub API."
        exit 1
    fi

    if $HAS_JQ; then
        release_tag=$(echo "$response" | jq -r .tag_name)
    else
        # Basic parsing if jq is not available
        release_tag=$(echo "$response" | grep -o '"tag_name": *"[^"]*"' | sed -E 's/"tag_name": *"([^"]*)"/\1/')
    fi

    if [ -z "$release_tag" ] || [ "$release_tag" == "null" ]; then
        log_error "Could not determine the latest release tag. Response was:"
        echo "$response"
        exit 1
    fi
    log_info "Using latest release tag: $release_tag"
fi

# --- Download Asset ---
target_asset_filename="${selected_platform}.zip"
download_url="https://github.com/${REPO_OWNER}/${REPO_NAME}/releases/download/${release_tag}/${target_asset_filename}"
zip_file_path="${effective_install_dir}/${target_asset_filename}"
extraction_dir_path="${effective_install_dir}/${EXTRACTION_SUBDIR}" # e.g. /path/to/Perla/perla

log_info "Downloading $target_asset_filename to $zip_file_path from $download_url..."

if curl -sSL -f -o "$zip_file_path" "$download_url"; then
    log_info "Successfully downloaded to $zip_file_path"
else
    log_error "Failed to download the asset from $download_url"
    log_info "Attempting to list available assets for release $release_tag..."
    release_assets_url="https://api.github.com/repos/${REPO_OWNER}/${REPO_NAME}/releases/tags/${release_tag}"
    assets_response=$(curl -sL "$release_assets_url")
    if [ $? -eq 0 ]; then
        log_info "Available assets for release $release_tag:"
        if $HAS_JQ; then
            echo "$assets_response" | jq -r '.assets[].name' | sed 's/^/- /'
        else
            echo "$assets_response" | grep -o '"name": *"[^"]*"' | sed -E 's/"name": *"([^"]*)"/\1/' | sed 's/^/- /'
        fi
    else
        log_info "Could not retrieve asset list for tag $release_tag."
    fi
    exit 1
fi

# --- Extract Asset ---
if [ ! -d "$extraction_dir_path" ]; then
    mkdir -p "$extraction_dir_path"
fi

temp_suffix=$(date +%s)-$RANDOM
temp_extraction_dir_path="${effective_install_dir}/${EXTRACTION_SUBDIR}.tmp.${temp_suffix}"
mkdir -p "$temp_extraction_dir_path"

log_info "Extracting $zip_file_path to $temp_extraction_dir_path..."
if unzip -qo "$zip_file_path" -d "$temp_extraction_dir_path"; then # -q for quiet, -o for overwrite
    log_info "Successfully extracted to $temp_extraction_dir_path"
else
    log_error "Failed to extract $zip_file_path."
    rm -f "$zip_file_path"
    rm -rf "$temp_extraction_dir_path"
    exit 1
fi

# Atomically swap directories to avoid stale files
backup_dir_path="${extraction_dir_path}.bak.$(date +%s)"
if [ -d "$extraction_dir_path" ]; then
    if mv "$extraction_dir_path" "$backup_dir_path" 2>/dev/null; then
        log_info "Moved existing install to backup: $backup_dir_path"
    else
        log_error "Failed to move existing install. Attempting to remove it. Is Perla running?"
        if rm -rf "$extraction_dir_path"; then
            log_info "Removed existing install directory."
        else
            log_error "Failed to remove existing install directory. Aborting."
            rm -rf "$temp_extraction_dir_path"
            rm -f "$zip_file_path"
            exit 1
        fi
    fi
fi

if mv "$temp_extraction_dir_path" "$extraction_dir_path"; then
    log_info "Installed new version into: $extraction_dir_path"
    # Cleanup backup
    if [ -d "$backup_dir_path" ]; then
        if rm -rf "$backup_dir_path"; then
            log_info "Removed backup directory: $backup_dir_path"
        else
            log_info "Could not remove backup directory: $backup_dir_path. You may remove it manually."
        fi
    fi
else
    log_error "Failed to move new install into place."
    # Try to restore backup
    if [ -d "$backup_dir_path" ]; then
        mv "$backup_dir_path" "$extraction_dir_path" 2>/dev/null || true
    fi
    rm -rf "$temp_extraction_dir_path"
    rm -f "$zip_file_path"
    exit 1
fi

rm -f "$zip_file_path"
log_info "Removed $zip_file_path"

# --- Create Proxy/Shim Script ---
# The actual executable is assumed to be named 'Perla' inside the EXTRACTION_SUBDIR
executable_name_in_zip="${TOOL_NAME}" # Assumed name, e.g., "Perla"
proxy_script_file_path="${effective_install_dir}/${TOOL_NAME,,}" # e.g. /path/to/Perla/perla (lowercase)

log_info "Creating Perla proxy script at $proxy_script_file_path"

cat << EOF > "$proxy_script_file_path"
#!/bin/sh
# This script executes Perla from its installation subdirectory.
# PROXY_SCRIPT_DIR is the directory where this proxy script itself resides.
PROXY_SCRIPT_DIR="\$(cd "\$(dirname "\$0")" >/dev/null 2>&1 && pwd)"
EXECUTABLE_PATH="\$PROXY_SCRIPT_DIR/${EXTRACTION_SUBDIR}/${executable_name_in_zip}"

# Check if the executable exists and is executable
if [ ! -f "\$EXECUTABLE_PATH" ]; then
    echo "Error: Perla executable not found at \$EXECUTABLE_PATH" >&2
    exit 1
fi
if [ ! -x "\$EXECUTABLE_PATH" ]; then
    echo "Error: Perla executable at \$EXECUTABLE_PATH is not executable. Attempting to chmod +x." >&2
    chmod +x "\$EXECUTABLE_PATH"
    if [ ! -x "\$EXECUTABLE_PATH" ]; then
        echo "Error: Failed to make Perla executable. Please check permissions." >&2
        exit 1
    fi
fi

"\$EXECUTABLE_PATH" "\$@"
EOF

if chmod +x "$proxy_script_file_path"; then
    log_info "Successfully created and made executable proxy script: $proxy_script_file_path"
else
    log_error "Failed to make proxy script $proxy_script_file_path executable."
    # Attempt to clean up
    rm -f "$proxy_script_file_path"
    # Potentially remove extraction_dir_path as well if appropriate
    exit 1
fi

# --- Add to Profile ---
if [ "$ADD_TO_PROFILE" = true ]; then
    path_to_add="$effective_install_dir" # This is the directory containing the proxy script

    current_shell_basename=$(basename "$SHELL")
    profile_file=""

    if [ "$current_shell_basename" = "bash" ]; then
        profile_file="$HOME/.bashrc"
    elif [ "$current_shell_basename" = "zsh" ]; then
        profile_file="$HOME/.zshrc"
    else
        log_info "Unsupported shell: $current_shell_basename. Cannot automatically update PATH."
        log_info "Please add '$path_to_add' to your PATH manually."
        profile_file="" # Skip profile update
    fi

    if [ -n "$profile_file" ]; then
        log_info "Attempting to add '$path_to_add' to PATH in shell profile ($profile_file)..."

        # Ensure the profile file exists, create if not
        if [ ! -f "$profile_file" ]; then
            log_info "Profile file ($profile_file) does not exist. Creating it..."
            if touch "$profile_file"; then
                log_info "Successfully created profile file: $profile_file"
            else
                log_error "Failed to create profile file ($profile_file). Please create it manually and add '$path_to_add' to your PATH."
                profile_file="" # Skip further profile operations
            fi
        fi

        if [ -f "$profile_file" ]; then
            # Check if PATH already contains the target or PERLA_HOME is already set
            if grep -q "PERLA_HOME=.*${path_to_add}" "$profile_file" || grep -q "PATH=.*${path_to_add}" "$profile_file"; then
                log_info "'$path_to_add' appears to be already configured in $profile_file. Skipping profile update."
            else
                comment="# Added by perla_install.sh to include Perla CLI"
                perla_home_command="export PERLA_HOME=\"${path_to_add}\""
                path_add_command="export PATH=\"\$PERLA_HOME:\$PATH\"" # Use PERLA_HOME in PATH
                # If you want to use the PERLA_HOME in PATH:
                # path_add_command="export PATH=\"$PERLA_HOME:\$PATH\""

                # Add a newline before the comment if the file is not empty and doesn't end with a newline
                if [ -s "$profile_file" ] && [ "$(tail -c1 "$profile_file"; echo x)" != $'\nx' ]; then
                    echo "" >> "$profile_file"
                fi

                echo "" >> "$profile_file" # Ensure separation
                echo "$comment" >> "$profile_file"
                echo "$perla_home_command" >> "$profile_file"
                echo "$path_add_command" >> "$profile_file"
                log_info "Successfully added '$path_to_add' to PATH and set PERLA_HOME in $profile_file."
                log_info "Please restart your shell session or run 'source $profile_file' to apply the changes."
            fi
        fi
    fi
else
    log_info "Skipping profile update as per --no-profile flag."
    log_info "You can manually add '$effective_install_dir' to your PATH if needed."
fi

log_info "Perla installation completed successfully!"
log_info "You can now use the '${TOOL_NAME,,}' command."

exit 0
