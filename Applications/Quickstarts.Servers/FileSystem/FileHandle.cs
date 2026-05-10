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

namespace Quickstarts.FileSystem
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    /// <summary>
    /// File handle for an opened file. Provides read/write streams keyed by
    /// the file handle integer returned to OPC UA clients via the Open method.
    /// </summary>
    public sealed class FileHandle : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileHandle"/> class.
        /// </summary>
        internal FileHandle(FileSystemNodeId nodeId)
        {
            NodeId = nodeId;
        }

        /// <summary>
        /// The parsed node id of the file the handle refers to.
        /// </summary>
        internal FileSystemNodeId NodeId { get; }

        private bool IsOpenForWrite => m_write != null;

        private bool IsOpenForRead => m_reads.Count > 0;

        /// <summary>
        /// Length
        /// </summary>
        public long Length => new FileInfo(NodeId.RootId).Length;

        /// <summary>
        /// Can be written to
        /// </summary>
        public bool IsWriteable => !IsOpenForRead && !IsOpenForWrite
            && !new FileInfo(NodeId.RootId).IsReadOnly;

        /// <summary>
        /// Last modification
        /// </summary>
        public DateTime LastModifiedTime => File.GetLastWriteTimeUtc(NodeId.RootId);

        /// <summary>
        /// How many file handles are open
        /// </summary>
        public ushort OpenCount => (ushort)(m_reads.Count + (IsOpenForWrite ? 1 : 0));

        /// <summary>
        /// Mime type
        /// </summary>
        public string MimeType { get; } = "text/plain";

        /// <summary>
        /// Max byte string length
        /// </summary>
        public uint MaxByteStringLength { get; } = 4 * 1024;

        /// <summary>
        /// Get stream
        /// </summary>
        public Stream GetStream(uint fileHandle)
        {
            lock (m_lock)
            {
                if (m_write != null && fileHandle == 1)
                {
                    return m_write;
                }
                else if (m_reads.TryGetValue(fileHandle, out Stream stream))
                {
                    return stream;
                }
                return null;
            }
        }

        /// <summary>
        /// Open
        /// </summary>
        public ServiceResult Open(byte mode, out uint fileHandle)
        {
            lock (m_lock)
            {
                fileHandle = 0u;
                try
                {
                    if (mode == 0x1)
                    {
                        if (m_write != null)
                        {
                            return ServiceResult.Create(StatusCodes.BadInvalidState,
                                "File already open for write");
                        }
                        // read
                        var stream = new FileStream(NodeId.RootId,
                            FileMode.Open, FileAccess.Read);
                        fileHandle = ++m_handles;
                        m_reads.Add(fileHandle, stream);
                    }
                    else if ((mode & 0x2) != 0)
                    {
                        if (m_reads.Count != 0 || m_write != null)
                        {
                            return ServiceResult.Create(StatusCodes.BadInvalidState,
                                "File already open for read or write");
                        }
                        if ((mode & 0x4) != 0)
                        {
                            // Erase = OpenOrCreate + Truncate
                            m_write = new FileStream(NodeId.RootId,
                                FileMode.Create, FileAccess.Write);
                        }
                        else if ((mode & 0x8) != 0)
                        {
                            // Append
                            m_write = new FileStream(NodeId.RootId,
                                FileMode.Append, FileAccess.Write);
                        }
                        else
                        {
                            // Open or create
                            m_write = new FileStream(NodeId.RootId,
                                FileMode.OpenOrCreate, FileAccess.Write);
                        }
                        fileHandle = 1u;
                    }
                    else
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidArgument,
                            "Unknown mode value.");
                    }
                }
                catch (Exception ex)
                {
                    return ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                        "Failed to open file");
                }
            }
            return ServiceResult.Good;
        }

        public bool Close(uint fileHandle)
        {
            lock (m_lock)
            {
                if (m_write != null && fileHandle == 1)
                {
                    m_write.Dispose();
                    m_write = null;
                    return true;
                }
                if (m_reads.TryGetValue(fileHandle, out Stream stream))
                {
                    stream.Dispose();
                    m_reads.Remove(fileHandle);
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            m_write?.Dispose();
            foreach (Stream stream in m_reads.Values)
            {
                stream.Dispose();
            }
            m_reads.Clear();
        }

        private uint m_handles = 1;
        private readonly Dictionary<uint, Stream> m_reads = new Dictionary<uint, Stream>();
        private readonly Lock m_lock = new Lock();
        private Stream m_write;
    }
}
