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
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Streams companion files through an explicitly configured, host-owned transfer directory.
    /// Rejects path escapes and links and enforces the byte policy before crossing the stream seam.
    /// </summary>
    /// <remarks>
    /// The host must keep the root and its ancestors unwritable by untrusted local users.
    /// Portable path checks do not protect against a local attacker concurrently replacing directories.
    /// Destination directories must already exist; downloads never overwrite existing files.
    /// </remarks>
    public sealed class McpFileTransfers
    {
        /// <summary>
        /// Creates a transfer module using the host's policy.
        /// </summary>
        public McpFileTransfers(OpcUaMcpOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Uploads a bounded file stream. Rejects oversized files before invoking the remote operation.
        /// </summary>
        public async ValueTask<JsonObject> UploadAsync(
            string filePath,
            Func<Stream, CancellationToken, ValueTask<long>> upload,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(upload);
            ct.ThrowIfCancellationRequested();
            (string root, long limit) = GetPolicy();
            string path = ResolvePath(root, filePath);
            var file = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            await using ConfiguredAsyncDisposable fileScope = file.ConfigureAwait(false);
            if (file.Length > limit)
            {
                throw new IOException($"The upload exceeds the host limit of {limit} bytes.");
            }
            var bounded = new BoundedTransferStream(file, limit);
            await using ConfiguredAsyncDisposable boundedScope = bounded.ConfigureAwait(false);
            long reported = await upload(bounded, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (reported != bounded.BytesTransferred || bounded.BytesTransferred != file.Length)
            {
                throw new IOException("The upload did not report consumption of the complete source file.");
            }
            return TransferResult(root, path, bounded.BytesTransferred);
        }

        /// <summary>
        /// Downloads through a bounded destination stream and atomically publishes the completed file.
        /// Failed or cancelled transfers remove their temporary file and never replace the destination.
        /// </summary>
        public async ValueTask<JsonObject> DownloadAsync(
            string filePath,
            Func<Stream, CancellationToken, ValueTask> download,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(download);
            ct.ThrowIfCancellationRequested();
            (string root, long limit) = GetPolicy();
            string path = ResolvePath(root, filePath);
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new IOException("The download destination already exists; choose a new file name.");
            }
            string directory = Path.GetDirectoryName(path) ??
                throw new ArgumentException("A download destination must have a parent directory.", nameof(filePath));
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("The download's parent directory must already exist.");
            }
            string temporaryPath = Path.Combine(directory, $".opcua-mcp-{Guid.NewGuid():N}.tmp");
            try
            {
                long transferred;
                var file = new FileStream(temporaryPath, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });
                await using (file.ConfigureAwait(false))
                {
                    var bounded = new BoundedTransferStream(file, limit);
                    await using ConfiguredAsyncDisposable boundedScope = bounded.ConfigureAwait(false);
                    await download(bounded, ct).ConfigureAwait(false);
                    await file.FlushAsync(ct).ConfigureAwait(false);
                    transferred = bounded.BytesTransferred;
                }
                ct.ThrowIfCancellationRequested();
                _ = ResolvePath(root, filePath);
                File.Move(temporaryPath, path, overwrite: false);
                return TransferResult(root, path, transferred);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        /// <summary>
        /// Captures and validates the host policy for one transfer.
        /// </summary>
        private (string Root, long Limit) GetPolicy()
        {
            if (string.IsNullOrWhiteSpace(m_options.TransferRoot))
            {
                throw new InvalidOperationException(
                    "Companion file transfers are disabled. Configure OpcUaMcpOptions.TransferRoot or " +
                    "OPCUA_MCP_TRANSFER_ROOT on the host first.");
            }
            if (m_options.MaxTransferBytes <= 0)
            {
                throw new InvalidOperationException("MaxTransferBytes must be a positive integer.");
            }
            string root = Path.GetFullPath(m_options.TransferRoot);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("The configured companion transfer root must already exist.");
            }
            RejectLinks(root);
            return (root, m_options.MaxTransferBytes);
        }

        /// <summary>
        /// Resolves a regular file beneath the root using platform-correct path comparison.
        /// </summary>
        private static string ResolvePath(string root, string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            string path = Path.GetFullPath(filePath, root);
            string relative = Path.GetRelativePath(root, path);
            if (relative is "." or ".." ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative) ||
                (OperatingSystem.IsWindows() && relative.Contains(':', StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "The transfer path must identify a file beneath TransferRoot.", nameof(filePath));
            }
            RejectLinks(path);
            return path;
        }

        /// <summary>
        /// Rejects symlinks, junctions and other reparse points in every existing path segment.
        /// </summary>
        private static void RejectLinks(string path)
        {
            string current = Path.GetPathRoot(path) ??
                throw new ArgumentException("The transfer path must be absolute.", nameof(path));
            string[] segments = path[current.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments)
            {
                current = Path.Combine(current, segment);
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(current);
                }
                catch (FileNotFoundException)
                {
                    break;
                }
                catch (DirectoryNotFoundException)
                {
                    break;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ArgumentException(
                        "Transfer paths must not contain symbolic links or junctions.", nameof(path));
                }
            }
        }

        /// <summary>
        /// Reports the root-relative artifact and actual transferred byte count.
        /// </summary>
        private static JsonObject TransferResult(string root, string path, long bytes)
        {
            return new JsonObject
            {
                ["filePath"] = Path.GetRelativePath(root, path),
                ["bytesTransferred"] = bytes
            };
        }

        /// <summary>
        /// Enforces a byte limit and closes the transfer's private file stream.
        /// </summary>
        private sealed class BoundedTransferStream : Stream
        {
            /// <summary>
            /// Wraps one sequential source or destination.
            /// </summary>
            public BoundedTransferStream(Stream stream, long limit)
            {
                m_stream = stream;
                m_limit = limit;
            }

            /// <summary>
            /// Bytes successfully consumed or produced through this wrapper.
            /// </summary>
            public long BytesTransferred { get; private set; }

            /// <inheritdoc/>
            public override bool CanRead => m_stream.CanRead;

            /// <inheritdoc/>
            public override bool CanSeek => false;

            /// <inheritdoc/>
            public override bool CanWrite => m_stream.CanWrite;

            /// <inheritdoc/>
            public override long Length => m_stream.Length;

            /// <inheritdoc/>
            public override long Position
            {
                get => BytesTransferred;
                set => throw new NotSupportedException("Transfers are sequential.");
            }

            /// <inheritdoc/>
            public override void Flush()
            {
                m_stream.Flush();
            }

            /// <inheritdoc/>
            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                return m_stream.FlushAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public override int Read(byte[] buffer, int offset, int count)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                return Read(buffer.AsSpan(offset, count));
            }

            /// <inheritdoc/>
            public override int Read(Span<byte> buffer)
            {
                int read = m_stream.Read(buffer[..ReadSize(buffer.Length)]);
                RecordRead(read);
                return read;
            }

            /// <inheritdoc/>
            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }

            /// <inheritdoc/>
            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                int read = await m_stream.ReadAsync(
                    buffer[..ReadSize(buffer.Length)], cancellationToken).ConfigureAwait(false);
                RecordRead(read);
                return read;
            }

            /// <inheritdoc/>
            public override void Write(byte[] buffer, int offset, int count)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                Write(buffer.AsSpan(offset, count));
            }

            /// <inheritdoc/>
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                CheckWrite(buffer.Length);
                m_stream.Write(buffer);
                BytesTransferred += buffer.Length;
            }

            /// <inheritdoc/>
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }

            /// <inheritdoc/>
            public override async ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                CheckWrite(buffer.Length);
                await m_stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                BytesTransferred += buffer.Length;
            }

            /// <inheritdoc/>
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException("Transfers are sequential.");
            }

            /// <inheritdoc/>
            public override void SetLength(long value)
            {
                throw new NotSupportedException("Transfers are sequential.");
            }

            /// <inheritdoc/>
            public override async ValueTask DisposeAsync()
            {
                await m_stream.DisposeAsync().ConfigureAwait(false);
                await base.DisposeAsync().ConfigureAwait(false);
                GC.SuppressFinalize(this);
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_stream.Dispose();
                }
                base.Dispose(disposing);
            }

            /// <summary>
            /// Allows one extra byte on a source to detect growth beyond the policy before uploading it.
            /// </summary>
            private int ReadSize(int requested)
            {
                long remaining = m_limit - BytesTransferred;
                return remaining >= requested ? requested : (int)remaining + 1;
            }

            /// <summary>
            /// Accounts for source bytes without passing an oversized chunk to the remote operation.
            /// </summary>
            private void RecordRead(int read)
            {
                CheckWrite(read);
                BytesTransferred += read;
            }

            /// <summary>
            /// Rejects a chunk before writing it when it would cross the host policy.
            /// </summary>
            private void CheckWrite(int count)
            {
                if (count > m_limit - BytesTransferred)
                {
                    throw new IOException($"The transfer exceeds the host limit of {m_limit} bytes.");
                }
            }

            private readonly Stream m_stream;
            private readonly long m_limit;
        }

        private readonly OpcUaMcpOptions m_options;
    }
}
