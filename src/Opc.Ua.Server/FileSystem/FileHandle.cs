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
using System.Threading;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Per-file handle bag. Tracks the streams opened against a single
    /// file via repeated <c>FileType.Open</c> calls (one writer +
    /// many readers per the spec) and dispenses the integer
    /// file-handle values returned to OPC UA clients.
    /// </summary>
    internal sealed class FileHandle : IDisposable
    {
        public FileHandle(IFileSystemProvider provider, string providerPath)
        {
            m_provider = provider;
            ProviderPath = providerPath;
        }

        public string ProviderPath { get; }

        public ushort OpenCount
        {
            get
            {
                lock (m_lock)
                {
                    return (ushort)(m_reads.Count + (m_write != null ? 1 : 0));
                }
            }
        }

        public bool IsWriteable
        {
            get
            {
                lock (m_lock)
                {
                    if (!m_provider.IsWritable || m_reads.Count != 0 || m_write != null)
                    {
                        return false;
                    }
                }
                // Outside the lock: ask the provider for the latest
                // writable bit without serialising readers.
                FileSystemEntry? entry = m_provider.GetEntryAsync(
                    ProviderPath, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                return entry?.IsWritable ?? false;
            }
        }

        public long Length
        {
            get
            {
                FileSystemEntry? entry = m_provider.GetEntryAsync(
                    ProviderPath, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                return entry?.Length ?? 0L;
            }
        }

        public DateTime LastModifiedTime
        {
            get
            {
                FileSystemEntry? entry = m_provider.GetEntryAsync(
                    ProviderPath, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                return entry?.LastModifiedUtc ?? DateTime.MinValue;
            }
        }

        public string MimeType
        {
            get
            {
                FileSystemEntry? entry = m_provider.GetEntryAsync(
                    ProviderPath, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                return entry?.MimeType ?? string.Empty;
            }
        }

        public Stream? GetStream(NodeId sessionId, uint fileHandle)
        {
            lock (m_lock)
            {
                if (m_write != null &&
                    fileHandle == m_write.Handle &&
                    m_write.SessionId.Equals(sessionId))
                {
                    return m_write.Stream;
                }
                if (m_reads.TryGetValue(fileHandle, out OpenFile? openFile) &&
                    openFile.SessionId.Equals(sessionId))
                {
                    return openFile.Stream;
                }
                return null;
            }
        }

        /// <summary>
        /// Implements <c>FileType.Open</c>. The mode bits are per
        /// Part 5 §C: 0x1 = Read, 0x2 = Write, 0x4 = EraseExisting,
        /// 0x8 = Append.
        /// </summary>
        public ServiceResult Open(NodeId sessionId, byte mode, out uint fileHandle)
        {
            fileHandle = 0u;
            if (sessionId.IsNull)
            {
                return ServiceResult.Create(
                    StatusCodes.BadSessionIdInvalid,
                    "A valid Session is required to open a file.");
            }

            bool wantsRead = (mode & 0x1) != 0;
            bool wantsWrite = (mode & 0x2) != 0;

            if (!wantsRead && !wantsWrite)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "FileType.Open mode must include read or write.");
            }
            if (wantsRead && wantsWrite)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Simultaneous read + write open not supported.");
            }
            if (wantsWrite && !m_provider.IsWritable)
            {
                return ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Provider is read-only.");
            }

            try
            {
                if (wantsRead)
                {
                    Stream stream = m_provider
                        .OpenReadAsync(ProviderPath, CancellationToken.None)
                        .AsTask().GetAwaiter().GetResult();
                    bool fileAlreadyOpen;
                    lock (m_lock)
                    {
                        fileAlreadyOpen = m_write != null;
                        if (!fileAlreadyOpen)
                        {
                            fileHandle = CreateFileHandle();
                            m_reads.Add(fileHandle, new OpenFile(fileHandle, sessionId, stream));
                        }
                    }
                    if (fileAlreadyOpen)
                    {
                        stream.Dispose();
                        return ServiceResult.Create(
                            StatusCodes.BadInvalidState,
                            "File already open for write.");
                    }
                    return ServiceResult.Good;
                }

                FileWriteMode writeMode;
                if ((mode & 0x4) != 0)
                {
                    writeMode = FileWriteMode.Truncate;
                }
                else if ((mode & 0x8) != 0)
                {
                    writeMode = FileWriteMode.Append;
                }
                else
                {
                    writeMode = FileWriteMode.OpenOrCreate;
                }

                Stream writeStream = m_provider
                    .OpenWriteAsync(ProviderPath, writeMode, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                bool fileAlreadyOpenForReadOrWrite;
                lock (m_lock)
                {
                    fileAlreadyOpenForReadOrWrite = m_reads.Count != 0 || m_write != null;
                    if (!fileAlreadyOpenForReadOrWrite)
                    {
                        fileHandle = CreateFileHandle();
                        m_write = new OpenFile(fileHandle, sessionId, writeStream);
                    }
                }
                if (fileAlreadyOpenForReadOrWrite)
                {
                    writeStream.Dispose();
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "File already open for read or write.");
                }
                return ServiceResult.Good;
            }
            catch (FileNotFoundException ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadNotFound,
                    "File not found");
            }
            catch (UnauthorizedAccessException ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                    "Failed to open file");
            }
            catch (IOException ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadInvalidState,
                    "Failed to open file");
            }
        }

        public bool Close(NodeId sessionId, uint fileHandle)
        {
            Stream? stream = null;
            lock (m_lock)
            {
                if (m_write != null &&
                    fileHandle == m_write.Handle &&
                    m_write.SessionId.Equals(sessionId))
                {
                    stream = m_write.Stream;
                    m_write = null;
                }
                else if (m_reads.TryGetValue(fileHandle, out OpenFile? openFile) &&
                    openFile.SessionId.Equals(sessionId))
                {
                    stream = openFile.Stream;
                    m_reads.Remove(fileHandle);
                }
            }
            stream?.Dispose();
            return stream != null;
        }

        public void CloseSession(NodeId sessionId)
        {
            List<Stream> streamsToClose = [];
            lock (m_lock)
            {
                if (m_write != null && m_write.SessionId.Equals(sessionId))
                {
                    streamsToClose.Add(m_write.Stream);
                    m_write = null;
                }

                var handlesToClose = new List<uint>();
                foreach (KeyValuePair<uint, OpenFile> entry in m_reads)
                {
                    if (entry.Value.SessionId.Equals(sessionId))
                    {
                        streamsToClose.Add(entry.Value.Stream);
                        handlesToClose.Add(entry.Key);
                    }
                }

                foreach (uint fileHandle in handlesToClose)
                {
                    m_reads.Remove(fileHandle);
                }
            }

            DisposeStreams(streamsToClose);
        }

        public void Dispose()
        {
            List<Stream> streamsToClose;
            lock (m_lock)
            {
                streamsToClose = new List<Stream>(m_reads.Count + (m_write != null ? 1 : 0));
                if (m_write != null)
                {
                    streamsToClose.Add(m_write.Stream);
                }
                m_write = null;
                foreach (OpenFile openFile in m_reads.Values)
                {
                    streamsToClose.Add(openFile.Stream);
                }
                m_reads.Clear();
            }

            DisposeStreams(streamsToClose);
        }

        private uint CreateFileHandle()
        {
            uint fileHandle;
            do
            {
                fileHandle = BitConverter.ToUInt32(
                    Nonce.CreateRandomNonceData(sizeof(uint)),
                    0);
            }
            while (fileHandle == 0 ||
                m_write?.Handle == fileHandle ||
                m_reads.ContainsKey(fileHandle));

            return fileHandle;
        }

        private static void DisposeStreams(List<Stream> streams)
        {
            foreach (Stream stream in streams)
            {
                stream.Dispose();
            }
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<uint, OpenFile> m_reads = [];
        private readonly IFileSystemProvider m_provider;
        private OpenFile? m_write;

        private sealed class OpenFile
        {
            public OpenFile(uint handle, NodeId sessionId, Stream stream)
            {
                Handle = handle;
                SessionId = sessionId;
                Stream = stream;
            }

            public uint Handle { get; }

            public NodeId SessionId { get; }

            public Stream Stream { get; }
        }
    }
}
