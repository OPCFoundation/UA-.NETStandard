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
using System.Threading.Tasks;

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
                    int count = m_write?.Stream != null ? 1 : 0;
                    foreach (OpenFile file in m_reads.Values)
                    {
                        if (file.Stream != null)
                        {
                            count++;
                        }
                    }
                    return (ushort)count;
                }
            }
        }

        public bool IsWriteable
        {
            get
            {
                lock (m_lock)
                {
                    if (!m_provider.IsWritable)
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

        public Stream? GetStream(NodeId sessionId, uint fileHandle, byte requiredMode = 0)
        {
            lock (m_lock)
            {
                if (m_write != null &&
                    fileHandle == m_write.Handle &&
                    m_write.SessionId.Equals(sessionId) &&
                    (m_write.Mode & requiredMode) == requiredMode)
                {
                    return m_write.Stream;
                }
                if (m_reads.TryGetValue(fileHandle, out OpenFile? openFile) &&
                    openFile.SessionId.Equals(sessionId) &&
                    (openFile.Mode & requiredMode) == requiredMode)
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
            if (!TryReserveOpen(sessionId, mode, out OpenFile? pending, out ServiceResult error))
            {
                return error;
            }
            Stream? stream = null;
            bool accepted = false;
            try
            {
                stream = (mode & 1) != 0
                    ? m_provider.OpenReadAsync(ProviderPath, CancellationToken.None).AsTask().GetAwaiter().GetResult()
                    : m_provider.OpenWriteAsync(ProviderPath, GetWriteMode(mode), CancellationToken.None)
                        .AsTask().GetAwaiter().GetResult();
                error = CompleteOpen(pending!, stream);
                accepted = ServiceResult.IsGood(error);
                if (accepted)
                {
                    fileHandle = pending!.Handle;
                }
                return error;
            }
            catch (FileNotFoundException ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadNotFound, "File not found");
            }
            catch (UnauthorizedAccessException ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied, "Failed to open file");
            }
            catch (IOException ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadInvalidState, "Failed to open file");
            }
            finally
            {
                if (!accepted)
                {
                    CancelOpen(pending!);
                    stream?.Dispose();
                }
            }
        }

        public async ValueTask<(ServiceResult Result, uint Handle)> OpenAsync(
            NodeId sessionId,
            byte mode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReserveOpen(sessionId, mode, out OpenFile? pending, out ServiceResult error))
            {
                return (error, 0);
            }
            Stream? stream = null;
            bool accepted = false;
            try
            {
                stream = (mode & 1) != 0
                    ? await m_provider.OpenReadAsync(ProviderPath, cancellationToken).ConfigureAwait(false)
                    : await m_provider.OpenWriteAsync(ProviderPath, GetWriteMode(mode), cancellationToken)
                        .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                error = CompleteOpen(pending!, stream);
                accepted = ServiceResult.IsGood(error);
                return (error, accepted ? pending!.Handle : 0);
            }
            catch (FileNotFoundException ex)
            {
                return (ServiceResult.Create(ex, StatusCodes.BadNotFound, "File not found"), 0);
            }
            catch (UnauthorizedAccessException ex)
            {
                return (ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied, "Failed to open file"), 0);
            }
            catch (IOException ex)
            {
                return (ServiceResult.Create(ex, StatusCodes.BadInvalidState, "Failed to open file"), 0);
            }
            finally
            {
                if (!accepted)
                {
                    CancelOpen(pending!);
                    stream?.Dispose();
                }
            }
        }

        private bool TryReserveOpen(
            NodeId sessionId,
            byte mode,
            out OpenFile? pending,
            out ServiceResult error)
        {
            pending = null;
            if (sessionId.IsNull)
            {
                error = ServiceResult.Create(
                    StatusCodes.BadSessionIdInvalid,
                    "A valid Session is required to open a file.");
                return false;
            }

            bool wantsRead = (mode & 0x1) != 0;
            bool wantsWrite = (mode & 0x2) != 0;

            if (!wantsRead && !wantsWrite)
            {
                error = ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "FileType.Open mode must include read or write.");
                return false;
            }
            if (wantsRead && wantsWrite)
            {
                error = ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Simultaneous read + write open not supported.");
                return false;
            }
            if (wantsWrite && !m_provider.IsWritable)
            {
                error = ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Provider is read-only.");
                return false;
            }

            lock (m_lock)
            {
                if (m_disposed)
                {
                    error = StatusCodes.BadShutdown;
                    return false;
                }
                if (m_write != null || (wantsWrite && m_reads.Count != 0))
                {
                    error = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "File already open with incompatible access.");
                    return false;
                }
                pending = new OpenFile(CreateFileHandle(), sessionId, mode);
                if (wantsWrite)
                {
                    m_write = pending;
                }
                else
                {
                    m_reads.Add(pending.Handle, pending);
                }
            }
            error = ServiceResult.Good;
            return true;
        }

        private ServiceResult CompleteOpen(OpenFile pending, Stream stream)
        {
            lock (m_lock)
            {
                if (!ReferenceEquals(m_write, pending) &&
                    (!m_reads.TryGetValue(pending.Handle, out OpenFile? current) || !ReferenceEquals(current, pending)))
                {
                    return ServiceResult.Create(StatusCodes.BadSessionClosed,
                        "The file open was closed before the provider completed.");
                }
                pending.Stream = stream;
                return ServiceResult.Good;
            }
        }

        private void CancelOpen(OpenFile pending)
        {
            lock (m_lock)
            {
                if (ReferenceEquals(m_write, pending))
                {
                    m_write = null;
                }
                else if (m_reads.TryGetValue(pending.Handle, out OpenFile? current) && ReferenceEquals(current, pending))
                {
                    m_reads.Remove(pending.Handle);
                }
            }
        }

        private static FileWriteMode GetWriteMode(byte mode)
        {
            if ((mode & 4) != 0)
            {
                return FileWriteMode.Truncate;
            }
            return (mode & 8) != 0 ? FileWriteMode.Append : FileWriteMode.OpenOrCreate;
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
                    if (m_write.Stream != null)
                    {
                        streamsToClose.Add(m_write.Stream);
                    }
                    m_write = null;
                }

                var handlesToClose = new List<uint>();
                foreach (KeyValuePair<uint, OpenFile> entry in m_reads)
                {
                    if (entry.Value.SessionId.Equals(sessionId))
                    {
                        if (entry.Value.Stream != null)
                        {
                            streamsToClose.Add(entry.Value.Stream);
                        }
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
                m_disposed = true;
                streamsToClose = new List<Stream>(m_reads.Count + (m_write != null ? 1 : 0));
                if (m_write?.Stream != null)
                {
                    streamsToClose.Add(m_write.Stream);
                }
                m_write = null;
                foreach (OpenFile openFile in m_reads.Values)
                {
                    if (openFile.Stream != null)
                    {
                        streamsToClose.Add(openFile.Stream);
                    }
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
        private bool m_disposed;

        private sealed class OpenFile
        {
            public OpenFile(uint handle, NodeId sessionId, byte mode)
            {
                Handle = handle;
                SessionId = sessionId;
                Mode = mode;
            }

            public uint Handle { get; }

            public NodeId SessionId { get; }

            public byte Mode { get; }

            public Stream? Stream { get; set; }
        }
    }
}
