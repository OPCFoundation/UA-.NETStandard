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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.AutoID.Client
{
    public sealed partial class AutoIdClient
    {
        /// <summary>
        /// Reads a reader's nested <c>RuntimeParameters</c> and <c>ScanSettings</c>.
        /// </summary>
        /// <remarks>
        /// Values that the device does not publish or cannot read keep their default and expose
        /// their individual status in the result. <c>Duration</c> is expressed in milliseconds;
        /// <c>CodeTypes</c> contains the standard enumeration indexes.
        /// </remarks>
        /// <param name="reader">The reader.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="ServiceResultException">The server rejected a browse or read.</exception>
        public async ValueTask<AutoIdScanParameterReadResult> ReadScanParametersAsync(
            AutoIdReaderEntry reader,
            CancellationToken cancellationToken = default)
        {
            RequireReader(reader);
            var nodes = await FindScanParameterNodesAsync(reader.NodeId, cancellationToken).ConfigureAwait(false);
            var reads = new List<ReadValueId>(nodes.Count);
            var slots = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach ((string name, NodeId nodeId) in nodes)
            {
                if (!nodeId.IsNull)
                {
                    slots.Add(name, reads.Count);
                    reads.Add(new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value });
                }
            }

            ReadResponse response = reads.Count == 0
                ? new ReadResponse { Results = [] }
                : await Session.ReadAsync(
                    requestHeader: null,
                    maxAge: 0,
                    timestampsToReturn: TimestampsToReturn.Neither,
                    nodesToRead: reads.ToArray().ToArrayOf(),
                    ct: cancellationToken).ConfigureAwait(false);
            ValidateResultCount(response.Results.Count, reads.Count, "read");

            DataValue Value(string name)
            {
                return slots.TryGetValue(name, out int slot)
                    ? response.Results[slot]
                    : DataValue.FromStatusCode(StatusCodes.BadNotFound);
            }

            DataValue durationValue = Value("Duration");
            DataValue cyclesValue = Value("Cycles");
            DataValue dataAvailableValue = Value("DataAvailable");
            DataValue codeTypeValue = Value("CodeType");
            DataValue codeTypesValue = Value(BrowseNames.CodeTypes);
            DataValue namesValue = Value(Opc.Ua.BrowseNames.EnumStrings);

            bool durationRead = durationValue.WrappedValue.TryGetValue(out double duration);
            bool cyclesRead = cyclesValue.WrappedValue.TryGetValue(out int cycles);
            bool dataAvailableRead = dataAvailableValue.WrappedValue.TryGetValue(out bool dataAvailable);
            bool codeTypeRead = codeTypeValue.WrappedValue.TryGetValue(out string codeType);
            bool codeTypesRead = codeTypesValue.WrappedValue.TryGetValue(out ArrayOf<uint> codeTypes);
            var parameters = new AutoIdScanParameters
            {
                Duration = StatusCode.IsBad(durationValue.StatusCode) ? default : duration,
                Cycles = StatusCode.IsBad(cyclesValue.StatusCode) ? default : cycles,
                DataAvailable = StatusCode.IsNotBad(dataAvailableValue.StatusCode) && dataAvailable,
                CodeType = StatusCode.IsBad(codeTypeValue.StatusCode) ? string.Empty : codeType ?? string.Empty,
                CodeTypes = StatusCode.IsBad(codeTypesValue.StatusCode) ? default : codeTypes
            };

            return new AutoIdScanParameterReadResult(
                parameters,
                new AutoIdScanParameterStatus
                {
                    Duration = TypedStatus(durationValue, durationRead),
                    Cycles = TypedStatus(cyclesValue, cyclesRead),
                    DataAvailable = TypedStatus(dataAvailableValue, dataAvailableRead),
                    CodeType = TypedStatus(codeTypeValue, codeTypeRead),
                    CodeTypes = TypedStatus(codeTypesValue, codeTypesRead)
                })
            {
                CodeTypeNames = StatusCode.IsNotBad(namesValue.StatusCode) &&
                    namesValue.WrappedValue.TryGetValue(out ArrayOf<LocalizedText> names) ? names : default,
                CodeTypeNamesStatus = TypedStatus(namesValue,
                    namesValue.WrappedValue.TryGetValue(out ArrayOf<LocalizedText> _))
            };
        }

        /// <summary>
        /// Writes a reader's <c>RuntimeParameters/ScanSettings</c> values in one service call.
        /// </summary>
        /// <remarks>
        /// Every published parameter is written independently by the server. Missing nodes are
        /// reported as <see cref="StatusCodes.BadNotFound"/> and individual write statuses are
        /// returned rather than collapsed into one result. The CodeTypes indexes select entries
        /// from the server-owned EnumStrings property; this call is not a transaction.
        /// </remarks>
        /// <param name="reader">The reader.</param>
        /// <param name="parameters">The values to write.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The status of each parameter write.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
        /// <exception cref="ServiceResultException">The server rejected a browse or write request.</exception>
        public async ValueTask<AutoIdScanParameterStatus> WriteScanParametersAsync(
            AutoIdReaderEntry reader,
            AutoIdScanParameters parameters,
            CancellationToken cancellationToken = default)
        {
            RequireReader(reader);
            if (parameters is null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            var nodes = await FindScanParameterNodesAsync(reader.NodeId, cancellationToken).ConfigureAwait(false);
            var writes = new List<WriteValue>(5);
            var slots = new Dictionary<string, int>(StringComparer.Ordinal);
            AddWrite("Duration", Variant.From(parameters.Duration));
            AddWrite("Cycles", Variant.From(parameters.Cycles));
            AddWrite("DataAvailable", Variant.From(parameters.DataAvailable));
            AddWrite("CodeType", Variant.From(parameters.CodeType));
            AddWrite(BrowseNames.CodeTypes, Variant.From(parameters.CodeTypes));

            void AddWrite(string name, Variant value)
            {
                NodeId nodeId = nodes[name];
                if (!nodeId.IsNull)
                {
                    slots.Add(name, writes.Count);
                    writes.Add(new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(value)
                    });
                }
            }

            WriteResponse response = writes.Count == 0
                ? new WriteResponse { Results = [] }
                : await Session.WriteAsync(
                    requestHeader: null,
                    nodesToWrite: writes.ToArray().ToArrayOf(),
                    ct: cancellationToken).ConfigureAwait(false);
            ValidateResultCount(response.Results.Count, writes.Count, "write");
            StatusCode Status(string name)
            {
                return slots.TryGetValue(name, out int slot)
                    ? response.Results[slot]
                    : StatusCodes.BadNotFound;
            }
            return new AutoIdScanParameterStatus
            {
                Duration = Status("Duration"),
                Cycles = Status("Cycles"),
                DataAvailable = Status("DataAvailable"),
                CodeType = Status("CodeType"),
                CodeTypes = Status(BrowseNames.CodeTypes)
            };
        }

        /// <summary>
        /// Reads the current <c>ScanActive</c> control state of a reader.
        /// </summary>
        /// <param name="reader">The reader.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="ServiceResultException">The server rejected a browse or read.</exception>
        public async ValueTask<AutoIdScanActiveReadResult> ReadScanActiveAsync(
            AutoIdReaderEntry reader,
            CancellationToken cancellationToken = default)
        {
            RequireReader(reader);
            NodeId nodeId = await FindChildIdAsync(
                reader.NodeId,
                BrowseNames.ScanActive,
                NodeClass.Variable,
                cancellationToken).ConfigureAwait(false);
            if (nodeId.IsNull)
            {
                return new AutoIdScanActiveReadResult(false, StatusCodes.BadNotFound);
            }
            ReadResponse response = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: new[]
                {
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);
            ValidateResultCount(response.Results.Count, 1, "read");
            DataValue value = response.Results[0];
            bool isTyped = value.WrappedValue.TryGetValue(out bool active);
            return new AutoIdScanActiveReadResult(
                StatusCode.IsNotBad(value.StatusCode) && active, TypedStatus(value, isTyped));
        }

        /// <summary>
        /// Sets the <c>ScanActive</c> control state of a reader.
        /// </summary>
        /// <remarks>
        /// This writes the standard variable. The server owns the transition and reports whether
        /// the request was accepted; a bad per-node status is returned to the caller.
        /// </remarks>
        /// <param name="reader">The reader.</param>
        /// <param name="active">Whether to start or stop variable-driven scanning.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The server's write status, or <see cref="StatusCodes.BadNotFound"/> if absent.</returns>
        /// <exception cref="ServiceResultException">The server rejected the browse or write request.</exception>
        public async ValueTask<StatusCode> SetScanActiveAsync(
            AutoIdReaderEntry reader,
            bool active,
            CancellationToken cancellationToken = default)
        {
            RequireReader(reader);
            NodeId nodeId = await FindChildIdAsync(
                reader.NodeId,
                BrowseNames.ScanActive,
                NodeClass.Variable,
                cancellationToken).ConfigureAwait(false);
            if (nodeId.IsNull)
            {
                return StatusCodes.BadNotFound;
            }
            WriteResponse response = await Session.WriteAsync(
                requestHeader: null,
                nodesToWrite: new[]
                {
                    new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(active))
                    }
                }.ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);
            ValidateResultCount(response.Results.Count, 1, "write");
            return response.Results[0];
        }

        /// <summary>
        /// Observes ScanActive through a caller-owned streaming subscription.
        /// Disposing the enumerator removes its monitored item, not the shared session.
        /// </summary>
        /// <param name="streaming">The subscription to borrow for this monitored item.</param>
        /// <param name="reader">The reader.</param>
        /// <param name="cancellationToken">Ends the enumeration and removes its monitored item.</param>
        /// <exception cref="ServiceResultException">
        /// The reader omits ScanActive or the server rejects the request.
        /// </exception>
        public IAsyncEnumerable<AutoIdScanActiveReadResult> ObserveScanActiveAsync(
            IStreamingSubscription streaming,
            AutoIdReaderEntry reader,
            CancellationToken cancellationToken = default)
        {
            if (streaming is null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            RequireReader(reader);
            return ObserveScanActiveCoreAsync(streaming, reader, cancellationToken);
        }

        /// <summary>
        /// Resolves the control variable and enumerates its data changes.
        /// </summary>
        private async IAsyncEnumerable<AutoIdScanActiveReadResult> ObserveScanActiveCoreAsync(
            IStreamingSubscription streaming,
            AutoIdReaderEntry reader,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId node = await FindChildIdAsync(reader.NodeId, BrowseNames.ScanActive,
                NodeClass.Variable, cancellationToken).ConfigureAwait(false);
            if (node.IsNull)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotFound, "The reader omits ScanActive.");
            }
            await foreach (DataValueChange change in streaming.SubscribeDataChangesAsync(
                node, ct: cancellationToken).ConfigureAwait(false))
            {
                bool typed = change.Value.WrappedValue.TryGetValue(out bool active);
                yield return new AutoIdScanActiveReadResult(
                    StatusCode.IsNotBad(change.Value.StatusCode) && active, TypedStatus(change.Value, typed));
            }
        }

        /// <summary>
        /// Resolves the standard nested parameter paths without browsing vendor namespaces.
        /// </summary>
        private async ValueTask<Dictionary<string, NodeId>> FindScanParameterNodesAsync(
            NodeId readerId,
            CancellationToken cancellationToken)
        {
            NodeId runtimeParameters = await FindChildIdAsync(
                readerId,
                BrowseNames.RuntimeParameters,
                NodeClass.Object,
                cancellationToken).ConfigureAwait(false);
            NodeId scanSettings = runtimeParameters.IsNull
                ? NodeId.Null
                : await FindChildIdAsync(
                    runtimeParameters,
                    BrowseNames.ScanSettings,
                    NodeClass.Object,
                    cancellationToken).ConfigureAwait(false);
            var result = new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["Duration"] = NodeId.Null,
                ["Cycles"] = NodeId.Null,
                ["DataAvailable"] = NodeId.Null,
                ["CodeType"] = NodeId.Null,
                [BrowseNames.CodeTypes] = NodeId.Null,
                [Opc.Ua.BrowseNames.EnumStrings] = NodeId.Null
            };
            if (!runtimeParameters.IsNull)
            {
                result[BrowseNames.CodeTypes] = await FindChildIdAsync(
                    runtimeParameters,
                    BrowseNames.CodeTypes,
                    NodeClass.Variable,
                    cancellationToken).ConfigureAwait(false);
                result[Opc.Ua.BrowseNames.EnumStrings] = await FindChildIdAsync(
                    result[BrowseNames.CodeTypes], Opc.Ua.BrowseNames.EnumStrings,
                    NodeClass.Variable, cancellationToken, namespaceIndex: 0).ConfigureAwait(false);
            }
            if (!scanSettings.IsNull)
            {
                foreach (string name in new[]
                {
                    "Duration",
                    "Cycles",
                    "DataAvailable",
                    "CodeType"
                })
                {
                    result[name] = await FindChildIdAsync(
                        scanSettings,
                        name,
                        NodeClass.Variable,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            return result;
        }

        /// <summary>
        /// Finds one namespace-qualified child, rejecting ambiguous server metadata.
        /// </summary>
        private async ValueTask<NodeId> FindChildIdAsync(
            NodeId parentId,
            string browseName,
            NodeClass nodeClass,
            CancellationToken cancellationToken,
            int namespaceIndex = -1)
        {
            if (parentId.IsNull)
            {
                return NodeId.Null;
            }
            int autoIdNamespace = namespaceIndex < 0
                ? Session.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri) : namespaceIndex;
            if (autoIdNamespace < 0)
            {
                return NodeId.Null;
            }
            ArrayOf<ReferenceDescription> children = await BrowseChildrenAsync(
                parentId,
                nodeClass,
                cancellationToken).ConfigureAwait(false);
            NodeId match = NodeId.Null;
            for (int index = 0; index < children.Count; index++)
            {
                ReferenceDescription child = children[index];
                if (child.BrowseName.NamespaceIndex != autoIdNamespace ||
                    !string.Equals(child.BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    continue;
                }
                var childId = ExpandedNodeId.ToNodeId(child.NodeId, Session.NamespaceUris);
                if (childId.IsNull)
                {
                    continue;
                }
                if (!match.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated,
                        "The reader has multiple {0} children.",
                        browseName);
                }
                match = childId;
            }
            return match;
        }

        /// <summary>
        /// Preserves a bad service status or reports a missing typed value.
        /// </summary>
        private static StatusCode TypedStatus(in DataValue value, bool hasTypedValue)
        {
            return StatusCode.IsBad(value.StatusCode)
                ? value.StatusCode
                : hasTypedValue ? value.StatusCode : StatusCodes.BadTypeMismatch;
        }

        /// <summary>
        /// Rejects service responses that cannot be matched to the requested nodes.
        /// </summary>
        private static void ValidateResultCount(int actual, int expected, string operation)
        {
            if (actual != expected)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "The server returned {0} {1} results for {2} requested values.",
                    actual,
                    operation,
                    expected);
            }
        }

    }
}
