# Team chat over VPN or Sync

CyRevision includes a project-scoped team conversation under **Team & Network > Team chat**. It never changes the Git repository.

## VPN transport

One peer starts the host on its WireGuard IPv4 and TCP port `47843`. Other peers enter that VPN address as their endpoint and copy the same 256-bit project token through a separate trusted channel. CyRevision refuses public listener and peer addresses, verifies the token in constant time, limits attachment size, and validates every attachment with SHA-256.

Conversation saving is optional for the VPN host. When enabled, messages and attachments are stored beneath the local CyRevision data directory for that project. When disabled, message history is kept only for the running host session; received attachments still use the local CyRevision receive cache so they can be opened safely.

VPN messages that cannot reach the host immediately are placed in a project-scoped local outbox. CyRevision reports their pending/failed state and retries them when the conversation is refreshed. The message list only transfers attachment metadata; the bytes are downloaded and SHA-256 verified when **Open attachment** is used.

## Sync-folder transport

Choose an existing synchronized folder. CyRevision creates a `.cyrevision-chat` subfolder and writes one immutable JSON file per message plus a separate content-addressed attachment location. It never appends several peers to one shared file, avoiding the conflict pattern of a synchronized JSONL/chat log. The folder may be a project-independent shared folder managed by CyRevision Sync.

Images and ordinary files use the same attachment mechanism. Select a received attachment and choose **Open attachment** to hand it to the operating system. Retention controls which archived messages are loaded; CyRevision does not silently delete older synchronized messages.

CyRevision keeps a disposable incremental index in `Project/.cyrevision/cache/chat`. Reopening a large conversation compares file metadata and parses only new or changed message files. A watcher refreshes an open Sync conversation after a short debounce, while presence files expose recently connected teammates without rescanning attachments.

## Optional encryption at rest

Enable **Encrypt stored conversations** to protect synchronized or locally archived message bodies and attachments with AES-256-GCM. The project chat token derives the encryption key and authenticated metadata prevents a ciphertext from being moved to another project unnoticed. Peers must therefore share the exact token through an already trusted channel. Losing that token makes the encrypted archive intentionally unrecoverable.

The cache and temporary decrypted attachment copies are local, disposable CyRevision data. They are never a replacement for the synchronized source of truth.
