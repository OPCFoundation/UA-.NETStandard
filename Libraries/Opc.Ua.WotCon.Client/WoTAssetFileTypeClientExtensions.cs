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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon;

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// Extension methods on the generated <see cref="WoTAssetFileTypeClient"/>
    /// proxy that implement the OPC 10100-1 §6.3.10 upload-and-update
    /// flow (<c>Open(Write|EraseExisting) → Write* → CloseAndUpdate</c>).
    /// </summary>
    public static class WoTAssetFileTypeClientExtensions
    {
        // Spec §6.3.10 only allows Read (1) and Write|EraseExisting (6).
        private const byte WriteEraseMode = 6;

        /// <summary>
        /// Uploads <paramref name="thingDescriptionJson"/> to the WoT
        /// asset file and then triggers <c>CloseAndUpdate</c> so the
        /// server materialises the new asset shape.
        /// </summary>
        /// <param name="file">The asset file proxy.</param>
        /// <param name="thingDescriptionJson">The TD payload as UTF-8 JSON bytes.</param>
        /// <param name="chunkSize">Maximum per-write chunk size.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async ValueTask UploadAndUpdateAsync(
            this WoTAssetFileTypeClient file,
            ReadOnlyMemory<byte> thingDescriptionJson,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
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
            uint handle = await file.OpenAsync(WriteEraseMode, ct).ConfigureAwait(false);
            try
            {
                int offset = 0;
                while (offset < thingDescriptionJson.Length)
                {
                    int take = Math.Min(chunkSize, thingDescriptionJson.Length - offset);
                    ByteString chunk = ByteString.From(
                        thingDescriptionJson.Slice(offset, take).ToArray());
                    await file.WriteAsync(handle, chunk, ct).ConfigureAwait(false);
                    offset += take;
                }
                await file.CloseAndUpdateAsync(handle, ct).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup.
                }
                throw;
            }
        }

        /// <summary>
        /// Uploads the contents of <paramref name="thingDescriptionJson"/>
        /// to the WoT asset file in chunks and then triggers
        /// <c>CloseAndUpdate</c> so the server materialises the new
        /// asset shape. The stream is read sequentially until
        /// end-of-stream; non-seekable streams (e.g.
        /// <see cref="System.Net.Sockets.NetworkStream"/>) are
        /// supported. The caller retains ownership of
        /// <paramref name="thingDescriptionJson"/> and is responsible
        /// for disposing it.
        /// </summary>
        /// <param name="file">The asset file proxy.</param>
        /// <param name="thingDescriptionJson">A readable stream producing
        /// the TD payload as UTF-8 JSON bytes.</param>
        /// <param name="chunkSize">Maximum per-write chunk size and
        /// size of the rented intermediate buffer.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async ValueTask UploadAndUpdateAsync(
            this WoTAssetFileTypeClient file,
            Stream thingDescriptionJson,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (thingDescriptionJson is null)
            {
                throw new ArgumentNullException(nameof(thingDescriptionJson));
            }
            if (!thingDescriptionJson.CanRead)
            {
                throw new ArgumentException(
                    "Stream must be readable.", nameof(thingDescriptionJson));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
            }
            uint handle = await file.OpenAsync(WriteEraseMode, ct).ConfigureAwait(false);
            try
            {
                await FileTypeClientExtensions.CopyStreamInChunksAsync(
                    thingDescriptionJson,
                    chunkSize,
                    (chunk, token) => file.WriteAsync(handle, ByteString.From(chunk), token),
                    ct).ConfigureAwait(false);
                await file.CloseAndUpdateAsync(handle, ct).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup.
                }
                throw;
            }
        }
    }
}
