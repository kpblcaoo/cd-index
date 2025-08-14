#!/usr/bin/env bash
# Helper script for human-friendly inspection of cd-index JSON output using jq.
# Requires: jq
# Usage examples:
#   scripts/inspect-index.sh scan ../ClubDoorman/ClubDoorman.sln --scan-entrypoints --scan-di --scan-commands --scan-flow --flow-handler StartCommandHandler
#   scripts/inspect-index.sh file path/to/project_index.json summary
#
# Modes:
#   scan <scan-args...> [-- <extra-cli-args>]
#       Runs the cd-index CLI scan with provided args (those after 'scan') and captures JSON.
#       Everything after a standalone -- is passed verbatim to the CLI (useful for --verbose).
#   file <json-file> <subcommand>
#       Work with an existing JSON file.
#
# Subcommands (default: summary):
#   summary          : Top-level counts (files, DI regs, commands, entrypoints, flow nodes)
#   files            : List files (loc + path)
#   top-files [N]    : Top N files by loc (default 20)
#   di               : DI registrations (service -> impl [lifetime])
#   commands         : Commands (name: handler)
#   entrypoints      : Entrypoints (name path:line)
#   flow             : Flow nodes (order kind symbol path:line)
#   flow-delegates   : Only delegate nodes (order symbol)
#   sha              : sha256  path list
#   raw              : Pretty-print whole JSON
#   select <jq-expr> : Run custom jq expression against the document
#
# Output is stable & newline separated for easy grepping/fzf when desired.

set -euo pipefail

if ! command -v jq >/dev/null 2>&1; then
  echo "Error: jq is required" >&2
  exit 1
fi

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/.." && pwd)
CLI_PROJECT="$REPO_ROOT/src/CdIndex.Cli"
TMP_JSON="${TMPDIR:-/tmp}/cd-index-scan-$$.json"

cleanup() { rm -f "$TMP_JSON" 2>/dev/null || true; }
trap cleanup EXIT

color() { local c=$1; shift; printf "\033[%sm%s\033[0m" "$c" "$*"; }
headline() { color 1 "# $*"; echo; }

ensure_json() {
  local mode=$1
  if [[ $mode == scan ]]; then
    shift
    local args=()
    local cli_extra=()
    local passthrough=0
    for a in "$@"; do
      if [[ $a == -- ]]; then
        passthrough=1; continue
      fi
      if (( passthrough )); then cli_extra+=("$a"); else args+=("$a"); fi
    done
    headline "Running scan" >&2
    # Run CLI (JSON to stdout). We redirect to file; verbose logs are expected on stderr.
    dotnet run --project "$CLI_PROJECT" -- scan "${args[@]}" "${cli_extra[@]}" >"$TMP_JSON"
    echo "$TMP_JSON"
  else
    echo "$2"
  fi
}

subcommand_summary() {
  jq -r '
    def sum(l): reduce l[] as $x (0; . + $x);
    def files: ( ( .Tree // [] ) | map( (.Files // []) | length ) | sum );
    def diRegs: ( ( .DI // [] ) | map( (.Registrations // []) | length ) | sum );
    def commands: ( ( .Commands // [] ) | map( (.Items // []) | length ) | sum );
    def entrypoints: ( ( .Entrypoints // [] ) | length );
    def flowNodes: ( ( .MessageFlow // [] ) | map( (.Nodes // []) | length ) | sum );
    "files=" + (files|tostring) +
    " di=" + (diRegs|tostring) +
    " commands=" + (commands|tostring) +
    " entrypoints=" + (entrypoints|tostring) +
    " flowNodes=" + (flowNodes|tostring)
  ' "$1"
}

# Collect all files across Tree sections then list
subcommand_files() { jq -r '( .Tree // [] ) | map(.Files[]) | .[] | "\(.Loc)\t\(.Path)"' "$1" | sort -n; }
subcommand_top_files() { local n=${2:-20}; jq -r '( .Tree // [] ) | map(.Files[]) | .[] | sort_by(-.Loc)[0:'"$n"'][] | "\(.Loc)\t\(.Path)"' "$1"; }
subcommand_di() { jq -r '( .DI // [] ) | map(.Registrations[]) | .[] | "\(.Interface) -> \(.Implementation) [\(.Lifetime)]"' "$1" | sort; }
subcommand_commands() { jq -r '( .Commands // [] ) | map(.Items[]) | .[] | sort_by(.Command)[]? | "\(.Command): \(.Handler // "")"' "$1"; }
subcommand_entrypoints() { jq -r '( .Entrypoints // [] )[] | "\(.Project.Name)\t\(.ProgramMain.File):\(.ProgramMain.Line)"' "$1" | sort; }
subcommand_flow() { jq -r '( .MessageFlow // [] ) | map(.Nodes[]) | .[]? | sort_by(.Order)[]? | "\(.Order)\t\(.Kind)\t\(.Detail)\t\(.File):\(.Line)"' "$1"; }
subcommand_flow_delegates() { jq -r '( .MessageFlow // [] ) | map(.Nodes[]) | .[]? | map(select(.Kind=="delegate")) | .[]? | sort_by(.Order)[]? | "\(.Order)\t\(.Detail)"' "$1"; }
subcommand_sha() { jq -r '( .Tree // [] ) | map(.Files[]) | .[] | "\(.Sha256)\t\(.Path)"' "$1" | sort; }
subcommand_raw() { jq '.' "$1"; }
subcommand_select() { shift 2; jq -r "$*" "$1"; }

run_subcommand() {
  local json=$1; shift
  local sub=${1:-summary}; shift || true
  case $sub in
    summary) subcommand_summary "$json" ;;
    files) subcommand_files "$json" ;;
    top-files) subcommand_top_files "$json" "$@" ;;
    di) subcommand_di "$json" ;;
    commands) subcommand_commands "$json" ;;
    entrypoints) subcommand_entrypoints "$json" ;;
    flow) subcommand_flow "$json" ;;
    flow-delegates) subcommand_flow_delegates "$json" ;;
    sha) subcommand_sha "$json" ;;
    raw) subcommand_raw "$json" ;;
    select) subcommand_select "$json" "$@" ;;
    *) echo "Unknown subcommand: $sub" >&2; exit 2 ;;
  esac
}

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 scan <scan-args...> [-- <extra-cli-args>] | file <json> <subcommand>" >&2
  exit 1
fi

mode=$1; shift
case $mode in
  scan)
    if [[ $# -lt 1 ]]; then
      echo "Provide at least --sln <solution>" >&2; exit 2
    fi
    json_file=$(ensure_json scan "$@")
    run_subcommand "$json_file" summary
    ;;
  file)
    if [[ $# -lt 1 ]]; then echo "file mode requires path" >&2; exit 2; fi
    json_path=$1; shift
    if [[ ! -f $json_path ]]; then echo "Not found: $json_path" >&2; exit 2; fi
    run_subcommand "$json_path" "$@"
    ;;
  *)
    echo "Unknown mode: $mode" >&2; exit 2 ;;
 esac
