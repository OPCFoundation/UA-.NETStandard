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
using Opc.Ua;

namespace AiModelManagement.Server
{
    /// <summary>
    /// Serves Part 5 <c>FileType</c> methods over in-memory buffers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The AI specification carries oversized inference payloads over
    /// <c>FileType</c> rather than defining a transfer of its own, so this is the
    /// piece that makes a large request work with a client that already knows how to
    /// read a file. It is deliberately small: the interesting behaviour belongs to
    /// the transfer, not to the plumbing that moves its bytes.
    /// </para>
    /// <para>
    /// Buffers are in memory because an inference payload is transient by nature -
    /// it exists between a caller assembling it and a model consuming it, and it has
    /// no reason to reach a disk in between.
    /// </para>
    /// </remarks>
    internal sealed class StreamFileManager : IDisposable
    {
        private readonly Lock m_lock = new();
        private readonly Dictionary<NodeId, Entry> m_files = [];
        private readonly ulong m_maxSize;
        private uint m_nextHandle;

        /// <summary>
        /// Creates the manager.
        /// </summary>
        /// <param name="maxSize">
        /// Largest buffer a writer may produce, so that an unbounded write cannot
        /// exhaust the Server.
        /// </param>
        public StreamFileManager(ulong maxSize)
        {
            m_maxSize = maxSize;
        }

        /// <summary>
        /// Serves a file node from a buffer the caller owns.
        /// </summary>
        /// <param name="file">The file node to serve.</param>
        /// <param name="content">The buffer behind it.</param>
        /// <param name="writable">
        /// Whether a client may open the file for writing. A response buffer is
        /// readable and not writable, which is not a restriction so much as a
        /// statement: a client that could overwrite a model's answer could forge one.
        /// </param>
        public void Attach(FileState file, MemoryStream content, bool writable)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(content);

            var entry = new Entry(content, writable);

            lock (m_lock)
            {
                m_files[file.NodeId] = entry;
                entry.Node = file;
            }

            if (file.Writable is not null)
            {
                file.Writable.Value = writable;
            }

            if (file.UserWritable is not null)
            {
                file.UserWritable.Value = writable;
            }

            if (file.Size is not null)
            {
                file.Size.Value = (ulong)content.Length;
            }

            if (file.Open is not null)
            {
                file.Open.OnCall = (ISystemContext context,
                    MethodState _,
                    NodeId objectId,
                    byte mode,
                    ref uint fileHandle) => Open(objectId, mode, ref fileHandle);
            }

            if (file.Close is not null)
            {
                file.Close.OnCall = (ISystemContext _,
                    MethodState _,
                    NodeId objectId,
                    uint fileHandle) => Close(objectId, fileHandle);
            }

            if (file.Read is not null)
            {
                file.Read.OnCall = (ISystemContext _,
                    MethodState _,
                    NodeId objectId,
                    uint fileHandle,
                    int length,
                    ref ByteString data) => Read(objectId, fileHandle, length, ref data);
            }

            if (file.Write is not null)
            {
                file.Write.OnCall = (ISystemContext _,
                    MethodState _,
                    NodeId objectId,
                    uint fileHandle,
                    ByteString data) => Write(objectId, fileHandle, data);
            }

            if (file.GetPosition is not null)
            {
                file.GetPosition.OnCall = (ISystemContext _,
                    MethodState _,
                    NodeId objectId,
                    uint fileHandle,
                    ref ulong position) => GetPosition(objectId, fileHandle, ref position);
            }

            if (file.SetPosition is not null)
            {
                file.SetPosition.OnCall = (ISystemContext _,
                    MethodState _,
                    NodeId objectId,
                    uint fileHandle,
                    ulong position) => SetPosition(objectId, fileHandle, position);
            }
        }

        /// <summary>
        /// Stops serving every file under a node, closing whatever was open.
        /// </summary>
        public void Detach(NodeState parent)
        {
            ArgumentNullException.ThrowIfNull(parent);

            lock (m_lock)
            {
                var stale = new List<NodeId>();

                foreach (KeyValuePair<NodeId, Entry> pair in m_files)
                {
                    stale.Add(pair.Key);
                }

                foreach (NodeId id in stale)
                {
                    if (m_files.TryGetValue(id, out Entry? entry) && entry.Owner == parent.NodeId)
                    {
                        entry.Handles.Clear();
                        m_files.Remove(id);
                    }
                }
            }
        }

        /// <summary>
        /// Associates already attached files with the object that owns them, so
        /// discarding that object closes them.
        /// </summary>
        public void Own(NodeState owner, params FileState[] files)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(files);

            lock (m_lock)
            {
                foreach (FileState file in files)
                {
                    if (m_files.TryGetValue(file.NodeId, out Entry? entry))
                    {
                        entry.Owner = owner.NodeId;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                m_files.Clear();
            }
        }

        private ServiceResult Open(NodeId objectId, byte mode, ref uint fileHandle)
        {
            const byte read = 1;
            const byte writeEraseExisting = 6;

            lock (m_lock)
            {
                if (!m_files.TryGetValue(objectId, out Entry? entry))
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                bool writing = mode == writeEraseExisting;

                if (mode != read && !writing)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadNotSupported,
                        "Only Read (1) and Write+EraseExisting (6) are supported.");
                }

                if (writing && !entry.Writable)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "This file is not writable.");
                }

                if (writing)
                {
                    entry.Content.SetLength(0);
                    RefreshSize(entry);
                }

                fileHandle = ++m_nextHandle;
                entry.Handles[fileHandle] = new Handle(writing);
                entry.Handles[fileHandle].Position = writing ? 0 : 0;
                return ServiceResult.Good;
            }
        }

        private ServiceResult Close(NodeId objectId, uint fileHandle)
        {
            lock (m_lock)
            {
                if (!m_files.TryGetValue(objectId, out Entry? entry))
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                return entry.Handles.Remove(fileHandle)
                    ? ServiceResult.Good
                    : StatusCodes.BadInvalidArgument;
            }
        }

        private ServiceResult Read(
            NodeId objectId,
            uint fileHandle,
            int length,
            ref ByteString data)
        {
            data = ByteString.Empty;

            lock (m_lock)
            {
                if (!TryGet(objectId, fileHandle, out Entry? entry, out Handle? handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }

                if (handle.Writing)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "File handle is open for writing.");
                }

                if (length <= 0)
                {
                    return ServiceResult.Good;
                }

                long available = entry.Content.Length - handle.Position;
                int take = (int)Math.Min(available, length);

                if (take <= 0)
                {
                    return ServiceResult.Good;
                }

                byte[] buffer = new byte[take];
                entry.Content.Position = handle.Position;
                int read = entry.Content.Read(buffer, 0, take);
                handle.Position += read;

                if (read != buffer.Length)
                {
                    Array.Resize(ref buffer, read);
                }

                data = ByteString.From(buffer);
                return ServiceResult.Good;
            }
        }

        private ServiceResult Write(NodeId objectId, uint fileHandle, ByteString data)
        {
            lock (m_lock)
            {
                if (!TryGet(objectId, fileHandle, out Entry? entry, out Handle? handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }

                if (!handle.Writing)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "File handle is open for reading.");
                }

                if (data.IsNull || data.Span.Length == 0)
                {
                    return ServiceResult.Good;
                }

                if ((ulong)(entry.Content.Length + data.Span.Length) > m_maxSize)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadOutOfMemory,
                        "Payload exceeds the configured maximum transfer size.");
                }

                entry.Content.Position = handle.Position;
                entry.Content.Write(data.Span);
                handle.Position = entry.Content.Position;
                RefreshSize(entry);
                return ServiceResult.Good;
            }
        }

        private ServiceResult GetPosition(NodeId objectId, uint fileHandle, ref ulong position)
        {
            lock (m_lock)
            {
                if (!TryGet(objectId, fileHandle, out _, out Handle? handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }

                position = (ulong)handle.Position;
                return ServiceResult.Good;
            }
        }

        private ServiceResult SetPosition(NodeId objectId, uint fileHandle, ulong position)
        {
            lock (m_lock)
            {
                if (!TryGet(objectId, fileHandle, out Entry? entry, out Handle? handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }

                if (position > (ulong)entry.Content.Length)
                {
                    return StatusCodes.BadInvalidArgument;
                }

                handle.Position = (long)position;
                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Keeps the published size in step with the buffer.
        /// </summary>
        /// <remarks>
        /// A client sizes its reads from this, so a stale value is not cosmetic: it
        /// makes a correct client read the wrong number of bytes.
        /// </remarks>
        private static void RefreshSize(Entry entry)
        {
            if (entry.Node?.Size is not null)
            {
                entry.Node.Size.Value = (ulong)entry.Content.Length;
            }
        }

        private bool TryGet(
            NodeId objectId,
            uint fileHandle,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Entry? entry,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Handle? handle)
        {
            handle = null;

            if (!m_files.TryGetValue(objectId, out entry))
            {
                return false;
            }

            return entry.Handles.TryGetValue(fileHandle, out handle);
        }

        private sealed class Entry
        {
            public Entry(MemoryStream content, bool writable)
            {
                Content = content;
                Writable = writable;
            }

            public MemoryStream Content { get; }

            public bool Writable { get; }

            public NodeId Owner { get; set; } = NodeId.Null;

            public FileState? Node { get; set; }

            public Dictionary<uint, Handle> Handles { get; } = [];
        }

        private sealed class Handle
        {
            public Handle(bool writing)
            {
                Writing = writing;
            }

            public bool Writing { get; }

            public long Position { get; set; }
        }
    }
}
