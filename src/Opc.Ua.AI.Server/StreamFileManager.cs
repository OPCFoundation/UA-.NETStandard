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

namespace Opc.Ua.AI.Server
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
        /// Serves a file node from a buffer this manager owns.
        /// </summary>
        /// <param name="file">The file node to serve.</param>
        /// <param name="content">The buffer behind it.</param>
        /// <param name="writable">
        /// Whether a client may open the file for writing. A response buffer is
        /// readable and not writable, which is not a restriction so much as a
        /// statement: a client that could overwrite a model's answer could forge one.
        /// </param>
        /// <remarks>
        /// The manager takes ownership of <paramref name="content"/> from here on.
        /// Everything that touches it afterwards goes through
        /// <see cref="Snapshot"/> or <see cref="Replace"/>, so the FileType methods
        /// and whatever is producing the content serialise against each other. Two
        /// components holding the same MemoryStream under two different locks is a
        /// data race that shows up as a torn payload rather than an exception.
        /// </remarks>
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
                    ref uint fileHandle) => Open(context, objectId, mode, ref fileHandle);
            }

            if (file.Close is not null)
            {
                file.Close.OnCall = (ISystemContext context,
                    MethodState _,
                    NodeId objectId,
                    uint fileHandle) => Close(context, objectId, fileHandle);
            }

            if (file.Read is not null)
            {
                file.Read.OnCall = (ISystemContext context,
                    MethodState _,
                    NodeId objectId,
                    uint fileHandle,
                    int length,
                    ref ByteString data) => Read(context, objectId, fileHandle, length, ref data);
            }

            if (file.Write is not null)
            {
                file.Write.OnCall = (ISystemContext context,
                    MethodState _,
                    NodeId objectId,
                    uint fileHandle,
                    ByteString data) => Write(context, objectId, fileHandle, data);
            }

            if (file.GetPosition is not null)
            {
                file.GetPosition.OnCall = (ISystemContext context,
                    MethodState _,
                    NodeId objectId,
                    uint fileHandle,
                    ref ulong position) => GetPosition(context, objectId, fileHandle, ref position);
            }

            if (file.SetPosition is not null)
            {
                file.SetPosition.OnCall = (ISystemContext context,
                    MethodState _,
                    NodeId objectId,
                    uint fileHandle,
                    ulong position) => SetPosition(context, objectId, fileHandle, position);
            }
        }

        /// <summary>
        /// Copies out the current contents of a file, under this manager's lock.
        /// </summary>
        /// <remarks>
        /// The copy is the point. A caller that read the MemoryStream directly would
        /// be racing every concurrent Write, and Method calls are not serialised by
        /// the Server - so a client is entirely free to keep uploading while
        /// something else reads.
        /// </remarks>
        public byte[] Snapshot(FileState file)
        {
            ArgumentNullException.ThrowIfNull(file);

            lock (m_lock)
            {
                return m_files.TryGetValue(file.NodeId, out Entry? entry)
                    ? entry.Content.ToArray()
                    : [];
            }
        }

        /// <summary>
        /// Replaces the contents of a file, under this manager's lock.
        /// </summary>
        /// <remarks>
        /// Refreshes the published Size as well. A client sizes its reads from that
        /// value, so leaving it stale does not merely mislead - it makes a correct
        /// client read the wrong number of bytes, and a response written this way
        /// would appear empty to anyone following Part 5 properly.
        /// </remarks>
        public void Replace(FileState file, ReadOnlySpan<byte> content)
        {
            ArgumentNullException.ThrowIfNull(file);

            lock (m_lock)
            {
                if (!m_files.TryGetValue(file.NodeId, out Entry? entry))
                {
                    return;
                }

                entry.Content.SetLength(0);
                entry.Content.Write(content);
                entry.Content.Position = 0;

                // Any handle open on the old contents now points into a buffer that
                // no longer holds what it was reading, so they are closed rather
                // than left to return bytes from two different answers.
                entry.Handles.Clear();

                RefreshSize(entry);
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

        private ServiceResult Open(ISystemContext context, NodeId objectId, byte mode, ref uint fileHandle)
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
                entry.Handles[fileHandle] = new Handle(writing, SessionIdOf(context));
                return ServiceResult.Good;
            }
        }

        private ServiceResult Close(ISystemContext context, NodeId objectId, uint fileHandle)
        {
            lock (m_lock)
            {
                if (!TryGet(context, objectId, fileHandle, out Entry? entry, out _))
                {
                    return StatusCodes.BadInvalidArgument;
                }

                entry.Handles.Remove(fileHandle);
                return ServiceResult.Good;
            }
        }

        private ServiceResult Read(
            ISystemContext context,
            NodeId objectId,
            uint fileHandle,
            int length,
            ref ByteString data)
        {
            data = ByteString.Empty;

            lock (m_lock)
            {
                if (!TryGet(context, objectId, fileHandle, out Entry? entry, out Handle? handle))
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

        private ServiceResult Write(ISystemContext context, NodeId objectId, uint fileHandle, ByteString data)
        {
            lock (m_lock)
            {
                if (!TryGet(context, objectId, fileHandle, out Entry? entry, out Handle? handle))
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

        private ServiceResult GetPosition(ISystemContext context, NodeId objectId, uint fileHandle, ref ulong position)
        {
            lock (m_lock)
            {
                if (!TryGet(context, objectId, fileHandle, out _, out Handle? handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }

                position = (ulong)handle.Position;
                return ServiceResult.Good;
            }
        }

        private ServiceResult SetPosition(ISystemContext context, NodeId objectId, uint fileHandle, ulong position)
        {
            lock (m_lock)
            {
                if (!TryGet(context, objectId, fileHandle, out Entry? entry, out Handle? handle))
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

        /// <summary>
        /// Finds a handle, and refuses one that belongs to another Session.
        /// </summary>
        /// <remarks>
        /// Part 5 scopes a FileHandle to the Session that opened it, and the reason
        /// is worth stating: handles here are small sequential integers, and a
        /// transfer's NodeId is handed out by <c>BeginTransfer</c>. Without this
        /// check any session could guess a handle and inject bytes into, reposition,
        /// or close another session's in-flight upload - which for an inference
        /// payload means altering what a model is asked, from outside the
        /// conversation.
        /// </remarks>
        private bool TryGet(
            ISystemContext context,
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

            if (!entry.Handles.TryGetValue(fileHandle, out handle))
            {
                return false;
            }

            if (handle.SessionId != SessionIdOf(context))
            {
                handle = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// The Session a call arrived on, or a null NodeId outside a Session.
        /// </summary>
        private static NodeId SessionIdOf(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
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
            public Handle(bool writing, NodeId sessionId)
            {
                Writing = writing;
                SessionId = sessionId;
            }

            public bool Writing { get; }

            /// <summary>The Session that opened this handle.</summary>
            public NodeId SessionId { get; }

            public long Position { get; set; }
        }
    }
}
