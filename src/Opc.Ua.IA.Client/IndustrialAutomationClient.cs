/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.IA.Client
{
    /// <summary>
    /// Identifies one independently composable IA building block.
    /// </summary>
    public enum IaConcept
    {
        /// <summary>
        /// A basic or device-health-enabled stacklight.
        /// </summary>
        Stacklight,

        /// <summary>
        /// An object implementing the aggregate statistics interface.
        /// </summary>
        AggregateStatistics,

        /// <summary>
        /// An object implementing the rolling statistics interface.
        /// </summary>
        RollingStatistics,

        /// <summary>
        /// A calibration target describing reference information.
        /// </summary>
        CalibrationTarget
    }

    /// <summary>
    /// A discovered IA object or statistics interface implementation.
    /// </summary>
    public sealed record IaEntry(NodeId NodeId, QualifiedName BrowseName, IaConcept Concept);

    /// <summary>
    /// Discovers IA concepts and opens their generated typed clients on a borrowed session.
    /// </summary>
    /// <remarks>
    /// Generated variable accessors preserve source status and timestamps and expose subscription
    /// observation. Generated method proxies preserve operation errors. This facade neither owns
    /// nor disposes the session and does not retry state-changing operations after disconnection.
    /// </remarks>
    public sealed partial class IndustrialAutomationClient
    {
        /// <summary>
        /// Creates the facade without opening or taking ownership of a connection.
        /// </summary>
        public IndustrialAutomationClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            session.MessageContext.Factory.Builder.AddOpcUaIA().Commit();
        }

        /// <summary>
        /// Gets the caller's session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the caller's telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Opens a generated stacklight proxy with typed accessors and ordered-element enumeration.
        /// </summary>
        public BasicStacklightTypeClient Stacklight(NodeId node)
        {
            return new BasicStacklightTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the generated aggregate-statistics proxy, including authorized ResetStatistics.
        /// </summary>
        public IAggregateStatisticsTypeClient AggregateStatistics(NodeId node)
        {
            return new IAggregateStatisticsTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the generated rolling-statistics proxy, including window metadata.
        /// </summary>
        public IRollingStatisticsTypeClient RollingStatistics(NodeId node)
        {
            return new IRollingStatisticsTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the generated calibration-target proxy; no certificate content is fetched.
        /// </summary>
        public CalibrationTargetTypeClient CalibrationTarget(NodeId node)
        {
            return new CalibrationTargetTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Discovers IA building blocks below an application-selected root, including vendor subtypes.
        /// </summary>
        public IAsyncEnumerable<IaEntry> DiscoverAsync(
            NodeId root,
            int maximumNodes = 4096,
            CancellationToken cancellationToken = default)
        {
            Require(root);
            if (maximumNodes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumNodes));
            }
            if (Session.NamespaceUris.GetIndex(Namespaces.IA) < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The server has no IA model.");
            }
            return DiscoverCoreAsync(root, maximumNodes, cancellationToken);
        }

        /// <summary>
        /// Performs cycle-safe bounded discovery without assuming instance namespace indexes.
        /// </summary>
        private async IAsyncEnumerable<IaEntry> DiscoverCoreAsync(
            NodeId root,
            int maximumNodes,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId stacklight = ExpandedNodeId.ToNodeId(ObjectTypeIds.BasicStacklightType, Session.NamespaceUris);
            NodeId calibration = ExpandedNodeId.ToNodeId(ObjectTypeIds.CalibrationTargetType, Session.NamespaceUris);
            NodeId aggregate = ExpandedNodeId.ToNodeId(ObjectTypeIds.IAggregateStatisticsType, Session.NamespaceUris);
            NodeId rolling = ExpandedNodeId.ToNodeId(ObjectTypeIds.IRollingStatisticsType, Session.NamespaceUris);
            var pending = new Queue<NodeId>();
            var seen = new HashSet<NodeId> { root };
            pending.Enqueue(root);
            while (pending.Count != 0)
            {
                ArrayOf<ReferenceDescription> children = await ClientModelAccess.BrowseAsync(
                    Session, pending.Dequeue(), (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);
                for (int i = 0; i < children.Count; i++)
                {
                    ReferenceDescription child = children[i];
                    NodeId node = ExpandedNodeId.ToNodeId(child.NodeId, Session.NamespaceUris);
                    if (node.IsNull || !seen.Add(node))
                    {
                        continue;
                    }
                    if (seen.Count > maximumNodes)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "IA discovery exceeded the configured node limit.");
                    }
                    NodeId type = ExpandedNodeId.ToNodeId(child.TypeDefinition, Session.NamespaceUris);
                    if (!type.IsNull &&
                        await Session.NodeCache.IsTypeOfAsync(type, stacklight, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        yield return new IaEntry(node, child.BrowseName, IaConcept.Stacklight);
                    }
                    else if (!type.IsNull &&
                        await Session.NodeCache.IsTypeOfAsync(type, calibration, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        yield return new IaEntry(node, child.BrowseName, IaConcept.CalibrationTarget);
                    }
                    ArrayOf<NodeId> interfaces = await FindInterfacesAsync(node, type, maximumNodes,
                        cancellationToken).ConfigureAwait(false);
                    var concepts = new HashSet<IaConcept>();
                    for (int j = 0; j < interfaces.Count; j++)
                    {
                        NodeId implemented = interfaces[j];
                        if (!implemented.IsNull &&
                            await Session.NodeCache.IsTypeOfAsync(implemented, aggregate,
                                cancellationToken).ConfigureAwait(false) && concepts.Add(IaConcept.AggregateStatistics))
                        {
                            yield return new IaEntry(node, child.BrowseName, IaConcept.AggregateStatistics);
                        }
                        if (!implemented.IsNull &&
                            await Session.NodeCache.IsTypeOfAsync(implemented, rolling,
                                cancellationToken).ConfigureAwait(false) && concepts.Add(IaConcept.RollingStatistics))
                        {
                            yield return new IaEntry(node, child.BrowseName, IaConcept.RollingStatistics);
                        }
                    }
                    pending.Enqueue(node);
                }
            }
        }

        /// <summary>
        /// Combines instance interfaces with those declared on its type and supertypes.
        /// </summary>
        private async Task<ArrayOf<NodeId>> FindInterfacesAsync(
            NodeId node,
            NodeId type,
            int maximumTypes,
            CancellationToken cancellationToken)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> references, ArrayOf<ServiceResult> errors) =
                await Session.ManagedBrowseAsync(null, null, [node], 0, BrowseDirection.Forward,
                    Opc.Ua.ReferenceTypeIds.HasInterface, true, (uint)NodeClass.ObjectType,
                    cancellationToken).ConfigureAwait(false);
            if (errors.Count != 0 && ServiceResult.IsBad(errors[0]))
            {
                throw new ServiceResultException(errors[0]);
            }
            if (references.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }
            var interfaces = new HashSet<NodeId>();
            for (int i = 0; i < references[0].Count; i++)
            {
                interfaces.Add(ExpandedNodeId.ToNodeId(references[0][i].NodeId, Session.NamespaceUris));
            }
            var seen = new HashSet<NodeId>();
            while (!type.IsNull && seen.Add(type))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seen.Count > maximumTypes)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "IA discovery exceeded the configured type-hierarchy limit.");
                }
                ArrayOf<INode> declared = await Session.NodeCache.GetReferencesAsync(
                    type, Opc.Ua.ReferenceTypeIds.HasInterface, false, true, cancellationToken)
                    .ConfigureAwait(false);
                for (int i = 0; i < declared.Count; i++)
                {
                    interfaces.Add(ExpandedNodeId.ToNodeId(declared[i].NodeId, Session.NamespaceUris));
                }
                type = await Session.NodeCache.FindSuperTypeAsync(type, cancellationToken).ConfigureAwait(false);
            }
            return interfaces.ToArray();
        }

        /// <summary>
        /// Rejects missing identities before constructing a proxy.
        /// </summary>
        private static NodeId Require(NodeId node)
        {
            return !node.IsNull ? node : throw new ArgumentNullException(nameof(node));
        }
    }
}
