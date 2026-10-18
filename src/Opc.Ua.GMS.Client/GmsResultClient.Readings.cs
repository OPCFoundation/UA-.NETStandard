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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.GMS.Client
{
    public sealed partial class GmsResultClient
    {
        /// <summary>
        /// Reads bounded derived result slots. A null array means the optional Results folder is absent.
        /// </summary>
        public async ValueTask<ArrayOf<GmsResultVariableValue>> ReadPublishedResultsAsync(
            CancellationToken cancellationToken = default)
        {
            GMSResultManagementTypeClient management = await ResultsAsync(cancellationToken).ConfigureAwait(false);
            FolderTypeClient? folder = await management.GetResultsAsync(m_telemetry, cancellationToken).ConfigureAwait(false);
            if (folder == null)
            {
                return default;
            }
            ArrayOf<NodeId> nodes = await BrowseChildrenAsync(
                folder.ObjectId, NodeClass.Variable, m_limits.MaximumResults, cancellationToken).ConfigureAwait(false);
            var result = new List<GmsResultVariableValue>();
            var budget = new ReadBudget();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeId node = nodes[i];
                DataValue value = await ReadValueAsync(node, budget, cancellationToken).ConfigureAwait(false);
                DataValue tools = await ReadMemberAsync(
                    node, BrowseNames.UsedTools, optional: true, budget, cancellationToken).ConfigureAwait(false);
                if (tools.WrappedValue.TryGetValue(out ArrayOf<string> identifiers) &&
                    identifiers.Count > m_limits.MaximumUsedTools)
                {
                    throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                        "The result's UsedTools property exceeds its identifier bound.");
                }
                result.Add(new GmsResultVariableValue(node, value, tools));
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Reads bounded calculated corrections. Null means the optional folder is absent, not a successful empty reading.
        /// </summary>
        public async ValueTask<ArrayOf<GmsCorrectionValues>> ReadCorrectionsAsync(
            CancellationToken cancellationToken = default)
        {
            GMSResultManagementTypeClient management = await ResultsAsync(cancellationToken).ConfigureAwait(false);
            FolderTypeClient? folder = await management.GetCorrectionsFolderAsync(
                m_telemetry, cancellationToken).ConfigureAwait(false);
            if (folder == null)
            {
                return default;
            }
            ArrayOf<NodeId> nodes = await BrowseChildrenAsync(
                folder.ObjectId, NodeClass.Object, m_limits.MaximumCorrections, cancellationToken).ConfigureAwait(false);
            var result = new List<GmsCorrectionValues>();
            var budget = new ReadBudget();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeId node = nodes[i];
                var proxy = new CorrectionTypeClient(Session, node, m_telemetry);
                DataValue identifier = await ReadMemberAsync(proxy.ObjectId, BrowseNames.Identifier,
                    optional: false, budget, cancellationToken).ConfigureAwait(false);
                DataValue characteristic = await ReadMemberAsync(proxy.ObjectId, BrowseNames.CharacteristicIdentifier,
                    optional: false, budget, cancellationToken).ConfigureAwait(false);
                GmsAnalogReading? relative = await ReadAnalogAsync(node, BrowseNames.CorrectionValueRelative,
                    budget, cancellationToken).ConfigureAwait(false);
                GmsAnalogReading? absolute = await ReadAnalogAsync(node, BrowseNames.CorrectionValueAbsolute,
                    budget, cancellationToken).ConfigureAwait(false);
                if (relative == null && absolute == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadUnexpectedError, "CorrectionType requires a relative or absolute correction value.");
                }
                result.Add(new GmsCorrectionValues
                {
                    NodeId = node,
                    Identifier = identifier,
                    CharacteristicIdentifier = characteristic,
                    RelativeValue = relative,
                    AbsoluteValue = absolute,
                    LowerControlLimit = await ReadAnalogAsync(node, BrowseNames.LowerControlLimit,
                        budget, cancellationToken).ConfigureAwait(false),
                    UpperControlLimit = await ReadAnalogAsync(node, BrowseNames.UpperControlLimit,
                        budget, cancellationToken).ConfigureAwait(false),
                    Description = await ReadMemberAsync(node, BrowseNames.Description,
                        optional: true, budget, cancellationToken).ConfigureAwait(false),
                    ProgramName = await ReadMemberAsync(node, BrowseNames.ProgramName,
                        optional: true, budget, cancellationToken).ConfigureAwait(false)
                });
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Reads an optional analog node with its mandatory namespace-zero engineering units.
        /// </summary>
        private async ValueTask<GmsAnalogReading?> ReadAnalogAsync(
            NodeId parent,
            string name,
            ReadBudget budget,
            CancellationToken cancellationToken)
        {
            NodeId node = await ResolveMemberAsync(parent, GmsModel.NamespaceUri, name,
                optional: true, cancellationToken).ConfigureAwait(false);
            if (node.IsNull)
            {
                return null;
            }
            NodeId units = await ResolveMemberAsync(node, Opc.Ua.Namespaces.OpcUa,
                Opc.Ua.BrowseNames.EngineeringUnits, optional: false, cancellationToken).ConfigureAwait(false);
            DataValue value = await ReadValueAsync(node, budget, cancellationToken).ConfigureAwait(false);
            DataValue engineeringUnits = await ReadValueAsync(units, budget, cancellationToken).ConfigureAwait(false);
            return new GmsAnalogReading(value, engineeringUnits);
        }

        /// <summary>
        /// Keeps a missing optional node distinct from a present node returning a bad-quality sample.
        /// </summary>
        private async ValueTask<DataValue> ReadMemberAsync(
            NodeId parent,
            string name,
            bool optional,
            ReadBudget budget,
            CancellationToken cancellationToken)
        {
            NodeId node = await ResolveMemberAsync(
                parent, GmsModel.NamespaceUri, name, optional, cancellationToken).ConfigureAwait(false);
            return node.IsNull ? DataValue.Null : await ReadValueAsync(node, budget, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the complete sample while checking service status, result cardinality and cumulative response bytes.
        /// </summary>
        private async ValueTask<DataValue> ReadValueAsync(
            NodeId node,
            ReadBudget budget,
            CancellationToken cancellationToken)
        {
            ReadResponse response = await Session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [new ReadValueId { NodeId = node, AttributeId = Attributes.Value }],
                cancellationToken).ConfigureAwait(false);
            ThrowIfBad(response.ResponseHeader.ServiceResult);
            RequireCount(response.Results.Count, 1);
            DataValue value = response.Results[0];
            budget.Bytes += m_limits.Measure(Variant.From(value), Session.MessageContext);
            if (budget.Bytes > m_limits.MaximumMetadataBytes)
            {
                throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "The GMS reading exceeds its total metadata byte bound.");
            }
            return value.Copy();
        }

        /// <summary>
        /// Resolves exact qualified fields without confusing access failures with optional absence.
        /// </summary>
        private async ValueTask<NodeId> ResolveMemberAsync(
            NodeId parent,
            string namespaceUri,
            string name,
            bool optional,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Session.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotSupported,
                    "The server has no namespace for the requested GMS field.");
            }
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(null,
            [
                new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(name, (ushort)index)
                            }
                        ]
                    }
                }
            ], cancellationToken).ConfigureAwait(false);
            ThrowIfBad(response.ResponseHeader.ServiceResult);
            RequireCount(response.Results.Count, 1);
            BrowsePathResult path = response.Results[0];
            if (optional && (path.StatusCode == StatusCodes.BadNoMatch || path.StatusCode == StatusCodes.BadNotFound))
            {
                return NodeId.Null;
            }
            ThrowIfBad(path.StatusCode);
            RequireCount(path.Targets.Count, 1);
            NodeId node = ExpandedNodeId.ToNodeId(path.Targets[0].TargetId, Session.NamespaceUris);
            if (node.IsNull || path.Targets[0].RemainingPathIndex != uint.MaxValue)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotSupported, "Remote GMS field targets are not followed.");
            }
            return node;
        }

        /// <summary>
        /// Browses a bounded optional subtree, releasing only this call's outstanding continuation on failure.
        /// </summary>
        private async ValueTask<ArrayOf<NodeId>> BrowseChildrenAsync(
            NodeId parent,
            NodeClass nodeClass,
            int maximum,
            CancellationToken cancellationToken)
        {
            ByteString continuation = default;
            Exception? failure = null;
            try
            {
                BrowseResponse response = await Session.BrowseAsync(null, null, (uint)Math.Min(maximum + 1, 64),
                [
                    new BrowseDescription
                    {
                        NodeId = parent,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)nodeClass,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], cancellationToken).ConfigureAwait(false);
                ThrowIfBad(response.ResponseHeader.ServiceResult);
                RequireCount(response.Results.Count, 1);
                BrowseResult page = response.Results[0];
                var nodes = new List<NodeId>();
                int pages = 0;
                while (true)
                {
                    continuation = page.ContinuationPoint;
                    ThrowIfBad(page.StatusCode);
                    if (++pages > maximum + 1 || nodes.Count + page.References.Count > maximum)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                            "The GMS subtree exceeds the configured browse bound.");
                    }
                    foreach (ReferenceDescription reference in page.References)
                    {
                        NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                        if (node.IsNull)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadNotSupported, "Remote GMS nodes are not followed.");
                        }
                        nodes.Add(node);
                    }
                    if (continuation.IsNull || continuation.IsEmpty)
                    {
                        return nodes.ToArrayOf();
                    }
                    BrowseNextResponse next = await Session.BrowseNextAsync(
                        null, false, [continuation], cancellationToken).ConfigureAwait(false);
                    ThrowIfBad(next.ResponseHeader.ServiceResult);
                    RequireCount(next.Results.Count, 1);
                    page = next.Results[0];
                }
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                await ReleaseContinuationAsync(continuation, failure).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases an acquired continuation while preserving both a primary failure and a cleanup failure.
        /// </summary>
        private async ValueTask ReleaseContinuationAsync(ByteString continuation, Exception? failure)
        {
            if (continuation.IsNull || continuation.IsEmpty)
            {
                return;
            }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await Session.BrowseNextAsync(null, true, [continuation], cleanup.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (failure != null)
            {
                throw new AggregateException("The GMS browse and continuation release both failed.", failure, error);
            }
        }

        /// <summary>
        /// Preserves original UA operation or service failure status.
        /// </summary>
        private static void ThrowIfBad(StatusCode code)
        {
            if (StatusCode.IsBad(code))
            {
                throw new ServiceResultException(code);
            }
        }

        /// <summary>
        /// Rejects incomplete or ambiguous service responses.
        /// </summary>
        private static void RequireCount(int count, int expected)
        {
            if (count != expected)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Unexpected GMS response cardinality.");
            }
        }

        /// <summary>
        /// Counts bytes for one asynchronous reading without shared mutable client state.
        /// </summary>
        private sealed class ReadBudget
        {
            /// <summary>
            /// Gets or sets the encoded response bytes consumed by this operation.
            /// </summary>
            internal long Bytes { get; set; }
        }
    }
}
