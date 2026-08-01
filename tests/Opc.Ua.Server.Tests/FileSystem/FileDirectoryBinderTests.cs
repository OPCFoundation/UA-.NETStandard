/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Tests for materialised FileDirectoryType bindings.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public class FileDirectoryBinderTests
    {
        [Test]
        public async Task BindMaterialisesFilesAndDirectoriesWithoutInitialRegistrationsAsync()
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            provider.AddFile("readme.txt", "hello");
            provider.AddDirectory("programs");
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();
            var registered = new List<NodeState>();

            await using IFileDirectoryBinding binding = await CreateBinder().BindAsync(
                root,
                provider,
                context,
                registerNode: (node, _) =>
                {
                    registered.Add(node);
                    return default;
                }).ConfigureAwait(false);

            Assert.That(binding.Directory, Is.SameAs(root));
            Assert.That(binding.Provider, Is.SameAs(provider));
            Assert.That(Find<FileState>(root, context, "readme.txt"), Is.Not.Null);
            Assert.That(Find<FileDirectoryState>(root, context, "programs"), Is.Not.Null);
            Assert.That(registered, Is.Empty);
        }

        [Test]
        public async Task ReadFileReturnsProviderContentAsync()
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            provider.AddFile("program.mod", "movej");
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();

            await using IFileDirectoryBinding binding = await CreateBinder().BindAsync(
                root, provider, context).ConfigureAwait(false);
            FileState file = Find<FileState>(root, context, "program.mod")!;

            byte[] content = ReadAll(context, file);

            Assert.That(Encoding.UTF8.GetString(content), Is.EqualTo("movej"));
        }

        [Test]
        public async Task WriteFileUpdatesProviderContentAsync()
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            provider.AddFile("program.mod", "old");
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();

            await using IFileDirectoryBinding binding = await CreateBinder().BindAsync(
                root, provider, context).ConfigureAwait(false);
            FileState file = Find<FileState>(root, context, "program.mod")!;

            WriteAll(context, file, "new-content");

            Assert.That(provider.ReadText("program.mod"), Is.EqualTo("new-content"));
        }

        [Test]
        public async Task CreateReturnsAccessDeniedWhenProviderIsReadOnlyAsync()
        {
            InMemoryFileSystemProvider provider = CreateProvider(isWritable: false);
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();

            await using IFileDirectoryBinding binding = await CreateBinder().BindAsync(
                root, provider, context).ConfigureAwait(false);

            CreateFileMethodStateResult result = await root.CreateFile!.OnCallAsync!(
                context, root.CreateFile!, root.NodeId, "denied.txt", false, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task CreateReturnsAccessDeniedWhenOptionsWithholdCreateAsync()
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();
            var options = new FileDirectoryBindingOptions { AllowCreate = false };

            await using IFileDirectoryBinding binding = await CreateBinder().BindAsync(
                root, provider, context, options).ConfigureAwait(false);

            CreateDirectoryMethodStateResult result = await root.CreateDirectory!.OnCallAsync!(
                context, root.CreateDirectory!, root.NodeId, "denied", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestCase("../escape")]
        [TestCase("..\\escape")]
        [TestCase("sub/child")]
        [TestCase("sub\\child")]
        [TestCase("/rooted")]
        [TestCase("\\rooted")]
        [TestCase("C:\\Windows\\System32\\evil")]
        [TestCase("..")]
        [TestCase(".")]
        [TestCase("stream:name")]
        [TestCase("   ")]
        public async Task CreateRejectsANameThatIsNotASingleSegmentAsync(string name)
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();

            await using IFileDirectoryBinding binding = await CreateBinder().BindAsync(
                root, provider, context).ConfigureAwait(false);

            CreateDirectoryMethodStateResult directoryResult =
                await root.CreateDirectory!.OnCallAsync!(
                    context, root.CreateDirectory!, root.NodeId, name, CancellationToken.None)
                    .ConfigureAwait(false);
            CreateFileMethodStateResult fileResult = await root.CreateFile!.OnCallAsync!(
                context, root.CreateFile!, root.NodeId, name, false, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    directoryResult.ServiceResult.StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(
                    fileResult.ServiceResult.StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidArgument));
            });
        }

        [Test]
        public async Task MoveOrCopyRejectsANewNameThatIsNotASingleSegmentAsync()
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            provider.AddDirectory("target");
            provider.AddFile("old.txt", "x");
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();

            await using IFileDirectoryBinding binding = await CreateBinder().BindAsync(
                root, provider, context).ConfigureAwait(false);

            NodeId sourceId = Find<FileState>(root, context, "old.txt")!.NodeId;
            NodeId targetId = Find<FileDirectoryState>(root, context, "target")!.NodeId;

            MoveOrCopyMethodStateResult result = await root.MoveOrCopy!.OnCallAsync!(
                context,
                root.MoveOrCopy!,
                root.NodeId,
                sourceId,
                targetId,
                false,
                "..\\..\\escape",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                result.ServiceResult.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CreateDeleteAndMoveReconcileMaterialisedNodesAsync()
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            provider.AddDirectory("target");
            provider.AddFile("old.txt", "x");
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();
            var registered = new List<NodeState>();

            await using IFileDirectoryBinding binding = await CreateBinder().BindAsync(
                root,
                provider,
                context,
                registerNode: (node, _) =>
                {
                    registered.Add(node);
                    return default;
                }).ConfigureAwait(false);

            CreateFileMethodStateResult createResult = await root.CreateFile!.OnCallAsync!(
                context, root.CreateFile!, root.NodeId, "new.txt", false, CancellationToken.None)
                .ConfigureAwait(false);
            FileDirectoryState target = Find<FileDirectoryState>(root, context, "target")!;
            FileState oldFile = Find<FileState>(root, context, "old.txt")!;
            MoveOrCopyMethodStateResult moveResult = await root.MoveOrCopy!.OnCallAsync!(
                context, root.MoveOrCopy!, root.NodeId, oldFile.NodeId, target.NodeId, false, "moved.txt",
                CancellationToken.None).ConfigureAwait(false);
            FileState newFile = Find<FileState>(root, context, "new.txt")!;
            DeleteFileMethodStateResult deleteResult = await root.DeleteFileSystemObject!.OnCallAsync!(
                context, root.DeleteFileSystemObject!, root.NodeId, newFile.NodeId, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(createResult.ServiceResult), Is.True);
            Assert.That(ServiceResult.IsGood(moveResult.ServiceResult), Is.True);
            Assert.That(ServiceResult.IsGood(deleteResult.ServiceResult), Is.True);
            Assert.That(provider.Exists("new.txt"), Is.False);
            Assert.That(provider.Exists("target/moved.txt"), Is.True);
            Assert.That(Find<FileState>(root, context, "new.txt"), Is.Null);
            Assert.That(Find<FileState>(target, context, "moved.txt"), Is.Not.Null);
            Assert.That(registered, Is.Not.Empty);
        }

        [Test]
        public void MaxEntriesThrowsBeforeMaterialisingOverflow()
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            provider.AddFile("a.txt", "a");
            provider.AddFile("b.txt", "b");
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();
            var options = new FileDirectoryBindingOptions { MaxEntries = 1 };

            Assert.That(
                async () => await CreateBinder().BindAsync(root, provider, context, options).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(Find<FileState>(root, context, "a.txt"), Is.Null);
        }

        [Test]
        public async Task MaxDepthStopsNestedMaterialisationAsync()
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            provider.AddDirectory("level1");
            provider.AddFile("level1/deep.txt", "deep");
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();
            var options = new FileDirectoryBindingOptions { MaxDepth = 1 };

            await using IFileDirectoryBinding binding = await CreateBinder().BindAsync(
                root, provider, context, options).ConfigureAwait(false);
            FileDirectoryState level1 = Find<FileDirectoryState>(root, context, "level1")!;

            Assert.That(level1, Is.Not.Null);
            Assert.That(Find<FileState>(level1, context, "deep.txt"), Is.Null);
        }

        [Test]
        public async Task RefreshAddsAndRemovesChildrenAsync()
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            provider.AddFile("old.txt", "old");
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();

            await using IFileDirectoryBinding binding = await CreateBinder().BindAsync(
                root, provider, context).ConfigureAwait(false);
            provider.Delete("old.txt");
            provider.AddFile("new.txt", "new");

            await binding.RefreshAsync().ConfigureAwait(false);

            Assert.That(Find<FileState>(root, context, "old.txt"), Is.Null);
            Assert.That(Find<FileState>(root, context, "new.txt"), Is.Not.Null);
        }

        [Test]
        public async Task DisposeClosesHandlesAndDetachesCallbacksAsync()
        {
            InMemoryFileSystemProvider provider = CreateProvider();
            provider.AddFile("open.txt", "content");
            FileDirectoryState root = CreateRoot();
            SessionSystemContext context = CreateContext();

            IFileDirectoryBinding binding = await CreateBinder().BindAsync(root, provider, context).ConfigureAwait(false);
            FileState file = Find<FileState>(root, context, "open.txt")!;
            _ = Open(context, file, 0x1);

            await binding.DisposeAsync().ConfigureAwait(false);

            Assert.That(provider.OpenStreamCount, Is.Zero);
            Assert.That(file.Close!.OnCall, Is.Null);
            Assert.That(root.CreateFile!.OnCallAsync!, Is.Null);
        }

        [Test]
        public void AddFileDirectoryBinderRegistersDefaultBinder()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaServerBuilder builder = new TestServerBuilder(services);

            builder.AddFileDirectoryBinder();
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetService<IFileDirectoryBinder>(), Is.TypeOf<FileDirectoryBinder>());
        }

        private static FileDirectoryBinder CreateBinder()
        {
            return new FileDirectoryBinder();
        }

        private static SessionSystemContext CreateContext()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://opcfoundation.org/UA/");
            namespaceUris.Append("urn:test");
            return new SessionSystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = namespaceUris,
                ServerUris = new StringTable(),
                TypeTable = new TypeTable(namespaceUris),
                SessionId = new NodeId("test-session", 0)
            };
        }

        private static FileDirectoryState CreateRoot()
        {
            return new FileDirectoryState(null)
            {
                TypeDefinitionId = ObjectTypeIds.FileDirectoryType,
                NodeId = new NodeId("Programs", 2),
                BrowseName = new QualifiedName("Programs", 2),
                DisplayName = new LocalizedText("Programs")
            };
        }

        private static T? Find<T>(FileDirectoryState directory, ISystemContext context, string browseName)
            where T : BaseInstanceState
        {
            return directory.FindChild(context, new QualifiedName(browseName, directory.BrowseName.NamespaceIndex)) as T;
        }

        private static uint Open(ISystemContext context, FileState file, byte mode)
        {
            uint handle = 0;
            ServiceResult result = file.Open!.OnCall!(context, file.Open!, file.NodeId, mode, ref handle);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            return handle;
        }

        private static byte[] ReadAll(ISystemContext context, FileState file)
        {
            uint handle = Open(context, file, 0x1);
            ByteString data = ByteString.From([]);
            ServiceResult readResult = file.Read!.OnCall!(context, file.Read!, file.NodeId, handle, 1024, ref data);
            ServiceResult closeResult = file.Close!.OnCall!(context, file.Close!, file.NodeId, handle);
            Assert.That(ServiceResult.IsGood(readResult), Is.True);
            Assert.That(ServiceResult.IsGood(closeResult), Is.True);
            return data.ToArray();
        }

        private static void WriteAll(ISystemContext context, FileState file, string text)
        {
            uint handle = Open(context, file, 0x6);
            ServiceResult writeResult = file.Write!.OnCall!(
                context, file.Write!, file.NodeId, handle, ByteString.From(Encoding.UTF8.GetBytes(text)));
            ServiceResult closeResult = file.Close!.OnCall!(context, file.Close!, file.NodeId, handle);
            Assert.That(ServiceResult.IsGood(writeResult), Is.True);
            Assert.That(ServiceResult.IsGood(closeResult), Is.True);
        }

        private static InMemoryFileSystemProvider CreateProvider(bool isWritable = true)
        {
            return new InMemoryFileSystemProvider(isWritable);
        }

        private sealed class TestServerBuilder : IOpcUaServerBuilder
        {
            public TestServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }

            public IOpcUaServerBuilder AddNodeManager<TFactory>()
                where TFactory : class, IAsyncNodeManagerFactory
            {
                throw new NotSupportedException();
            }

            public IOpcUaServerBuilder AddNodeManager(string namespaceUri, Action<INodeManagerBuilder> build)
            {
                throw new NotSupportedException();
            }

            public IOpcUaServerBuilder AddSyncNodeManager<TFactory>()
                where TFactory : class, INodeManagerFactory
            {
                throw new NotSupportedException();
            }
        }

        private sealed class InMemoryFileSystemProvider : IFileSystemProvider
        {
            public InMemoryFileSystemProvider(bool isWritable)
            {
                IsWritable = isWritable;
                m_entries[string.Empty] = Entry.Directory("Programs");
            }

            public string MountName => "Programs";

            public bool IsWritable { get; }

            public int OpenStreamCount
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_openStreamCount;
                    }
                }
            }

            public ValueTask<FileSystemEntry?> GetEntryAsync(string path, CancellationToken ct)
            {
                lock (m_lock)
                {
                    return new ValueTask<FileSystemEntry?>(
                        m_entries.TryGetValue(Normalize(path), out Entry? entry)
                            ? entry.ToFileSystemEntry(Normalize(path), IsWritable)
                            : null);
                }
            }

            public async IAsyncEnumerable<FileSystemEntry> EnumerateAsync(
                string path,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                List<FileSystemEntry> entries;
                string parent = Normalize(path);
                lock (m_lock)
                {
                    if (!m_entries.TryGetValue(parent, out Entry? parentEntry) || !parentEntry.IsDirectory)
                    {
                        throw new DirectoryNotFoundException(parent);
                    }

                    entries = m_entries
                        .Where(kv => IsImmediateChild(parent, kv.Key))
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => kv.Value.ToFileSystemEntry(kv.Key, IsWritable))
                        .ToList();
                }

                foreach (FileSystemEntry entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return entry;
                }
            }

            public ValueTask<Stream> OpenReadAsync(string path, CancellationToken ct)
            {
                lock (m_lock)
                {
                    string normalized = Normalize(path);
                    if (!m_entries.TryGetValue(normalized, out Entry? entry) || entry.IsDirectory)
                    {
                        throw new FileNotFoundException(normalized);
                    }
                    m_openStreamCount++;
                    return new ValueTask<Stream>(new TrackingReadStream(
                        entry.Content.ToArray(),
                        () =>
                        {
                            lock (m_lock)
                            {
                                m_openStreamCount--;
                            }
                        }));
                }
            }

            public ValueTask<Stream> OpenWriteAsync(string path, FileWriteMode mode, CancellationToken ct)
            {
                if (!IsWritable)
                {
                    throw new UnauthorizedAccessException();
                }

                lock (m_lock)
                {
                    string normalized = Normalize(path);
                    byte[] initial = [];
                    if (m_entries.TryGetValue(normalized, out Entry? entry))
                    {
                        if (entry.IsDirectory)
                        {
                            throw new IOException(normalized);
                        }
                        if (mode == FileWriteMode.Append)
                        {
                            initial = entry.Content.ToArray();
                        }
                    }
                    else
                    {
                        EnsureParentDirectory(normalized);
                        m_entries[normalized] = Entry.File([]);
                    }

                    var stream = new CommitMemoryStream(bytes => Commit(normalized, bytes));
                    stream.Write(initial, 0, initial.Length);
                    if (mode == FileWriteMode.Append)
                    {
                        stream.Position = stream.Length;
                    }
                    else
                    {
                        stream.SetLength(0);
                    }
                    return new ValueTask<Stream>(stream);
                }
            }

            public ValueTask CreateDirectoryAsync(string path, CancellationToken ct)
            {
                if (!IsWritable)
                {
                    throw new UnauthorizedAccessException();
                }

                lock (m_lock)
                {
                    AddDirectory(Normalize(path));
                }
                return default;
            }

            public ValueTask CreateFileAsync(string path, CancellationToken ct)
            {
                if (!IsWritable)
                {
                    throw new UnauthorizedAccessException();
                }

                lock (m_lock)
                {
                    string normalized = Normalize(path);
                    if (m_entries.ContainsKey(normalized))
                    {
                        throw new IOException(normalized);
                    }
                    EnsureParentDirectory(normalized);
                    m_entries[normalized] = Entry.File([]);
                }
                return default;
            }

            public ValueTask DeleteAsync(string path, CancellationToken ct)
            {
                if (!IsWritable)
                {
                    throw new UnauthorizedAccessException();
                }

                lock (m_lock)
                {
                    Delete(Normalize(path));
                }
                return default;
            }

            public ValueTask MoveAsync(string source, string target, CancellationToken ct)
            {
                if (!IsWritable)
                {
                    throw new UnauthorizedAccessException();
                }

                lock (m_lock)
                {
                    CopyCore(Normalize(source), Normalize(target));
                    Delete(Normalize(source));
                }
                return default;
            }

            public ValueTask CopyAsync(string source, string target, CancellationToken ct)
            {
                if (!IsWritable)
                {
                    throw new UnauthorizedAccessException();
                }

                lock (m_lock)
                {
                    CopyCore(Normalize(source), Normalize(target));
                }
                return default;
            }

            public void AddDirectory(string path)
            {
                string normalized = Normalize(path);
                EnsureParentDirectory(normalized);
                if (m_entries.TryGetValue(normalized, out Entry? existing) && !existing.IsDirectory)
                {
                    throw new IOException(normalized);
                }
                m_entries[normalized] = Entry.Directory(NameOf(normalized));
            }

            public void AddFile(string path, string content)
            {
                string normalized = Normalize(path);
                EnsureParentDirectory(normalized);
                m_entries[normalized] = Entry.File(Encoding.UTF8.GetBytes(content));
            }

            public bool Exists(string path)
            {
                lock (m_lock)
                {
                    return m_entries.ContainsKey(Normalize(path));
                }
            }

            public string ReadText(string path)
            {
                lock (m_lock)
                {
                    return Encoding.UTF8.GetString(m_entries[Normalize(path)].Content);
                }
            }

            public void Delete(string path)
            {
                string normalized = Normalize(path);
                if (string.IsNullOrEmpty(normalized) || !m_entries.ContainsKey(normalized))
                {
                    throw new FileNotFoundException(normalized);
                }

                string prefix = normalized + "/";
                foreach (string key in m_entries.Keys.Where(key => key == normalized ||
                    key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                {
                    m_entries.Remove(key);
                }
            }

            private void Commit(string path, byte[] bytes)
            {
                lock (m_lock)
                {
                    m_entries[path] = Entry.File(bytes);
                }
            }

            private void CopyCore(string source, string target)
            {
                if (!m_entries.TryGetValue(source, out Entry? sourceEntry))
                {
                    throw new FileNotFoundException(source);
                }
                if (m_entries.ContainsKey(target))
                {
                    throw new IOException(target);
                }
                EnsureParentDirectory(target);
                m_entries[target] = sourceEntry.Clone(NameOf(target));
                string sourcePrefix = source + "/";
                foreach (KeyValuePair<string, Entry> kv in m_entries.ToArray())
                {
                    if (kv.Key.StartsWith(sourcePrefix, StringComparison.Ordinal))
                    {
                        string suffix = kv.Key[sourcePrefix.Length..];
                        m_entries[target + "/" + suffix] = kv.Value.Clone(NameOf(suffix));
                    }
                }
            }

            private void EnsureParentDirectory(string path)
            {
                int slash = path.LastIndexOf('/');
                if (slash < 0)
                {
                    return;
                }

                string parent = path[..slash];
                if (!m_entries.TryGetValue(parent, out Entry? entry) || !entry.IsDirectory)
                {
                    throw new DirectoryNotFoundException(parent);
                }
            }

            private static bool IsImmediateChild(string parent, string candidate)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(parent))
                {
                    return !candidate.Contains('/', StringComparison.Ordinal);
                }
                if (!candidate.StartsWith(parent + "/", StringComparison.Ordinal))
                {
                    return false;
                }
                return !candidate[(parent.Length + 1)..].Contains('/', StringComparison.Ordinal);
            }

            private static string NameOf(string path)
            {
                int slash = path.LastIndexOf('/');
                return slash < 0 ? path : path[(slash + 1)..];
            }

            private static string Normalize(string path)
            {
                return string.IsNullOrEmpty(path) || path == "/" ? string.Empty : path.Trim('/');
            }

            private readonly Dictionary<string, Entry> m_entries = new(StringComparer.Ordinal);
            private readonly Lock m_lock = new();
            private int m_openStreamCount;

            private sealed class CommitMemoryStream : MemoryStream
            {
                public CommitMemoryStream(Action<byte[]> commit)
                {
                    m_commit = commit;
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        m_commit(ToArray());
                    }
                    base.Dispose(disposing);
                }

                private readonly Action<byte[]> m_commit;
            }

            private sealed class TrackingReadStream : MemoryStream
            {
                public TrackingReadStream(byte[] buffer, Action onDispose)
                    : base(buffer, writable: false)
                {
                    m_onDispose = onDispose;
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing && !m_disposed)
                    {
                        m_disposed = true;
                        m_onDispose();
                    }
                    base.Dispose(disposing);
                }

                private readonly Action m_onDispose;
                private bool m_disposed;
            }

            private sealed class Entry
            {
                private Entry(string name, bool isDirectory, byte[] content)
                {
                    Name = name;
                    IsDirectory = isDirectory;
                    Content = content;
                    LastModifiedUtc = DateTime.UtcNow;
                }

                public string Name { get; }

                public bool IsDirectory { get; }

                public byte[] Content { get; }

                public DateTime LastModifiedUtc { get; }

                public static Entry Directory(string name)
                {
                    return new Entry(name, isDirectory: true, []);
                }

                public static Entry File(byte[] content)
                {
                    return new Entry(string.Empty, isDirectory: false, content);
                }

                public Entry Clone(string name)
                {
                    return IsDirectory ? Directory(name) : File(Content.ToArray());
                }

                public FileSystemEntry ToFileSystemEntry(string path, bool isWritable)
                {
                    return new FileSystemEntry(
                        path,
                        string.IsNullOrEmpty(path) ? Name : NameOf(path),
                        IsDirectory,
                        Content.Length,
                        isWritable,
                        LastModifiedUtc,
                        IsDirectory ? string.Empty : "text/plain");
                }
            }
        }
    }
}
