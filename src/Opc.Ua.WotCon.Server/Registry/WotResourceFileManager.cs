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

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Manages the inherited OPC UA <c>FileType</c> primitives
    /// (<c>Open</c>/<c>Read</c>/<c>Write</c>/<c>Close</c>/<c>GetPosition</c>/
    /// <c>SetPosition</c>) exposed by a single xRegistry <c>ResourceType</c>
    /// document node in the WoT Connectivity V2 registry.
    /// </summary>
    /// <remarks>
    /// Generalizes the legacy <c>WotAssetFileManager</c> for the xRegistry/V2
    /// document surface: per-session handles, a bounded write buffer, a single
    /// exclusive writer, and commit-on-close semantics. Read handles serve an
    /// immutable snapshot of the resource's active/default version bytes; a write
    /// handle buffers the upload and, when it is closed, commits the buffer as a
    /// new version through the injected callback (which stores a validated or an
    /// invalid version - the bytes are never lost).
    /// <para>
    /// Per the xRegistry document model the only supported <c>OpenFileMode</c>
    /// values are <c>Read</c> (1) and <c>Write | EraseExisting</c> (6); other
    /// modes are rejected with <see cref="StatusCodes.BadNotSupported"/>.
    /// </para>
    /// </remarks>
    internal sealed class WotResourceFileManager : IDisposable
    {
        public const byte ReadMode = 1;
        public const byte WriteEraseMode = 6;

        public WotResourceFileManager(
            FileState file,
            int maxOpenHandles,
            int maxDocumentSize,
            Func<byte[], NodeId?, CancellationToken, ValueTask<ServiceResult>> onCommit)
        {
            m_file = file ?? throw new ArgumentNullException(nameof(file));
            m_maxHandles = maxOpenHandles;
            m_maxSize = maxDocumentSize;
            m_onCommit = onCommit ?? throw new ArgumentNullException(nameof(onCommit));

            if (m_file.Writable is not null)
            {
                m_file.Writable.Value = true;
            }
            if (m_file.UserWritable is not null)
            {
                m_file.UserWritable.Value = true;
            }
            if (m_file.OpenCount is not null)
            {
                m_file.OpenCount.Value = 0;
            }
            if (m_file.MaxByteStringLength is not null)
            {
                m_file.MaxByteStringLength.Value = (uint)maxDocumentSize;
            }

            if (m_file.Open is not null)
            {
                m_file.Open.OnCall = new OpenMethodStateMethodCallHandler(OnOpen);
            }
            if (m_file.Close is not null)
            {
                m_file.Close.OnCall = new CloseMethodStateMethodCallHandler(OnClose);
            }
            if (m_file.Read is not null)
            {
                m_file.Read.OnCall = new ReadMethodStateMethodCallHandler(OnRead);
            }
            if (m_file.Write is not null)
            {
                m_file.Write.OnCall = new WriteMethodStateMethodCallHandler(OnWrite);
            }
            if (m_file.GetPosition is not null)
            {
                m_file.GetPosition.OnCall = new GetPositionMethodStateMethodCallHandler(OnGetPosition);
            }
            if (m_file.SetPosition is not null)
            {
                m_file.SetPosition.OnCall = new SetPositionMethodStateMethodCallHandler(OnSetPosition);
            }
        }

        /// <summary>The current version bytes served to readers.</summary>
        public byte[] CurrentContent { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// Replaces the served content (called when the registry snapshot changes).
        /// </summary>
        public void UpdatePersistedContent(byte[] content, string? mimeType)
        {
            CurrentContent = content ?? throw new ArgumentNullException(nameof(content));
            if (m_file.Size is not null)
            {
                m_file.Size.Value = (ulong)content.Length;
            }
            if (m_file.LastModifiedTime is not null)
            {
                m_file.LastModifiedTime.Value = DateTime.UtcNow;
            }
            if (mimeType is not null && m_file.MimeType is not null)
            {
                m_file.MimeType.Value = mimeType;
            }
        }

        /// <summary>
        /// Opens an exclusive write handle for the supplied session without a
        /// method call, returning the handle to a client that requested a file
        /// upload as part of a create operation.
        /// </summary>
        public ServiceResult TryOpenWriteHandle(NodeId? sessionId, out uint fileHandle)
        {
            fileHandle = 0;
            lock (m_handles)
            {
                if (m_handles.Count >= m_maxHandles)
                {
                    return StatusCodes.BadTooManyOperations;
                }
                if (m_writingHandle != 0)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState, "Another writer is already open on this file.");
                }
                fileHandle = ++m_nextHandle;
                m_handles.Add(fileHandle, Handle.OpenWrite(sessionId));
                m_writingHandle = fileHandle;
                if (m_file.OpenCount is not null)
                {
                    m_file.OpenCount.Value = (ushort)m_handles.Count;
                }
            }
            return ServiceResult.Good;
        }

        public void Dispose()
        {
            lock (m_handles)
            {
                foreach (Handle handle in m_handles.Values)
                {
                    handle.Dispose();
                }
                m_handles.Clear();
            }
        }

        private static NodeId? SessionIdOf(ISystemContext context)
            => (context as ISessionSystemContext)?.SessionId;

        private ServiceResult OnOpen(
            ISystemContext context, MethodState method, NodeId objectId, byte mode, ref uint fileHandle)
        {
            if (mode is not ReadMode and not WriteEraseMode)
            {
                return ServiceResult.Create(StatusCodes.BadNotSupported,
                    "A WoT document file only supports modes Read (1) and Write+EraseExisting (6).");
            }
            NodeId? sessionId = SessionIdOf(context);
            lock (m_handles)
            {
                if (m_handles.Count >= m_maxHandles)
                {
                    return StatusCodes.BadTooManyOperations;
                }
                if (mode == WriteEraseMode && m_writingHandle != 0)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Another writer is already open on this file.");
                }
                Handle handle = mode == WriteEraseMode
                    ? Handle.OpenWrite(sessionId)
                    : Handle.OpenRead(sessionId, CurrentContent);
                fileHandle = ++m_nextHandle;
                m_handles.Add(fileHandle, handle);
                if (mode == WriteEraseMode)
                {
                    m_writingHandle = fileHandle;
                }
                if (m_file.OpenCount is not null)
                {
                    m_file.OpenCount.Value = (ushort)m_handles.Count;
                }
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnClose(
            ISystemContext context, MethodState method, NodeId objectId, uint fileHandle)
        {
            Handle handle;
            bool commit;
            lock (m_handles)
            {
                if (!TryGetHandleLocked(context, fileHandle, out handle, out ServiceResult err))
                {
                    return err;
                }
                m_handles.Remove(fileHandle);
                commit = m_writingHandle == fileHandle;
                if (commit)
                {
                    m_writingHandle = 0;
                }
                if (m_file.OpenCount is not null)
                {
                    m_file.OpenCount.Value = (ushort)m_handles.Count;
                }
            }

            try
            {
                if (!commit)
                {
                    return ServiceResult.Good;
                }
                byte[] content = ((MemoryStream)handle.Stream).ToArray();
                if (content.Length == 0)
                {
                    // Nothing was written: closing a fresh writer is a no-op.
                    return ServiceResult.Good;
                }
                return m_onCommit(content, SessionIdOf(context), CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                handle.Dispose();
            }
        }

        private ServiceResult OnRead(
            ISystemContext context, MethodState method, NodeId objectId,
            uint fileHandle, int length, ref ByteString data)
        {
            lock (m_handles)
            {
                if (!TryGetHandleLocked(context, fileHandle, out Handle handle, out ServiceResult err))
                {
                    data = default;
                    return err;
                }
                if (handle.Writing)
                {
                    data = default;
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState, "File handle is opened for writing.");
                }
                if (length <= 0)
                {
                    data = ByteString.Empty;
                    return ServiceResult.Good;
                }
                int available = checked((int)(handle.Stream.Length - handle.Stream.Position));
                int toRead = Math.Min(available, length);
                if (toRead <= 0)
                {
                    data = ByteString.Empty;
                    return ServiceResult.Good;
                }
                byte[] buffer = new byte[toRead];
                int totalRead = 0;
                while (totalRead < buffer.Length)
                {
                    int n = handle.Stream.Read(buffer, totalRead, buffer.Length - totalRead);
                    if (n <= 0)
                    {
                        break;
                    }
                    totalRead += n;
                }
                if (totalRead != buffer.Length)
                {
                    Array.Resize(ref buffer, totalRead);
                }
                data = ByteString.From(buffer);
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnWrite(
            ISystemContext context, MethodState method, NodeId objectId,
            uint fileHandle, ByteString data)
        {
            lock (m_handles)
            {
                if (!TryGetHandleLocked(context, fileHandle, out Handle handle, out ServiceResult err))
                {
                    return err;
                }
                if (!handle.Writing)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState, "File handle is opened for reading.");
                }
                if (data.IsNull || data.Span.Length == 0)
                {
                    return ServiceResult.Good;
                }
                ReadOnlySpan<byte> bytes = data.Span;
                if (handle.Stream.Length + bytes.Length > m_maxSize)
                {
                    return ServiceResult.Create(StatusCodes.BadOutOfMemory,
                        "The document exceeds the configured maximum size.");
                }
                byte[] copy = bytes.ToArray();
                handle.Stream.Write(copy, 0, copy.Length);
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnGetPosition(
            ISystemContext context, MethodState method, NodeId objectId,
            uint fileHandle, ref ulong position)
        {
            lock (m_handles)
            {
                if (!TryGetHandleLocked(context, fileHandle, out Handle handle, out ServiceResult err))
                {
                    return err;
                }
                position = (ulong)handle.Stream.Position;
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnSetPosition(
            ISystemContext context, MethodState method, NodeId objectId,
            uint fileHandle, ulong position)
        {
            lock (m_handles)
            {
                if (!TryGetHandleLocked(context, fileHandle, out Handle handle, out ServiceResult err))
                {
                    return err;
                }
                if (position > (ulong)handle.Stream.Length)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument, "Requested position exceeds file length.");
                }
                handle.Stream.Position = (long)position;
            }
            return ServiceResult.Good;
        }

        private bool TryGetHandleLocked(
            ISystemContext context, uint fileHandle, out Handle handle, out ServiceResult error)
        {
            if (!m_handles.TryGetValue(fileHandle, out Handle? located))
            {
                handle = null!;
                error = ServiceResult.Create(StatusCodes.BadInvalidArgument, "Unknown file handle.");
                return false;
            }
            NodeId? expected = SessionIdOf(context);
            if (expected != null && located.SessionId != null && located.SessionId != expected)
            {
                handle = null!;
                error = ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied, "File handle is owned by another session.");
                return false;
            }
            handle = located;
            error = ServiceResult.Good;
            return true;
        }

        private sealed class Handle : IDisposable
        {
            private Handle(NodeId? sessionId, Stream stream, bool writing)
            {
                SessionId = sessionId;
                Stream = stream;
                Writing = writing;
            }

            public NodeId? SessionId { get; }
            public Stream Stream { get; }
            public bool Writing { get; }

            public static Handle OpenRead(NodeId? sessionId, byte[] snapshot)
                => new(sessionId, new MemoryStream(snapshot, writable: false), writing: false);

            public static Handle OpenWrite(NodeId? sessionId)
                => new(sessionId, new MemoryStream(), writing: true);

            public void Dispose() => Stream.Dispose();
        }

        private readonly FileState m_file;
        private readonly int m_maxHandles;
        private readonly int m_maxSize;
        private readonly Func<byte[], NodeId?, CancellationToken, ValueTask<ServiceResult>> m_onCommit;
        private readonly Dictionary<uint, Handle> m_handles = new();
        private uint m_nextHandle;
        private uint m_writingHandle;
    }
}
