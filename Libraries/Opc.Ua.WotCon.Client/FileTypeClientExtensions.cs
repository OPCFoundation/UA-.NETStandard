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
        public static async ValueTask UploadAsync(
            this FileTypeClient file,
            ReadOnlyMemory<byte> content,
            byte mode = 6,
            int chunkSize = DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (file is null) { throw new ArgumentNullException(nameof(file)); }
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
                    ByteString chunk = ByteString.From(content.Slice(offset, take).ToArray());
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
        /// Reads the entire content of the file behind <paramref name="file"/>
        /// into memory using chunked <c>Read</c> calls.
        /// </summary>
        public static async ValueTask<byte[]> DownloadAllAsync(
            this FileTypeClient file,
            int chunkSize = DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (file is null) { throw new ArgumentNullException(nameof(file)); }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
            }
            const byte readMode = 1;
            uint handle = await file.OpenAsync(readMode, ct).ConfigureAwait(false);
            try
            {
                using System.IO.MemoryStream buffer = new();
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
    }
}
