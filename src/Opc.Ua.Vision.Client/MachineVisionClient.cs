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
using Opc.Ua.Client;
using Mv = Opc.Ua.Vision.MachineVision;
using Amcm = Opc.Ua.Vision.MachineVision.AMCM;

namespace Opc.Ua.Vision.Client
{
    public sealed partial class VisionClient
    {
        /// <summary>
        /// Opens the released OPC 40100 discovery and client interface on the same session.
        /// It does not require the provisional Vision namespace.
        /// </summary>
        public MachineVisionClient MachineVision()
        {
            return new MachineVisionClient(Session, Telemetry);
        }
    }

    /// <summary>
    /// Discovers the independent released Machine Vision models, including vendor subtypes
    /// and systems organized through Machinery rather than the draft Vision root.
    /// </summary>
    public sealed partial class MachineVisionClient
    {
        /// <summary>
        /// Opens the released-model interface on an existing session.
        /// </summary>
        public MachineVisionClient(ISession session, ITelemetryContext telemetry)
        {
            m_operations = new VisionClientOperations(session, telemetry);
        }

        /// <summary>
        /// Gets the underlying connected session.
        /// </summary>
        public ISession Session => m_operations.Session;

        /// <summary>
        /// Gets whether the server publishes the OPC 40100-1 namespace.
        /// </summary>
        public bool SupportsControl => Session.NamespaceUris.GetIndex(MachineVisionModels.ControlNamespaceUri) >= 0;

        /// <summary>
        /// Gets whether the server publishes the OPC 40100-2 namespace.
        /// </summary>
        public bool SupportsAssets => Session.NamespaceUris.GetIndex(MachineVisionModels.AssetNamespaceUri) >= 0;

        /// <summary>
        /// Discovers Part 1 systems below a supplied root, or the Objects folder.
        /// A traversal limit is a reported error, never silent truncation.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public IAsyncEnumerable<VisionNodeEntry> EnumerateSystemsAsync(
            NodeId root = default,
            int maximumNodes = 10000,
            CancellationToken cancellationToken = default)
        {
            if (maximumNodes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumNodes));
            }
            return EnumerateAsync(
                Mv.ObjectTypeIds.VisionSystemType, root, maximumNodes, cancellationToken);
        }

        /// <summary>
        /// Discovers Part 2 system assets below a supplied root, or the Objects folder.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public IAsyncEnumerable<VisionNodeEntry> EnumerateAssetsAsync(
            NodeId root = default,
            int maximumNodes = 10000,
            CancellationToken cancellationToken = default)
        {
            if (maximumNodes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumNodes));
            }
            return EnumerateAsync(
                Amcm.ObjectTypeIds.VisionSystemAssetType, root, maximumNodes, cancellationToken);
        }

        /// <summary>
        /// Opens the complete Part 1 interface for the supplied system instance.
        /// </summary>
        public MachineVisionSystemClient System(NodeId system)
        {
            return new MachineVisionSystemClient(Session, system, m_operations.Telemetry);
        }

        /// <summary>
        /// Opens the generated Part 2 proxy for the supplied system asset.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public Amcm.VisionSystemAssetTypeClient Asset(NodeId asset)
        {
            if (asset.IsNull)
            {
                throw new ArgumentNullException(nameof(asset));
            }
            return new Amcm.VisionSystemAssetTypeClient(Session, asset, m_operations.Telemetry);
        }

        /// <summary>
        /// Enumerates the nodes linked by the standard same-entity relationship.
        /// </summary>
        public async ValueTask<ArrayOf<NodeId>> GetSameEntityNodesAsync(
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<ReferenceDescription> references = await BrowseAsync(
                node, global::Opc.Ua.ReferenceTypeIds.RepresentsSameEntityAs,
                BrowseDirection.Both, 0, cancellationToken).ConfigureAwait(false);
            var result = new HashSet<NodeId>();
            foreach (ReferenceDescription reference in references)
            {
                NodeId target = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                if (!target.IsNull)
                {
                    result.Add(target);
                }
            }
            return new List<NodeId>(result).ToArrayOf();
        }

        private async IAsyncEnumerable<VisionNodeEntry> EnumerateAsync(
            ExpandedNodeId type,
            NodeId root,
            int maximumNodes,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId typeId = ExpandedNodeId.ToNodeId(type, Session.NamespaceUris);
            if (typeId.IsNull)
            {
                yield break;
            }
            var visited = new HashSet<NodeId>();
            var discovered = new HashSet<NodeId>();
            var scheduled = new HashSet<NodeId>();
            var pending = new Queue<NodeId>();
            NodeId start = root.IsNull ? global::Opc.Ua.ObjectIds.ObjectsFolder : root;
            pending.Enqueue(start);
            scheduled.Add(start);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeId current = pending.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }
                if (visited.Count > maximumNodes)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "Machine Vision discovery exceeded its node limit; supply a narrower root.");
                }
                ArrayOf<ReferenceDescription> references = await BrowseAsync(
                    current, global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    BrowseDirection.Forward, (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);
                for (int ii = 0; ii < references.Count; ii++)
                {
                    ReferenceDescription reference = references[ii];
                    NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    if (node.IsNull || !scheduled.Add(node))
                    {
                        continue;
                    }
                    NodeId actualType = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                    if (!actualType.IsNull &&
                        await Session.NodeCache.IsTypeOfAsync(actualType, typeId, cancellationToken)
                            .ConfigureAwait(false) &&
                        discovered.Add(node))
                    {
                        yield return new VisionNodeEntry(
                            node, reference.BrowseName, reference.DisplayName, actualType);
                    }
                    pending.Enqueue(node);
                }
            }
        }

        private async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            NodeId node,
            NodeId referenceType,
            BrowseDirection direction,
            uint nodeClassMask,
            CancellationToken cancellationToken)
        {
            if (node.IsNull)
            {
                throw new ArgumentNullException(nameof(node));
            }
            (ArrayOf<ArrayOf<ReferenceDescription>> results, ArrayOf<ServiceResult> errors) =
                await Session.ManagedBrowseAsync(
                    null, null, [node], 0, direction, referenceType, true, nodeClassMask,
                    cancellationToken).ConfigureAwait(false);
            if (errors.Count != 0 && ServiceResult.IsBad(errors[0]))
            {
                throw new ServiceResultException(errors[0]);
            }
            if (results.Count != 1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError, "Browse did not return the requested result.");
            }
            return results[0];
        }

        private readonly VisionClientOperations m_operations;
    }
}
