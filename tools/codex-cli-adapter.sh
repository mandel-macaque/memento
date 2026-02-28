#!/bin/sh
set -eu

cmd="${1:-}"
sub="${2:-}"

if [ "$cmd" = "sessions" ] && [ "$sub" = "get" ]; then
  sid="${3:-}"
  if [ -z "$sid" ]; then
    echo "missing session id" >&2
    exit 1
  fi

  session_file="$(find "$HOME/.codex/sessions" -type f -name "*${sid}*.jsonl" 2>/dev/null | head -n1)"
  if [ -z "$session_file" ] || [ ! -f "$session_file" ]; then
    echo "session not found: $sid" >&2
    exit 1
  fi

  jq -cs --arg sid "$sid" '
    def text_of_message:
      (.payload.content // []
      | map(select(.type == "input_text" or .type == "output_text") | .text)
      | join("\n")
      | gsub("^\\s+|\\s+$"; ""));

    def first_user_title:
      ([ .[]
          | select(.type == "response_item" and .payload.type == "message" and .payload.role == "user")
          | text_of_message
          | select(length > 0)
        ][0] // "Codex session");

    {
      id: $sid,
      title: first_user_title,
      messages:
        [ .[]
          | select(.type == "response_item" and .payload.type == "message" and (.payload.role == "user" or .payload.role == "assistant"))
          | {
              role: .payload.role,
              content: text_of_message
            }
          | select(.content | length > 0)
        ]
    }
  ' "$session_file"
  exit 0
fi

if [ "$cmd" = "sessions" ] && [ "$sub" = "list" ]; then
  files="$(find "$HOME/.codex/sessions" -type f -name '*.jsonl' 2>/dev/null | sort | tail -n 50)"
  if [ -z "$files" ]; then
    echo "[]"
    exit 0
  fi

  printf '%s\n' "$files" | while IFS= read -r file; do
    [ -n "$file" ] || continue
    head -n 1 "$file" | jq -c '
      {
          id: (.payload.id // ""),
          title: (((.payload.cwd // "session") + " @ " + (.payload.timestamp // "")))
        }
    '
  done | jq -s 'map(select(.id != ""))'
  exit 0
fi

echo "unsupported command: $*" >&2
exit 2
