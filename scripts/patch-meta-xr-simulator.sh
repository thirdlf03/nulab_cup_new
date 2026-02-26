#!/usr/bin/env bash
set -euo pipefail

log() {
  echo "[meta-xr-hotfix] $*"
}

project_dir="${1:-${GITHUB_WORKSPACE:-$PWD}}"
package_cache_dir="${project_dir}/Library/PackageCache"

if [ ! -d "$package_cache_dir" ]; then
  log "PackageCache not found: ${package_cache_dir}"
  log "Nothing to patch."
  exit 0
fi

found=0
patched=0

while IFS= read -r installer_file; do
  found=1
  if grep -q "downloadedInstallerPath" "$installer_file"; then
    sed -i 's/\<downloadedInstallerPath\>/string.Empty/g' "$installer_file"
    log "Patched: ${installer_file}"
    patched=1
  else
    log "Token not found, skipped: ${installer_file}"
  fi
done < <(find "$package_cache_dir" -type f -path "*/com.meta.xr.sdk.core@*/Editor/MetaXRSimulator/Installer.cs")

if [ "$found" -eq 0 ]; then
  log "Installer.cs for Meta XR simulator was not found."
  exit 0
fi

if [ "$patched" -eq 0 ]; then
  log "No replacements were needed."
  exit 0
fi

log "Patch completed."
