---
id: native-help-links
title: Contextual help addresses
description: Open a validated local page, section or illustration from a native host.
section: Developers / Public API
parent: plugin-api
kind: api
order: 150
revision: 1
---

## Contract {#contract}

The native C++ host includes `DocumentationLinks.h`. `bool isLocalDocumentationLink(const std::string& url)` accepts the installed reader URL shape and rejects control characters; it is a narrow reader check, not a general filesystem authorization service.

The managed host receives `docs.open` with `topicId`, optional `anchor` and `mediaId`, and returns a `url`. IPC `id` is reserved for request correlation. The host validates the page and section against the installed manifest before opening the browser.

The installed reader is `Documentation/Documentation.html`; its language/page/section remain in the URL fragment. A path-to-URI API encodes spaces and non-ASCII characters before the fragment is added.

F1 targets the current view without changing the selected project or discarding drafts. A page ID is stable across English and French.

[Keyboard help](doc:keyboard-shortcuts)
