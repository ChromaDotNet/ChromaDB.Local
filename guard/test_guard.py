"""The cases of chroma-local-guard, with sleep or sh as the command, and an anonymous pipe as its standard input, as ChromaDotNet.Local
gives it: the pipe closes, the child ignores SIGTERM, SIGTERM to the guard, the guard killed with SIGKILL (Linux: PR_SET_PDEATHSIG),
the exit code passed through, the line with the pid, a missing command, the process group.

Usage: python3 guard/test_guard.py <guard>
"""
import os
import platform
import signal
import subprocess
import sys
import time

guard = sys.argv[1]
failures = 0


def check(name, actual, expected):
    global failures
    if actual == expected:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: expected {expected!r}, got {actual!r}")
        failures += 1


def alive(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    stat = subprocess.run(["ps", "-o", "stat=", "-p", str(pid)], capture_output=True, text=True).stdout.strip()
    return stat != "" and not stat.startswith("Z")


def wait_until(condition, seconds):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        if condition():
            return True
        time.sleep(0.02)
    return condition()


def start(*args):
    """The guard with a pipe as standard input; returns the process and the pid of its child, from the line of the guard."""
    process = subprocess.Popen([guard, *args], stdin=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    line = process.stderr.readline().strip()
    prefix = "chroma-local-guard: pid "
    child = int(line[len(prefix):]) if line.startswith(prefix) else None
    if child is None:
        print(f"FAIL no pid line for {args}: {line!r}")
    return process, child


def finish(process, seconds=10):
    try:
        return process.wait(seconds)
    except subprocess.TimeoutExpired:
        print(f"FAIL guard {process.pid} still running after {seconds} s: killed")
        process.kill()
        return process.wait()


# 1. The pipe closes: SIGTERM to the child, which ends.
p, child = start("--grace-ms", "3000", "--", "sleep", "100")
check("pid line: the child of the guard", subprocess.run(["ps", "-o", "ppid=", "-p", str(child)], capture_output=True, text=True).stdout.strip(), str(p.pid))
t0 = time.monotonic(); p.stdin.close(); code = finish(p); t = round((time.monotonic() - t0) * 1000)
check("pipe closed: guard exit code", code, 143)
check("pipe closed: child gone", alive(child), False)
print(f"     pipe closed -> end in {t} ms")

# 2. The child ignores SIGTERM: SIGKILL after the grace time.
p, child = start("--grace-ms", "500", "--", "sh", "-c", "trap '' TERM; while :; do sleep 1; done")
time.sleep(0.2)
t0 = time.monotonic(); p.stdin.close(); code = finish(p); t = round((time.monotonic() - t0) * 1000)
check("SIGTERM ignored: guard exit code", code, 137)
check("SIGTERM ignored: child gone", alive(child), False)
print(f"     SIGTERM ignored -> SIGKILL after {t} ms (grace 500)")

# 3. SIGTERM to the guard.
p, child = start("--", "sleep", "100")
p.send_signal(signal.SIGTERM); code = finish(p)
check("SIGTERM to the guard: exit code", code, 143)
check("SIGTERM to the guard: child gone", alive(child), False)

# 3b. SIGTERM right after the line with the pid, when the child may not have reached its exec: it must still end with SIGTERM, not
# with the SIGKILL after the grace time.
late = []
for attempt in range(50):
    p, child = start("--grace-ms", "3000", "--", "sleep", "100")
    t0 = time.monotonic(); p.send_signal(signal.SIGTERM); code = finish(p)
    if code != 143 or time.monotonic() - t0 > 2:
        late.append((attempt, code, round(time.monotonic() - t0, 2)))
check("SIGTERM at once, 50 times: always 143 within 2 s", late, [])

# 4. The guard killed with SIGKILL: on Linux the child gets SIGKILL too.
p, child = start("--", "sleep", "100")
p.kill(); finish(p)
if platform.system() == "Linux":
    check("guard SIGKILL: child gone (PDEATHSIG)", wait_until(lambda: not alive(child), 2), True)
else:
    print(f"info guard SIGKILL: child {'alive' if alive(child) else 'gone'} (no PDEATHSIG on {platform.system()})")
    if alive(child):
        os.kill(child, signal.SIGKILL)

# 5. The exit code of the command.
p, child = start("--", "sh", "-c", "sleep 0.2; exit 7")
check("exit code of the command", finish(p), 7)

# 6. A command that does not exist.
p = subprocess.Popen([guard, "--", "/nonexistent"], stdin=subprocess.PIPE, stderr=subprocess.DEVNULL)
check("missing command: exit code", finish(p), 127)

# 7. The guard leads a process group of its own, with the child in it.
p, child = start("--", "sleep", "100")
check("guard leads its group", os.getpgid(p.pid), p.pid)
check("child in the group", os.getpgid(child), p.pid)
p.stdin.close(); finish(p)

print(f"failures: {failures}")
sys.exit(failures)
