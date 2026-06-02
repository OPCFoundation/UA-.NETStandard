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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// Client-side wrapper for the OPC 10000-100 §10.4
    /// <c>TransferServicesType</c> facet. Invokes the
    /// <c>TransferToDevice</c> / <c>TransferFromDevice</c> methods to
    /// kick off an asynchronous transfer, then polls
    /// <c>FetchTransferResultData</c> in a loop, decoding each chunk
    /// and yielding parameter entries until the server flags
    /// <c>EndOfResults</c>.
    /// </summary>
    public sealed class DiTransferClient
    {
        /// <summary>
        /// Creates a new transfer client rooted at the supplied
        /// <c>TransferServicesType</c> instance.
        /// </summary>
        public DiTransferClient(
            ISession session,
            NodeId transferServicesNodeId,
            ITelemetryContext telemetry)
        {
            if (session is null) { throw new ArgumentNullException(nameof(session)); }
            if (transferServicesNodeId.IsNull)
            {
                throw new ArgumentException(
                    "TransferServices NodeId is required.",
                    nameof(transferServicesNodeId));
            }
            if (telemetry is null) { throw new ArgumentNullException(nameof(telemetry)); }

            Session = session;
            TransferServicesNodeId = transferServicesNodeId;
            Telemetry = telemetry;
        }

        /// <summary>
        /// The owning session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// The NodeId of the <c>TransferServicesType</c> instance.
        /// </summary>
        public NodeId TransferServicesNodeId { get; }

        /// <summary>
        /// Telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Invokes <c>TransferToDevice</c>. Returns the transfer ID
        /// that subsequent <see cref="StreamAsync"/> calls poll on.
        /// </summary>
        public ValueTask<int> TransferToDeviceAsync(CancellationToken ct = default)
            => InitiateTransferAsync(
                Opc.Ua.Di.Methods.TransferServicesType_TransferToDevice, ct);

        /// <summary>
        /// Invokes <c>TransferFromDevice</c>. Returns the transfer ID
        /// that subsequent <see cref="StreamAsync"/> calls poll on.
        /// </summary>
        public ValueTask<int> TransferFromDeviceAsync(CancellationToken ct = default)
            => InitiateTransferAsync(
                Opc.Ua.Di.Methods.TransferServicesType_TransferFromDevice, ct);

        /// <summary>
        /// Streams parameter entries from a previously-initiated
        /// transfer by calling <c>FetchTransferResultData</c> in a
        /// loop with monotonically-increasing sequence numbers until
        /// the server flags <c>EndOfResults</c>.
        /// </summary>
        /// <param name="transferId">
        /// The transfer ID returned by
        /// <see cref="TransferToDeviceAsync"/> or
        /// <see cref="TransferFromDeviceAsync"/>.
        /// </param>
        /// <param name="maxResultsPerChunk">
        /// Maximum entries per server-side chunk. 0 = unlimited.
        /// </param>
        /// <param name="omitGoodResults">
        /// When true, ask the server to skip <c>Good</c> entries.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        public async IAsyncEnumerable<ParameterFetchEntry> StreamAsync(
            int transferId,
            int maxResultsPerChunk = 50,
            bool omitGoodResults = false,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            NodeId methodId = NodeId.Create(
                Opc.Ua.Di.Methods.TransferServicesType_FetchTransferResultData,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                Session.NamespaceUris);

            int sequence = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                CallMethodRequest request = new CallMethodRequest
                {
                    ObjectId = TransferServicesNodeId,
                    MethodId = methodId,
                    InputArguments = new Variant[]
                    {
                        new Variant(transferId),
                        new Variant(sequence),
                        new Variant(maxResultsPerChunk),
                        new Variant(omitGoodResults)
                    }.ToArrayOf()
                };

                CallResponse response = await Session
                    .CallAsync(
                        requestHeader: null,
                        methodsToCall: new[] { request }.ToArrayOf(),
                        ct: ct)
                    .ConfigureAwait(false);

                if (response.Results.Count == 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "FetchTransferResultData returned no results.");
                }

                CallMethodResult result = response.Results[0];
                if (StatusCode.IsBad(result.StatusCode))
                {
                    throw new ServiceResultException(
                        result.StatusCode,
                        $"FetchTransferResultData failed with status {result.StatusCode}.");
                }

                if (result.OutputArguments.Count == 0)
                {
                    yield break;
                }

                // The output is a FetchResultDataType ExtensionObject
                // (TransferResultDataDataType or TransferResultErrorDataType).
                // We decode generically: an error subtype carries Status +
                // Diagnostics, a data subtype carries SequenceNumber +
                // EndOfResults + ParameterDefs.
                Variant payload = result.OutputArguments[0];
                FetchChunk chunk = DecodeChunk(payload);

                if (chunk.IsError)
                {
                    throw new ServiceResultException(
                        chunk.ErrorStatus,
                        "Transfer ended with error status from server.");
                }

                if (chunk.Entries != null)
                {
                    foreach (ParameterFetchEntry entry in chunk.Entries)
                    {
                        yield return entry;
                    }
                }

                if (chunk.EndOfResults)
                {
                    yield break;
                }

                sequence = chunk.NextSequenceNumber;
            }
        }

        private async ValueTask<int> InitiateTransferAsync(
            uint methodTypeId,
            CancellationToken ct)
        {
            NodeId methodId = NodeId.Create(
                methodTypeId,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                Session.NamespaceUris);

            CallMethodRequest request = new CallMethodRequest
            {
                ObjectId = TransferServicesNodeId,
                MethodId = methodId,
                InputArguments = Array.Empty<Variant>().ToArrayOf()
            };

            CallResponse response = await Session
                .CallAsync(
                    requestHeader: null,
                    methodsToCall: new[] { request }.ToArrayOf(),
                    ct: ct)
                .ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Initiate-transfer call returned no results.");
            }

            CallMethodResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(
                    result.StatusCode,
                    $"Initiate-transfer call returned bad status {result.StatusCode}.");
            }

            if (result.OutputArguments.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Initiate-transfer returned no output arguments.");
            }

            if (!result.OutputArguments[0].TryGetValue(out int transferId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    "Initiate-transfer transferId output was not an Int32.");
            }
            return transferId;
        }

        private static FetchChunk DecodeChunk(Variant payload)
        {
            object? boxed = payload.AsBoxedObject();
            // The wire form is an ExtensionObject carrying the concrete
            // subtype's IEncodeable body; unwrap before pattern matching.
            if (boxed is ExtensionObject ext)
            {
                if (ext.TryGetValue(out IEncodeable? encodable))
                {
                    boxed = encodable;
                }
            }
            if (boxed is null)
            {
                return new FetchChunk(EndOfResults: true);
            }

            // The generated proxies for TransferResultDataDataType
            // and TransferResultErrorDataType live in Opc.Ua.Di and
            // implement IEncodeable. We avoid the typed dependency
            // here by reflecting over the field names via a small
            // helper — the spec field layout is stable.
            if (boxed is Opc.Ua.Di.TransferResultErrorDataType err)
            {
                return new FetchChunk(
                    IsError: true,
                    ErrorStatus: new StatusCode((uint)err.Status));
            }
            if (boxed is Opc.Ua.Di.TransferResultDataDataType data)
            {
                var entries = new List<ParameterFetchEntry>(data.ParameterDefs.Count);
                for (int i = 0; i < data.ParameterDefs.Count; i++)
                {
                    Opc.Ua.Di.ParameterResultDataType p = data.ParameterDefs[i];
                    QualifiedName[] nodePath = p.NodePath.ToArray() ?? Array.Empty<QualifiedName>();
                    entries.Add(new ParameterFetchEntry(
                        NodePath: nodePath,
                        StatusCode: p.StatusCode));
                }
                return new FetchChunk(
                    Entries: entries,
                    EndOfResults: data.EndOfResults,
                    NextSequenceNumber: data.SequenceNumber);
            }

            // Unknown subtype: treat as end-of-results.
            return new FetchChunk(EndOfResults: true);
        }

        private readonly record struct FetchChunk(
            List<ParameterFetchEntry>? Entries = null,
            bool EndOfResults = false,
            int NextSequenceNumber = 0,
            bool IsError = false,
            StatusCode ErrorStatus = default);
    }

    /// <summary>
    /// One parameter entry yielded by
    /// <see cref="DiTransferClient.StreamAsync"/>.
    /// </summary>
    public sealed record ParameterFetchEntry(
        QualifiedName[] NodePath,
        StatusCode StatusCode);
}
