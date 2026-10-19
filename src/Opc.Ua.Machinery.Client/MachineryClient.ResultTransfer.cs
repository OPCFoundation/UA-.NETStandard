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
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Machinery.Result;
using ResultBrowseNames = Opc.Ua.Machinery.Result.BrowseNames;

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        /// <summary>
        /// Downloads exactly the selected result to a caller-owned stream,
        /// enforcing the byte limit while reading rather than after buffering.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Resolves only the machine's ResultManagement/ResultTransfer path and
        /// sends the supplied ResultId in ResultTransferOptionsDataType. No
        /// result enumeration, URI retrieval or method replay is performed.
        /// </para>
        /// <para>
        /// Uses at most 1024 reads of at most 64 KiB, including a one-byte
        /// end-of-file probe at the byte limit. Zero permits only an empty
        /// payload. The acquired handle is closed under an independent
        /// five-second deadline. The session and destination remain owned by
        /// the caller. A failed download may leave partial destination content,
        /// which the caller must discard.
        /// </para>
        /// </remarks>
        /// <param name="machine">The machine that produced the result.</param>
        /// <param name="resultId">The exact, non-blank identifier to download.</param>
        /// <param name="destination">The writable, caller-owned destination.</param>
        /// <param name="maxBytes">The non-negative maximum payload size.</param>
        /// <param name="cancellationToken">Cancels the download, but not cleanup.</param>
        /// <returns>The bytes written after successful cleanup.</returns>
        /// <exception cref="ArgumentNullException">The destination is null.</exception>
        /// <exception cref="ArgumentException">
        /// The machine is null, the result identifier is blank, or the destination is not writable.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">The byte limit is negative.</exception>
        /// <exception cref="ServiceResultException">
        /// A limit is exceeded, a response is invalid, a session is replaced,
        /// the transfer path is absent, or a service or method has a non-Good status.
        /// </exception>
        /// <exception cref="IOException">Generation or destination writing fails.</exception>
        /// <exception cref="OperationCanceledException">The download or cleanup is cancelled.</exception>
        /// <exception cref="AggregateException">
        /// Download and cleanup both fail, in that order in InnerExceptions.
        /// </exception>
        public async ValueTask<long> DownloadResultAsync(
            NodeId machine,
            string resultId,
            Stream destination,
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            if (machine.IsNull)
            {
                throw new ArgumentException("A machine NodeId is required.", nameof(machine));
            }
            if (string.IsNullOrWhiteSpace(resultId))
            {
                throw new ArgumentException("A result identifier is required.", nameof(resultId));
            }
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (!destination.CanWrite)
            {
                throw new ArgumentException("The destination must be writable.", nameof(destination));
            }
            if (maxBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }
            cancellationToken.ThrowIfCancellationRequested();

            SessionRequestContext? requestContext = Session is ManagedSession
                ? await SessionRequestContext.CaptureAsync(Session, cancellationToken).ConfigureAwait(false)
                : null;
            try
            {
                MachineryClient scoped = requestContext is null
                    ? this
                    : new MachineryClient(requestContext.Session, Telemetry);
                NodeId sessionId = scoped.Session.SessionId;
                NodeId transfer = await scoped.ResolveBoundedResultTransferAsync(machine, cancellationToken)
                    .ConfigureAwait(false);
                scoped.VerifySessionIdentity(sessionId);
                var client = new TemporaryFileTransferClient(scoped.Session, transfer);
                var options = new ResultTransferOptionsDataType { ResultId = resultId };
                return await client.CopyToAsync(
                    Variant.FromStructure(options),
                    destination,
                    maxBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (requestContext is not null)
                {
                    await requestContext.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async ValueTask<NodeId> ResolveBoundedResultTransferAsync(
            NodeId machine,
            CancellationToken ct)
        {
            if (!TryGetNamespaceIndex(
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                out ushort resultNamespaceIndex))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The server does not publish the Machinery Result namespace.");
            }
            ArrayOf<BrowsePath> paths =
            [
                new BrowsePath
                {
                    StartingNode = machine,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(
                                    ResultBrowseNames.ResultManagement,
                                    resultNamespaceIndex)
                            },
                            new RelativePathElement
                            {
                                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(
                                    ResultBrowseNames.ResultTransfer,
                                    resultNamespaceIndex)
                            }
                        ]
                    }
                }
            ];
            TranslateBrowsePathsToNodeIdsResponse? response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, ct).ConfigureAwait(false);
            if (response?.ResponseHeader is null)
            {
                throw new ServiceResultException(StatusCodes.BadUnknownResponse);
            }
            if (!StatusCode.IsGood(response.ResponseHeader.ServiceResult))
            {
                throw new ServiceResultException(response.ResponseHeader.ServiceResult);
            }
            ClientBase.ValidateResponse(response.Results, paths);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, paths);
            BrowsePathResult result = response.Results[0];
            if (!StatusCode.IsGood(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            if (result.Targets.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }
            if (result.Targets.Count != 1 ||
                result.Targets[0].RemainingPathIndex != uint.MaxValue ||
                result.Targets[0].TargetId.ServerIndex != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "ResultTransfer must resolve to exactly one complete local target.");
            }
            NodeId transfer = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);
            if (transfer.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
            }
            return transfer;
        }
    }
}
