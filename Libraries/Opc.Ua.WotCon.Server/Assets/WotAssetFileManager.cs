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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Server.Assets
{
    /// <summary>
    /// Manages the OPC UA file primitives exposed by a single
    /// <c>WoTAssetFileType</c> instance (OPC 10100-1 §6.3.10).
    /// </summary>
    /// <remarks>
    /// Per the spec, the only supported OpenFileMode values are <c>Read</c> (1)
    /// and <c>Write | EraseExisting</c> (6); other modes are rejected with
    /// <see cref="StatusCodes.BadNotSupported"/>.
    /// <para>
    /// <c>Close</c> discards a pending write (the TD is not materialised).
    /// <c>CloseAndUpdate</c> parses the buffered JSON and invokes the
    /// supplied callback to trigger asset rebuild + persistence.
    /// </para>
    /// </remarks>
    internal sealed class WotAssetFileManager : IDisposable
    {
        public WotAssetFileManager(
            WoTAssetFileState file,
            int maxOpenHandles,
            int maxThingDescriptionSize,
            Func<ThingDescription, CancellationToken, ValueTask<ServiceResult>> onCloseAndUpdate,
            ILogger logger)
        {
            m_file = file ?? throw new ArgumentNullException(nameof(file));
            m_maxHandles = maxOpenHandles;
            m_maxSize = maxThingDescriptionSize;
            m_onCloseAndUpdate = onCloseAndUpdate ?? throw new ArgumentNullException(nameof(onCloseAndUpdate));
            m_logger = logger;

            file.Size?.Value = 0;
            file.Writable?.Value = true;
            file.UserWritable?.Value = true;
            file.OpenCount?.Value = 0;
            file.MimeType?.Value = "application/td+json";
            file.MaxByteStringLength?.Value = (uint)maxThingDescriptionSize;
            file.LastModifiedTime?.Value = DateTime.UtcNow;

            file.Open?.OnCall = new OpenMethodStateMethodCallHandler(OnOpen);
            file.Close?.OnCall = new CloseMethodStateMethodCallHandler(OnClose);
            file.Read?.OnCall = new ReadMethodStateMethodCallHandler(OnRead);
            file.Write?.OnCall = new WriteMethodStateMethodCallHandler(OnWrite);
            file.GetPosition?.OnCall = new GetPositionMethodStateMethodCallHandler(OnGetPosition);
            file.SetPosition?.OnCall = new SetPositionMethodStateMethodCallHandler(OnSetPosition);
            file.CloseAndUpdate?.OnCall = new CloseAndUpdateMethodStateMethodCallHandler(OnCloseAndUpdate);
        }

        /// <summary>The currently persisted Thing Description bytes (UTF-8, JSON).</summary>
        public byte[] CurrentContent { get; private set; } = [];

        /// <summary>Replaces the persisted content (called by the registry).</summary>
        public void UpdatePersistedContent(byte[] content)
        {
            CurrentContent = content ?? throw new ArgumentNullException(nameof(content));
            m_file.Size?.Value = (ulong)content.Length;
            m_file.LastModifiedTime?.Value = DateTime.UtcNow;
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
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            ref uint fileHandle)
        {
            const byte readMode = 1;
            const byte writeEraseMode = 6;
            if (mode is not readMode and not writeEraseMode)
            {
                return ServiceResult.Create(StatusCodes.BadNotSupported,
                    "WoTAssetFileType only supports modes Read (1) and Write+EraseExisting (6).");
            }
            NodeId? sessionId = SessionIdOf(context);
            lock (m_handles)
            {
                if (m_handles.Count >= m_maxHandles)
                {
                    return StatusCodes.BadTooManyOperations;
                }
                if (mode == writeEraseMode && m_writingHandle != 0)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Another writer is already open on this file.");
                }
                Handle handle = mode == writeEraseMode
                    ? Handle.OpenWrite(sessionId)
                    : Handle.OpenRead(sessionId, CurrentContent);
                fileHandle = ++m_nextHandle;
                m_handles.Add(fileHandle, handle);
                if (mode == writeEraseMode)
                {
                    m_writingHandle = fileHandle;
                }
                m_file.OpenCount?.Value = (ushort)m_handles.Count;
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnClose(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle)
        {
            lock (m_handles)
            {
                if (!TryGetHandleLocked(context, fileHandle, out Handle handle, out ServiceResult err))
                {
                    return err;
                }
                m_handles.Remove(fileHandle);
                if (m_writingHandle == fileHandle)
                {
                    m_writingHandle = 0;
                }
                handle.Dispose();
                m_file.OpenCount?.Value = (ushort)m_handles.Count;
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnRead(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            ref ByteString data)
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
                    return ServiceResult.Create(StatusCodes.BadInvalidState,
                        "File handle is opened for writing.");
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
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ByteString data)
        {
            lock (m_handles)
            {
                if (!TryGetHandleLocked(context, fileHandle, out Handle handle, out ServiceResult err))
                {
                    return err;
                }
                if (!handle.Writing)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState,
                        "File handle is opened for reading.");
                }
                if (data.IsNull || data.Span.Length == 0)
                {
                    return ServiceResult.Good;
                }
                ReadOnlySpan<byte> bytes = data.Span;
                if (handle.Stream.Length + bytes.Length > m_maxSize)
                {
                    return ServiceResult.Create(StatusCodes.BadOutOfMemory,
                        "Thing description exceeds the configured maximum size.");
                }
                byte[] copy = bytes.ToArray();
                handle.Stream.Write(copy, 0, copy.Length);
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnGetPosition(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ref ulong position)
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
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ulong position)
        {
            lock (m_handles)
            {
                if (!TryGetHandleLocked(context, fileHandle, out Handle handle, out ServiceResult err))
                {
                    return err;
                }
                if (position > (ulong)handle.Stream.Length)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidArgument,
                        "Requested position exceeds file length.");
                }
                handle.Stream.Position = (long)position;
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnCloseAndUpdate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle)
        {
            Handle handle;
            lock (m_handles)
            {
                if (!TryGetHandleLocked(context, fileHandle, out handle, out ServiceResult err))
                {
                    return err;
                }
                if (!handle.Writing)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState,
                        "CloseAndUpdate requires a write handle.");
                }
                m_handles.Remove(fileHandle);
                m_writingHandle = 0;
                m_file.OpenCount?.Value = (ushort)m_handles.Count;
            }

            try
            {
                byte[] content = ((MemoryStream)handle.Stream).ToArray();
                ThingDescription? td;
                try
                {
                    td = JsonSerializer.Deserialize(
                        content,
                        ThingDescriptionJsonContext.Default.ThingDescription);
                }
                catch (JsonException ex)
                {
                    m_logger.LogWarning(ex, "Thing description JSON could not be parsed");
                    return ServiceResult.Create(ex, StatusCodes.BadDecodingError,
                        "Failed to parse Thing Description JSON.");
                }
                if (td == null)
                {
                    return ServiceResult.Create(StatusCodes.BadDecodingError,
                        "Empty Thing Description payload.");
                }

                ServiceResult result = m_onCloseAndUpdate(td, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (ServiceResult.IsGood(result))
                {
                    UpdatePersistedContent(content);
                }
                return result;
            }
            finally
            {
                handle.Dispose();
            }
        }

        private bool TryGetHandleLocked(
            ISystemContext context,
            uint fileHandle,
            out Handle handle,
            out ServiceResult error)
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
                error = ServiceResult.Create(StatusCodes.BadUserAccessDenied,
                    "File handle is owned by another session.");
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

        private readonly WoTAssetFileState m_file;
        private readonly int m_maxHandles;
        private readonly int m_maxSize;
        private readonly Func<ThingDescription, CancellationToken, ValueTask<ServiceResult>> m_onCloseAndUpdate;
        private readonly ILogger m_logger;
        private readonly Dictionary<uint, Handle> m_handles = [];
        private uint m_nextHandle;
        private uint m_writingHandle;
    }
}
