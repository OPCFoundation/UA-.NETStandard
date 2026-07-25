# FileSystemClient — a `System.IO`-style async client for OPC UA file systems

`FileSystemClient` (in `src/Opc.Ua.Client/FileSystem/`, namespace
`Opc.Ua.Client.FileSystem`) is an ergonomic, async-only wrapper around the
OPC UA file-system primitives defined in **Part 5 §C** and **Part 20 §4** —
the `FileType`, `FileDirectoryType`, and `TemporaryFileTransferType`
ObjectTypes. It is layered on top of the source-generated proxies emitted
into `Opc.Ua.Core` (`FileTypeClient`, `FileDirectoryTypeClient`,
`TemporaryFileTransferTypeClient`) and exposes a surface that closely
mirrors `System.IO.{File, Directory, FileInfo, DirectoryInfo, FileStream}`
to make remote file-system navigation feel like working with a local disk.

## Quick reference

| Type | Mirrors | Purpose |
|---|---|---|
| `FileSystemClient` | `System.IO.Directory` + `System.IO.File` | Top-level entry point. Exposes path-based operations (`GetFileAsync`, `CreateDirectoryAsync`, `DeleteAsync`, `MoveAsync`, `CopyAsync`, `ReadAllBytesAsync`, …). Rooted at any `FileDirectoryType` instance. |
| `UaFileSystemInfo` | `System.IO.FileSystemInfo` | Abstract base for `UaFileInfo` and `UaDirectoryInfo`. Carries the resolved `NodeId`, parent reference, browse name, and canonical path. |
| `UaFileInfo` | `System.IO.FileInfo` | A single file. Lazy-loaded `Size` / `Writable` / `MimeType` / etc. metadata; `OpenAsync` / `OpenReadAsync` / `OpenWriteAsync` returning a `UaFileStream`; bulk `ReadAllBytes` / `WriteAllText` shortcuts. |
| `UaDirectoryInfo` | `System.IO.DirectoryInfo` | A single directory. `EnumerateAsync` / `EnumerateFilesAsync` / `EnumerateDirectoriesAsync` (`IAsyncEnumerable<>`); `CreateSubdirectoryAsync`, `CreateFileAsync`, `DeleteAsync(recursive)`. |
| `UaFileStream` | `System.IO.FileStream` | A `Stream`-derived wrapper around an open server file handle. Async members hit the wire via `FileTypeClient`; sync members forward via `GetAwaiter().GetResult()`. |
| `UaPath` | `System.IO.Path` | Helpers for parsing, formatting, combining, and splitting OPC UA file paths. |
| `FileSystemClientOptions` | n/a | Tuning knobs: `ChunkSize`, `MaxBufferedReadSize`, `PathCacheSize`, type-subtype filters. |
| `TemporaryFileTransferClient` + `UaTemporaryWriteFile` | n/a | Separate surface for Part 5 §C.5 atomic temp-file transfers (`GenerateFileForRead/Write` + `CloseAndCommit`). |

## Getting started

### Open the standard server file system

Many OPC UA servers expose the standard `Server.FileSystem` object
(`NodeId i=16314`):

```csharp
using Opc.Ua.Client.FileSystem;

ISession session = /* an active OPC UA client session */;

var fs = FileSystemClient.OpenServerFileSystem(session);

await foreach (UaFileSystemInfo entry in fs.EnumerateAsync("/"))
{
    Console.WriteLine($"{(entry.IsDirectory ? "DIR " : "FILE")} {entry.FullPath}");
}
```

### Open any `FileDirectoryType` instance

```csharp
NodeId vendorRoot = /* result of a Browse / TranslateBrowsePathsToNodeIds */;
var fs = new FileSystemClient(session, vendorRoot);
```

### Read a file

```csharp
string content = await fs.ReadAllTextAsync("/Reports/2024/summary.json");
```

### Stream a large file

```csharp
await using UaFileStream stream = await fs.OpenReadAsync("/Logs/server.log");
using var reader = new System.IO.StreamReader(stream);
string firstLine = await reader.ReadLineAsync();
```

### Create a directory tree and write a file

```csharp
UaDirectoryInfo dir = await fs.CreateDirectoryAsync("/Uploads/2024-05/test", createIntermediate: true);
await fs.WriteAllBytesAsync($"{dir.FullPath}/payload.bin", payload);
```

### Move and delete

```csharp
UaFileInfo file = await fs.GetFileAsync("/Drafts/v1.txt");
await file.MoveToAsync("/Published/v1.txt");

await fs.DeleteAsync("/Drafts", recursive: true);
```

## Path syntax

Paths use the forward slash `'/'` as the segment separator. Each segment
is parsed by `QualifiedName.Parse(...)`, so the standard
`"<ns>:<name>"` form is supported. Examples:

| Path | Meaning |
|---|---|
| `""` or `"/"` | The root directory |
| `"foo"` | The child `foo` (namespace 0) of the root |
| `"/foo/bar"` | Same as `foo/bar`; leading slash is optional |
| `"1:Reports/1:2024/data.csv"` | Qualified path using namespace index 1 for the first two segments and namespace 0 for `data.csv` |

`UaPath.Combine`, `UaPath.GetDirectoryName`, `UaPath.GetFileName`, and
`UaPath.Normalize` mirror the equivalents on `System.IO.Path`. Canonical
paths returned from `UaFileSystemInfo.FullPath` always include the
namespace prefix when it is non-zero, so siblings with the same `Name`
in different namespaces are never collapsed.

## Path resolution and caching

Path → NodeId resolution uses
`TranslateBrowsePathsToNodeIdsAsync` segment-by-segment, with each step
following `ReferenceTypeIds.HierarchicalReferences` (with subtypes).
`(parent NodeId, browse name) → child NodeId` mappings are cached in a
small LRU controlled by `FileSystemClientOptions.PathCacheSize` (default
`1024`; set to zero to disable). The cache is **best-effort**: when a
resolved entry stops working (`BadNodeIdUnknown` from the server), the
entry is evicted and the path is re-resolved before the error propagates.
After any successful create/delete/move/copy, the path-cache entries
rooted at the affected parent are invalidated to avoid serving stale
NodeIds.

## Type classification (FileType vs FileDirectoryType)

Each child returned by `EnumerateAsync` is classified by its
`HasTypeDefinition` reference. Subtype-aware classification uses
`Session.TypeTree.IsTypeOf(...)` — by default subtypes count, so
`TrustListType`, `AddressSpaceFileType`, `ConfigurationFileType` etc.
appear as files. To restrict enumeration to the exact `FileType` /
`FileDirectoryType` set:

```csharp
var fs = new FileSystemClient(session, ObjectIds.FileSystem,
    new FileSystemClientOptions
    {
        IncludeFileTypeSubtypes = false,
        IncludeFileDirectoryTypeSubtypes = false,
    });
```

## File metadata

`UaFileInfo` exposes the seven well-known `FileType` properties (`Size`,
`Writable`, `UserWritable`, `OpenCount`, `MimeType`,
`MaxByteStringLength`, `LastModifiedTime`). They are populated lazily by
`UaFileInfo.RefreshAsync` (a single batched `Read` against the
properties resolved via `TranslateBrowsePathsToNodeIds`). The optional
properties (`MimeType`, `MaxByteStringLength`, `LastModifiedTime`) are
returned as `null` when the server does not expose them
(`BadNoMatch`, `BadNodeIdUnknown`, or empty target lists are tolerated).

`Writable` / `UserWritable` are advisory: callers should not pre-check
them before opening a file. Rely on the server's `Open` response for the
authoritative answer.

## Streams

`UaFileStream` derives from `System.IO.Stream` and supports both async
(`ReadAsync`, `WriteAsync`, `DisposeAsync`) and sync (`Read`, `Write`,
`Dispose`) members. The sync members forward to the async ones via
`GetAwaiter().GetResult()` — this means they can deadlock on
single-threaded synchronization contexts (e.g. WPF UI thread). Prefer
the async overrides.

Reads and writes are chunked at `FileSystemClientOptions.ChunkSize`,
clamped down to `FileType.MaxByteStringLength` when the server advertises
a smaller maximum. Empty `ByteString` returns are interpreted as EOF;
zero-length reads/writes never hit the wire.

`Position` is tracked locally; the server is informed via
`SetPosition` only when the local cursor diverges from the last
successfully transmitted position. `Length` is tracked locally too —
opened from `FileType.Size` at construction and bumped whenever a write
extends past it. Callers that mutate the underlying file through other
handles (or other clients) should call `UaFileInfo.RefreshAsync()`
before relying on `Length`.

`UaFileStream` is not thread-safe in the sense that multiple threads can
share a single instance: concurrent calls are serialised by an internal
`SemaphoreSlim`, matching `FileStream`'s "not thread-safe but doesn't
corrupt" contract. `DisposeAsync` issues `FileType.Close` exactly once
even when called concurrently.

Server-issued file handles are opaque and bound to the Session that
opened them. They cannot be used by another Session, and the server
automatically closes all remaining handles when their owning Session
closes.

## Error mapping

OPC UA Bad status codes returned by the server are translated into the
familiar `System.IO` exception family at the public boundary:

| StatusCode | Mapped exception |
|---|---|
| `BadNoMatch`, `BadNodeIdUnknown`, `BadNotFound` | `FileNotFoundException` (files) / `DirectoryNotFoundException` (directories) |
| `BadBrowseNameDuplicated` | `IOException("already exists: …")` |
| `BadUserAccessDenied`, `BadNotWritable`, `BadWriteNotSupported`, `BadSecurityChecksFailed` | `UnauthorizedAccessException` |
| `BadInvalidArgument`, `BadOutOfRange`, `BadInvalidState`, `BadResourceUnavailable`, `BadOutOfMemory` | `IOException` |
| Other `Bad…` codes | `ServiceResultException` (preserved unchanged) |

Mapped exceptions wrap the original `ServiceResultException` as
`InnerException`, so callers that need the raw OPC UA status code can
still retrieve it.

`TranslateBrowsePathsToNodeIds` returning more than one target throws
`IOException("ambiguous path: …")`; the client never silently picks the
first match.

## Move, copy, and delete semantics

These three operations are routed through the **source's parent**
directory (per Part 20 §4.3) — the server's `MoveOrCopy` and `Delete`
methods take a NodeId and operate from the directory's perspective.

`DeleteAsync(recursive: false)` on a directory first enumerates the
directory and throws `IOException("directory not empty: …")` if any
child exists. `DeleteAsync(recursive: true)` invokes the server's
`Delete` exactly once and lets the server perform recursive removal
(per spec). The client never walks the tree itself for a recursive
delete — that would weaken the server's atomicity / locking guarantees
and double-traverse.

`CreateDirectoryAsync` and `CreateFileAsync` accept only a plain
**string** for the leaf name; they reject leaf segments with a namespace
prefix (the server picks the BrowseName namespace). After the server
returns the new NodeId, the client reads its actual `BrowseName` and
uses that for the canonical path / cache entry.

`CreateFileAsync` always passes `requestFileOpen: false` to the server
— we never leak a server-allocated handle out of the create call.
Callers that want an immediate stream should call `UaFileInfo.OpenAsync`
afterwards.

## Temporary file transfer (Part 5 §C.5)

The temporary-file-transfer pattern is exposed on a separate surface
because its lifecycle does not fit the `System.IO` abstraction. The
server allocates a transient file, the client streams data through it,
and a final commit (or rollback) tells the server to either publish or
discard the result.

```csharp
using Opc.Ua.Client.FileSystem;

NodeId transferObject = /* TemporaryFileTransferType instance */;
var temp = new TemporaryFileTransferClient(session, transferObject);

// Read flow
await using UaFileStream readStream = await temp
    .GenerateFileForReadAsync(generateOptions: default);
byte[] payload = ReadAll(readStream);

// Write flow
await using UaTemporaryWriteFile write = await temp.GenerateFileForWriteAsync();
await write.Stream.WriteAsync(payload, 0, payload.Length);
NodeId completion = await write.CommitAsync(); // CloseAndCommit
```

`UaTemporaryWriteFile` owns the close lifecycle: exactly one terminal
call — `CommitAsync` (CloseAndCommit) or `DisposeAsync` (Close,
implicit server rollback) — is sent to the server. The wrapped
`Stream` cannot accidentally close the server handle; its `Dispose`
is a no-op.

## What lives where

```
src/Opc.Ua.Client/FileSystem/
├── FileSystemClient.cs                 # entry point
├── FileSystemClientOptions.cs          # tuning knobs
├── FileMetadata.cs                     # internal property snapshot
├── FileSystemErrors.cs                 # status code → IO exception mapper
├── PathCache.cs                        # internal LRU
├── UaPath.cs                           # System.IO.Path mirror
├── UaFileMode.cs                       # OpenFileMode flags wrapper
├── UaFileSystemInfo.cs                 # abstract base
├── UaFileInfo.cs                       # System.IO.FileInfo mirror
├── UaDirectoryInfo.cs                  # System.IO.DirectoryInfo mirror
├── UaFileStream.cs                     # System.IO.Stream mirror
├── TemporaryFileTransferClient.cs      # Part 5 §C.5 surface
└── UaTemporaryWriteFile.cs             # commit/rollback wrapper
```

Tests live under `tests/Opc.Ua.Client.Tests/FileSystem/`.

## See also

- [SourceGeneratedDataTypes.md](SourceGeneratedDataTypes.md) — for an
  overview of the generated `*TypeClient` proxies the FileSystem client
  builds on.
- OPC UA Part 5 Annex C (FileType, TemporaryFileTransferType).
- OPC UA Part 20 §4 (FileSystem object model).
