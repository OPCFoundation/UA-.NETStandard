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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.LADS.Client
{
    public sealed partial class LadsClient
    {
        /// <summary>
        /// Downloads a result file into a stream through its <c>File</c> object,
        /// bounded by <see cref="LadsClientOptions.MaximumFileSize"/>.
        /// </summary>
        /// <param name="file">The result file.</param>
        /// <param name="destination">The stream to write the content to.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The number of bytes written.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotSupported"/> when the result file has no <c>File</c> object.
        /// </exception>
        public ValueTask<long> DownloadResultFileAsync(
            LadsResultFile file,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (!file.CanDownload)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, $"The result file {file.Name} has no File object to download.");
            }
            return DownloadFileAsync(file.FileId, destination, cancellationToken);
        }

        /// <summary>
        /// Downloads the content of an OPC UA <c>FileType</c> object into a stream,
        /// bounded by <see cref="LadsClientOptions.MaximumFileSize"/>.
        /// </summary>
        /// <param name="fileId">The NodeId of the <c>FileType</c> object.</param>
        /// <param name="destination">The stream to write the content to.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The number of bytes written.</returns>
        public ValueTask<long> DownloadFileAsync(
            NodeId fileId,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(fileId, nameof(fileId));
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            var file = new FileTypeClient(Session, fileId, Telemetry);
            return file.DownloadToStreamAsync(
                destination, Options.MaximumFileSize, Options.FileChunkSize, cancellationToken);
        }

        /// <summary>
        /// Reads the content of a result file into memory, bounded by
        /// <see cref="LadsClientOptions.MaximumFileSize"/>.
        /// </summary>
        /// <param name="file">The result file.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The content.</returns>
        public async ValueTask<ByteString> ReadResultFileAsync(
            LadsResultFile file,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await DownloadResultFileAsync(file, buffer, cancellationToken).ConfigureAwait(false);
            return ByteString.From(buffer.ToArray());
        }
    }
}
