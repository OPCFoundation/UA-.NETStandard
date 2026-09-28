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
            ushort jobsNamespaceIndex = NamespaceIndexOf(
                Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs);
            NodeId control = await ResolvePathAsync(
                machine,
                cancellationToken,
                new QualifiedName(JobsBrowseNames.JobManagement, jobsNamespaceIndex),
                new QualifiedName(JobsBrowseNames.JobOrderControl, jobsNamespaceIndex))
                .ConfigureAwait(false);
            if (control.IsNull)
            {
                return null;
            }
            NodeId results = await ResolvePathAsync(
                machine,
                cancellationToken,
                new QualifiedName(JobsBrowseNames.JobManagement, jobsNamespaceIndex),
                new QualifiedName(JobsBrowseNames.JobOrderResults, jobsNamespaceIndex))
                .ConfigureAwait(false);

            // OPC 40001-3 composes only the receiver and the response
            // provider; there is no response receiver below a machine, so the
            // receiver NodeId stands in for it and the corresponding client
            // methods are simply never called.
            return new Isa95JobControlV2Client(
                Session,
                control,
                results.IsNull ? control : results,
                control,
                Telemetry);
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
            ushort resultNamespaceIndex = NamespaceIndexOf(
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult);
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
            UaFileStream stream = await client
                .GenerateFileForReadAsync(
                    Variant.FromStructure(options),
                    cancellationToken)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable _ = stream.ConfigureAwait(false);

            using var buffer = new MemoryStream();
            // The three-argument overload is the one netstandard2.0 and the
            // .NET Framework targets have; the two-argument cancellable one is
            // net5.0 and later only.
            await stream.CopyToAsync(buffer, DownloadChunkSize, cancellationToken)
                .ConfigureAwait(false);
            return new ByteString(buffer.ToArray());
        }
    }
}
