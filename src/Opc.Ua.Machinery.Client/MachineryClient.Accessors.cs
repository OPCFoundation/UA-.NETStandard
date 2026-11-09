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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.ISA95.Client;
using Opc.Ua.Machinery.Result;
using JobsBrowseNames = Opc.Ua.Machinery.Jobs.BrowseNames;
using ResultBrowseNames = Opc.Ua.Machinery.Result.BrowseNames;

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        /// <summary>
        /// The buffer size used while draining a result download.
        /// </summary>
        private const int DownloadChunkSize = 64 * 1024;

        /// <summary>
        /// Opens the OPC 40001-3 job-control surface of a machine, or
        /// <see langword="null"/> when the machine publishes no
        /// <c>JobManagement</c>.
        /// </summary>
        /// <remarks>
        /// The eleven job verbs belong to the ISA-95 Job Control V2 model that
        /// OPC 40001-3 composes, so the returned client is the ISA-95 one —
        /// the same type a stand-alone ISA-95 server is driven with.
        /// </remarks>
        /// <param name="machine">The machine to drive.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<Isa95JobControlV2Client?> JobManagementAsync(
            NodeId machine,
            CancellationToken cancellationToken = default)
        {
            MachineryJobManagementEndpoints endpoints = await GetJobManagementEndpointsAsync(
                machine, cancellationToken)
                .ConfigureAwait(false);
            NodeId control = endpoints.JobOrderReceiverId;
            if (control.IsNull)
            {
                return null;
            }
            NodeId results = endpoints.JobResponseProviderId;

            // Preserve the legacy three-role client's fallback identifiers.
            // New callers should use GetJobManagementEndpointsAsync and per-role
            // ISA-95 proxies instead: Machinery does not define a response receiver.
            return new Isa95JobControlV2Client(
                Session,
                control,
                results.IsNull ? control : results,
                control,
                Telemetry);
        }

        /// <summary>
        /// Resolves the actual OPC 40001-3 job-management endpoints without
        /// substituting another role when an optional endpoint is absent.
        /// </summary>
        /// <remarks>
        /// Absent objects have <see cref="NodeId.Null"/> identifiers. Machinery
        /// composes an ISA-95 V2 order receiver and response provider, but no
        /// response receiver. Use the returned identifiers with the corresponding
        /// generated ISA-95 role proxies.
        /// </remarks>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<MachineryJobManagementEndpoints> GetJobManagementEndpointsAsync(
            NodeId machine,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetNamespaceIndex(
                Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs,
                out ushort jobsNamespaceIndex))
            {
                return new MachineryJobManagementEndpoints(default, default, default);
            }
            NodeId management = await ResolveChildAsync(
                machine,
                new QualifiedName(JobsBrowseNames.JobManagement, jobsNamespaceIndex),
                cancellationToken).ConfigureAwait(false);
            if (management.IsNull)
            {
                return new MachineryJobManagementEndpoints(default, default, default);
            }
            NodeId receiver = await ResolveChildAsync(
                management,
                new QualifiedName(JobsBrowseNames.JobOrderControl, jobsNamespaceIndex),
                cancellationToken).ConfigureAwait(false);
            NodeId provider = await ResolveChildAsync(
                management,
                new QualifiedName(JobsBrowseNames.JobOrderResults, jobsNamespaceIndex),
                cancellationToken).ConfigureAwait(false);
            return new MachineryJobManagementEndpoints(management, receiver, provider);
        }

        /// <summary>
        /// Resolves a machine's OPC 40001-101 <c>ResultManagement</c> object, or
        /// <see cref="NodeId.Null"/> when it publishes none.
        /// </summary>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolveResultManagementAsync(
            NodeId machine,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetNamespaceIndex(
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                out ushort resultNamespaceIndex))
            {
                return new ValueTask<NodeId>(NodeId.Null);
            }
            return ResolveChildAsync(
                machine,
                new QualifiedName(ResultBrowseNames.ResultManagement, resultNamespaceIndex),
                cancellationToken);
        }

        /// <summary>
        /// Opens the typed proxy for a machine's <c>ResultManagement</c>
        /// object, or <see langword="null"/> when it publishes none.
        /// </summary>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ResultManagementTypeClient?> ResultManagementAsync(
            NodeId machine,
            CancellationToken cancellationToken = default)
        {
            NodeId management = await ResolveResultManagementAsync(machine, cancellationToken)
                .ConfigureAwait(false);
            return management.IsNull
                ? null
                : new ResultManagementTypeClient(Session, management, Telemetry);
        }

        /// <summary>
        /// Downloads the payload of one result through the OPC 40001-101
        /// transfer path.
        /// </summary>
        /// <remarks>
        /// <c>ResultTransferType</c> derives from
        /// <c>TemporaryFileTransferType</c>, so the download is the standard
        /// OPC 10000-5 sequence: <c>GenerateFileForRead</c> hands back a
        /// transient file object, and the payload is read from it. The
        /// <c>ResultId</c> travels in the method's <c>generateOptions</c> as a
        /// <c>ResultTransferOptionsDataType</c>.
        /// </remarks>
        /// <param name="machine">The machine that produced the result.</param>
        /// <param name="resultId">The identifier of the result to download.</param>
        /// <param name="cancellationToken">Cancels the download.</param>
        /// <returns>The downloaded payload.</returns>
        /// <exception cref="ServiceResultException">
        /// The machine publishes no result transfer, or the result carries no
        /// transferable data.
        /// </exception>
        public async ValueTask<ByteString> DownloadResultAsync(
            NodeId machine,
            string resultId,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await DownloadResultAsync(machine, resultId, buffer, cancellationToken).ConfigureAwait(false);
            return new ByteString(buffer.ToArray());
        }

        /// <summary>
        /// Downloads a result into a caller-owned writable stream without
        /// buffering the whole payload in memory.
        /// </summary>
        /// <param name="machine">The machine that produced the result.</param>
        /// <param name="resultId">The result identifier.</param>
        /// <param name="destination">The writable stream, which remains open.</param>
        /// <param name="cancellationToken">Cancels the download.</param>
        public async ValueTask DownloadResultAsync(
            NodeId machine,
            string resultId,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            destination = destination ?? throw new ArgumentNullException(nameof(destination));
            if (!destination.CanWrite)
            {
                throw new ArgumentException("The destination must be writable.", nameof(destination));
            }
            UaFileStream stream = await OpenResultStreamAsync(machine, resultId, cancellationToken)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable _ = stream.ConfigureAwait(false);
            await stream.CopyToAsync(destination, DownloadChunkSize, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens the standard temporary-file result transfer for incremental
        /// reading. The caller must asynchronously dispose the returned stream.
        /// </summary>
        /// <param name="machine">The machine that produced the result.</param>
        /// <param name="resultId">The result identifier.</param>
        /// <param name="cancellationToken">Cancels opening the transfer.</param>
        /// <returns>The open, caller-owned result stream.</returns>
        public async ValueTask<UaFileStream> OpenResultStreamAsync(
            NodeId machine,
            string resultId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(resultId))
            {
                throw new ArgumentException(
                    "A result identifier is required.",
                    nameof(resultId));
            }

            NodeId management = await ResolveResultManagementAsync(machine, cancellationToken)
                .ConfigureAwait(false);
            if (management.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "The machine publishes no OPC 40001-101 ResultManagement object.");
            }

            ushort resultNamespaceIndex = NamespaceIndexOf(
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult);
            NodeId transfer = await ResolveChildAsync(
                management,
                new QualifiedName(ResultBrowseNames.ResultTransfer, resultNamespaceIndex),
                cancellationToken).ConfigureAwait(false);
            if (transfer.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "The machine's ResultManagement publishes no ResultTransfer object.");
            }

            var client = new TemporaryFileTransferClient(Session, transfer);
            var options = new ResultTransferOptionsDataType { ResultId = resultId };
            return await client
                .GenerateFileForReadAsync(
                    Variant.FromStructure(options),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
