---
id: pull-request-manager
title: Pull request manager
description: Review, create, check out, discuss, close, and merge GitHub pull requests inside CyRevision.
section: Git
order: 13
revision: 1
---

## Connection {#connection}
Select a Git project and open Pull Requests. CyRevision detects the GitHub origin remote. Public repositories can be read without a token; private repositories and write operations use a masked session token or the configured environment variable. Tokens are never persisted.

## Review workspace {#review-workspace}
Filter open, closed, or all requests. Inspect changed files, unified patches, comments, reviews, branches, draft state, change statistics, and mergeability. Create requests, publish a general comment, approve, request changes, or open the provider page. General reviews and conversation are available; provider capabilities govern line-level actions.

## Safe checkout {#safe-checkout}
Checkout stops when the working tree is dirty. CyRevision fetches the pull head into a dedicated reference and creates or fast-forwards cyrevision/pr-N; it never force-resets the active branch.

## Remote actions {#remote-actions}
Close, reopen, and merge change the remote repository and require confirmation. Choose merge, squash, or rebase according to repository policy. These actions may trigger CI or deployments.

## Providers {#providers}
GitHub is the first adapter, including Enterprise API overrides. The core is provider-neutral for future GitLab, Forgejo, and decentralized review adapters. Git, LFS, Sync, and backup remain fully usable without a forge.

## Tabs, state and sorting {#tabs-state-and-sorting}
Description, Files, Conversation, CI, Conflicts and Actions separate the work. Sort PR numbers numerically with the # header. Open/closed/merged state is independent from CI. Author/own-PR markers and colors supplement explicit labels.

## CI and conflicts {#ci-and-conflicts}
Use CI for runs, jobs, complete logs, error/warning filters and text search. Open the log in a separate window for long lines. The current queued/running check must not be hidden by an older successful run. Right-click an active job for cancellation when eligible; the action can cancel its entire parent run and the provider validates permissions. Conflicts reports paths only when the provider/local analysis can supply them.

## After merge {#after-merge}
Actions can offer confirmed deletion of the corresponding local branch. Verify that it is not active and contains no needed unpublished work. Linked Jira/ClickUp tasks can be detected and transitioned under the configured/manual policy. A merged PR and an updated task are separate results; inspect both.

```image
{"id": "ci", "src": "assets/ci.en.svg", "alt": "Workflow overview", "controls": "minimal"}
```

[See also](doc:ci-actions)
