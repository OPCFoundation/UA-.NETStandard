/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// A bounded FileType binding using the same session and open-mode ownership
    /// implementation as the file-system node manager.
    /// </summary>
    public interface IFileTypeBinding : IAsyncDisposable
    {
        /// <summary>
        /// Gets the bound FileType instance.
        /// </summary>
        FileState File { get; }

        /// <summary>
        /// Opens a session-owned stream, including the pre-opened handle required by temporary transfers.
        /// </summary>
        ValueTask<uint> OpenAsync(NodeId session, byte mode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits an open writer, invoking the supplied asynchronous commit callback before closing it.
        /// </summary>
        ValueTask CommitAsync(NodeId session, uint handle, CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes a stream. Temporary-transfer bindings discard an uncommitted writer.
        /// </summary>
        ValueTask CloseAsync(NodeId session, uint handle, CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces the content seen by subsequent opens; existing reader snapshots remain valid.
        /// </summary>
        ValueTask UpdateContentAsync(ByteString content, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Creates reusable FileType bindings without a dedicated file-system node manager.
    /// </summary>
    public interface IFileTypeBinder
    {
        /// <summary>
        /// Binds a generated FileType to bounded content and an optional commit callback.
        /// </summary>
        IFileTypeBinding Bind(
            FileState file,
            ISystemContext context,
            ByteString content,
            Func<ByteString, CancellationToken, ValueTask>? commit = null,
            bool commitOnClose = false,
            NodeId ownerSession = default,
            int maximumBytes = 16 * 1024 * 1024);
    }

    /// <summary>
    /// Default asynchronous FileType binder. FileHandle provides the shared single-writer,
    /// multi-reader, open-mode, and session-ownership semantics.
    /// </summary>
    public sealed class FileTypeBinder : IFileTypeBinder
    {
        /// <inheritdoc/>
        public IFileTypeBinding Bind(
            FileState file,
            ISystemContext context,
            ByteString content,
            Func<ByteString, CancellationToken, ValueTask>? commit = null,
            bool commitOnClose = false,
            NodeId ownerSession = default,
            int maximumBytes = 16 * 1024 * 1024)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (maximumBytes <= 0 || content.Span.Length > maximumBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            return new Binding(file, context, content, commit, commitOnClose, ownerSession, maximumBytes);
        }

        private sealed class Binding : IFileTypeBinding
        {
            public Binding(
                FileState file,
                ISystemContext context,
                ByteString content,
                Func<ByteString, CancellationToken, ValueTask>? commit,
                bool commitOnClose,
                NodeId owner,
                int maximumBytes)
            {
                File = file;
                m_context = context;
                m_provider = new ContentProvider(content, commit != null);
                m_handles = new FileHandle(m_provider, "content");
                m_commit = commit;
                m_commitOnClose = commitOnClose;
                m_owner = owner;
                m_maximumBytes = maximumBytes;
                File.CreateOrReplaceWritable(context, null).Value = commit != null;
                File.CreateOrReplaceUserWritable(context, null).Value = commit != null;
                File.CreateOrReplaceSize(context, null).Value = (ulong)content.Span.Length;
                File.CreateOrReplaceOpenCount(context, null).Value = 0;
                File.CreateOrReplaceMaxByteStringLength(context, null).Value =
                    (uint)Math.Min(maximumBytes, FileObjectState.GetServerReadLimit(context));
                BindMethod("Open", async (session, args, output, ct) =>
                {
                    if (!args[0].TryGetValue(out byte mode))
                    {
                        throw Error(StatusCodes.BadTypeMismatch, "A Byte open mode is required.");
                    }
                    output[0] = Variant.From(await OpenAsync(session, mode, ct).ConfigureAwait(false));
                });
                BindMethod("Close", (session, args, _, ct) => CloseAsync(session, Handle(args), ct));
                BindMethod("Read", async (session, args, output, ct) =>
                {
                    if (!args[1].TryGetValue(out int length) || length <= 0)
                    {
                        throw Error(StatusCodes.BadInvalidArgument, "The read length must be positive.");
                    }
                    await EnterAsync(ct).ConfigureAwait(false);
                    try
                    {
                        Stream stream = RequireStream(session, Handle(args), 1);
                        int limit = FileObjectState.GetServerReadLimit(m_context);
                        if (limit <= 0)
                        {
                            throw Error(StatusCodes.BadEncodingLimitsExceeded,
                                "A file response exceeds the message limit.");
                        }
                        int count = Math.Min(length, Math.Min(limit, m_maximumBytes));
                        count = (int)Math.Min(count, Math.Max(0, stream.Length - stream.Position));
                        byte[] buffer = new byte[count];
#if NET5_0_OR_GREATER
                        int read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
#else
                        int read = await stream.ReadAsync(buffer, 0, count, ct).ConfigureAwait(false);
#endif
                        output[0] = Variant.From(ByteString.From(buffer.AsSpan(0, read).ToArray()));
                    }
                    finally
                    {
                        m_gate.Release();
                    }
                });
                BindMethod("Write", async (session, args, _, ct) =>
                {
                    if (!args[1].TryGetValue(out ByteString bytes))
                    {
                        throw Error(StatusCodes.BadTypeMismatch, "A ByteString is required.");
                    }
                    await EnterAsync(ct).ConfigureAwait(false);
                    try
                    {
                        Stream stream = RequireStream(session, Handle(args), 2);
                        if (bytes.Span.Length > m_maximumBytes - stream.Position)
                        {
                            throw Error(StatusCodes.BadEncodingLimitsExceeded, "The file size limit was exceeded.");
                        }
                        byte[] buffer = bytes.Span.ToArray();
#if NET5_0_OR_GREATER
                        await stream.WriteAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
#else
                        await stream.WriteAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
#endif
                    }
                    finally
                    {
                        m_gate.Release();
                    }
                });
                BindMethod("GetPosition", async (session, args, output, ct) =>
                {
                    await EnterAsync(ct).ConfigureAwait(false);
                    try
                    {
                        output[0] = Variant.From((ulong)RequireStream(session, Handle(args), 0).Position);
                    }
                    finally
                    {
                        m_gate.Release();
                    }
                });
                BindMethod("SetPosition", async (session, args, _, ct) =>
                {
                    if (!args[1].TryGetValue(out ulong position))
                    {
                        throw Error(StatusCodes.BadTypeMismatch, "A UInt64 file position is required.");
                    }
                    await EnterAsync(ct).ConfigureAwait(false);
                    try
                    {
                        Stream stream = RequireStream(session, Handle(args), 0);
                        stream.Position = (long)Math.Min(position, (ulong)stream.Length);
                    }
                    finally
                    {
                        m_gate.Release();
                    }
                });
            }

            public FileState File { get; }

            public async ValueTask<uint> OpenAsync(
                NodeId session, byte mode, CancellationToken cancellationToken = default)
            {
                await EnterAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    ValidateOwner(session);
                    if (m_handles.OpenCount >= 64)
                    {
                        throw Error(StatusCodes.BadTooManyOperations, "The file handle limit was reached.");
                    }
                    (ServiceResult result, uint handle) = await m_handles.OpenAsync(session, mode, cancellationToken)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(result))
                    {
                        throw new ServiceResultException(result);
                    }
                    File.OpenCount!.Value = m_handles.OpenCount;
                    return handle;
                }
                finally
                {
                    m_gate.Release();
                }
            }

            public async ValueTask CommitAsync(
                NodeId session, uint handle, CancellationToken cancellationToken = default)
            {
                ByteString content;
                TaskCompletionSource<bool> completion;
                CancellationTokenSource lifetime;
                await EnterAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    Stream stream = RequireStream(session, handle, 2);
                    if (m_commit == null || stream is not MemoryStream buffer)
                    {
                        throw Error(StatusCodes.BadNotWritable, "The file has no write transaction.");
                    }
                    content = ByteString.From(buffer.ToArray());
                    lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime.Token);
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_commitCompletion = completion;
                }
                finally
                {
                    m_gate.Release();
                }
                using (lifetime)
                {
                    bool committed = false;
                    try
                    {
                        // Application publication may refresh other bindings; do not hold a file gate across it.
                        await m_commit(content, lifetime.Token).ConfigureAwait(false);
                        committed = true;
                    }
                    finally
                    {
                        await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                        try
                        {
                            if (committed)
                            {
                                m_provider.Content = content;
                                m_handles.Close(session, handle);
                                File.Size!.Value = (ulong)content.Span.Length;
                                File.OpenCount!.Value = m_handles.OpenCount;
                            }
                        }
                        finally
                        {
                            m_commitCompletion = null;
                            m_gate.Release();
                            try
                            {
                                if (committed)
                                {
                                    await File.ClearChangeMasksAsync(m_context, true, CancellationToken.None)
                                        .ConfigureAwait(false);
                                }
                            }
                            finally
                            {
                                completion.TrySetResult(true);
                            }
                        }
                    }
                }
            }

            public async ValueTask CloseAsync(
                NodeId session, uint handle, CancellationToken cancellationToken = default)
            {
                await EnterAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    ValidateOwner(session);
                    if (m_commitCompletion != null)
                    {
                        throw Error(StatusCodes.BadInvalidState, "The file is being committed.");
                    }
                    if (!m_commitOnClose || m_handles.GetStream(session, handle, 2) == null)
                    {
                        if (!m_handles.Close(session, handle))
                        {
                            throw Error(StatusCodes.BadInvalidArgument, "Unknown file handle.");
                        }
                        File.OpenCount!.Value = m_handles.OpenCount;
                        return;
                    }
                }
                finally
                {
                    m_gate.Release();
                }
                await CommitAsync(session, handle, cancellationToken).ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposeStarted, 1) != 0)
                {
                    return;
                }
                await m_lifetime.CancelAsync().ConfigureAwait(false);
                Task? commit;
                await m_gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    commit = m_commitCompletion?.Task;
                }
                finally
                {
                    m_gate.Release();
                }
                if (commit != null)
                {
                    await commit.ConfigureAwait(false);
                }
                await m_gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!m_disposed)
                    {
                        m_disposed = true;
                        m_handles.Dispose();
                        File.OpenCount!.Value = 0;
                    }
                }
                finally
                {
                    m_gate.Release();
                }
                m_gate.Dispose();
                m_lifetime.Dispose();
            }

            public async ValueTask UpdateContentAsync(
                ByteString content, CancellationToken cancellationToken = default)
            {
                if (content.Span.Length > m_maximumBytes)
                {
                    throw Error(StatusCodes.BadEncodingLimitsExceeded, "The file size limit was exceeded.");
                }
                await EnterAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    m_provider.Content = content;
                    File.Size!.Value = (ulong)content.Span.Length;
                }
                finally
                {
                    m_gate.Release();
                }
                await File.ClearChangeMasksAsync(m_context, true, CancellationToken.None).ConfigureAwait(false);
            }

            private async ValueTask EnterAsync(CancellationToken ct)
            {
                if (Volatile.Read(ref m_disposeStarted) != 0)
                {
                    throw Error(StatusCodes.BadInvalidState, "The file binding is being disposed.");
                }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_lifetime.Token);
                await m_gate.WaitAsync(linked.Token).ConfigureAwait(false);
            }

            private Stream RequireStream(NodeId session, uint handle, byte mode)
            {
                ValidateOwner(session);
                if (m_commitCompletion != null)
                {
                    throw Error(StatusCodes.BadInvalidState, "The file is being committed.");
                }
                Stream stream = m_handles.GetStream(session, handle, 0)
                    ?? throw Error(StatusCodes.BadInvalidArgument, "Unknown or foreign file handle.");
                if (mode != 0 && m_handles.GetStream(session, handle, mode) == null)
                {
                    throw Error(StatusCodes.BadInvalidState, "The file was not opened for this operation.");
                }
                return stream;
            }

            private void ValidateOwner(NodeId session)
            {
                if (m_disposed || Volatile.Read(ref m_disposeStarted) != 0)
                {
                    throw Error(StatusCodes.BadInvalidState, "The file binding was disposed.");
                }
                if (!m_owner.IsNull && m_owner != session)
                {
                    throw Error(StatusCodes.BadUserAccessDenied, "This temporary file belongs to another session.");
                }
            }

            private void BindMethod(
                string name,
                Func<NodeId, ArrayOf<Variant>, List<Variant>, CancellationToken, ValueTask> invoke)
            {
                MethodState method = File.FindChild(m_context, new QualifiedName(name)) as MethodState
                    ?? throw new InvalidOperationException("The FileType method is missing: " + name);
                method.OnCallMethod2Async = async (context, _, _, arguments, output, ct) =>
                {
                    try
                    {
                        await invoke((context as ISessionSystemContext)?.SessionId ?? NodeId.Null,
                            arguments, output, ct).ConfigureAwait(false);
                        return ServiceResult.Good;
                    }
                    catch (ServiceResultException exception)
                    {
                        return exception.Result;
                    }
                };
            }

            private static uint Handle(ArrayOf<Variant> arguments)
            {
                return arguments[0].TryGetValue(out uint value) ? value
                    : throw Error(StatusCodes.BadTypeMismatch, "A UInt32 file handle is required.");
            }

            private readonly ISystemContext m_context;
            private readonly ContentProvider m_provider;
            private readonly FileHandle m_handles;
            private readonly Func<ByteString, CancellationToken, ValueTask>? m_commit;
            private readonly bool m_commitOnClose;
            private readonly NodeId m_owner;
            private readonly int m_maximumBytes;
            private readonly SemaphoreSlim m_gate = new(1, 1);
            private readonly CancellationTokenSource m_lifetime = new();
            private TaskCompletionSource<bool>? m_commitCompletion;
            private int m_disposeStarted;
            private bool m_disposed;
        }

        private static ServiceResultException Error(StatusCode status, string message)
        {
            return ServiceResultException.Create(status, "{0}", message);
        }

        private sealed class ContentProvider : IFileSystemProvider
        {
            public ContentProvider(ByteString content, bool writable)
            {
                Content = content;
                IsWritable = writable;
            }

            public ByteString Content { get; set; }

            public string MountName => "FileType";

            public bool IsWritable { get; }

            public ValueTask<FileSystemEntry?> GetEntryAsync(string path, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<FileSystemEntry?>(new FileSystemEntry(
                    path, "content", false, Content.Span.Length, IsWritable, DateTime.MinValue, string.Empty));
            }

            public ValueTask<Stream> OpenReadAsync(string path, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (Content.IsNull)
                {
                    throw new FileNotFoundException("File content has not been committed.");
                }
                return new ValueTask<Stream>(new MemoryStream(Content.Span.ToArray(), false));
            }

            public ValueTask<Stream> OpenWriteAsync(string path, FileWriteMode mode, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                var stream = new MemoryStream();
                if (mode != FileWriteMode.Truncate)
                {
                    stream.SetLength(Content.Span.Length);
                    Content.Span.CopyTo(stream.GetBuffer());
                    stream.Position = mode == FileWriteMode.Append ? stream.Length : 0;
                }
                return new ValueTask<Stream>(stream);
            }

            public IAsyncEnumerable<FileSystemEntry> EnumerateAsync(string path, CancellationToken ct) =>
                throw new NotSupportedException("A FileType content provider has no directory operations.");

            public ValueTask CreateDirectoryAsync(string path, CancellationToken ct) =>
                throw new NotSupportedException("A FileType content provider has no directory operations.");

            public ValueTask CreateFileAsync(string path, CancellationToken ct) =>
                throw new NotSupportedException("The bound file already exists.");

            public ValueTask DeleteAsync(string path, CancellationToken ct) =>
                throw new NotSupportedException("The binding owns the file lifetime.");

            public ValueTask MoveAsync(string source, string target, CancellationToken ct) =>
                throw new NotSupportedException("The binding owns the file identity.");

            public ValueTask CopyAsync(string source, string target, CancellationToken ct) =>
                throw new NotSupportedException("The binding owns the file identity.");
        }
    }
}
