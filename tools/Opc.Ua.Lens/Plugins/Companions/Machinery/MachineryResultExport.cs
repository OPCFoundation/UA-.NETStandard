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
using Opc.Ua;
using Opc.Ua.Machinery.Client;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Plugins.Companions.Machinery
{
    /// <summary>
    /// Borrows the strict client for result identity and bounded stream transfer.
    /// </summary>
    internal interface IMachineryResultSource
    {
        /// <summary>
        /// Reads the actual currently published ID, or null for an empty slot.
        /// </summary>
        ValueTask<string?> ReadResultIdAsync(
            CompanionContext context, NodeId variable, CancellationToken cancellationToken);

        /// <summary>
        /// Downloads an exact ID, enforcing the byte limit during transfer and closing the remote handle.
        /// </summary>
        ValueTask<long> DownloadAsync(
            CompanionContext context, NodeId owner, string resultId, Stream destination,
            long maximumBytes, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Uses the existing bounded Machinery client without owning the session or destination.
    /// </summary>
    internal sealed class MachineryResultSource : IMachineryResultSource
    {
        /// <inheritdoc/>
        public async ValueTask<string?> ReadResultIdAsync(
            CompanionContext context, NodeId variable, CancellationToken cancellationToken)
        {
            MachineryResultSnapshot result = await CreateClient(context)
                .ReadPublishedResultAsync(variable, cancellationToken).ConfigureAwait(false);
            string? id = result.Result?.ResultMetaData?.ResultId;
            if (result.Result is not null && (string.IsNullOrWhiteSpace(id) || id.Length > 256))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The published ResultId is invalid.");
            }
            return id;
        }

        /// <inheritdoc/>
        public ValueTask<long> DownloadAsync(
            CompanionContext context, NodeId owner, string resultId, Stream destination,
            long maximumBytes, CancellationToken cancellationToken)
        {
            return CreateClient(context).DownloadResultAsync(
                owner, resultId, destination, maximumBytes, cancellationToken);
        }

        private static MachineryClient CreateClient(CompanionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return new MachineryClient(context.Session, context.Telemetry, new MachineryClientOptions
            {
                MaximumBrowseReferences = 1024,
                MaximumBrowsePages = 128,
                MaximumReadArrayItems = 1024
            });
        }
    }

    /// <summary>
    /// Publishes only a complete, still-selected result through the shared new-file exporter.
    /// </summary>
    internal static class MachineryResultExport
    {
        /// <summary>
        /// Checks selection and published ID before and after the bounded transfer, then publishes once.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<long> WriteAsync(
            string destination, MachineryActionSelection selection, IMachineryResultSource source,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(selection);
            ArgumentNullException.ThrowIfNull(source);
            cancellationToken.ThrowIfCancellationRequested();
            string path = OpenUsdCompanionExporter.ValidateDestination(destination);
            string exactId = selection.RequireResultId();
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lifetime.CancelAfter(TimeSpan.FromSeconds(30));
            void RequireCurrent()
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (selection.RequireResultId() != exactId)
                {
                    throw new InvalidOperationException("The selected result changed. Inspect again.");
                }
            }
            async Task RequirePublishedAsync()
            {
                RequireCurrent();
                string? current = await source.ReadResultIdAsync(
                    selection.Context, selection.Item.NodeId, lifetime.Token).ConfigureAwait(false);
                RequireCurrent();
                if (!string.Equals(current, exactId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The published result changed. Inspect again.");
                }
            }
            await RequirePublishedAsync().ConfigureAwait(false);
            byte[] buffer = new byte[CompanionLocalExport.MaximumBytes];
            using var stream = new MemoryStream(buffer, writable: true);
            stream.SetLength(0);
            long count = await source.DownloadAsync(selection.Context, selection.Machine.NodeId, exactId,
                stream, CompanionLocalExport.MaximumBytes, lifetime.Token).ConfigureAwait(false);
            RequireCurrent();
            if (count is < 1 or > CompanionLocalExport.MaximumBytes || count != stream.Length)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The bounded result transfer is incomplete.");
            }
            await RequirePublishedAsync().ConfigureAwait(false);
            await CompanionLocalExport.WriteAsync(
                path, ByteString.From(buffer.AsSpan(0, checked((int)count))), RequireCurrent, lifetime.Token)
                .ConfigureAwait(false);
            return count;
        }
    }
}
