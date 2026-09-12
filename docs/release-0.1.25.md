# CyRevision 0.1.25 Alpha — privacy-hardened release

Built from the cleaned Git history with GitHub noreply identities. Previous drafts
and their files are not reused or republished.

- Application and agent binaries are rebuilt with neutral compiler source roots.
- Debug-symbol files are excluded from distribution. Final archives and installers
  are unpacked and checked before upload; each package has a SHA-256-bound privacy
  report. `SHA256SUMS.txt` covers the published packages and audit reports.
- The seven UE 5.2–5.8 precompiled plugin DLLs must exactly match the approved cleaned
  DLL hashes. Rebuilding or replacing a DLL requires a new privacy review and a
  deliberate manifest update, otherwise packaging fails.
- These Unreal DLLs were **not recompiled or tested in each Unreal version** in
  this release. The earlier diagnostic-path neutralization is not a functional
  compatibility certification.
- Two exact, publicly distributed NuGet DLLs retain their upstream debug-record
  source roots. Their provenance and SHA-256 are pinned; they contain no CyRevision
  user's build paths. We do not modify signed third-party code. This exemption
  applies only to that diagnostic record, never to secrets or runtime data.

See [repository privacy](repository-privacy.md) for the checks and their limitations.
The release remains Alpha. Static privacy checks and automated tests cannot prove
the absence of every possible secret representation or runtime defect.
