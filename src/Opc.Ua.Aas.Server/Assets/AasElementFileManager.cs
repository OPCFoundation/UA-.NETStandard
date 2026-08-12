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

namespace Opc.Ua.Aas.Server.Assets
{
    /// <summary>
    /// Wires OPC 10000-20 FileType methods for AAS File and Blob elements.
    /// </summary>
    public sealed class AasElementFileManager : IDisposable
    {
        /// <summary>
        /// The OPC UA read mode.
        /// </summary>
        public const byte ReadMode = 1;

        /// <summary>
        /// The OPC UA write and erase-existing mode.
        /// </summary>
        public const byte WriteEraseMode = 6;

        /// <summary>
        /// Initializes a file manager.
        /// </summary>
        /// <param name="file">The FileType instance to serve.</param>
        /// <param name="content">The initial content.</param>
        /// <param name="contentType">The media type to publish.</param>
        /// <param name="maxOpenHandles">The number of handles that may be open at once.</param>
        /// <param name="maxWriteBytes">The largest content a single write handle may accumulate.</param>
        /// <exception cref="ArgumentNullException"><paramref name="file"/> is <c>null</c>.</exception>
        public AasElementFileManager(
            FileState file,
            ByteString content,
            string contentType,
            int maxOpenHandles = 16,
            long maxWriteBytes = 16 * 1024 * 1024)
        {
            m_file = file ?? throw new ArgumentNullException(nameof(file));
            m_content = content;
            m_maxOpenHandles = maxOpenHandles;
            m_maxWriteBytes = maxWriteBytes;
            if (m_file.Size is not null)
            {
                m_file.Size.Value = (ulong)m_content.Length;
            }
            if (m_file.MimeType is not null)
            {
                m_file.MimeType.Value = contentType ?? string.Empty;
            }
            if (m_file.OpenCount is not null)
            {
                m_file.OpenCount.Value = (ushort)m_handles.Count;
            }
            if (m_file.Open is not null)
            {
                m_file.Open.OnCall = OnOpen;
            }
            if (m_file.Close is not null)
            {
                m_file.Close.OnCall = OnClose;
            }
            if (m_file.Read is not null)
            {
                m_file.Read.OnCall = OnRead;
            }
            if (m_file.Write is not null)
            {
                m_file.Write.OnCall = OnWrite;
            }
            if (m_file.GetPosition is not null)
            {
                m_file.GetPosition.OnCall = OnGetPosition;
            }
            if (m_file.SetPosition is not null)
            {
                m_file.SetPosition.OnCall = OnSetPosition;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                foreach (Handle handle in m_handles.Values)
                {
                    handle.Dispose();
                }
                m_handles.Clear();
            }
        }

        private ServiceResult OnOpen(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            ref uint fileHandle)
        {
            if (mode is not ReadMode and not WriteEraseMode)
            {
                return StatusCodes.BadNotSupported;
            }

            lock (m_lock)
            {
                if (m_handles.Count >= m_maxOpenHandles)
                {
                    return StatusCodes.BadTooManyOperations;
                }
                if (mode == WriteEraseMode && m_writingHandle != 0)
                {
                    return StatusCodes.BadInvalidState;
                }

                fileHandle = ++m_nextHandle;
                var handle = mode == WriteEraseMode
                    ? new Handle(new MemoryStream(), true, SessionIdOf(context))
                    : new Handle(
                        new MemoryStream(m_content.Span.ToArray(), writable: false),
                        false,
                        SessionIdOf(context));
                m_handles.Add(fileHandle, handle);
                if (mode == WriteEraseMode)
                {
                    m_writingHandle = fileHandle;
                }
                if (m_file.OpenCount is not null)
                {
                    m_file.OpenCount.Value = (ushort)m_handles.Count;
                }
                return ServiceResult.Good;
            }
        }

        private ServiceResult OnClose(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle)
        {
            lock (m_lock)
            {
                if (!TryGetHandle(context, fileHandle, out Handle? handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                if (handle.Writing)
                {
                    m_content = ByteString.From(((MemoryStream)handle.Stream).ToArray());
                    if (m_file.Size is not null)
                    {
                        m_file.Size.Value = (ulong)m_content.Length;
                    }
                    m_writingHandle = 0;
                }
                handle.Dispose();
                m_handles.Remove(fileHandle);
                if (m_file.OpenCount is not null)
                {
                    m_file.OpenCount.Value = (ushort)m_handles.Count;
                }
                return ServiceResult.Good;
            }
        }

        private ServiceResult OnRead(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            ref ByteString data)
        {
            lock (m_lock)
            {
                if (!TryGetHandle(context, fileHandle, out Handle? handle) || handle.Writing)
                {
                    data = ByteString.Empty;
                    return StatusCodes.BadInvalidArgument;
                }
                int toRead = (int)Math.Min(length, handle.Stream.Length - handle.Stream.Position);
                if (toRead <= 0)
                {
                    data = ByteString.Empty;
                    return ServiceResult.Good;
                }
                byte[] buffer = new byte[toRead];
                int read = handle.Stream.Read(buffer, 0, buffer.Length);
                if (read != buffer.Length)
                {
                    Array.Resize(ref buffer, read);
                }
                data = ByteString.From(buffer);
                return ServiceResult.Good;
            }
        }

        private ServiceResult OnWrite(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ByteString data)
        {
            lock (m_lock)
            {
                if (!TryGetHandle(context, fileHandle, out Handle? handle) || !handle.Writing)
                {
                    return StatusCodes.BadInvalidArgument;
                }

                // The handle count is bounded but the bytes behind one were
                // not, so a client could grow the buffer without limit by
                // writing and never closing.
                if (handle.Stream.Length + data.Length > m_maxWriteBytes)
                {
                    return StatusCodes.BadTooManyOperations;
                }

                handle.Stream.Write(data.Span.ToArray(), 0, data.Length);
                return ServiceResult.Good;
            }
        }

        private ServiceResult OnGetPosition(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ref ulong position)
        {
            lock (m_lock)
            {
                if (!TryGetHandle(context, fileHandle, out Handle? handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                position = (ulong)handle.Stream.Position;
                return ServiceResult.Good;
            }
        }

        private ServiceResult OnSetPosition(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ulong position)
        {
            lock (m_lock)
            {
                if (!TryGetHandle(context, fileHandle, out Handle? handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                handle.Stream.Position = (long)Math.Min(position, (ulong)handle.Stream.Length);
                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Resolves a handle that belongs to the calling Session.
        /// </summary>
        /// <remarks>
        /// Handles are sequential, so one Session can name another's simply by
        /// counting. Without an owner check any Session could read another's
        /// buffer, seek in it, or Close it and thereby publish it as the file's
        /// content.
        /// </remarks>
        private bool TryGetHandle(ISystemContext context, uint fileHandle, out Handle handle)
        {
            if (!m_handles.TryGetValue(fileHandle, out Handle? located))
            {
                handle = null!;
                return false;
            }

            NodeId expected = SessionIdOf(context);
            if (!expected.IsNull && !located.SessionId.IsNull && located.SessionId != expected)
            {
                handle = null!;
                return false;
            }

            handle = located;
            return true;
        }

        private static NodeId SessionIdOf(ISystemContext context)
        {
            return context is ISessionSystemContext sessionContext
                ? sessionContext.SessionId.GetValueOrDefault()
                : NodeId.Null;
        }

        private readonly FileState m_file;
        private readonly int m_maxOpenHandles;
        private readonly long m_maxWriteBytes;
        private readonly System.Threading.Lock m_lock = new();
        private readonly Dictionary<uint, Handle> m_handles = [];
        private ByteString m_content;
        private uint m_nextHandle;
        private uint m_writingHandle;

        private sealed class Handle : IDisposable
        {
            public Handle(Stream stream, bool writing, NodeId sessionId)
            {
                Stream = stream;
                Writing = writing;
                SessionId = sessionId;
            }

            public Stream Stream { get; }

            public bool Writing { get; }

            public NodeId SessionId { get; }

            public void Dispose()
            {
                Stream.Dispose();
            }
        }
    }
}
