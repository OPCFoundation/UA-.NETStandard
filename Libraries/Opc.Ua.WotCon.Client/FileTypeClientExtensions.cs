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
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// Extension methods that add System.IO-style upload / download
    /// helpers on top of the generated <see cref="FileTypeClient"/>
    /// proxy. These keep the WoT Connectivity client free of any
    /// inheritance over the existing <c>Opc.Ua.Client.FileSystem</c>
    /// types — every wire interaction goes through the auto-generated
    /// <c>Open</c>/<c>Read</c>/<c>Write</c>/<c>Close</c> method
    /// wrappers.
    /// </summary>
    public static class FileTypeClientExtensions
    {
        /// <summary>
        /// Default per-call chunk size used by upload / download when
        /// the server does not advertise a smaller
        /// <c>MaxByteStringLength</c>.
        /// </summary>
        public const int DefaultChunkSize = 4096;

        /// <summary>
        /// Uploads <paramref name="content"/> to the file behind
        /// <paramref name="file"/> in chunks. The file is opened with
        /// <c>Write | EraseExisting</c> (the only write mode supported
        /// by some servers, including WoT Connectivity), written in
        /// pieces of at most <paramref name="chunkSize"/> bytes, and
        /// then closed.
        /// </summary>
        /// <param name="file">The <c>FileType</c> proxy.</param>
        /// <param name="content">The bytes to upload.</param>
        /// <param name="mode">The Open mode to use; defaults to
        /// <c>Write | EraseExisting</c>.</param>
        /// <param name="chunkSize">Maximum per-write chunk size.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is less than or equal to zero.</exception>
        public static async ValueTask UploadAsync(
            this FileTypeClient file,
            ReadOnlyMemory<byte> content,
            byte mode = 6,
            int chunkSize = DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
            }
            uint handle = await file.OpenAsync(mode, ct).ConfigureAwait(false);
            try
            {
                int offset = 0;
                while (offset < content.Length)
                {
                    int take = Math.Min(chunkSize, content.Length - offset);
                    var chunk = ByteString.From(content.Slice(offset, take).ToArray());
                    await file.WriteAsync(handle, chunk, ct).ConfigureAwait(false);
                    offset += take;
                }
            }
            finally
            {
                await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Uploads the contents of <paramref name="content"/> to the
        /// file behind <paramref name="file"/> in chunks. The stream is
        /// read sequentially until end-of-stream; non-seekable streams
        /// (e.g. <see cref="System.Net.Sockets.NetworkStream"/>,
        /// <see cref="System.IO.Compression.GZipStream"/>) are
        /// supported. The caller retains ownership of
        /// <paramref name="content"/> and is responsible for disposing
        /// it.
        /// </summary>
        /// <param name="file">The <c>FileType</c> proxy.</param>
        /// <param name="content">A readable stream producing the bytes
        /// to upload.</param>
        /// <param name="mode">The Open mode to use; defaults to
        /// <c>Write | EraseExisting</c>.</param>
        /// <param name="chunkSize">Maximum per-write chunk size and
        /// size of the rented intermediate buffer.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="content"/> is not readable.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is less than or equal to zero.</exception>
        public static async ValueTask UploadAsync(
            this FileTypeClient file,
            Stream content,
            byte mode = 6,
            int chunkSize = DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (!content.CanRead)
            {
                throw new ArgumentException("Stream must be readable.", nameof(content));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
            }
            uint handle = await file.OpenAsync(mode, ct).ConfigureAwait(false);
            try
            {
                await CopyStreamInChunksAsync(
                    content,
                    chunkSize,
                    (chunk, token) => file.WriteAsync(handle, ByteString.From(chunk), token),
                    ct).ConfigureAwait(false);
            }
            finally
            {
                await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads the entire content of the file behind <paramref name="file"/>
        /// into memory using chunked <c>Read</c> calls.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is less than or equal to zero.</exception>
        public static async ValueTask<byte[]> DownloadAllAsync(
            this FileTypeClient file,
            int chunkSize = DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
            }
            const byte readMode = 1;
            uint handle = await file.OpenAsync(readMode, ct).ConfigureAwait(false);
            try
            {
                using MemoryStream buffer = new();
                while (true)
                {
                    ByteString chunk = await file.ReadAsync(handle, chunkSize, ct).ConfigureAwait(false);
                    if (chunk.IsNull || chunk.Span.Length == 0)
                    {
                        break;
                    }
                    byte[] copy = chunk.Span.ToArray();
                    buffer.Write(copy, 0, copy.Length);
                    if (chunk.Span.Length < chunkSize)
                    {
                        break;
                    }
                }
                return buffer.ToArray();
            }
            finally
            {
                await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads the entire content of the file behind
        /// <paramref name="file"/> using chunked <c>Read</c> calls and
        /// writes it sequentially into <paramref name="destination"/>.
        /// The caller retains ownership of
        /// <paramref name="destination"/> and is responsible for
        /// disposing it.
        /// </summary>
        /// <param name="file">The <c>FileType</c> proxy.</param>
        /// <param name="destination">A writable stream that receives
        /// the file contents.</param>
        /// <param name="chunkSize">Maximum per-read chunk size.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is less than or equal to zero.</exception>
        public static async ValueTask DownloadToAsync(
            this FileTypeClient file,
            Stream destination,
            int chunkSize = DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (!destination.CanWrite)
            {
                throw new ArgumentException("Stream must be writable.", nameof(destination));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
            }
            const byte readMode = 1;
            uint handle = await file.OpenAsync(readMode, ct).ConfigureAwait(false);
            try
            {
                await CopyChunksToStreamAsync(
                    destination,
                    chunkSize,
                    async (size, token) =>
                    {
                        ByteString chunk = await file.ReadAsync(handle, size, token).ConfigureAwait(false);
                        if (chunk.IsNull)
                        {
                            return ReadOnlyMemory<byte>.Empty;
                        }
                        return chunk.Span.ToArray();
                    },
                    ct).ConfigureAwait(false);
            }
            finally
            {
                await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads from <paramref name="source"/> in chunks of up to
        /// <paramref name="chunkSize"/> bytes and invokes
        /// <paramref name="writeChunk"/> for each chunk that contained
        /// data. The loop terminates when
        /// <see cref="Stream.ReadAsync(byte[], int, int, CancellationToken)"/>
        /// returns 0 (end-of-stream). The buffer is rented from
        /// <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        internal static async ValueTask CopyStreamInChunksAsync(
            Stream source,
            int chunkSize,
            Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> writeChunk,
            CancellationToken ct)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
#if NETSTANDARD2_1_OR_GREATER || NET
                    int read = await source.ReadAsync(buffer.AsMemory(0, chunkSize), ct)
                        .ConfigureAwait(false);
#else
                    int read = await source.ReadAsync(buffer, 0, chunkSize, ct)
                        .ConfigureAwait(false);
#endif
                    if (read <= 0)
                    {
                        break;
                    }
                    await writeChunk(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Repeatedly invokes <paramref name="readChunk"/> until it
        /// returns an empty chunk (end-of-file) or a partial chunk
        /// shorter than <paramref name="chunkSize"/> (which signals the
        /// last chunk for a chunk-aligned server protocol like
        /// <c>FileType.Read</c>) and writes each chunk sequentially
        /// into <paramref name="destination"/>.
        /// </summary>
        internal static async ValueTask CopyChunksToStreamAsync(
            Stream destination,
            int chunkSize,
            Func<int, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> readChunk,
            CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                ReadOnlyMemory<byte> chunk = await readChunk(chunkSize, ct).ConfigureAwait(false);
                if (chunk.IsEmpty)
                {
                    break;
                }
#if NETSTANDARD2_1_OR_GREATER || NET
                await destination.WriteAsync(chunk, ct).ConfigureAwait(false);
#else
                byte[] copy = chunk.ToArray();
                await destination.WriteAsync(copy, 0, copy.Length, ct).ConfigureAwait(false);
#endif
                if (chunk.Length < chunkSize)
                {
                    break;
                }
            }
        }
    }
}
