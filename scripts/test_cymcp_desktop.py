"""Drive the real Avalonia-owned native backend, including live approvals and streaming."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import threading
import time
import http.server
from test_cymcp_integration import ROOT, JsonProcess, Model

def run(app, cli, native=None, capture=None):
    with tempfile.TemporaryDirectory(prefix="cymcp-desktop-", dir=ROOT / "artifacts") as temp:
        root = Path(temp); repo = root / "repository"; repo.mkdir()
        def git(*args): subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True)
        git("init", "-b", "main"); git("config", "user.name", "CyRevision Tests"); git("config", "user.email", "9800352+MrMybal@users.noreply.github.com")
        (repo / "notes.txt").write_text("desktop synthetic fixture\n"); git("add", "."); git("commit", "-m", "Desktop fixture")
        backend = JsonProcess([str(app), "--native-backend", "--profile-dir", str(root / "profile")])
        model = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Model)
        threading.Thread(target=model.serve_forever, daemon=True).start()
        sequence = 0; project = ""; pending = {}
        def send(command, **values):
            nonlocal sequence
            sequence += 1; backend.send(dict(id=sequence, command=command, projectId=project, **values)); return sequence
        def receive(number):
            while number not in pending:
                response = backend.receive(40); pending[response.get("id")] = response
            response = pending.pop(number)
            assert response.get("ok"), response.get("error")
            return response.get("result")
        def request(command, **values): return receive(send(command, **values))
        def state(): return request("state", workspace="ai", table="AiSdkMessages", fields=["AiSdkStatus", "AiSdkPendingApproval", "AiSdkHasApproval", "IsAiSdkBusy", "AiSdkInstanceFile", "AiSdkConnected"])
        def wait(predicate, label):
            deadline = time.monotonic() + 35
            while time.monotonic() < deadline:
                snapshot = state()
                if predicate(snapshot): return snapshot
                time.sleep(.07)
            raise AssertionError(label + ": " + json.dumps(snapshot))
        def value(snapshot, field): return snapshot["fields"][field]["value"]
        try:
            wait(lambda s: s["ready"] and not s["busy"], "initialization")
            request("invoke", action="AddExistingRepositoryAsync", parameters={"path": str(repo)})
            project = wait(lambda s: bool(s["projectId"]) and not s["busy"], "project loading")["projectId"]
            plugins = request("state", workspace="plugins", table="Plugins")["table"]["rows"]
            plugin = next(row for row in plugins if row["values"].get("Id") == "cyrevision.ai")
            if not plugin["values"]["IsEnabled"]:
                request("select", source="Plugins", selection="SelectedPlugin", index=plugin["index"], key=plugin["key"])
                request("invoke", action="EnableSelectedPluginAsync")
                wait(lambda s: not s["busy"], "AI plugin activation")
            request("navigation", operation="visibility", workspace="ai", visible=True)
            request("workspace", workspace="ai")
            assert state()["navigation"]["activeWorkspace"] == "ai"
            request("set", property="AiSdkLocalAccessProfile", value="Protected")
            request("set", property="AiSdkAllowWrite", value=True)
            request("invoke", action="StartAiSdkMcpServerAsync")
            endpoint_file = value(state(), "AiSdkInstanceFile")
            assert endpoint_file
            result = subprocess.run([str(cli), "invoke", "app.state", "--instance", endpoint_file], capture_output=True, text=True, encoding="utf-8", timeout=15)
            assert result.returncode == 0, result.stderr
            assert json.loads(result.stdout)["result"]["value"]["projectId"] == project
            print("PASS MCP uses actual selected desktop project", flush=True)
            write_args = {"path": "live.txt", "text": "approved from desktop\n", "expectedSha256": "missing"}
            attached = subprocess.Popen([str(cli), "invoke", "file.write", "--instance", endpoint_file, "--arguments", json.dumps(write_args)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
            approval = wait(lambda s: value(s, "AiSdkHasApproval"), "live confirmation")
            assert "approved from desktop" in value(approval, "AiSdkPendingApproval")
            assert not (repo / "live.txt").exists()
            request("invoke", action="ApproveAiSdkOperation")
            out, err = attached.communicate(timeout=20); assert attached.returncode == 0, err
            assert (repo / "live.txt").read_text() == "approved from desktop\n"
            print("PASS attached CLI write waits for exact UI approval", flush=True)
            wait(lambda s: not s["busy"], "refresh after write")
            for prop, setting in {"AiSdkProviderId": "openai-compatible", "AiSdkModel": "fixture-model", "AiSdkEndpoint": f"http://127.0.0.1:{model.server_port}/v1", "AiSdkHistoryId": "desktop-test", "AiSdkPrompt": "WRITE fixture"}.items():
                request("set", property=prop, value=setting)
            turn = send("invoke", action="SendAiSdkMessageAsync")
            approval = wait(lambda s: value(s, "AiSdkHasApproval"), "chat confirmation")
            assert value(approval, "IsAiSdkBusy")
            request("invoke", action="ApproveAiSdkOperation")
            receive(turn)
            assert (repo / "assistant.txt").read_text() == "assistant fixture\n"
            assert "Tool finished." in json.dumps(state()["table"])
            print("PASS integrated assistant performs confirmed real write while native action gate is busy", flush=True)
            wait(lambda s: not s["busy"], "refresh after assistant write")
            request("set", property="AiSdkPrompt", value="CANCEL fixture")
            turn = send("invoke", action="SendAiSdkMessageAsync")
            wait(lambda s: "partial" in json.dumps(s["table"]), "visible streaming")
            request("cancel"); receive(turn)
            snapshot = state()
            assert not value(snapshot, "IsAiSdkBusy") and not value(snapshot, "AiSdkHasApproval")
            print("PASS desktop transcript updates progressively and Cancel releases the UI", flush=True)
            request("set", property="AiSdkAllowWrite", value=False)
            result = subprocess.run([str(cli), "invoke", "file.write", "--instance", endpoint_file, "--arguments", json.dumps({**write_args, "path": "denied.txt"})], capture_output=True, text=True, timeout=15)
            assert result.returncode == 3 and not (repo / "denied.txt").exists()
            print("PASS live permission revocation also affects attached CLI", flush=True)
            def attached_read(name, arguments):
                result = subprocess.run([str(cli), "invoke", name, "--instance", endpoint_file, "--arguments", json.dumps(arguments)], capture_output=True, text=True, encoding="utf-8", timeout=15)
                assert result.returncode == 0, result.stderr
                return json.loads(result.stdout)["result"]
            attached_read("file.read", {"path": "notes.txt"})
            (repo / "notes.txt").write_text("new desktop state\n")
            request("invoke", action="RefreshAsync"); wait(lambda s: not s["busy"], "normal refresh invalidation")
            assert not attached_read("resources.grep", {"text": "desktop synthetic fixture"}).get("matches")
            print("PASS ordinary desktop refresh invalidates SDK text snapshots", flush=True)
            request("invoke", action="StopAiSdkMcpServerAsync")
            assert not Path(endpoint_file).exists()
            request("shutdown"); backend.process.wait(15)
            if native:
                result = subprocess.run([str(native), "--backend", str(app), "--profile-dir", str(root / "profile"), "--capture-workspace", "ai", "--capture", str(capture)], capture_output=True, text=True, timeout=45, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
                assert result.returncode == 0 and capture.exists(), result.stderr
                assert json.loads(Path(str(capture)+".workspace.json").read_text())["workspace"] == "ai", "The capture silently fell back to another workspace"
                print("PASS native CyMCPSDK workspace rendered without ImGui layout errors", flush=True)
        finally:
            try:
                if backend.process.poll() is None: request("shutdown")
            except Exception: pass
            backend.close(); model.shutdown(); model.server_close()
    print("CyMCPSDK desktop integration passed (local model fixture).")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(); parser.add_argument("--app", type=Path, default=ROOT / "artifacts/native-v2/CyRevision.Desktop.exe"); parser.add_argument("--cli", type=Path, default=ROOT / "artifacts/native-v2/Cli/CyRevision.Cli.exe"); parser.add_argument("--native", type=Path); parser.add_argument("--capture", type=Path, default=ROOT / "artifacts/cymcp-workspace.bmp")
    args = parser.parse_args(); run(args.app.resolve(), args.cli.resolve(), args.native.resolve() if args.native else None, args.capture.resolve())
