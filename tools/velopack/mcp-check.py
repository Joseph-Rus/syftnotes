#!/usr/bin/env python3
"""Starts Syft Notes' MCP server exactly as an AI app's settings say, the way the app would (the Velopack bridge test,
.github/workflows/velopack-bridge.yml), and asks it for the student's classes: the connection works and the library
answers through it.
    python3 -I tools/velopack/mcp-check.py <settings.json|config.toml> <class it must list> [seconds]
Reads mcpServers["syft-notes"] (Claude's JSON) or [mcp_servers.syft-notes] (Codex's TOML); the library must already be
running. Prints what came back and exits 0 when list_classes answered with the class."""
import json
import queue
import subprocess
import sys
import threading

path, expect = sys.argv[1], sys.argv[2]
limit = float(sys.argv[3]) if len(sys.argv) > 3 else 60
text = open(path, encoding="utf-8").read()
if path.endswith(".toml"):
    import tomllib
    entry = tomllib.loads(text)["mcp_servers"]["syft-notes"]
else:
    entry = json.loads(text)["mcpServers"]["syft-notes"]
command = [entry["command"], *entry.get("args", [])]
print("mcp-check.py: starting", json.dumps(command))
p = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, bufsize=1)
lines: "queue.Queue[str]" = queue.Queue()
threading.Thread(target=lambda: [lines.put(l) for l in p.stdout], daemon=True).start()


def send(message):
    p.stdin.write(json.dumps(message) + "\n")
    p.stdin.flush()


def answer(want):
    while True:
        try:
            line = lines.get(timeout=limit)
        except queue.Empty:
            sys.exit(f"mcp-check.py: no answer to request {want} in {limit} s")
        try:
            message = json.loads(line)
        except json.JSONDecodeError:
            continue
        if message.get("id") == want:
            return message


try:
    send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18", "capabilities": {},
                                                                          "clientInfo": {"name": "bridge-test", "version": "1"}}})
    print("mcp-check.py: server", json.dumps(answer(1).get("result", {}).get("serverInfo")))
    send({"jsonrpc": "2.0", "method": "notifications/initialized"})
    send({"jsonrpc": "2.0", "id": 2, "method": "tools/call", "params": {"name": "list_classes", "arguments": {}}})
    result = answer(2)
    print("mcp-check.py: list_classes ->", json.dumps(result)[:400])
    if "error" in result or result.get("result", {}).get("isError") or expect not in json.dumps(result):
        sys.exit(f"mcp-check.py: list_classes failed, or didn't list {expect}")
finally:
    p.stdin.close()
    try:
        p.wait(timeout=10)
    except subprocess.TimeoutExpired:
        p.kill()
print("mcp-check.py: ok")
