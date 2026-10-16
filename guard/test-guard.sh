#!/bin/bash
# Usage: guard/test-guard.sh <guard>. The cases of chroma-local-guard, with sleep or sh as the command: the pipe closes, the child
# ignores SIGTERM, SIGTERM to the guard, the guard killed with SIGKILL (Linux: PR_SET_PDEATHSIG), the exit code passed through, the
# line with the pid, a missing command, the process group.
set -u
guard=$1
fails=0
dir=$(mktemp -d)
alive() { [ -n "$1" ] && kill -0 "$1" 2>/dev/null && [ "$(ps -o stat= -p "$1" 2>/dev/null | cut -c1)" != Z ]; }
check() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1: expected $3, got $2"; fails=$((fails + 1)); fi; }

# Starts the guard with a FIFO as standard input, whose write end is fd 3 of this shell; sets guard and child.
start() {
  rm -f "$dir/in"; mkfifo "$dir/in"
  "$guard" "$@" < "$dir/in" 2> "$dir/err" &
  guard_pid=$!
  exec 3> "$dir/in"
  for _ in $(seq 1 100); do child=$(pgrep -P "$guard_pid" | head -1); [ -n "$child" ] && break; sleep 0.02; done
  [ -n "$child" ] || { echo "FAIL no child for: $*"; fails=$((fails + 1)); }
}
# Milliseconds: the date of GNU has %N, on macOS perl has Time::HiRes.
now_ms() {
  local n
  n=$(date +%s%N)
  case "$n" in *N) perl -MTime::HiRes=time -e 'printf "%d", time * 1000' ;; *) echo $(( n / 1000000 )) ;; esac
}
elapsed_ms() { echo $(( $(now_ms) - $1 )); }

# 1. The pipe closes: SIGTERM to the child, which ends.
start --grace-ms 3000 -- sleep 100
t0=$(now_ms); exec 3>&-; wait "$guard_pid"; code=$?; t=$(elapsed_ms "$t0")
check "pipe closed: guard exit code" "$code" 143
check "pipe closed: child gone" "$(alive "$child" && echo alive || echo gone)" gone
echo "     pipe closed -> end in $t ms"

# 2. The child ignores SIGTERM: SIGKILL after the grace time.
start --grace-ms 500 -- sh -c 'trap "" TERM; while :; do sleep 1; done'
t0=$(now_ms); exec 3>&-; wait "$guard_pid"; code=$?; t=$(elapsed_ms "$t0")
check "SIGTERM ignored: guard exit code" "$code" 137
check "SIGTERM ignored: child gone" "$(alive "$child" && echo alive || echo gone)" gone
echo "     SIGTERM ignored -> SIGKILL after $t ms (grace 500)"

# 3. SIGTERM to the guard.
start -- sleep 100
kill -TERM "$guard_pid"; wait "$guard_pid"; code=$?; exec 3>&-
check "SIGTERM to the guard: exit code" "$code" 143
check "SIGTERM to the guard: child gone" "$(alive "$child" && echo alive || echo gone)" gone

# 4. The guard killed with SIGKILL: on Linux the child gets SIGKILL too.
start -- sleep 100
kill -KILL "$guard_pid"; wait "$guard_pid" 2>/dev/null; exec 3>&-; sleep 0.3
if [ "$(uname)" = Linux ]; then
  check "guard SIGKILL: child gone (PDEATHSIG)" "$(alive "$child" && echo alive || echo gone)" gone
else
  echo "info guard SIGKILL: child $(alive "$child" && echo alive || echo gone) (no PDEATHSIG on $(uname))"
  kill "$child" 2>/dev/null
fi

# 5. The exit code of the command, and the line with its pid.
start -- sh -c 'sleep 0.2; exit 7'
wait "$guard_pid"; code=$?; exec 3>&-
check "exit code of the command" "$code" 7
check "pid line" "$(head -1 "$dir/err")" "chroma-local-guard: pid $child"

# 6. A command that does not exist.
rm -f "$dir/in"; mkfifo "$dir/in"
"$guard" -- /nonexistent < "$dir/in" 2>/dev/null & guard_pid=$!; exec 3> "$dir/in"
wait "$guard_pid"; code=$?; exec 3>&-
check "missing command: exit code" "$code" 127

# 7. The guard leads a process group of its own, with the child in it.
start -- sleep 100
check "guard leads its group" "$(ps -o pgid= -p "$guard_pid" | tr -d ' ')" "$guard_pid"
check "child in the group" "$(ps -o pgid= -p "$child" | tr -d ' ')" "$guard_pid"
exec 3>&-; wait "$guard_pid"

rm -rf "$dir"
echo "failures: $fails"
exit $fails
