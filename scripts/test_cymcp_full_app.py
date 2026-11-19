"""Exercise live whole-application automation against disposable Git and a local GitHub fixture."""
import argparse
import http.server
import json
import os
from pathlib import Path
import subprocess
import tempfile
import threading
import time
from test_cymcp_integration import ROOT, JsonProcess

class GitHub(http.server.BaseHTTPRequestHandler):
    merged=False; cancelled=False; writes=[]; sha=""; fail_ci=False
    def log_message(self,*_): pass
    def pr(self): return dict(number=7,title="Fixture PR",state="closed" if self.merged else "open",merged=self.merged,mergeable=True,mergeable_state="clean",draft=False,user=dict(login="fixture-author"),head=dict(ref="feature",sha=self.sha),base=dict(ref="main"),created_at="2026-01-01T00:00:00Z",updated_at="2026-01-01T00:00:00Z",html_url="https://github.com/fixture/example/pull/7",body="Synthetic PR body",commits=1,changed_files=1,additions=1,deletions=0)
    def reply(self,value,status=200):
        data=json.dumps(value).encode();self.send_response(status);self.send_header("Content-Type","application/json");self.send_header("Content-Length",str(len(data)));self.end_headers();self.wfile.write(data)
    def body(self):
        if self.headers.get("Transfer-Encoding","").lower()=="chunked":
            chunks=[]
            while True:
                size=int(self.rfile.readline().split(b";")[0].strip(),16)
                if not size:self.rfile.readline();break
                chunks.append(self.rfile.read(size));self.rfile.read(2)
            return json.loads(b"".join(chunks) or b"{}")
        return json.loads(self.rfile.read(int(self.headers.get("Content-Length",0))) or b"{}")
    def do_GET(self):
        path=self.path.split("?")[0]
        if self.fail_ci and path.endswith(("/actions/workflows","/actions/runs")):return self.reply(dict(message="Synthetic unavailable service"),503)
        if path.startswith("/api/v1/projects/"):
            if path.endswith("/tasks"):return self.reply([dict(id="616e951d-5081-44b5-9e26-dac0f9e77dc2",key="TEST-7",title="Fixture task",status="in_progress")])
            return self.reply(dict(id="616e951d-5081-44b5-9e26-dac0f9e77dc3",name="Fixture tasks"))
        if path=="/user": return self.reply(dict(login="fixture-operator"))
        if path.endswith("/pulls"): return self.reply([self.pr()])
        if path.endswith("/pulls/7"): return self.reply(self.pr())
        if path.endswith("/files"): return self.reply([dict(filename="notes.txt",status="modified",additions=1,deletions=0,changes=1,patch="@@ -1 +1 @@\n-old\n+new")])
        if path.endswith(("/reviews","/comments","/commits")): return self.reply([])
        if path.endswith("/actions/workflows"): return self.reply(dict(workflows=[dict(id=2,name="Fixture build",path=".github/workflows/build.yml",state="active")]))
        if path.endswith("/actions/runs"): return self.reply(dict(workflow_runs=[dict(id=10,name="Fixture CI",event="pull_request",status="completed" if self.cancelled else "in_progress",conclusion="cancelled" if self.cancelled else None,head_branch="main",head_sha=self.sha,created_at="2026-01-01T00:00:00Z",updated_at="2026-01-01T00:00:00Z",html_url="https://github.com/fixture/example/actions/runs/10")]))
        if path.endswith("/jobs"): return self.reply(dict(jobs=[dict(id=101,name="Build",status="completed" if self.cancelled else "in_progress",conclusion="cancelled" if self.cancelled else None,steps=[])]))
        if path.endswith("/check-runs"): return self.reply(dict(check_runs=[]))
        if path.endswith("/status"): return self.reply(dict(state="pending",statuses=[]))
        return self.reply({})
    def do_PUT(self):
        body=self.body()
        self.writes.append((self.path,body))
        if self.path.endswith("/merge"): type(self).merged=True;return self.reply(dict(merged=True,sha=self.sha,message="Fixture merged"))
        return self.reply({})
    def do_POST(self):
        body=self.body()
        self.writes.append((self.path,body))
        if self.path.endswith("/cancel"): type(self).cancelled=True;return self.reply({},202)
        if self.path.endswith("/comments"): return self.reply(dict(id=100,body=body["body"],user=dict(login="fixture-operator"),created_at="2026-01-01T00:00:00Z",html_url="https://github.com/fixture/example/issues/7#issuecomment-100"),201)
        return self.reply({},204)

def run(app,cli):
    with tempfile.TemporaryDirectory(prefix="cymcp-full-app-",dir=ROOT/"artifacts") as temp:
        root=Path(temp);repo=root/"repository";repo.mkdir()
        def git(*args): return subprocess.run(["git","-C",str(repo),*args],check=True,capture_output=True,text=True).stdout
        git("init","-b","main");git("config","user.name","CyRevision Tests");git("config","user.email","9800352+MrMybal@users.noreply.github.com")
        (repo/"notes.txt").write_text("fixture\n");git("add",".");git("commit","-m","Fixture");git("branch","feature");git("remote","add","origin","https://github.com/fixture/example.git")
        GitHub.sha=git("rev-parse","HEAD").strip();GitHub.writes=[];GitHub.merged=False;GitHub.cancelled=False
        api=http.server.ThreadingHTTPServer(("127.0.0.1",0),GitHub);threading.Thread(target=api.serve_forever,daemon=True).start()
        environment_name="CYREVISION_TEST_TASK_FIXTURE_TOKEN";previous=os.environ.get(environment_name)
        os.environ[environment_name]="fixture-local-task-token"
        try:backend=JsonProcess([str(app),"--native-backend","--profile-dir",str(root/"profile")])
        finally:
            if previous is None:os.environ.pop(environment_name,None)
            else:os.environ[environment_name]=previous
        sequence=0;project="";pending={};endpoint=""
        def request(command,**values):
            nonlocal sequence
            sequence+=1;number=sequence;backend.send(dict(id=number,command=command,projectId=project,**values))
            while number not in pending:
                response=backend.receive(45);pending[response.get("id")]=response
            response=pending.pop(number);assert response.get("ok"),response.get("error");return response["result"]
        def state(): return request("state",workspace="ai",fields=["AiSdkInstanceFile","AiSdkStatus","AiSdkHasApproval","IsPullRequestLoading","IsCiLoading"])
        def wait(predicate,label):
            deadline=time.monotonic()+40
            while time.monotonic()<deadline:
                result=state()
                if predicate(result): return result
                time.sleep(.07)
            raise AssertionError(label+": "+json.dumps({**{key:result.get(key) for key in ["busy","loading","status"]},"sdk":{key:result.get("fields",{}).get(key,{}).get("value") for key in ["AiSdkStatus","AiSdkHasApproval","IsPullRequestLoading","IsCiLoading"]}}))
        def host_set(prop,value): request("set",property=prop,value=value)
        def call(name,args=None,code=0):
            result=subprocess.run([str(cli),"invoke",name,"--instance",endpoint,"--arguments",json.dumps(args or {})],capture_output=True,text=True,encoding="utf-8",timeout=40)
            assert result.returncode==code,(name,result.returncode,result.stderr)
            data=json.loads(result.stdout)
            return data.get("result",{}).get("value",data.get("result",{})) if code==0 else data
        def context(workspace):
            deadline=time.monotonic()+30
            while time.monotonic()<deadline:
                result=call("app.context",dict(workspace=workspace))
                if not result["busy"]:return dict(stateToken=result["stateToken"],target=result["target"])
                time.sleep(.1)
            raise AssertionError("Module remains busy: "+workspace)
        def approved(name,args,code=0,workspace=None,retries=2):
            process=subprocess.Popen([str(cli),"invoke",name,"--instance",endpoint,"--arguments",json.dumps(args)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding="utf-8")
            deadline=time.monotonic()+40
            while time.monotonic()<deadline:
                if state()["fields"]["AiSdkHasApproval"]["value"]:break
                if process.poll() is not None:
                    out,err=process.communicate();raise AssertionError((name,"returned before confirmation",process.returncode,out,err))
                time.sleep(.07)
            else:raise AssertionError(name+" did not request confirmation")
            request("invoke",action="ApproveAiSdkOperation")
            out,err=process.communicate(timeout=40)
            if code==0 and process.returncode!=0 and workspace and retries and json.loads(out).get("error",{}).get("code")=="StaleState":
                # A target refresh is rejected before side effects. Read again and
                # issue a new single-use host confirmation, never replay a business error.
                return approved(name,{**args,**context(workspace)},workspace=workspace,retries=retries-1)
            assert process.returncode==code,(name,out,err)
            return json.loads(out)["result"]["value"] if code==0 else json.loads(out)
        def action(name,workspace,parameters=None):
            arguments={**context(workspace),**({"parameters":parameters} if parameters is not None else {})}
            metadata=next(item for module in capabilities["modules"] for item in module["actions"] if item["name"]==name)
            return call(name,arguments) if metadata["readOnly"] else approved(name,arguments,workspace=workspace)
        def select(source,key,workspace): return approved("app.table.select",{**context(workspace),"source":source,"key":key})
        try:
            wait(lambda s:s["ready"] and not s["busy"],"startup")
            request("invoke",action="AddExistingRepositoryAsync",parameters={"path":str(repo)})
            project=wait(lambda s:s["projectId"] and not s["busy"],"project")["projectId"]
            plugins=request("state",workspace="plugins",table="Plugins")["table"]["rows"]
            row=next(row for row in plugins if row["values"].get("Id")=="cyrevision.ai")
            if not row["values"]["IsEnabled"]:
                request("select",source="Plugins",selection="SelectedPlugin",index=row["index"],key=row["key"]);request("invoke",action="EnableSelectedPluginAsync")
            for prop in ["Write","Stage","Commit","Network","Navigation","Publication","Destructive","Services","Administration","Builds"]: host_set("AiSdkAllow"+prop,True)
            request("invoke",action="StartAiSdkMcpServerAsync");snapshot=state();endpoint=snapshot["fields"]["AiSdkInstanceFile"]["value"]
            assert endpoint,snapshot["fields"]["AiSdkStatus"]
            capabilities=call("app.capabilities");assert len([a for module in capabilities["modules"] for a in module["actions"]])>200
            print("PASS reviewed live catalogue covers all application modules",flush=True)
            operations=subprocess.run([str(cli),"list","--instance",endpoint],capture_output=True,text=True,encoding="utf-8",check=True)
            names=[op["name"] for op in json.loads(operations.stdout)["result"]["operations"]]
            assert "app.fields.set.TeamChatAttachmentPath" in names
            assert not any(name.startswith("app.fields.set.Ai") or name.endswith("PullRequestToken") or "RunRepositoryCommand" in name for name in names)
            assert not any("app.fields.set."+field in names for field in ["SwarmAgentPath","SwarmCoordinatorPath","LoreCliPath","PerforceCliPath","PeerExchangeText","VpnExchangeText"])
            print("PASS tools cannot edit assistant grants, credentials or arbitrary console commands",flush=True)
            host_set("AiSdkAllowAdministration",False)
            call("app.fields.set.ProjectDisplayNameDraft",{**context("overview"),"value":"must not apply"},code=3)
            host_set("AiSdkAllowAdministration",True)
            stale=context("overview");host_set("ProjectDisplayNameDraft","changed by host")
            process=subprocess.Popen([str(cli),"invoke","app.fields.set.ProjectDisplayNameDraft","--instance",endpoint,"--arguments",json.dumps({**stale,"value":"stale"})],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding="utf-8")
            wait(lambda s:s["fields"]["AiSdkHasApproval"]["value"],"stale approval");request("invoke",action="ApproveAiSdkOperation");out,err=process.communicate(timeout=20)
            assert process.returncode!=0 and "StaleState" in out,(out,err)
            approved("app.fields.set.ProjectDisplayNameDraft",{**context("overview"),"value":"AI fixture project"})
            action("app.overview.SetSelectedProjectDisplayNameAsync","overview")
            assert state()["project"]=="AI fixture project"
            print("PASS capability denial, stale-state rejection and actual project rename",flush=True)
            branch=next(row for row in call("app.table.read",dict(source="FilteredBranches"))["rows"] if row["values"].get("Name")=="feature")
            select("FilteredBranches",branch["key"],"branches")
            action("app.branches.CheckoutSelectedBranchAsync","branches")
            assert git("branch","--show-current").strip()=="feature"
            wait(lambda s:not s["busy"],"checkout refresh")
            print("PASS stable table selection drives the real Git checkout workflow",flush=True)
            prefs=call("app.preferences.read");assert "themePresets" in prefs
            theme=prefs["themePresets"][-1]["id"]
            approved("app.preferences.save",{**context("overview"),"application":{"themePreset":theme,"confirmBeforeExit":True}})
            assert call("app.preferences.read")["application"]["themePreset"]==theme
            print("PASS shared preferences service persists the chosen theme",flush=True)
            approved("app.fields.set.TeamChatAttachmentPath",{**context("teamchat"),"value":"notes.txt"})
            approved("app.fields.set.TeamChatAttachmentPath",{**context("teamchat"),"value":"../outside.txt"},code=3)
            assert any(field["name"]=="TeamChatAttachmentPath" and Path(field["value"])==repo/"notes.txt" for field in call("app.context",dict(workspace="teamchat"))["fields"])
            action("app.assets.SetAssetBaseline","assets",dict(path="notes.txt"))
            action("app.assets.SetAssetCandidate","assets",dict(path="notes.txt"))
            asset_fields={field["name"]:field["value"] for field in call("app.context",dict(workspace="assets"))["fields"]}
            assert Path(asset_fields["AssetBaselinePath"])==repo/"notes.txt" and Path(asset_fields["AssetCandidatePath"])==repo/"notes.txt"
            print("PASS file-input drafts and actions resolve relative paths inside the selected project",flush=True)
            plugins=call("app.table.read",dict(source="Plugins"))["rows"]
            task_plugin=next(row for row in plugins if row["values"].get("Id")=="cyrevision.cytask")
            select("Plugins",task_plugin["key"],"plugins")
            action("app.plugins.EnableSelectedPluginAsync","plugins")
            assert any(item["id"]=="cytask" for item in call("app.tasks.providers")["providers"])
            request("taskSave",provider="cytask",settings=dict(baseUrl=f"http://127.0.0.1:{api.server_port}",scopeId="616e951d-5081-44b5-9e26-dac0f9e77dc3",tokenEnvironmentVariable=environment_name))
            approved("app.tasks.search",{**context("pullrequests"),"provider":"cytask","query":"Fixture task"})
            tasks=call("app.table.read",dict(source="NativeWorkItemSearch"))["rows"];assert len(tasks)==1
            approved("app.tasks.link",{**context("pullrequests"),"keys":[tasks[0]["key"]],"destination":"pullrequest","prefixTitle":True})
            assert any(field["name"]=="NewPullRequestBody" and "TEST-7" in field["value"] for field in call("app.context",dict(workspace="pullrequests"))["fields"])
            approved("app.tasks.link",{**context("changes"),"keys":[tasks[0]["key"]],"destination":"commit"})
            assert any(field["name"]=="CommitMessage" and "TEST-7" in field["value"] for field in call("app.context",dict(workspace="changes"))["fields"])
            print("PASS real plugin enablement, CyTask connector search and linking into both drafts",flush=True)
            # Only the test host establishes API trust and a synthetic credential, never the model.
            host_set("PullRequestApiBaseUrl",f"http://127.0.0.1:{api.server_port}/");host_set("PullRequestToken","fixture-local-token")
            action("app.pullrequests.ResolvePullRequestRepositoryAsync","pullrequests")
            action("app.pullrequests.RefreshPullRequestsAsync","pullrequests")
            rows=call("app.table.read",dict(source="FilteredPullRequests"))["rows"];assert len(rows)==1
            host_set("AiSdkAllowNetwork",False)
            approved("app.table.select",{**context("pullrequests"),"source":"FilteredPullRequests","key":rows[0]["key"]},code=3)
            host_set("AiSdkAllowNetwork",True)
            select("FilteredPullRequests",rows[0]["key"],"pullrequests")
            wait(lambda s:not s["fields"]["IsPullRequestLoading"]["value"],"PR details")
            approved("app.fields.set.PullRequestComment",{**context("pullrequests"),"value":"Synthetic assistant comment"})
            action("app.pullrequests.AddPullRequestCommentAsync","pullrequests")
            assert any(path.endswith("/comments") and body.get("body")=="Synthetic assistant comment" for path,body in GitHub.writes)
            print("PASS real PR service posts the confirmed comment to the local API fixture",flush=True)
            action("app.ci.RefreshCiAsync","ci")
            rows=call("app.table.read",dict(source="CiRuns"))["rows"];assert rows,call("app.context",dict(workspace="ci"))
            select("CiRuns",rows[0]["key"],"ci");wait(lambda s:not s["fields"]["IsCiLoading"]["value"],"CI jobs")
            action("app.ci.CancelSelectedCiRunAsync","ci");assert GitHub.cancelled
            print("PASS real CI service cancels the selected local-fixture run",flush=True)
            wait(lambda s:not s["busy"],"CI refresh")
            action("app.pullrequests.MergeSelectedPullRequestAsync","pullrequests");assert GitHub.merged
            print("PASS real PR service merges the exact reviewed PR in the local fixture",flush=True)
            GitHub.fail_ci=True
            call("app.ci.RefreshCiAsync",context("ci"),code=6)
            GitHub.fail_ci=False
            print("PASS a swallowed UI refresh failure still reaches the assistant as a business error",flush=True)
            host_set("AiSdkAllowPublication",False)
            call("app.ci.DispatchSelectedCiWorkflowAsync",context("ci"),code=3)
            host_set("AiSdkAllowAdministration",False)
            call("app.plugins.EnableSelectedPluginAsync",context("plugins"),code=3)
            host_set("AiSdkAllowServices",False)
            call("app.vpn.StartVpnAsync",context("vpn"),code=3)
            print("PASS publication, plugin administration and service revocation are enforced before side effects",flush=True)
            request("invoke",action="StopAiSdkMcpServerAsync")
        except Exception:
            print("Backend diagnostic:",backend.process.poll(),"".join(backend.errors[-12:]),flush=True)
            raise
        finally:
            try: request("shutdown")
            except Exception: pass
            backend.close();api.shutdown();api.server_close()
    print("Whole-application CyMCPSDK integration passed. External API responses were simulated locally.")

if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--app",type=Path,default=ROOT/"artifacts/cymcp-expanded/CyRevision.Desktop.exe");parser.add_argument("--cli",type=Path,default=ROOT/"artifacts/cymcp-expanded/Cli/CyRevision.Cli.exe")
    args=parser.parse_args();run(args.app.resolve(),args.cli.resolve())
