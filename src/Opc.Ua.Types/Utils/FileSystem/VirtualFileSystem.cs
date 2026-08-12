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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Virtual file system
    /// </summary>
    public sealed class VirtualFileSystem : IFileSystem, IDisposable
    {
        /// <summary>
        /// Get created files in this file system
        /// </summary>
        public IEnumerable<string> CreatedFiles => m_files
            .Where(f => !f.Value.MappedFromDisk)
            .Select(f => f.Key);

        /// <summary>
        /// Get all files in this file system
        /// </summary>
        public IEnumerable<string> Files => m_files.Keys;

        /// <summary>
        /// Virtual file system maintains produced files in memory from which
        /// the production picks what is to be produced.
        /// </summary>
        public VirtualFileSystem()
        {
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_files.Clear();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Add file to file system
        /// </summary>
        /// <param name="path"></param>
        /// <param name="data"></param>
        public void Add(string path, byte[] data)
        {
            Open(path, false).SetContent(data);
        }

        /// <summary>
        /// Get content of a file
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public byte[] Get(string path)
        {
            if (m_files.TryGetValue(path, out VirtualFile? data))
            {
                return data.GetContent();
            }
            throw new FileNotFoundException($"File {path} does not exist");
        }

        /// <inheritdoc/>
        public long GetLength(string path)
        {
            if (m_files.TryGetValue(path, out VirtualFile? data))
            {
                return data.Length;
            }
            return 0;
        }

        /// <inheritdoc/>
        public Stream OpenRead(string path)
        {
            // open a stream on the file - if it
            // exists it is loaded from the existing file
            // if it does not exist it must be in our
            // virtual file table already because it was created
            return Open(path, true).GetStream(false);
        }

        /// <inheritdoc/>
        public Stream OpenWrite(string path)
        {
            // Open an in-memory stream for writing. Existing content remains
            // available until it is overwritten, and the file is truncated
            // to the stream's final position when the stream is disposed.
            return Open(path, false).GetStream(true);
        }

        /// <inheritdoc/>
        public void Delete(string path, bool isDirectory = false)
        {
            m_files.TryRemove(path, out _);
            // real file system is immutable
        }

        /// <inheritdoc/>
        public bool Exists(string path, bool isDirectory = false)
        {
            if (isDirectory)
            {
                // All folders always exist
                return true;
            }
            // Either we loaded it already or it exists and can be mapped
            return m_files.ContainsKey(path) || SafeExists(path);
        }

        /// <inheritdoc/>
        public void Replace(string sourcePath, string destinationPath)
        {
            if (!m_files.TryRemove(sourcePath, out VirtualFile? staged))
            {
                throw new FileNotFoundException(
                    "The staged file to publish does not exist.",
                    sourcePath);
            }

            // Re-keying the whole entry publishes the file in one step, so a reader sees
            // either the previous content or the new content and never a partial write.
            m_files.AddOrUpdate(
                destinationPath,
                staged,
                (_, _) => staged);
        }

        /// <inheritdoc/>
        public DateTime GetLastWriteTime(string path)
        {
            if (m_files.TryGetValue(path, out VirtualFile? file))
            {
                return file.LastWrite;
            }
            try
            {
                return new FileInfo(path).LastWriteTimeUtc;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Get the file
        /// </summary>
        /// <param name="path"></param>
        /// <param name="mapFromDisk"></param>
        /// <returns></returns>
        private VirtualFile Open(string path, bool mapFromDisk)
        {
            return m_files.GetOrAdd(path, f => new VirtualFile(f, mapFromDisk));
        }

        /// <summary>
        /// Some files have formats that are not supported on the host file system
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool SafeExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// In-memory file wrapper
        /// </summary>
        private sealed class VirtualFile
        {
            /// <summary>
            /// Path of the file
            /// </summary>
            public string Path { get; }

            /// <summary>
            /// Whether the file was mapped from an existing file on disk
            /// </summary>
            public bool MappedFromDisk { get; }

            /// <summary>
            /// Last write time
            /// </summary>
            public DateTime LastWrite
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_lastWrite;
                    }
                }
            }

            /// <summary>
            /// Created time
            /// </summary>
            public DateTime Created { get; }

            /// <summary>
            /// Get current length
            /// </summary>
            internal long Length
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_length;
                    }
                }
            }

            /// <summary>
            /// Create a virtual file
            /// </summary>
            /// <param name="filePath"></param>
            /// <param name="createFromFile"></param>
            public VirtualFile(string filePath, bool createFromFile)
            {
                Path = filePath ?? throw new ArgumentNullException(nameof(filePath));
                MappedFromDisk = createFromFile;

                if (!createFromFile)
                {
                    Created = m_lastWrite = DateTime.UtcNow;
                }
                else
                {
                    var info = new FileInfo(filePath);
                    if (!info.Exists)
                    {
                        throw new FileNotFoundException(
                            $"File {filePath} does not exist",
                            filePath);
                    }

                    Created = info.CreationTimeUtc;
                    SetContent(File.ReadAllBytes(filePath));
                    lock (m_lock)
                    {
                        m_lastWrite = info.LastWriteTimeUtc;
                    }
                }
            }

            /// <summary>
            /// Get a stream for the file
            /// </summary>
            /// <param name="forWriting"></param>
            /// <returns></returns>
            public Stream GetStream(bool forWriting)
            {
                return new MemoryFileStream(this, forWriting);
            }

            /// <summary>
            /// Get file content
            /// </summary>
            /// <returns></returns>
            /// <exception cref="IOException">The file is too large to return as an array.</exception>
            public byte[] GetContent()
            {
                lock (m_lock)
                {
                    if (m_length > int.MaxValue)
                    {
                        throw new IOException("The virtual file is too large.");
                    }

                    byte[] content = new byte[(int)m_length];
                    CopyToCore(0, content);
                    return content;
                }
            }

            /// <summary>
            /// Set file content
            /// </summary>
            /// <param name="content"></param>
            /// <exception cref="ArgumentNullException"><paramref name="content"/> is <c>null</c>.</exception>
            public void SetContent(byte[] content)
            {
                if (content == null)
                {
                    throw new ArgumentNullException(nameof(content));
                }

                lock (m_lock)
                {
                    SetLengthCore(content.LongLength);
                    CopyFromCore(0, content);
                    m_lastWrite = DateTime.UtcNow;
                }
            }

            /// <summary>
            /// Read bytes from the file
            /// </summary>
            /// <param name="position"></param>
            /// <param name="destination"></param>
            /// <returns></returns>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public int Read(long position, Span<byte> destination)
            {
                if (position < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(position));
                }

                lock (m_lock)
                {
                    if (position >= m_length || destination.IsEmpty)
                    {
                        return 0;
                    }

                    int count = checked((int)Math.Min(destination.Length, m_length - position));
                    CopyToCore(position, destination[..count]);
                    return count;
                }
            }

            /// <summary>
            /// Write bytes to the file
            /// </summary>
            /// <param name="position"></param>
            /// <param name="source"></param>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public void Write(long position, ReadOnlySpan<byte> source)
            {
                if (position < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(position));
                }

                long endPosition = checked(position + source.Length);
                lock (m_lock)
                {
                    if (!source.IsEmpty)
                    {
                        EnsureCapacityCore(endPosition);
                        CopyFromCore(position, source);
                        if (endPosition > m_length)
                        {
                            m_length = endPosition;
                        }
                    }

                    m_lastWrite = DateTime.UtcNow;
                }
            }

            /// <summary>
            /// Write a byte to the file
            /// </summary>
            /// <param name="position"></param>
            /// <param name="value"></param>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public void WriteByte(long position, byte value)
            {
                if (position < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(position));
                }

                long endPosition = checked(position + 1);
                lock (m_lock)
                {
                    EnsureCapacityCore(endPosition);
                    int chunkIndex = checked((int)(position >> kChunkSizeBits));
                    int chunkOffset = (int)(position & kChunkOffsetMask);
                    m_chunks[chunkIndex][chunkOffset] = value;
                    if (endPosition > m_length)
                    {
                        m_length = endPosition;
                    }

                    m_lastWrite = DateTime.UtcNow;
                }
            }

            /// <summary>
            /// Set the file length
            /// </summary>
            /// <param name="value"></param>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public void SetLength(long value)
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                lock (m_lock)
                {
                    if (SetLengthCore(value))
                    {
                        m_lastWrite = DateTime.UtcNow;
                    }
                }
            }

            /// <summary>
            /// A memory file stream
            /// </summary>
            private sealed class MemoryFileStream : Stream
            {
                /// <inheritdoc/>
                public override bool CanRead { get; }

                /// <inheritdoc/>
                public override bool CanWrite { get; }

                /// <inheritdoc/>
                public override bool CanSeek => true;

                /// <inheritdoc/>
                public override long Length
                {
                    get
                    {
                        ThrowIfDisposed();
                        return m_file.Length;
                    }
                }

                /// <inheritdoc/>
                public override long Position
                {
                    get
                    {
                        ThrowIfDisposed();
                        return m_position;
                    }
                    set
                    {
                        ThrowIfDisposed();
                        if (value < 0)
                        {
                            throw new ArgumentOutOfRangeException(nameof(value));
                        }

                        m_position = value;
                    }
                }

                /// <summary>
                /// Create a memory file for reading or writing
                /// </summary>
                public MemoryFileStream(VirtualFile file, bool write)
                {
                    CanRead = !write;
                    CanWrite = write;
                    m_file = file;
                }

                /// <inheritdoc/>
                protected override void Dispose(bool disposing)
                {
                    if (disposing && !m_disposed)
                    {
                        try
                        {
                            if (CanWrite)
                            {
                                m_file.SetLength(m_position);
                            }
                        }
                        finally
                        {
                            m_disposed = true;
                        }
                    }

                    base.Dispose(disposing);
                }

                /// <inheritdoc/>
                public override void Flush()
                {
                    ThrowIfDisposed();
                }

                /// <inheritdoc/>
                public override Task FlushAsync(CancellationToken cancellationToken)
                {
                    ThrowIfDisposed();
                    return cancellationToken.IsCancellationRequested ?
                        Task.FromCanceled(cancellationToken) :
                        Task.CompletedTask;
                }

                /// <inheritdoc/>
                public override int Read(byte[] buffer, int offset, int count)
                {
                    EnsureCanRead();
                    ValidateArrayArguments(buffer, offset, count);
                    return ReadCore(buffer.AsSpan(offset, count));
                }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                /// <inheritdoc/>
#pragma warning disable CA1725 // .NET Framework used a different parameter name
                public override int Read(Span<byte> buffer)
#pragma warning restore CA1725
                {
                    return ReadCore(buffer);
                }
#endif

                /// <inheritdoc/>
                public override int ReadByte()
                {
                    Span<byte> buffer = stackalloc byte[1];
                    return ReadCore(buffer) == 0 ? -1 : buffer[0];
                }

                /// <inheritdoc/>
                public override Task<int> ReadAsync(
                    byte[] buffer,
                    int offset,
                    int count,
                    CancellationToken cancellationToken)
                {
                    EnsureCanRead();
                    ValidateArrayArguments(buffer, offset, count);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Task.FromCanceled<int>(cancellationToken);
                    }

                    return Task.FromResult(ReadCore(buffer.AsSpan(offset, count)));
                }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                /// <inheritdoc/>
                public override ValueTask<int> ReadAsync(
                    Memory<byte> buffer,
                    CancellationToken cancellationToken = default)
                {
                    EnsureCanRead();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return new ValueTask<int>(
                            Task.FromCanceled<int>(cancellationToken));
                    }

                    return new ValueTask<int>(ReadCore(buffer.Span));
                }
#endif

                /// <inheritdoc/>
                public override long Seek(long offset, SeekOrigin origin)
                {
                    ThrowIfDisposed();
                    long length = m_file.Length;
                    long position;
                    try
                    {
                        position = origin switch
                        {
                            SeekOrigin.Begin => offset,
                            SeekOrigin.Current => checked(m_position + offset),
                            SeekOrigin.End => checked(length + offset),
                            _ => throw new ArgumentException(
                                "Invalid seek origin.",
                                nameof(origin))
                        };
                    }
                    catch (OverflowException ex)
                    {
                        throw new IOException(
                            "Attempted to seek outside the bounds of the stream.",
                            ex);
                    }

                    if (position < 0)
                    {
                        throw new IOException(
                            "Attempted to seek before the beginning of the stream.");
                    }

                    if (CanWrite && position > length)
                    {
                        m_file.SetLength(position);
                    }

                    m_position = position;
                    return position;
                }

                /// <inheritdoc/>
                public override void SetLength(long value)
                {
                    ThrowIfDisposed();
                    if (m_file.Length == value)
                    {
                        return;
                    }

                    if (CanRead)
                    {
                        throw new InvalidOperationException(
                            "Cannot set a length when opened in read mode");
                    }

                    if (value < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value));
                    }

                    if (m_position > value)
                    {
                        // if we are beyond the new length just move to the end.
                        m_position = value;
                    }

                    m_file.SetLength(value);
                }

                /// <inheritdoc/>
                public override void Write(byte[] buffer, int offset, int count)
                {
                    EnsureCanWrite();
                    ValidateArrayArguments(buffer, offset, count);
                    WriteCore(buffer.AsSpan(offset, count));
                }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                /// <inheritdoc/>
#pragma warning disable CA1725 // .NET Framework used a different parameter name
                public override void Write(ReadOnlySpan<byte> buffer)
#pragma warning restore CA1725
                {
                    WriteCore(buffer);
                }
#endif

                /// <inheritdoc/>
                public override void WriteByte(byte value)
                {
                    EnsureCanWrite();
                    m_file.WriteByte(m_position, value);
                    m_position = checked(m_position + 1);
                }

                /// <inheritdoc/>
                public override Task WriteAsync(
                    byte[] buffer,
                    int offset,
                    int count,
                    CancellationToken cancellationToken)
                {
                    EnsureCanWrite();
                    ValidateArrayArguments(buffer, offset, count);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Task.FromCanceled(cancellationToken);
                    }

                    WriteCore(buffer.AsSpan(offset, count));
                    return Task.CompletedTask;
                }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                /// <inheritdoc/>
                public override ValueTask WriteAsync(
                    ReadOnlyMemory<byte> buffer,
                    CancellationToken cancellationToken = default)
                {
                    EnsureCanWrite();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return new ValueTask(Task.FromCanceled(cancellationToken));
                    }

                    WriteCore(buffer.Span);
                    return default;
                }
#endif

                private int ReadCore(Span<byte> buffer)
                {
                    EnsureCanRead();
                    int read = m_file.Read(m_position, buffer);
                    m_position += read;
                    return read;
                }

                private void WriteCore(ReadOnlySpan<byte> buffer)
                {
                    EnsureCanWrite();
                    m_file.Write(m_position, buffer);
                    m_position = checked(m_position + buffer.Length);
                }

                private void EnsureCanRead()
                {
                    if (!CanRead)
                    {
                        throw new InvalidOperationException("Cannot read");
                    }

                    ThrowIfDisposed();
                }

                private void EnsureCanWrite()
                {
                    if (!CanWrite)
                    {
                        throw new InvalidOperationException("Cannot write");
                    }

                    ThrowIfDisposed();
                }

                private void ThrowIfDisposed()
                {
                    if (m_disposed)
                    {
                        throw new ObjectDisposedException(nameof(MemoryFileStream));
                    }
                }

                private static void ValidateArrayArguments(
                    byte[] buffer,
                    int offset,
                    int count)
                {
                    if (buffer == null)
                    {
                        throw new ArgumentNullException(nameof(buffer));
                    }

                    if ((uint)offset > (uint)buffer.Length)
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset));
                    }

                    if ((uint)count > (uint)(buffer.Length - offset))
                    {
                        throw new ArgumentOutOfRangeException(nameof(count));
                    }
                }

                private readonly VirtualFile m_file;
                private long m_position;
                private bool m_disposed;
            }

            private void CopyFromCore(long position, ReadOnlySpan<byte> source)
            {
                int copied = 0;
                while (copied < source.Length)
                {
                    int chunkIndex = checked((int)(position >> kChunkSizeBits));
                    int chunkOffset = (int)(position & kChunkOffsetMask);
                    int count = Math.Min(source.Length - copied, kChunkSize - chunkOffset);
                    source.Slice(copied, count).CopyTo(
                        m_chunks[chunkIndex].AsSpan(chunkOffset, count));
                    copied += count;
                    position += count;
                }
            }

            private void CopyToCore(long position, Span<byte> destination)
            {
                int copied = 0;
                while (copied < destination.Length)
                {
                    int chunkIndex = checked((int)(position >> kChunkSizeBits));
                    int chunkOffset = (int)(position & kChunkOffsetMask);
                    int count = Math.Min(
                        destination.Length - copied,
                        kChunkSize - chunkOffset);
                    m_chunks[chunkIndex].AsSpan(chunkOffset, count).CopyTo(
                        destination.Slice(copied, count));
                    copied += count;
                    position += count;
                }
            }

            private void EnsureCapacityCore(long length)
            {
                int requiredChunkCount = GetChunkCount(length);
                int allocatedChunkCount = GetChunkCount(m_length);
                if (requiredChunkCount > m_chunks.Length)
                {
                    int capacity = m_chunks.Length == 0 ? 4 : m_chunks.Length;
                    while (capacity < requiredChunkCount)
                    {
                        capacity = capacity <= int.MaxValue / 2 ?
                            capacity * 2 :
                            requiredChunkCount;
                    }

                    Array.Resize(ref m_chunks, capacity);
                }

                for (int ii = allocatedChunkCount; ii < requiredChunkCount; ii++)
                {
                    m_chunks[ii] ??= new byte[kChunkSize];
                }
            }

            private bool SetLengthCore(long value)
            {
                if (value == m_length)
                {
                    return false;
                }

                if (value > m_length)
                {
                    EnsureCapacityCore(value);
                }
                else
                {
                    int chunkCount = GetChunkCount(value);
                    int chunkOffset = (int)(value & kChunkOffsetMask);
                    if (chunkCount > 0 && chunkOffset != 0)
                    {
                        Array.Clear(
                            m_chunks[chunkCount - 1],
                            chunkOffset,
                            kChunkSize - chunkOffset);
                    }

                    if (chunkCount < m_chunks.Length)
                    {
                        Array.Resize(ref m_chunks, chunkCount);
                    }
                }

                m_length = value;
                return true;
            }

            private static int GetChunkCount(long length)
            {
                if (length <= 0)
                {
                    return 0;
                }

                long chunkCount = ((length - 1) >> kChunkSizeBits) + 1;
                if (chunkCount > int.MaxValue)
                {
                    throw new IOException("The virtual file is too large.");
                }

                return (int)chunkCount;
            }

            private const int kChunkSizeBits = 16;
            private const int kChunkSize = 1 << kChunkSizeBits;
            private const int kChunkOffsetMask = kChunkSize - 1;
            private readonly Lock m_lock = new();
            private byte[][] m_chunks = [];
            private long m_length;
            private DateTime m_lastWrite;
        }

        private readonly ConcurrentDictionary<string, VirtualFile> m_files
            = new(StringComparer.OrdinalIgnoreCase);
    }
}
