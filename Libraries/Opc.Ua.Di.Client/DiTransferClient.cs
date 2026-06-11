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
    /// <c>TransferServicesType</c> facet. Composes (does <em>not</em>
    /// inherit) the source-generated
    /// <see cref="Opc.Ua.Di.TransferServicesTypeClient"/> proxy to
    /// invoke <c>TransferToDevice</c> / <c>TransferFromDevice</c> and
    /// poll <c>FetchTransferResultData</c>, decoding each chunk and
    /// yielding parameter entries until the server flags
    /// <c>EndOfResults</c>.
    /// </summary>
    public sealed class DiTransferClient
    {
        private TransferServicesTypeClient? m_proxy;

        /// <summary>
        /// Creates a new transfer client rooted at the supplied
        /// <c>TransferServicesType</c> instance.
        /// </summary>
        public DiTransferClient(
            ISession session,
            NodeId transferServicesNodeId,
            ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (transferServicesNodeId.IsNull)
            {
                throw new ArgumentException(
                    "TransferServices NodeId is required.",
                    nameof(transferServicesNodeId));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

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

        private TransferServicesTypeClient Proxy
        {
            get
            {
                return m_proxy ??= new TransferServicesTypeClient(
                    Session, TransferServicesNodeId, Telemetry);
            }
        }

        /// <summary>
        /// Invokes <c>TransferToDevice</c>. Returns the transfer ID
        /// that subsequent <see cref="StreamAsync"/> calls poll on.
        /// The generated proxy returns the tuple
        /// <c>(transferID, initTransferStatus)</c>; this wrapper
        /// surfaces only the transfer ID for caller convenience and
        /// raises on bad initialisation status.
        /// </summary>
        public async ValueTask<int> TransferToDeviceAsync(CancellationToken ct = default)
        {
            (int transferId, int initStatus) = await Proxy
                .TransferToDeviceAsync(ct)
                .ConfigureAwait(false);
            ThrowOnInitFailure(initStatus, "TransferToDevice");
            return transferId;
        }

        /// <summary>
        /// Invokes <c>TransferFromDevice</c>. Returns the transfer ID
        /// that subsequent <see cref="StreamAsync"/> calls poll on.
        /// The generated proxy returns the tuple
        /// <c>(transferID, initTransferStatus)</c>; this wrapper
        /// surfaces only the transfer ID for caller convenience and
        /// raises on bad initialisation status.
        /// </summary>
        public async ValueTask<int> TransferFromDeviceAsync(CancellationToken ct = default)
        {
            (int transferId, int initStatus) = await Proxy
                .TransferFromDeviceAsync(ct)
                .ConfigureAwait(false);
            ThrowOnInitFailure(initStatus, "TransferFromDevice");
            return transferId;
        }

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
            int sequence = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                ExtensionObject chunkPayload = await Proxy
                    .FetchTransferResultDataAsync(
                        transferId,
                        sequence,
                        maxResultsPerChunk,
                        omitGoodResults,
                        ct)
                    .ConfigureAwait(false);

                FetchChunk chunk = DecodeChunk(chunkPayload);

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

        private static void ThrowOnInitFailure(int initStatus, string method)
        {
            if (initStatus != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    $"{method} initialisation failed (status {initStatus}).");
            }
        }

        private static FetchChunk DecodeChunk(ExtensionObject payload)
        {
            // The generated FetchTransferResultData proxy returns an
            // ExtensionObject wrapping either TransferResultDataDataType
            // or TransferResultErrorDataType per OPC 10000-100 §10.4;
            // unwrap and dispatch on the subtype.
            if (payload.IsNull ||
                !payload.TryGetValue(out IEncodeable? body) ||
                body is null)
            {
                return new FetchChunk(EndOfResults: true);
            }
            if (body is Opc.Ua.Di.TransferResultErrorDataType err)
            {
                return new FetchChunk(
                    IsError: true,
                    ErrorStatus: new StatusCode((uint)err.Status));
            }
            if (body is Opc.Ua.Di.TransferResultDataDataType data)
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
                    NextSequenceNumber: data.SequenceNumber + 1,
                    EndOfResults: data.EndOfResults,
                    Entries: entries);
            }
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
