"""Exercise added Git operations against local disposable repositories only."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from test_cymcp_integration import ROOT

def run(cli):
    with tempfile.TemporaryDirectory(prefix="cymcp-git-actions-",dir=ROOT/"artifacts") as temporary:
        root=Path(temporary);repo=root/"repository";repo.mkdir();remote=root/"remote.git"
        def git(*args):return subprocess.run(["git","-C",str(repo),*args],capture_output=True,text=True,check=True).stdout
        git("init","-b","main");git("config","user.name","CyRevision Tests");git("config","user.email","9800352+MrMybal@users.noreply.github.com")
        def commit(text,message):
            (repo/"notes.txt").write_text(text,encoding="utf-8");git("add","notes.txt");git("commit","-m",message)
        commit("base\n","Base");git("branch","feature")
        def invoke(name,args=None,flags=(),code=0):
            result=subprocess.run([str(cli),"invoke",name,"--project",str(repo),"--profile-dir",str(root/"profile"),"--access-profile","protected","--arguments",json.dumps(args or {}),*flags],capture_output=True,text=True,encoding="utf-8",timeout=30)
            assert result.returncode==code,(name,result.stdout,result.stderr)
            return json.loads(result.stdout)
        write=["--allow-write","--confirm"];commit_flags=[*write,"--allow-commit"];destructive=[*write,"--allow-destructive"]
        invoke("git.checkout",dict(name="feature"),code=3)
        assert git("branch","--show-current").strip()=="main"
        invoke("git.checkout",dict(name="feature"),write);commit("feature\n","Feature")
        invoke("git.checkout",dict(name="main"),write);commit("main\n","Main")
        invoke("git.merge",dict(name="feature"),write,code=3)
        invoke("git.merge",dict(name="feature"),commit_flags,code=6)
        assert "UU notes.txt" in git("status","--porcelain")
        invoke("git.conflict.abort",flags=write,code=3)
        invoke("git.conflict.abort",flags=destructive)
        assert not git("status","--porcelain")
        invoke("git.merge",dict(name="feature"),commit_flags,code=6)
        invoke("git.conflict.resolve",dict(path="notes.txt",choice="Ours"),destructive,code=3)
        invoke("git.conflict.resolve",dict(path="notes.txt",choice="Ours"),[*destructive,"--allow-stage"])
        invoke("git.conflict.continue",flags=commit_flags)
        assert not git("status","--porcelain") and (repo/"notes.txt").read_text()=="main\n"
        print("PASS real checkout, conflicting merge, abort, resolution, continue and independent grants",flush=True)
        git("branch","retained-fixture")
        invoke("git.branch.analyzeRemoval",dict(name="retained-fixture"))
        invoke("git.branch.remove",dict(name="retained-fixture"),write,code=3)
        invoke("git.branch.remove",dict(name="retained-fixture"),destructive)
        assert "retained-fixture" not in git("branch","--format=%(refname:short)")
        subprocess.run(["git","init","--bare",str(remote)],capture_output=True,check=True)
        invoke("git.remote.set",dict(name="origin",url=str(remote)),write)
        network=[*write,"--allow-network"]
        invoke("git.push",flags=network,code=3)
        invoke("git.push",flags=[*network,"--allow-publication"])
        assert git("ls-remote","origin","refs/heads/main").split()[0]==git("rev-parse","HEAD").strip()
        git("branch","--set-upstream-to=origin/main","main")
        invoke("git.pull",flags=network,code=3)
        invoke("git.pull",flags=[*network,"--allow-commit"])
        invoke("git.remote.remove",dict(name="origin"),write,code=3)
        invoke("git.remote.remove",dict(name="origin"),destructive)
        assert not git("remote")
        print("PASS retained branch removal and publication-gated push/pull/remotes against a local bare repository",flush=True)
    print("Additional Git automation integration passed.")

if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--cli",type=Path,default=ROOT/"artifacts/cymcp-expanded/Cli/CyRevision.Cli.exe")
    run(parser.parse_args().cli.resolve())
