"""Exercise the native engine against an isolated local repository and profile."""
import argparse
import json
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parent.parent

def run(executable, capture_native=None, capture_output=None):
    with tempfile.TemporaryDirectory(prefix="native-integration-", dir=ROOT / "artifacts") as temporary:
        root = Path(temporary)
        repository = root / "Repository with spaces"
        repository.mkdir()
        git = lambda *args: subprocess.run(["git", "-C", str(repository), *args], check=True, capture_output=True, text=True, encoding="utf-8").stdout
        git("init", "-b", "main")
        git("config", "user.name", "CyRevision Test")
        git("config", "user.email", "automation@users.noreply.github.com")
        (repository / "Source").mkdir()
        source = repository / "Source" / "Example.cpp"
        other = repository / "notes.txt"
        source.write_text("int answer = 1;\n", encoding="utf-8")
        other.write_text("keep this\n", encoding="utf-8")
        (repository / ".gitignore").write_text("/.cyrevision/\n", encoding="utf-8")
        git("add", ".")
        git("commit", "-m", "Initial fixture")
        process = subprocess.Popen([str(executable), "--native-backend", "--profile-dir", str(root / "profile")],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding="utf-8", creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        messages = queue.Queue()
        stderr = []
        def read():
            for line in process.stdout:
                try: messages.put(json.loads(line))
                except ValueError: messages.put({"id": -1, "error": "Non-JSON engine output"})
        def errors():
            for line in process.stderr: stderr.append(line)
        reader = threading.Thread(target=read, daemon=True)
        reader.start()
        threading.Thread(target=errors, daemon=True).start()
        counter = 0
        project_id = ""
        def request(command, expect_error=False, **values):
            nonlocal counter
            counter += 1
            message = dict(id=counter, command=command, projectId=project_id, **values)
            process.stdin.write(json.dumps(message) + "\n")
            process.stdin.flush()
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                try: response = messages.get(timeout=0.5)
                except queue.Empty:
                    if process.poll() is not None: raise AssertionError("Engine exited unexpectedly")
                    continue
                if response.get("id") != counter:
                    if response.get("id") == 0 and not response.get("ok"): raise AssertionError(response.get("error"))
                    continue
                if expect_error:
                    assert not response.get("ok"), "The invalid request was accepted"
                else:
                    assert response.get("ok"), response.get("error")
                return response.get("result")
            raise AssertionError("Engine request timed out: " + command + ": " + " | ".join(stderr[-18:]))
        def state(workspace="changes", table="PreparedChanges"):
            return request("state", workspace=workspace, table=table)
        def wait_ready():
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                snapshot = state()
                if snapshot["ready"] and not snapshot["busy"]: return snapshot
                time.sleep(0.1)
            raise AssertionError("Engine did not become ready")
        try:
            catalog = request("catalog")
            assert len(catalog["workspaces"]) >= 20
            assert len(catalog["actions"]) > 100
            wait_ready()
            request("invoke", action="AddExistingRepositoryAsync", parameters={"path": str(repository)})
            snapshot = wait_ready()
            project_id = snapshot["projectId"]
            assert project_id and snapshot["branch"] == "main"
            assert snapshot["workspaceId"] == "changes"
            assert all(isinstance(operation["state"], str) for operation in snapshot["operations"])
            source.write_text("int answer = 2;\n", encoding="utf-8")
            other.write_text("unselected change\n", encoding="utf-8")
            request("invoke", action="RefreshAsync")
            snapshot = wait_ready()
            assert snapshot["table"]["total"] == 2, (snapshot["status"], snapshot["table"], git("status", "--porcelain"), stderr[-18:])
            request("include", path="Source/Example.cpp", included=True)
            request("set", property="CommitMessage", value="Native integration fixture")
            request("invoke", action="CommitAsync")
            wait_ready()
            assert git("log", "-1", "--format=%s").strip() == "Native integration fixture"
            assert "notes.txt" in git("status", "--porcelain")
            assert "notes.txt" not in git("show", "--format=", "--name-only", "HEAD")
            def explorer():
                return request("state",workspace="history",table="History",fields=["SelectedExplorerRevision","SelectedComparisonRevision","IsExplorerAnyLoading","ExplorerSummary","ExplorerDiff"],
                    tables=[dict(source="History"),dict(source="ExplorerFiles"),dict(source="ExplorerFileHistory")])
            def wait_explorer(hash_value, expected_text=None):
                deadline=time.monotonic()+30
                while time.monotonic()<deadline:
                    snapshot=explorer()
                    summary=snapshot["fields"]["ExplorerSummary"]["value"]
                    if hash_value[:7] in summary and not snapshot["fields"]["IsExplorerAnyLoading"]["value"]:
                        if expected_text is None or expected_text in snapshot["fields"]["ExplorerDiff"]["value"]:return snapshot
                    time.sleep(0.1)
                raise AssertionError("Selected history revision/diff did not update: " + json.dumps(dict(summary=summary,loading=snapshot.get("loading"),diff=snapshot["fields"]["ExplorerDiff"]["value"][:600])))
            revisions=explorer()["tables"]["History"]["rows"]
            assert len(revisions)==2
            newest,oldest=revisions
            def select_history(row, selection=""):
                request("select",source="History",selection=selection,index=row["index"],key=row["key"])
            select_history(oldest,"SelectedComparisonRevision")
            assert "GitRevision {" not in explorer()["selections"]["SelectedComparisonRevision"]["text"]
            select_history(oldest)
            initial=wait_explorer(oldest["values"]["Hash"])
            assert initial["tables"]["ExplorerFiles"]["total"]==3
            source_row=next(row for row in initial["tables"]["ExplorerFiles"]["rows"] if row["values"]["Path"]=="Source/Example.cpp")
            request("select",source="ExplorerFiles",index=source_row["index"],key=source_row["key"])
            wait_explorer(oldest["values"]["Hash"],"+int answer = 1;")
            # Rapid selections must cancel older loads, not keep displaying the old commit.
            select_history(newest);select_history(oldest);select_history(newest)
            current=wait_explorer(newest["values"]["Hash"],"+int answer = 2;")
            assert current["tables"]["ExplorerFiles"]["total"]==1
            assert current["selections"]["SelectedComparisonRevision"]["key"]==oldest["key"]
            select_history(oldest)
            wait_explorer(oldest["values"]["Hash"])
            request("invoke",action="RefreshAsync")
            refreshed_history=wait_explorer(oldest["values"]["Hash"])
            refreshed_oldest=next(row for row in refreshed_history["tables"]["History"]["rows"] if row["values"]["Hash"]==oldest["values"]["Hash"])
            assert refreshed_history["tableSelections"]["History"]==refreshed_oldest["key"]
            # Two windows may page/filter the same source without replacing one another.
            main_navigation=state()["navigation"]["activeWorkspace"]
            request("workspace",workspace="history",detached=True)
            multi=request("state",workspace="overview",workspaces=["history","ci"],tables=[
                dict(source="History",key="main-history",filter="Initial"),
                dict(source="History",key="view-1:History",filter="Native integration")])
            assert multi["workspaceId"]=="overview"
            assert multi["navigation"]["activeWorkspace"]==main_navigation
            assert "ExplorerDiff" in multi["fields"] and "CiStatus" in multi["fields"]
            assert multi["tables"]["main-history"]["total"]==1 and multi["tables"]["view-1:History"]["total"]==1
            assert multi["tables"]["main-history"]["rows"][0]["values"]["Hash"]!=multi["tables"]["view-1:History"]["rows"][0]["values"]["Hash"]
            request("fileHistory", path="Source/Example.cpp")
            history = state("code", "NativeFileHistory")
            assert history["table"]["total"] == 2
            history_values = history["table"]["rows"][0]["values"]
            assert all(name in history_values for name in ("CommitHash", "Author", "Date", "Message", "LfsOid"))
            request("directory", path="Source")
            directory = state("code", "NativeCodeFiles")
            row = directory["table"]["rows"][0]
            assert row["values"]["Name"] == "Example.cpp"
            request("select", source="NativeCodeFiles", index=row["index"], key=row["key"])
            request("select", expect_error=True, source="NativeCodeFiles", index=row["index"], key="outdated-selection")
            request("directory", expect_error=True, path="../outside")
            request("invoke", expect_error=True, action="DeleteSelectedAiConversationAsync", parameters={})
            request("set", expect_error=True, property="RepositoryPath", value="changed")
            request("invoke", expect_error=True, action="NotAnAction")
            first_project_id = project_id
            second_repository = root / "Second repository"
            second_repository.mkdir()
            subprocess.run(["git", "-C", str(second_repository), "init", "-b", "second-main"], check=True, capture_output=True)
            request("invoke", action="AddExistingRepositoryAsync", parameters={"path": str(second_repository)})
            second_project_id = state()["projectId"]
            assert second_project_id and second_project_id != first_project_id
            # Select another project without waiting for the current scan.
            request("project", targetProjectId=first_project_id)
            project_id = first_project_id
            assert state()["projectId"] == first_project_id
            request("project", targetProjectId=second_project_id)
            project_id = second_project_id
            assert state()["projectId"] == second_project_id
            request("project", targetProjectId=first_project_id)
            project_id = first_project_id
            restored = wait_ready()
            assert restored["branch"] == "main"
            assert Path(restored["fields"]["RepositoryPath"]["value"]) == repository
            request("navigation", operation="visibility", workspace="ci", visible=False)
            ci_tab = next(tab for tab in state()["navigation"]["tabs"] if tab["id"] == "ci")
            assert not ci_tab["visible"]
            request("workspace", workspace="changes")
            request("shutdown")
            process.wait(timeout=15)
            assert process.returncode == 0
            if capture_native:
                capture_started=time.time()
                capture = subprocess.run([str(capture_native), "--backend", str(executable), "--profile-dir", str(root / "profile"), "--open-window", "history", "--open-window", "code", "--verify-multi-window", "--capture", str(capture_output)], timeout=45, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
                assert capture.returncode == 0 and capture_output.exists(), "Native render did not produce its capture"
                report=Path(str(capture_output)+".windows.json")
                assert report.stat().st_mtime>=capture_started, "The multi-window result is from an earlier run"
                result=json.loads(report.read_text(encoding="utf-8"))
                assert result["nativeWindows"]>=2 and result["minimizedFrames"]>=8 and result["restored"], "Multi-window/minimized rendering was not verified"
            print("Native engine integration passed: independent history/comparison selections, rapid revision changes, actual file diffs, project switching and cancellation, context isolation, Git status, file history, folder browsing, validation and shutdown.")
        finally:
            if process.poll() is None:
                try:
                    request("cancel")
                    request("shutdown")
                    process.wait(timeout=15)
                except (AssertionError, OSError, subprocess.TimeoutExpired):
                    process.kill()
                    process.wait(timeout=15)
            process.stdin.close()
            process.stdout.close()
            process.stderr.close()

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--engine", type=Path, default=ROOT / "artifacts/native-v2/CyRevision.Desktop.exe")
    parser.add_argument("--capture-native", type=Path)
    parser.add_argument("--capture-output", type=Path, default=ROOT / "artifacts/native-v2-smoke.bmp")
    args = parser.parse_args()
    run(args.engine.resolve(), args.capture_native.resolve() if args.capture_native else None, args.capture_output.resolve())
