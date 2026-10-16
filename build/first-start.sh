#!/bin/bash
# Usage: first-start.sh <chroma> [guard]. Inside a container: the time of three starts of chroma until the heartbeat, each on new
# data, with the output on a pipe and the working directory in the data, as ChromaDotNet.Local starts it; with the guard when given.
set -u
bin=$1; guard=${2:-}
ready() { for _ in $(seq 1 3000); do curl -s -m 1 "http://127.0.0.1:$1/api/v2/heartbeat" >/dev/null && return 0; sleep 0.01; done; return 1; }
for i in 1 2 3; do
  data=/tmp/fs$RANDOM$i; mkdir -p "$data"; port=$((18960 + i))
  t0=$(date +%s%N)
  if [ -n "$guard" ]; then
    (cd "$data" && sleep 30 | NO_COLOR=1 "$guard" -- "$bin" run --path "$data" --host 127.0.0.1 --port $port 2>&1 | cat > "$data.log") &
  else
    (cd "$data" && NO_COLOR=1 "$bin" run --path "$data" --host 127.0.0.1 --port $port < /dev/null 2>&1 | cat > "$data.log") &
  fi
  ready $port
  echo "start $i: $(( ($(date +%s%N) - t0) / 1000000 )) ms"
  grep -E "subscriber set|listening" "$data.log" | sed 's/^ */     /'
  pkill -f -- "--port $port" 2>/dev/null
  sleep 0.5
done
