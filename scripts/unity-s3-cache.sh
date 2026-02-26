#!/usr/bin/env bash
set -euo pipefail

log() {
  echo "[unity-s3-cache] $*"
}

require_cmd() {
  local cmd="$1"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    log "required command not found: $cmd"
    return 1
  fi
}

normalize_prefix() {
  local prefix="${1:-}"
  prefix="${prefix#/}"
  prefix="${prefix%/}"
  printf '%s' "$prefix"
}

build_s3_key() {
  local object_path="$1"
  local prefix
  prefix="$(normalize_prefix "${UNITY_S3_CACHE_PREFIX:-}")"

  if [ -n "$prefix" ]; then
    printf '%s/%s' "$prefix" "$object_path"
  else
    printf '%s' "$object_path"
  fi
}

resolve_src_dir() {
  # Support both CodeBuild direct execution and GitHub Actions Runner mode
  printf '%s' "${CODEBUILD_SRC_DIR:-${GITHUB_WORKSPACE:-$PWD}}"
}

resolve_commit_hash() {
  # CODEBUILD_RESOLVED_SOURCE_VERSION for direct CodeBuild; GITHUB_SHA for GitHub Actions Runner
  local raw="${CODEBUILD_RESOLVED_SOURCE_VERSION:-${GITHUB_SHA:-}}"

  if [ -n "$raw" ] && [[ "$raw" =~ ^[0-9a-fA-F]{7,40}$ ]]; then
    printf '%s' "$raw"
    return
  fi

  local src_dir
  src_dir="$(resolve_src_dir)"
  if git -C "$src_dir" rev-parse --verify HEAD >/dev/null 2>&1; then
    git -C "$src_dir" rev-parse HEAD
    return
  fi

  printf ''
}

resolve_branch_name() {
  local fallback_branch="${UNITY_S3_CACHE_FALLBACK_BRANCH:-main}"
  local candidates=(
    # CodeBuild direct execution
    "${CODEBUILD_WEBHOOK_HEAD_REF:-}"
    "${CODEBUILD_SOURCE_VERSION:-}"
    # GitHub Actions Runner
    "${GITHUB_REF:-}"
    "${GITHUB_HEAD_REF:-}"
    # Generic fallbacks
    "${GIT_BRANCH:-}"
    "${BRANCH_NAME:-}"
  )

  local candidate=""
  for candidate in "${candidates[@]}"; do
    if [ -z "$candidate" ]; then
      continue
    fi

    case "$candidate" in
      refs/heads/*)
        printf '%s' "${candidate#refs/heads/}"
        return
        ;;
      refs/tags/*)
        continue
        ;;
      pr/*)
        continue
        ;;
      [0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F]*)
        continue
        ;;
      *)
        printf '%s' "$candidate"
        return
        ;;
    esac
  done

  printf '%s' "$fallback_branch"
}

restore_dir_from_cache() {
  local src="$1"
  local dst="$2"

  if [ ! -d "$src" ]; then
    log "cache path does not exist in archive: $src"
    return 0
  fi

  mkdir -p "$dst"

  if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete "$src/" "$dst/"
  else
    # Fallback if rsync is unavailable.
    find "$dst" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
    cp -a "$src/." "$dst/"
  fi

  log "restored: $dst"
}

restore_cache() {
  if [ -z "${UNITY_S3_CACHE_BUCKET:-}" ]; then
    log "UNITY_S3_CACHE_BUCKET is empty. skipping restore."
    return 0
  fi

  require_cmd aws
  require_cmd tar
  require_cmd zstd

  local commit_hash
  local branch_name
  local fallback_branch
  local commit_key
  local branch_key
  local fallback_key
  local selected_key=""

  commit_hash="$(resolve_commit_hash)"
  branch_name="$(resolve_branch_name)"
  fallback_branch="${UNITY_S3_CACHE_FALLBACK_BRANCH:-main}"

  if [ -n "$commit_hash" ]; then
    commit_key="$(build_s3_key "commits/${commit_hash}.tar.zst")"
  else
    commit_key=""
  fi
  branch_key="$(build_s3_key "branches/${branch_name}.tar.zst")"
  fallback_key="$(build_s3_key "branches/${fallback_branch}.tar.zst")"

  local candidates=()
  if [ -n "$commit_key" ]; then
    candidates+=("$commit_key")
  fi
  candidates+=("$branch_key")
  if [ "$fallback_key" != "$branch_key" ]; then
    candidates+=("$fallback_key")
  fi

  local key
  for key in "${candidates[@]}"; do
    if aws s3 ls "s3://${UNITY_S3_CACHE_BUCKET}/${key}" >/dev/null 2>&1; then
      selected_key="$key"
      break
    fi
  done

  if [ -z "$selected_key" ]; then
    log "cache miss for commit=${commit_hash:-unknown}, branch=${branch_name}"
    return 0
  fi

  log "restoring cache from s3://${UNITY_S3_CACHE_BUCKET}/${selected_key}"
  local tmp_dir
  local src_dir
  src_dir="$(resolve_src_dir)"
  tmp_dir="$(mktemp -d /tmp/unity-cache-restore.XXXXXX)"
  aws s3 cp "s3://${UNITY_S3_CACHE_BUCKET}/${selected_key}" - | tar -I 'zstd -d -T0' -xf - -C "$tmp_dir"

  restore_dir_from_cache "$tmp_dir/Library" "${src_dir}/Library"
  restore_dir_from_cache "$tmp_dir/.cache/unity3d" "${HOME:?HOME is required}/.cache/unity3d"
  restore_dir_from_cache "$tmp_dir/.gradle/caches" "${HOME:?HOME is required}/.gradle/caches"
  rm -rf "$tmp_dir"

  log "restore complete"
}

save_archive_to_key() {
  local key="$1"
  local src_dir
  src_dir="$(resolve_src_dir)"
  local tar_sources=()
  if [ -d "${src_dir}/Library" ]; then
    tar_sources+=( -C "$src_dir" "Library" )
  fi
  if [ -d "${HOME:?HOME is required}/.cache/unity3d" ]; then
    tar_sources+=( -C "$HOME" ".cache/unity3d" )
  fi
  if [ -d "$HOME/.gradle/caches" ]; then
    tar_sources+=( -C "$HOME" ".gradle/caches" )
  fi

  if [ "${#tar_sources[@]}" -eq 0 ]; then
    log "no cache directories found; skipping upload"
    return 0
  fi

  log "uploading cache to s3://${UNITY_S3_CACHE_BUCKET}/${key}"
  tar -I 'zstd -T0 -19' -cf - "${tar_sources[@]}" | aws s3 cp - "s3://${UNITY_S3_CACHE_BUCKET}/${key}"
}

copy_cache_object() {
  local src_key="$1"
  local dst_key="$2"

  if [ "$src_key" = "$dst_key" ]; then
    return 0
  fi

  log "copying cache object to s3://${UNITY_S3_CACHE_BUCKET}/${dst_key}"
  aws s3 cp "s3://${UNITY_S3_CACHE_BUCKET}/${src_key}" "s3://${UNITY_S3_CACHE_BUCKET}/${dst_key}"
}

save_cache() {
  if [ -z "${UNITY_S3_CACHE_BUCKET:-}" ]; then
    log "UNITY_S3_CACHE_BUCKET is empty. skipping save."
    return 0
  fi

  require_cmd aws
  require_cmd tar
  require_cmd zstd

  local commit_hash
  local branch_name
  local commit_key
  local branch_key

  commit_hash="$(resolve_commit_hash)"
  branch_name="$(resolve_branch_name)"
  branch_key="$(build_s3_key "branches/${branch_name}.tar.zst")"

  if [ -z "$commit_hash" ]; then
    log "could not resolve commit hash. skipping commit cache upload."
    save_archive_to_key "$branch_key"
  else
    commit_key="$(build_s3_key "commits/${commit_hash}.tar.zst")"
    save_archive_to_key "$commit_key"
    copy_cache_object "$commit_key" "$branch_key"
  fi

  log "save complete"
}

usage() {
  cat <<'USAGE'
Usage: unity-s3-cache.sh [restore|save]

Required environment variables:
  UNITY_S3_CACHE_BUCKET       S3 bucket name for cache storage.

Optional environment variables:
  UNITY_S3_CACHE_PREFIX       S3 key prefix (default: empty).
  UNITY_S3_CACHE_FALLBACK_BRANCH  Fallback branch for restore (default: main).
USAGE
}

main() {
  local action="${1:-}"

  case "$action" in
    restore)
      restore_cache
      ;;
    save)
      save_cache
      ;;
    *)
      usage
      exit 1
      ;;
  esac
}

main "$@"
