"""Local access profiles against the actual desktop engine, CLI and SDK chat adapter."""
import argparse
import http.server
import json
from pathlib import Path
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request
from test_cymcp_integration import ROOT, JsonProcess, Model, payload


class Mcp:
    def __init__(self, url, token=""):
        self.url, self.token, self.session, self.version, self.sequence = url, token, "", "", 0

    def call(self, method, params=None, extra_headers=None):
        self.sequence += 1
        headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
        if self.token: headers["Authorization"] = "Bearer " + self.token
        if self.session: headers["Mcp-Session-Id"] = self.session
        if self.version: headers["MCP-Protocol-Version"] = self.version
        headers.update(extra_headers or {})
        data = json.dumps(dict(jsonrpc="2.0", id=self.sequence, method=method, params=params or {})).encode()
        with urllib.request.urlopen(urllib.request.Request(self.url, data=data, headers=headers), timeout=25) as response:
            self.session = response.headers.get("Mcp-Session-Id", self.session)
            result = json.load(response)
        if method == "initialize": self.version = result["result"]["protocolVersion"]
        return result

    def initialize(self):
        return self.call("initialize", dict(protocolVersion="2025-11-25", capabilities={}, clientInfo=dict(name="CyRevision fixture", version="1")))

    def tool(self, name, arguments=None):
        return self.call("tools/call", dict(name="cy_tools_call", arguments=dict(name=name, arguments=arguments or {})))

    def operation(self, name, arguments=None):
        result = payload(self.tool(name, arguments))
        if "resource" in result:
            self.last_resource = result["resource"]["uri"]
            retained = payload(self.call("tools/call", dict(name="cy_resources_read", arguments=dict(uri=result["resource"]["uri"]))))
            document = json.loads(retained["contents"][0]["text"])
            if "firstUri" not in document: return document
            cursor = document["firstUri"]; chunks = []
            while cursor:
                page = payload(self.call("tools/call", dict(name="cy_resources_read", arguments=dict(uri=cursor))))
                chunks.append(page["contents"][0]["text"])
                cursor = page.get("_meta", {}).get("com.cymcp/chunk", {}).get("nextUri")
            result = json.loads("".join(chunks))
        return result


def run(app, cli):
    count = 0
    def check(condition, label):
        nonlocal count
        assert condition, label
        count += 1
        print("PASS " + label, flush=True)

    with tempfile.TemporaryDirectory(prefix="cymcp-access-", dir=ROOT / "artifacts") as temporary:
        root = Path(temporary); repo = root / "repository"; repo.mkdir()
        def git(*args): subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True)
        git("init", "-b", "main"); git("config", "user.name", "CyRevision Tests")
        git("config", "user.email", "9800352+MrMybal@users.noreply.github.com")
        (repo / "notes.txt").write_text("access fixture\n"); git("add", "."); git("commit", "-m", "Access fixture")
        backend = JsonProcess([str(app), "--native-backend", "--profile-dir", str(root / "profile")])
        model = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Model)
        threading.Thread(target=model.serve_forever, daemon=True).start()
        sequence, project, pending = 0, "", {}
        def request(command, **values):
            nonlocal sequence
            sequence += 1; number = sequence
            backend.send(dict(id=number, command=command, projectId=project, **values))
            while number not in pending:
                response = backend.receive(40); pending[response.get("id")] = response
            response = pending.pop(number); assert response.get("ok"), response.get("error")
            return response.get("result")
        def state(): return request("state", workspace="ai", fields=["AiSdkLocalAccessProfile", "AiSdkMcpUrl", "AiSdkInstanceFile", "AiSdkHasApproval", "AiSdkStatus", "AiSdkAccessStatus"])
        def value(field): return state()["fields"][field]["value"]
        def wait_ready():
            deadline = time.monotonic() + 40
            while time.monotonic() < deadline:
                snapshot = state()
                if snapshot["ready"] and not snapshot["busy"] and not snapshot.get("loading"): return snapshot
                time.sleep(.1)
            raise AssertionError("Desktop did not finish loading")
        def host_set(field, setting): request("set", property=field, value=setting)
        def start(profile=None):
            if profile: host_set("AiSdkLocalAccessProfile", profile)
            request("invoke", action="StartAiSdkMcpServerAsync")
            descriptor = json.loads(Path(value("AiSdkInstanceFile")).read_text())
            assert value("AiSdkMcpUrl") == descriptor["url"] + "/mcp"
            return descriptor
        def cli_call(*args, code=0):
            result = subprocess.run([str(cli), *args], capture_output=True, text=True, encoding="utf-8", timeout=30)
            assert result.returncode == code, (result.returncode, result.stderr)
            return json.loads(result.stdout)
        try:
            wait_ready(); request("invoke", action="AddExistingRepositoryAsync", parameters=dict(path=str(repo)))
            project = wait_ready()["projectId"]
            plugins = request("state", workspace="plugins", table="Plugins")["table"]["rows"]
            plugin = next(row for row in plugins if row["values"].get("Id") == "cyrevision.ai")
            if not plugin["values"]["IsEnabled"]:
                request("select", source="Plugins", selection="SelectedPlugin", index=plugin["index"], key=plugin["key"])
                request("invoke", action="EnableSelectedPluginAsync"); wait_ready()
            check(value("AiSdkLocalAccessProfile") == "Free", "unchosen desktop profile defaults to Free")
            descriptor = start(); check(not descriptor["token"] and descriptor["url"].startswith("http://127.0.0.1:"), "Free uses tokenless IPv4 loopback")
            mcp = Mcp(value("AiSdkMcpUrl")); mcp.initialize()
            check(payload(mcp.tool("file.read", dict(path="notes.txt")))["value"]["text"].strip() == "access fixture", "actual desktop MCP read by URL alone")
            arguments = dict(path="free.txt", text="written without approval\n", expectedSha256="missing")
            result = mcp.tool("file.write", arguments)
            check(not result.get("error") and (repo / "free.txt").read_text() == arguments["text"] and not value("AiSdkHasApproval"), "actual desktop MCP write with grants off and no confirmation")
            wait_ready()
            result = mcp.tool("file.write", dict(path="bad.txt", text="missing required argument"))
            check("error" in result or result.get("result", {}).get("isError"), "Free still validates required arguments")
            check(not (repo / "bad.txt").exists(), "invalid write has no side effects")
            result = mcp.tool("file.read", dict(path="../outside.txt"))
            check("error" in result or result.get("result", {}).get("isError"), "Free retains project path validation")
            try: mcp.call("tools/list", extra_headers={"Origin": "https://untrusted.invalid"}); rejected = False
            except urllib.error.HTTPError as error: rejected = error.code in (400, 403)
            check(rejected, "tokenless server keeps browser-origin protections")
            check(cli_call("invoke", "app.state", "--instance", mcp.url)["result"]["value"]["projectId"] == project, "attached CLI accepts URL alone")
            context = mcp.operation("app.context", dict(workspace="overview"))["value"]
            args = dict(stateToken=context["stateToken"], target=context["target"], value="Free fixture project")
            result = mcp.tool("app.fields.set.ProjectDisplayNameDraft", args)
            check(not result.get("error") and not result.get("result", {}).get("isError") and not value("AiSdkHasApproval"), "live UI field mutation needs no extra Free confirmation")
            for field, setting in dict(AiSdkProviderId="openai-compatible", AiSdkModel="fixture-model", AiSdkEndpoint=f"http://127.0.0.1:{model.server_port}/v1", AiSdkHistoryId="free-chat", AiSdkPrompt="WRITE fixture").items(): host_set(field, setting)
            request("invoke", action="SendAiSdkMessageAsync")
            check((repo / "assistant.txt").read_text() == "assistant fixture\n" and not value("AiSdkHasApproval"), "integrated Free chat calls real write without approval")
            wait_ready()
            host_set("AiSdkLocalAccessProfile", "Protected")
            result = mcp.tool("file.read", dict(path="notes.txt"))
            check("error" in result or result.get("result", {}).get("isError"), "pending profile change blocks the old Free session")
            result = mcp.call("tools/call", dict(name="cy_resources_read", arguments=dict(uri=mcp.last_resource)))
            check("error" in result or result.get("result", {}).get("isError"), "profile revocation also blocks retained Free resources")
            descriptor = start(); secure = Mcp(descriptor["url"] + "/mcp")
            try: secure.initialize(); denied = False
            except urllib.error.HTTPError as error: denied = error.code == 401
            check(denied and bool(descriptor["token"]), "Protected requires a token on actual desktop MCP")
            secure.token = descriptor["token"]; secure.initialize()
            check(payload(secure.tool("file.read", dict(path="notes.txt")))["value"]["text"].strip() == "access fixture", "Protected token authenticates reads")
            result = secure.tool("file.write", dict(path="denied.txt", text="x", expectedSha256="missing"))
            check(("error" in result or result.get("result", {}).get("isError")) and not (repo / "denied.txt").exists(), "Protected keeps grants enforced")
            cli_call("invoke", "app.state", "--instance", secure.url, code=3)
            cli_call("invoke", "app.state", "--instance", secure.url, "--token", secure.token)
            check(True, "attached CLI distinguishes unauthenticated and authenticated Protected URLs")
            request("invoke", action="RestartAiSdkAsync")
            check(value("AiSdkLocalAccessProfile") == "Protected", "explicit profile survives runtime restart")
            descriptor = start("LocalControlled"); controlled = Mcp(descriptor["url"] + "/mcp"); controlled.initialize()
            result = controlled.tool("file.write", dict(path="controlled-denied.txt", text="x", expectedSha256="missing"))
            check(not descriptor["token"] and ("error" in result or result.get("result", {}).get("isError")), "Local controlled has no token but retains operation grants")
            descriptor = start("TokenOnly"); token_only = Mcp(descriptor["url"] + "/mcp", descriptor["token"]); token_only.initialize()
            token_only.tool("file.write", dict(path="token-only.txt", text="token fixture", expectedSha256="missing"))
            check((repo / "token-only.txt").read_text() == "token fixture" and not value("AiSdkHasApproval"), "Token only permits writes without additional confirmation")
            try: Mcp(token_only.url).initialize(); denied = False
            except urllib.error.HTTPError as error: denied = error.code == 401
            check(denied, "Token only still rejects unauthenticated connections")
            wait_ready()
            request("invoke", action="StopAiSdkMcpServerAsync")
            request("shutdown"); backend.process.wait(15)
            common = ["--project", str(repo), "--profile-dir", str(root / "cli-profile")]
            cli_call("invoke", "file.write", "--arguments", json.dumps(dict(path="cli-free.txt", text="CLI fixture", expectedSha256="missing")), *common)
            check((repo / "cli-free.txt").read_text() == "CLI fixture", "headless CLI defaults to Free without grants or --confirm")
            cli_call("invoke", "file.write", "--arguments", json.dumps(dict(path="cli-denied.txt", text="x", expectedSha256="missing")), *common, "--access-profile", "protected", code=3)
            check(cli_call("invoke", "project.info", *common)["result"]["value"]["accessProfile"] == "protected_access", "explicit CLI profile survives later commands")
            (repo / "assistant.txt").unlink()
            result = cli_call("chat", "--provider", "openai-compatible", "--model", "fixture-model", "--endpoint", f"http://127.0.0.1:{model.server_port}/v1", "--text", "WRITE fixture", "--history-id", "free-cli-chat", "--access-profile", "free", *common)
            check(result["result"]["text"] == "Tool finished." and (repo / "assistant.txt").exists(), "Free CLI chat writes through real SDK provider tools")
        finally:
            try:
                if backend.process.poll() is None: request("shutdown")
            except Exception: pass
            backend.close(); model.shutdown(); model.server_close()
    print(f"{count} local access profile checks passed.", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--app", type=Path, default=ROOT / "artifacts/cymcp-access/CyRevision.Desktop.exe")
    parser.add_argument("--cli", type=Path, default=ROOT / "artifacts/cymcp-access/Cli/CyRevision.Cli.exe")
    args = parser.parse_args(); run(args.app.resolve(), args.cli.resolve())
