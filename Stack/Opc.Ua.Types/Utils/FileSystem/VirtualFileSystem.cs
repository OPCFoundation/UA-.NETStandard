/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;

namespace Opc.Ua
{
    /// <summary>
    /// Virtual file system
    /// </summary>
    public class VirtualFileSystem : IFileSystem, IDisposable
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
        /// Virtual file system maintains produced files in memory mapped
        /// files from which the production picks what is to be produced.
        /// </summary>
        public VirtualFileSystem()
        {
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Called when disposing the virtual file system
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (VirtualFile file in m_files.Values)
                {
                    file.Dispose();
                }
                m_files.Clear();
            }
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
            if (m_files.TryGetValue(path, out VirtualFile data))
            {
                return data.GetContent();
            }
            throw new FileNotFoundException($"File {path} does not exist");
        }

        /// <inheritdoc/>
        public Stream OpenRead(string path)
        {
            // open a stream on the file - if it
            // exists it is mapped from existing file
            // if it does not exist it must be in our
            // map already because it was created
            return Open(path, true).GetStream(false);
        }

        /// <inheritdoc/>
        public Stream OpenWrite(string path)
        {
            // Open a in memory stream for writing. If the file
            // exists it is not used, but a new in memory file
            // is added to the list from which we return a
            // stream to write to.
            return Open(path, false).GetStream(true);
        }

        /// <inheritdoc/>
        public void Delete(string path, bool isDirectory = false)
        {
            if (m_files.TryRemove(path, out VirtualFile file))
            {
                file.Dispose();
            }
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
        public DateTime GetLastWriteTime(string path)
        {
            if (m_files.TryGetValue(path, out VirtualFile file))
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
        /// Memory mapped file wrapper
        /// </summary>
        private sealed class VirtualFile : IDisposable
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
            /// File
            /// </summary>
            public MemoryMappedFile File { get; }

            /// <summary>
            /// Last write time
            /// </summary>
            public DateTime LastWrite { get; set; }

            /// <summary>
            /// Created time
            /// </summary>
            public DateTime Created { get; }

            /// <summary>
            /// Get current length
            /// </summary>
            internal long Length { get; set; }

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
                    Length = 0;
                    Created = LastWrite = DateTime.UtcNow;

                    File = MemoryMappedFile.CreateNew(
                        GetMapName(),
                        1 * 1024 * 1204,
                        MemoryMappedFileAccess.ReadWrite,
                        MemoryMappedFileOptions.DelayAllocatePages,
                        HandleInheritability.None);
                }
                else
                {
                    var info = new FileInfo(filePath);
                    Length = info.Length;
                    Created = info.LastWriteTimeUtc;
                    Created = info.CreationTimeUtc;
#if MAP_FILE
                    File = MemoryMappedFile.CreateFromFile(
                        new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite),
                        GetMapName(),
                        Length,
                        MemoryMappedFileAccess.ReadWrite,
                        HandleInheritability.None,
                        false);
#else // Copy file - avoid sharing issues
                    File = MemoryMappedFile.CreateNew(
                        GetMapName(),
                        Length,
                        MemoryMappedFileAccess.ReadWrite,
                        MemoryMappedFileOptions.DelayAllocatePages,
                        HandleInheritability.None);
                    SetContent(System.IO.File.ReadAllBytes(filePath));
#endif
                }

                static string GetMapName()
                {
                    return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                            Guid.NewGuid().ToString() :
                            null;
                }
            }

            /// <summary>
            /// Get a stream for the file
            /// </summary>
            /// <param name="forWriting"></param>
            /// <returns></returns>
            public Stream GetStream(bool forWriting)
            {
                return new MemoryFileStream(
                    this,
                    File.CreateViewStream(
                        0,
                        forWriting ? 0 : (int)Length,
                        forWriting ?
                            MemoryMappedFileAccess.ReadWrite :
                            MemoryMappedFileAccess.Read),
                    forWriting);
            }

            /// <summary>
            /// Get file content
            /// </summary>
            /// <returns></returns>
            public byte[] GetContent()
            {
                byte[] bytes = new byte[Length];
                using MemoryMappedViewAccessor accessor = File.CreateViewAccessor(
                    0,
                    Length,
                    MemoryMappedFileAccess.Read);
                int read = accessor.ReadArray(0, bytes, 0, (int)Length);
                return bytes;
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

                Length = content.LongLength;
                using MemoryMappedViewAccessor accessor = File.CreateViewAccessor(
                    0,
                    content.Length,
                    MemoryMappedFileAccess.ReadWrite);
                accessor.WriteArray(0, content, 0, content.Length);
                LastWrite = DateTime.UtcNow;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                File.Dispose();
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
                public override long Length => m_file.Length;

                /// <inheritdoc/>
                public override long Position { get; set; }

                /// <summary>
                /// Create a memory file for reading or writing
                /// </summary>
                public MemoryFileStream(VirtualFile file, Stream stream, bool write)
                {
                    CanRead = !write;
                    CanWrite = write;
                    m_file = file;
                    m_stream = stream;
                }

                /// <inheritdoc/>
                protected override void Dispose(bool disposing)
                {
                    if (disposing && CanWrite)
                    {
                        m_file.Length = m_stream.Position;
                    }
                    base.Dispose(disposing);
                }

                /// <inheritdoc/>
                public override void Flush()
                {
                    m_stream.Flush();
                }

                /// <inheritdoc/>
                public override int Read(byte[] buffer, int offset, int count)
                {
                    if (!CanRead)
                    {
                        throw new InvalidOperationException("Cannot read");
                    }

                    if (m_file.Length >= Position)
                    {
                        long available = m_file.Length - Position;
                        if (count > available)
                        {
                            count = checked((int)available);
                            if (count == 0)
                            {
                                return 0;
                            }
                        }
                    }
                    int read = m_stream.Read(buffer, offset, count);
                    Position += read;
                    return read;
                }

                /// <inheritdoc/>
                public override long Seek(long offset, SeekOrigin origin)
                {
                    long pos = m_stream.Seek(offset, origin);
                    Position = pos;
                    if (m_file.Length < pos)
                    {
                        m_file.Length = pos;
                    }
                    return pos;
                }

                /// <inheritdoc/>
                public override void SetLength(long value)
                {
                    if (Length == value)
                    {
                        return;
                    }

                    if (CanRead)
                    {
                        throw new InvalidOperationException(
                            "Cannot set a length when opened in read mode");
                    }

                    if (Position > value)
                    {
                        // if we are beyond the new length just move to the end.
                        Position = value;
                    }

                    m_file.Length = value;
                    m_file.LastWrite = DateTime.UtcNow;
                }

                /// <inheritdoc/>
                public override void Write(byte[] buffer, int offset, int count)
                {
                    if (!CanWrite)
                    {
                        throw new InvalidOperationException("Cannot write");
                    }
                    m_stream.Write(buffer, offset, count);
                    Position += count;
                    m_file.LastWrite = DateTime.UtcNow;
                }

                private readonly VirtualFile m_file;
                private readonly Stream m_stream;
            }
        }

        private readonly ConcurrentDictionary<string, VirtualFile> m_files
            = new(StringComparer.OrdinalIgnoreCase);
    }
}
