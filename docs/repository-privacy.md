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

## Release privacy gates

Release builds map compiler source locations to `/_/` and omit PDB files, including
PDB files copied by dependencies. `scripts/audit-release-privacy.py` scans published
files and final decompressed ZIP/TAR/DEB/DMG/Inno installer payloads. ASCII and
UTF-16 in both byte orders/alignment offsets are checked. Windows installers are
extracted with a checksum-pinned tool, never run on the user's installed profile;
DMGs are mounted read-only. Tool failures block publication.

Checks cover user/project paths, private-key markers, common token formats,
personal-mailbox patterns, runtime/configuration files and symbols. Release
contents must not contain real peer identities/certificates, WireGuard private
keys, Syncthing profiles, local databases, backups, journals or app preferences.
Do not treat filename checks as a complete secret detector: manual review and the
repository's redacted Gitleaks history check remain necessary.

`scripts/release-unreal-sha256.json` pins all seven cleaned native DLLs. After an
Unreal rebuild, review strings and symbols again, validate executable-section
integrity if using the sanitizer, and test the rebuilt plugin in its target engine
before approving new hashes. Never automatically refresh the manifest to silence
a failed check. The manifest currently identifies sanitized precompiled DLLs,
not freshly rebuilt or newly runtime-certified versions.

`scripts/public-vendor-debug-records.json` identifies two unmodified public NuGet
DLLs by exact content hash. Their upstream home-directory debug records are not
user data from this project. Only that one rule is exempted for those exact bytes;
modified DLLs and all credential rules remain blocking. No symbol files ship.

Each package produces a `.privacy.json` receipt with its hash, inspected file count
and approved Unreal variants. The publication job requires exactly the ten expected
packages and matching receipts. Existing releases (including drafts) cannot be
overwritten or revived. The release must descend from the privacy-cleaned base.

Run the release checker tests with:

```sh
python -m unittest discover -s scripts -p 'test_*privacy.py'
```
