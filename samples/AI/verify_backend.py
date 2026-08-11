"""A minimal OpenAI-compatible endpoint, for verifying the sample end to end.

This is not part of the sample. It exists so that a developer without access to a
hosted inference service, and without a local runtime installed, can still see the
whole path work: OPC UA client, Server, backend, HTTP, and back.

    python verify_backend.py 5273
"""

import json
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer


class Handler(BaseHTTPRequestHandler):
    def _send(self, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path.rstrip("/").endswith("models"):
            self._send(
                {
                    "data": [
                        {"id": "verify-model", "object": "model"},
                    ]
                }
            )
        else:
            self.send_error(404)

    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        self.rfile.read(length)
        self._send(
            {
                "id": "chatcmpl-verify",
                "model": "verify-model",
                "choices": [
                    {
                        "index": 0,
                        "finish_reason": "stop",
                        "message": {
                            "role": "assistant",
                            "content": "The last shift ran without incident.",
                        },
                    }
                ],
                "usage": {
                    "prompt_tokens": 11,
                    "completion_tokens": 8,
                    "total_tokens": 19,
                },
            }
        )

    def log_message(self, fmt, *args):
        sys.stderr.write("stub: " + (fmt % args) + "\n")


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 5273
    HTTPServer(("127.0.0.1", port), Handler).serve_forever()
