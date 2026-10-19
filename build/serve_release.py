"""A mirror of the GitHub release for the tests of the download: serves the files of a directory, and appends to a log file the name
of each file it serves.

Usage: python3 build/serve_release.py <directory> <port> <log> [--corrupt]

--corrupt serves each .gz with one byte of the file inside changed, compressed again: a valid gzip whose content does not have the
hash the package expects.
"""
import gzip
import http.server
import os
import sys

directory, port, log = sys.argv[1], int(sys.argv[2]), sys.argv[3]
corrupt = "--corrupt" in sys.argv


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        name = os.path.basename(self.path.split("?")[0])
        path = os.path.join(directory, name)
        if not os.path.isfile(path):
            self.send_error(404)
            return
        with open(path, "rb") as f:
            data = f.read()
        if corrupt and name.endswith(".gz"):
            content = bytearray(gzip.decompress(data))
            content[len(content) // 2] ^= 0xFF
            data = gzip.compress(bytes(content))
        with open(log, "a") as f:
            f.write(name + "\n")
        self.send_response(200)
        self.send_header("Content-Type", "application/gzip")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, format, *args):
        pass


http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
