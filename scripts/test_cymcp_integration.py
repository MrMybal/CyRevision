"""Real SDK + real CyRevision handlers, disposable Git repo, simulated local model.

No account, paid API, global Git configuration or personal application profile is used.
"""
import argparse
import base64
import hashlib
import http.server
import json
import os
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time
import urllib.request
import urllib.error

ROOT = Path(__file__).resolve().parent.parent

class JsonProcess:
    def __init__(self, command):
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, encoding="utf-8", text=True, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        self.lines = queue.Queue()
        self.errors = []
        threading.Thread(target=lambda: [self.lines.put(json.loads(line)) for line in self.process.stdout], daemon=True).start()
        threading.Thread(target=lambda: self.errors.extend(self.process.stderr.readlines()), daemon=True).start()
    def send(self, message):
        self.process.stdin.write(json.dumps(message) + "\n")
        self.process.stdin.flush()
    def receive(self, timeout=25):
        return self.lines.get(timeout=timeout)
    def close(self):
        if self.process.poll() is None:
            self.process.stdin.close()
            try: self.process.wait(8)
            except subprocess.TimeoutExpired:
                self.process.terminate()
                self.process.wait(8)

def payload(response):
    result = response["result"]
    if "structuredContent" in result: return result["structuredContent"]
    return json.loads(result["content"][0]["text"])

class Model(http.server.BaseHTTPRequestHandler):
    requests = []
    remote = ""
    def log_message(self, *_): pass
    def do_GET(self):
        value = {"data": [{"id": "fixture-model", "object": "model"}]}
        data = json.dumps(value).encode()
        self.send_response(200); self.send_header("Content-Length", str(len(data))); self.end_headers(); self.wfile.write(data)
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        self.requests.append(body)
        messages = body.get("messages", [])
        prompt = next((str(m.get("content", "")) for m in reversed(messages) if m["role"] == "user"), "")
        if messages[-1]["role"] == "tool":
            chunks = [{"content": "Tool "}, {"content": "finished."}]
        elif "WRITE" in prompt:
            chunks = [{"tool_calls": [{"index": 0, "id": "fixture-call", "type": "function", "function": {"name": "cy_tools_call", "arguments": json.dumps({"name": "file.write", "arguments": {"path": "assistant.txt", "text": "assistant fixture\n", "expectedSha256": "missing"}})}}]}]
        elif "REMOTE" in prompt:
            chunks = [{"tool_calls": [{"index": 0, "id": "fixture-call", "type": "function", "function": {"name": "cy_tools_call", "arguments": json.dumps({"name": self.remote, "arguments": {"path": "absent.txt" if "ERROR" in prompt else "notes.txt"}})}}]}]
        elif "READ" in prompt:
            chunks = [{"tool_calls": [{"index": 0, "id": "fixture-call", "type": "function", "function": {"name": "cy_tools_call", "arguments": json.dumps({"name": "file.read", "arguments": {"path": "notes.txt"}})}}]}]
        else: chunks = [{"content": "partial "}] * (100 if "CANCEL" in prompt else 2)
        self.send_response(200); self.send_header("Content-Type", "text/event-stream"); self.end_headers()
        try:
            for delta in chunks:
                self.wfile.write(("data: " + json.dumps({"choices": [{"index": 0, "delta": delta}]}) + "\n\n").encode()); self.wfile.flush()
                time.sleep(.06 if "CANCEL" not in prompt else .1)
            finish = "tool_calls" if "tool_calls" in chunks[-1] else "stop"
            self.wfile.write(("data: " + json.dumps({"choices": [{"index": 0, "delta": {}, "finish_reason": finish}]}) + "\n\ndata: [DONE]\n\n").encode()); self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError): pass

def run(cli):
    count = 0
    def check(condition, label):
        nonlocal count
        assert condition, label
        count += 1
        print("PASS " + label, flush=True)
    with tempfile.TemporaryDirectory(prefix="cymcp-e2e-", dir=ROOT / "artifacts") as temporary:
        work = Path(temporary); repo = work / "repository"; repo.mkdir()
        profile = work / "profile"
        def git(*args): return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True, text=True).stdout
        git("init", "-b", "main"); git("config", "user.name", "CyRevision Tests"); git("config", "user.email", "9800352+MrMybal@users.noreply.github.com")
        (repo / "notes.txt").write_text("synthetic fixture marker\n", encoding="utf-8")
        (repo / "large.txt").write_text("large fixture line\n" * 70000, encoding="utf-8")
        (repo / ".env").write_text("SYNTHETIC_PRIVATE_MARKER=hidden\n", encoding="utf-8")
        git("add", "notes.txt", "large.txt"); git("commit", "-m", "Initial fixture")
        common = ["--project", str(repo), "--profile-dir", str(profile)]
        def call(*args, code=0):
            result = subprocess.run([str(cli), *args, *common], capture_output=True, encoding="utf-8", timeout=40)
            assert result.returncode == code, (args[:2], result.returncode, result.stderr)
            rows = [json.loads(line) for line in result.stdout.splitlines()]
            assert rows, result.stderr
            if code: assert result.stderr and rows[-1]["ok"] is False
            return rows
        def invoke(name, args, *flags, code=0): return call("invoke", name, "--arguments", json.dumps(args), *flags, code=code)[-1]
        check(len(call("list")[-1]["result"]) >= 34, "CLI shared registry")
        check(call("schema", "file.write")[-1]["result"]["inputSchema"]["required"] == ["path", "text", "expectedSha256"], "CLI schema")
        check(invoke("project.info", {})["result"]["value"]["sdkVersion"] == "0.7.0", "source SDK revision")
        check(invoke("file.read", {"path": "notes.txt"})["result"]["value"]["text"].startswith("synthetic"), "real file read")
        invoke("file.read", {"path": "../outside"}, code=3); invoke("file.read", {"path": ".env"}, code=3)
        check(not invoke("code.search", {"text": "SYNTHETIC_PRIVATE_MARKER"})["result"]["value"]["items"], "private paths excluded from search")
        write = {"path": "new.txt", "text": "created\n", "expectedSha256": "missing"}
        invoke("file.write", write, code=3); invoke("file.write", write, "--allow-write", code=4)
        check(not (repo / "new.txt").exists(), "denied and unconfirmed writes unchanged")
        invoke("file.write", write, "--allow-write", "--confirm")
        check((repo / "new.txt").read_text() == "created\n", "confirmed real write")
        invoke("file.write", write, "--allow-write", "--confirm", code=6)
        invoke("git.stage", {"paths": ["new.txt"]}, "--allow-write", "--confirm", code=3)
        invoke("git.stage", {"paths": ["new.txt"]}, "--allow-write", "--allow-stage", "--confirm")
        check("A  new.txt" in git("status", "--porcelain"), "independent stage permission")
        call("list", "--unknown-flag", "x", code=2)
        snapshot = invoke("backup.create", {}, "--allow-write", "--confirm")["result"]["value"]["snapshotId"]
        check(invoke("backup.manifest", {"snapshotId": snapshot})["result"]["value"]["snapshot"]["snapshotId"] == snapshot, "actual backup snapshot and manifest")
        invoke("backup.restoreFile", {"snapshotId": snapshot, "path": "notes.txt", "destination": "restored-notes.txt"}, "--allow-write", "--confirm")
        check((repo / "restored-notes.txt").read_text() == (repo / "notes.txt").read_text(), "actual backup restore to absent destination")
        other = work / "other-project"; other.mkdir()
        denied = subprocess.run([str(cli), "invoke", "backup.manifest", "--project", str(other), "--profile-dir", str(profile), "--arguments", json.dumps({"snapshotId": snapshot})], capture_output=True, encoding="utf-8", timeout=15)
        check(denied.returncode == 3, "backup scope rejects another project's snapshot")
        mcp = JsonProcess([str(cli), "mcp", *common, "--metrics", "--notifications", "--disk-spool", "--result-ttl-ms", "2000", "--inline-bytes", "4096", "--chunk-bytes", "4096"])
        seq = 0
        def rpc(method, params=None):
            nonlocal seq
            seq += 1; mcp.send(dict(jsonrpc="2.0", id=seq, method=method, params=params or {}))
            while True:
                response = mcp.receive()
                if response.get("id") == seq: return response
        def tool(name, arguments): return rpc("tools/call", dict(name=name, arguments=arguments))
        try:
            check("protocolVersion" in rpc("initialize", {"protocolVersion": "2025-11-25", "capabilities": {}, "clientInfo": {"name": "fixture", "version": "1"}})["result"], "MCP initialize")
            check(len(rpc("tools/list")["result"]["tools"]) == 5, "MCP compact discovery")
            large = payload(tool("cy_tools_call", {"name": "file.read", "arguments": {"path": "large.txt", "length": 131072}}))
            resource = large["resource"]; uri = resource["uri"]
            manifest = payload(tool("cy_resources_read", {"uri": uri}))
            first = json.loads(manifest["contents"][0]["text"])["firstUri"]
            chunks = []; cursor = first
            while cursor:
                page = payload(tool("cy_resources_read", {"uri": cursor})); chunks.append(page["contents"][0]["text"])
                cursor = page.get("_meta", {}).get("com.cymcp/chunk", {}).get("nextUri")
            recovered = json.loads("".join(chunks))["value"]
            check(base64.b64decode(recovered["bytesBase64"]) == (repo / "large.txt").read_bytes()[:131072], "complete large retained result")
            grep = payload(tool("cy_resources_grep", {"text": "large fixture"}))
            check(bool(grep.get("matches")), "native grep on registered snapshot")
            tool("cy_tools_call", {"name": "project.refresh", "arguments": {}})
            grep = payload(tool("cy_resources_grep", {"text": "large fixture"}))
            check(not grep.get("matches"), "explicit refresh invalidates snapshots before TTL")
            (repo / "large.txt").write_text("changed large fixture line\n" * 70000, encoding="utf-8")
            spooled = payload(tool("cy_tools_call", {"name": "git.diff", "arguments": {"path": "large.txt"}}))
            check("resource" in spooled and any((profile / "automation/results").iterdir()), "large actual Git diff uses private disk spool")
            denied = tool("cy_tools_call", {"name": "file.write", "arguments": {"path": "forbidden.txt", "text": "x", "expectedSha256": "missing"}})
            check(denied.get("result", {}).get("isError", False) or "error" in denied, "MCP same write denial")
            time.sleep(2.1)
            expired = tool("cy_resources_read", {"uri": uri})
            check(expired.get("result", {}).get("isError", False) or "error" in expired, "expired handle fails without replay")
        finally: mcp.close()
        metrics = (profile / "automation/performance.jsonl").read_text(encoding="utf-8")
        check("large fixture" not in metrics and "SYNTHETIC_PRIVATE_MARKER" not in metrics and "bytesBase64" not in metrics, "metadata-only measurements")
        endpoint = work / "endpoint.json"
        server = JsonProcess([str(cli), "serve", *common, "--endpoint-file", str(endpoint), "--full-discovery"])
        try:
            server.receive(); descriptor = json.loads(endpoint.read_text())
            check(call("invoke", "git.status", "--instance", str(endpoint))[-1]["result"]["value"]["currentBranch"] == "main", "CLI attaches real instance")
            try: urllib.request.urlopen(urllib.request.Request(descriptor["url"] + "/v1", data=b'{"method":"describe"}', headers={"Content-Type":"application/json"})); unauthorized = False
            except urllib.error.HTTPError as error: unauthorized = error.code == 401
            check(unauthorized, "loopback endpoint requires authentication")
            model = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Model)
            threading.Thread(target=model.serve_forever, daemon=True).start()
            endpoint_model = "http://127.0.0.1:" + str(model.server_port) + "/v1"
            chat = ["chat", "--provider", "openai-compatible", "--model", "fixture-model", "--endpoint", endpoint_model, "--stream"]
            rows = call(*chat, "--text", "READ synthetic fixture", "--history-id", "read-test")
            check(any(row.get("type") == "event" and row["event"]["type"] == "TextDelta" for row in rows), "SDK chat streaming")
            check(rows[-1]["result"]["text"] == "Tool finished.", "chat invokes actual read handler")
            approval = work / "approval.json"
            approval.write_text(json.dumps({"operation": "file.write", "arguments": {"path": "assistant.txt", "text": "assistant fixture\n", "expectedSha256": "missing"}}))
            call(*chat, "--text", "WRITE fixture", "--allow-write", "--approval-file", str(approval), "--history-id", "write-test")
            check((repo / "assistant.txt").read_text() == "assistant fixture\n", "chat confirmed real business modification")
            call(*chat, "--text", "WRITE denied fixture", "--history-id", "denied-test")
            check((repo / "assistant.txt").read_text() == "assistant fixture\n", "chat denied operation unchanged")
            call(*chat, "--text", "READ restored fixture", "--history-id", "read-test", "--restore-history", "--context-bytes", "12000")
            history = json.loads((profile / "automation/conversations/read-test.json").read_text())
            check(len(history["messages"]) >= 8, "restoration preserves tool groups and full history")
            long_prompt = "LONG_CONTEXT_FIXTURE " + "x" * 6000
            call(*chat, "--text", long_prompt, "--history-id", "context-test")
            call(*chat, "--text", "hello", "--history-id", "context-test", "--restore-history", "--context-bytes", "1200")
            history = (profile / "automation/conversations/context-test.json").read_text()
            check(long_prompt in history and long_prompt not in json.dumps(Model.requests[-1]), "API context reduced while complete history retained")
            call(*chat, "--text", "CANCEL fixture", "--timeout-ms", "800", "--history-id", "cancel-test", code=130)
            check((profile / "automation/conversations/cancel-test.json").exists(), "cancelled session persisted")
            remote_config = work / "remote.json"
            remote_config.write_text(json.dumps({"id": "fixture-peer", "url": descriptor["url"] + "/mcp", "tokenEnvironment": "CYREVISION_FIXTURE_TOKEN", "allowedTools": ["file.read"]}))
            Model.remote = "remote_" + hashlib.sha256(b"fixture-peer/file.read").hexdigest()[:24]
            os.environ["CYREVISION_FIXTURE_TOKEN"] = descriptor["token"]
            approval.write_text(json.dumps({"operation": Model.remote, "arguments": {"path": "notes.txt"}}))
            remote_rows = call(*chat, "--text", "REMOTE fixture", "--allow-network", "--remote-config-file", str(remote_config), "--approval-file", str(approval))
            check(remote_rows[-1]["result"]["text"] == "Tool finished.", "external SDK MCP tool from chat")
            approval.write_text(json.dumps({"operation": Model.remote, "arguments": {"path": "absent.txt"}}))
            remote_rows = call(*chat, "--text", "REMOTE ERROR fixture", "--allow-network", "--remote-config-file", str(remote_config), "--approval-file", str(approval))
            check("RemoteToolError" in json.dumps(remote_rows), "external tool error reaches assistant")
            model.shutdown(); model.server_close(); os.environ.pop("CYREVISION_FIXTURE_TOKEN", None)
        finally: server.close()
    print(f"CyMCPSDK integration: {count} checks passed. Model responses were simulated locally.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(); parser.add_argument("--cli", type=Path, default=ROOT / "artifacts/native-v2/Cli/CyRevision.Cli.exe")
    run(parser.parse_args().cli.resolve())
