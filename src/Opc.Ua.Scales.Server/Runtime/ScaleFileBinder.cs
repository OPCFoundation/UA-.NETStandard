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

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// Serves OPC 10000-5 <c>FileType</c> nodes of a scale - the
    /// <c>RecipeFile</c> of a recipe and the <c>ReportFile</c> of a recipe
    /// product - from in-memory buffers.
    /// </summary>
    /// <remarks>
    /// Supports the <c>Read</c> (1) and <c>Write | EraseExisting</c> (6) open
    /// modes; a handle belongs to the session that opened it. A writable file
    /// reports each completed upload - a write handle being closed - to its
    /// callback, which is where an application parses its recipe format:
    /// OPC 40200 leaves the recipe file format to the vendor.
    /// </remarks>
    internal sealed class ScaleFileBinder
    {
        public ScaleFileBinder(long maximumSize = 16 * 1024 * 1024)
        {
            m_maximumSize = maximumSize;
        }

        /// <summary>
        /// Serves a file node.
        /// </summary>
        /// <param name="file">The file node.</param>
        /// <param name="content">The initial content.</param>
        /// <param name="uploaded">
        /// Called with the new content when a client closes a write handle; a
        /// bad result is returned from <c>Close</c> and the previous content
        /// restored. Null makes the file read-only.
        /// </param>
        public void Attach(FileState file, byte[] content, Func<byte[], ServiceResult>? uploaded)
        {
            var entry = new Entry(file, uploaded);
            entry.Content.Write(content, 0, content.Length);
            lock (m_lock)
            {
                m_files[file] = entry;
            }
            Set(file.Writable, uploaded != null);
            Set(file.UserWritable, uploaded != null);
            Set(file.OpenCount, (ushort)0);
            RefreshSize(entry);

            if (file.Open != null)
            {
                file.Open.OnCall = (ISystemContext context, MethodState _, NodeId __, byte mode, ref uint handle) =>
                    Open(context, file, mode, ref handle);
            }
            if (file.Close != null)
            {
                file.Close.OnCall = (context, _, __, handle) => Close(context, file, handle);
            }
            if (file.Read != null)
            {
                file.Read.OnCall = (ISystemContext context, MethodState _, NodeId __, uint handle, int length, ref ByteString data) =>
                    Read(context, file, handle, length, ref data);
            }
            if (file.Write != null)
            {
                file.Write.OnCall = (context, _, __, handle, data) => Write(context, file, handle, data);
            }
            if (file.GetPosition != null)
            {
                file.GetPosition.OnCall = (ISystemContext context, MethodState _, NodeId __, uint handle, ref ulong position) =>
                    GetPosition(context, file, handle, ref position);
            }
            if (file.SetPosition != null)
            {
                file.SetPosition.OnCall = (context, _, __, handle, position) => SetPosition(context, file, handle, position);
            }
        }

        /// <summary>
        /// Gets the content of a served file.
        /// </summary>
        /// <param name="file">The file node.</param>
        public byte[] Snapshot(FileState file)
        {
            lock (m_lock)
            {
                return m_files.TryGetValue(file, out Entry? entry) ? entry.Content.ToArray() : [];
            }
        }

        /// <summary>
        /// Replaces the content of a served file; open handles are closed
        /// because they would read a mix of old and new content.
        /// </summary>
        /// <param name="file">The file node.</param>
        /// <param name="content">The new content.</param>
        public void Replace(FileState file, byte[] content)
        {
            lock (m_lock)
            {
                if (!m_files.TryGetValue(file, out Entry? entry))
                {
                    return;
                }
                entry.Content.SetLength(0);
                entry.Content.Write(content, 0, content.Length);
                entry.Handles.Clear();
                Set(file.OpenCount, (ushort)0);
                RefreshSize(entry);
            }
        }

        private ServiceResult Open(ISystemContext context, FileState file, byte mode, ref uint handle)
        {
            const byte read = 1;
            const byte writeEraseExisting = 6;
            lock (m_lock)
            {
                if (!m_files.TryGetValue(file, out Entry? entry))
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
                if (writing && entry.Uploaded == null)
                {
                    return ServiceResult.Create(StatusCodes.BadNotWritable, "The file is read-only.");
                }
                if (entry.Uploading)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "The file is being updated.");
                }
                if (writing && entry.Handles.Count > 0)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "The file is open.");
                }
                handle = ++m_nextHandle;
                entry.Handles[handle] = new Handle(writing, SessionIdOf(context))
                {
                    Buffer = writing ? new MemoryStream() : null
                };
                Set(file.OpenCount, (ushort)entry.Handles.Count);
                return ServiceResult.Good;
            }
        }

        private ServiceResult Close(ISystemContext context, FileState file, uint handle)
        {
            Entry? entry;
            Handle? open;
            lock (m_lock)
            {
                if (!TryGet(context, file, handle, out entry, out open))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                entry.Handles.Remove(handle);
                Set(file.OpenCount, (ushort)entry.Handles.Count);
                if (open.Writing)
                {
                    // Until the upload is applied or refused no handle may
                    // open: Replace would drop it without its client knowing.
                    entry.Uploading = true;
                }
            }
            if (!open.Writing)
            {
                return ServiceResult.Good;
            }

            // The upload is handed to the application outside the lock: it
            // parses a vendor format and may take its time.
            try
            {
                byte[] uploaded = open.Buffer!.ToArray();
                ServiceResult result = entry.Uploaded!(uploaded);
                if (ServiceResult.IsGood(result))
                {
                    Replace(file, uploaded);
                }
                return result;
            }
            finally
            {
                lock (m_lock)
                {
                    entry.Uploading = false;
                }
            }
        }

        private ServiceResult Read(ISystemContext context, FileState file, uint handle, int length, ref ByteString data)
        {
            data = ByteString.Empty;
            lock (m_lock)
            {
                if (!TryGet(context, file, handle, out Entry? entry, out Handle? open))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                if (open.Writing)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "The handle is open for writing.");
                }
                int take = (int)Math.Min(Math.Max(0, length), entry.Content.Length - open.Position);
                if (take <= 0)
                {
                    return ServiceResult.Good;
                }
                byte[] buffer = new byte[take];
                entry.Content.Position = open.Position;
                int count = entry.Content.Read(buffer, 0, take);
                open.Position += count;
                data = ByteString.From(count == take ? buffer : buffer.AsSpan(0, count).ToArray());
                return ServiceResult.Good;
            }
        }

        private ServiceResult Write(ISystemContext context, FileState file, uint handle, ByteString data)
        {
            lock (m_lock)
            {
                if (!TryGet(context, file, handle, out _, out Handle? open))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                if (!open.Writing)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState, "The handle is open for reading.");
                }
                if (data.IsNull || data.Span.Length == 0)
                {
                    return ServiceResult.Good;
                }
                if (open.Buffer!.Length + data.Span.Length > m_maximumSize)
                {
                    return ServiceResult.Create(StatusCodes.BadOutOfMemory, "The file exceeds the maximum size.");
                }
                byte[] bytes = data.Span.ToArray();
                open.Buffer.Position = open.Position;
                open.Buffer.Write(bytes, 0, bytes.Length);
                open.Position = open.Buffer.Position;
                return ServiceResult.Good;
            }
        }

        private ServiceResult GetPosition(ISystemContext context, FileState file, uint handle, ref ulong position)
        {
            lock (m_lock)
            {
                if (!TryGet(context, file, handle, out _, out Handle? open))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                position = (ulong)open.Position;
                return ServiceResult.Good;
            }
        }

        private ServiceResult SetPosition(ISystemContext context, FileState file, uint handle, ulong position)
        {
            lock (m_lock)
            {
                if (!TryGet(context, file, handle, out Entry? entry, out Handle? open))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                long length = open.Writing ? open.Buffer!.Length : entry.Content.Length;
                if (position > (ulong)length)
                {
                    return StatusCodes.BadInvalidArgument;
                }
                open.Position = (long)position;
                return ServiceResult.Good;
            }
        }

        private bool TryGet(
            ISystemContext context,
            FileState file,
            uint handle,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Entry? entry,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Handle? open)
        {
            open = null;
            if (!m_files.TryGetValue(file, out entry) || !entry.Handles.TryGetValue(handle, out open))
            {
                return false;
            }
            if (open.SessionId != SessionIdOf(context))
            {
                open = null;
                return false;
            }
            return true;
        }

        private static NodeId SessionIdOf(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
        }

        private static void RefreshSize(Entry entry)
        {
            Set(entry.File.Size, (ulong)entry.Content.Length);
        }

        private static void Set<T>(PropertyState<T>? property, T value)
        {
            if (property != null)
            {
                property.Value = value;
            }
        }

        private sealed class Entry
        {
            public Entry(FileState file, Func<byte[], ServiceResult>? uploaded)
            {
                File = file;
                Uploaded = uploaded;
            }

            public FileState File { get; }

            public Func<byte[], ServiceResult>? Uploaded { get; }

            public MemoryStream Content { get; } = new();

            public Dictionary<uint, Handle> Handles { get; } = [];

            public bool Uploading { get; set; }
        }

        private sealed class Handle
        {
            public Handle(bool writing, NodeId sessionId)
            {
                Writing = writing;
                SessionId = sessionId;
            }

            public bool Writing { get; }

            public NodeId SessionId { get; }

            public long Position { get; set; }

            public MemoryStream? Buffer { get; init; }
        }

        private readonly Dictionary<FileState, Entry> m_files = [];
        private readonly Lock m_lock = new();
        private readonly long m_maximumSize;
        private uint m_nextHandle;
    }
}
