#!/usr/bin/env python3
"""Waits until something takes connections on 127.0.0.1:<port>, for up to <seconds>, the way the app's Poll.UntilAsync
waits: the real condition, looked at again every half second, and a deadline (exit 1 when it passes).
    python3 -I tools/velopack/wait-port.py <port> [seconds]"""
import socket
import sys
import time

port = int(sys.argv[1])
limit = float(sys.argv[2]) if len(sys.argv) > 2 else 120
deadline = time.monotonic() + limit
while True:
    try:
        socket.create_connection(("127.0.0.1", port), timeout=5).close()
        print(f"wait-port.py: something takes connections on {port}")
        break
    except OSError as e:
        if time.monotonic() > deadline:
            sys.exit(f"wait-port.py: nothing took connections on {port} in {limit} s ({e})")
        time.sleep(0.5)  # the poll's interval, as Poll.UntilAsync's `every`; the deadline ends it
