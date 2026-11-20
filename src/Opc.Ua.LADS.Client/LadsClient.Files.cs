/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        public async ValueTask<long> DownloadFileAsync(
            NodeId fileId,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(fileId, nameof(fileId));
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (Session is ManagedSession)
            {
                SessionRequestContext context = await SessionRequestContext.CaptureAsync(
                    Session, cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    (long bytesTransferred, _) = await context.DownloadToStreamWithResultAsync(
                        fileId,
                        destination,
                        Options.MaximumFileSize,
                        Options.FileChunkSize,
                        cancellationToken).ConfigureAwait(false);
                    return bytesTransferred;
                }
            }

            var file = new FileTypeClient(Session, fileId, Telemetry);
            return await file.DownloadToStreamAsync(
                destination, Options.MaximumFileSize, Options.FileChunkSize, cancellationToken).ConfigureAwait(false);
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
