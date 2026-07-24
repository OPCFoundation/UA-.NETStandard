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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Positioning.Client
{
    internal sealed class PositioningClientOperations
    {
        public PositioningClientOperations(
            ISession session,
            ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        public ISession Session { get; }

        public ITelemetryContext Telemetry { get; }

        public async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            uint nodeClassMask,
            CancellationToken cancellationToken)
        {
            (
                ArrayOf<ArrayOf<ReferenceDescription>> results,
                ArrayOf<ServiceResult> errors
            ) = await Session.ManagedBrowseAsync(
                requestHeader: null,
                view: null,
                nodesToBrowse: [nodeId],
                maxResultsToReturn: 0,
                browseDirection: BrowseDirection.Forward,
                referenceTypeId: referenceTypeId,
                includeSubtypes: true,
                nodeClassMask: nodeClassMask,
                ct: cancellationToken).ConfigureAwait(false);

            if (errors.Count > 0 && ServiceResult.IsBad(errors[0]))
            {
                throw new ServiceResultException(errors[0]);
            }

            return results.Count > 0
                ? results[0]
                : default;
        }

        public async ValueTask<NodeId> ResolveChildAsync(
            NodeId parentId,
            NodeId referenceTypeId,
            QualifiedName browseName,
            CancellationToken cancellationToken)
        {
            var path = new BrowsePath
            {
                StartingNode = parentId,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = referenceTypeId,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = browseName
                        }
                    ]
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    requestHeader: null,
                    browsePaths: new[] { path }.ToArrayOf(),
                    ct: cancellationToken).ConfigureAwait(false);

            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count == 0)
            {
                return NodeId.Null;
            }

            return ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId,
                Session.NamespaceUris);
        }

        public async ValueTask<NodeId> ResolveRequiredChildAsync(
            NodeId parentId,
            NodeId referenceTypeId,
            QualifiedName browseName,
            CancellationToken cancellationToken)
        {
            NodeId nodeId = await ResolveChildAsync(
                parentId,
                referenceTypeId,
                browseName,
                cancellationToken).ConfigureAwait(false);
            if (nodeId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoMatch,
                    $"Node '{parentId}' does not expose '{browseName}'.");
            }
            return nodeId;
        }

        public async ValueTask<T> ReadStructureAsync<T>(
            NodeId nodeId,
            CancellationToken cancellationToken)
            where T : class, IEncodeable
        {
            DataValue dataValue = await Session.ReadValueAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            T value;
            if (!dataValue.WrappedValue.TryGetValue(
                out value!,
                Session.MessageContext))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    $"Node '{nodeId}' does not contain a {typeof(T).Name} value.");
            }
            return value;
        }

        public async ValueTask<ArrayOf<T>> ReadStructureArrayAsync<T>(
            NodeId nodeId,
            CancellationToken cancellationToken)
            where T : class, IEncodeable
        {
            DataValue dataValue = await Session.ReadValueAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            if (!dataValue.WrappedValue.TryGetValue(
                out ArrayOf<T> value,
                Session.MessageContext))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    $"Node '{nodeId}' does not contain an array of {typeof(T).Name} values.");
            }
            return value;
        }

        public async ValueTask<NodeId> ReadNodeIdAsync(
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            DataValue dataValue = await Session.ReadValueAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            if (!dataValue.WrappedValue.TryGetValue(out NodeId value))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    $"Node '{nodeId}' does not contain a NodeId value.");
            }
            return value;
        }

        public async ValueTask<uint> ReadUInt32Async(
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            DataValue dataValue = await Session.ReadValueAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            if (!dataValue.WrappedValue.TryGetValue(out uint value))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    $"Node '{nodeId}' does not contain a UInt32 value.");
            }
            return value;
        }

        public PositioningObjectEntry CreateEntry(
            ReferenceDescription reference)
        {
            var nodeId = ExpandedNodeId.ToNodeId(
                reference.NodeId,
                Session.NamespaceUris);
            var typeDefinitionId = ExpandedNodeId.ToNodeId(
                reference.TypeDefinition,
                Session.NamespaceUris);
            return new PositioningObjectEntry(
                nodeId,
                reference.BrowseName,
                reference.DisplayName,
                typeDefinitionId);
        }
    }
}
