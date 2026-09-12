# Repository privacy

Use the GitHub noreply email address from your account settings for commits. The
privacy check also checks annotated tag identities. Keep personal paths, private
environment files, certificates, local assistant state and runtime data outside Git.

Enable the local pre-commit check in a fresh clone:

```sh
git config user.email "YOUR_GITHUB_NOREPLY_ADDRESS"
git config core.hooksPath .githooks
```

Python 3 is required for the local check. GitHub Actions additionally scans the
reachable commit history with Gitleaks. These checks reduce accidental exposure;
they are not a substitute for reviewing staged files and release contents.

Review `git diff --cached` before committing. Use relative project paths and
synthetic screenshots. Never put credentials into reports or issue attachments.
Build outputs and release archives need a separate privacy review before uploading.
Changing Git history does not remove old installers, remote caches or third-party
clones. After an authorized history rewrite, use a fresh clone rather than merging
or pushing an old checkout, which can reintroduce removed data.

For unsigned, precompiled Unreal plugin DLLs, `scripts/sanitize-unreal-source-paths.py`
can replace diagnostic source-root strings in `.rdata` with fixed-length neutral
labels. It refuses signed files and executable/writable string sections. It does
not rebuild the plugin and does not change executable sections, offsets, imports,
exports or relocations. Run it on release copies, then validate those copies before
packaging. Source-location messages will contain a neutral build root.
