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
using Opc.Ua.Extrusion.Calender;
using Opc.Ua.Extrusion.Calibrator;
using Opc.Ua.Extrusion.Corrugator;
using Opc.Ua.Extrusion.Cutter;
using Opc.Ua.Extrusion.Die;
using Opc.Ua.Extrusion.Extruder;
using Opc.Ua.Extrusion.Filter;
using Opc.Ua.Extrusion.HaulOff;
using Opc.Ua.Extrusion.Line;
using Opc.Ua.Extrusion.MeltPump;
using Opc.Ua.Extrusion.Pelletizer;
using Opc.Ua.PlasticsRubber;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// A discovered real extrusion sibling type, preserving its actual type definition.
    /// </summary>
    /// <param name="NodeId">The current-session instance identifier.</param>
    /// <param name="BrowseName">The actual instance BrowseName.</param>
    /// <param name="DisplayName">The server's display name.</param>
    /// <param name="TypeDefinition">The real type definition, including a vendor subtype when present.</param>
    public sealed record ExtrusionDeviceEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        ExpandedNodeId TypeDefinition);

    /// <summary>
    /// Complete extrusion-family discovery and generated typed proxy entrypoints.
    /// </summary>
    /// <remarks>
    /// Every returned proxy exposes the model's exact methods and child objects. Model-derived
    /// member extensions add typed variable reads/writes and repeated-child enumeration. The
    /// facade does not own or dispose its session and does not substitute one sibling device type for another.
    /// </remarks>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the complete extrusion family on an existing connected session.
        /// </summary>
        public ExtrusionClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            session.MessageContext.Factory.Builder
                .AddOpcUaPlasticsRubber()
                .AddOpcUaExtrusion()
                .AddOpcUaExtrusionLine()
                .AddOpcUaExtrusionExtruder()
                .AddOpcUaExtrusionFilter()
                .AddOpcUaExtrusionCutter()
                .Commit();
        }

        /// <summary>
        /// Gets the original caller's session, normally a ManagedSession.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the supplied telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets whether the server publishes the extrusion general-types namespace.
        /// </summary>
        public bool IsSupported => Session.NamespaceUris.GetIndex(Extrusion.Namespaces.Extrusion) >= 0;

        /// <summary>
        /// Opens the exact OPC 40084-2 line proxy.
        /// </summary>
        public ExtrusionLine_InterfaceTypeClient Line(NodeId node)
        {
            return new ExtrusionLine_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the exact OPC 40084-3 extruder proxy.
        /// </summary>
        public Extruder_InterfaceTypeClient Extruder(NodeId node)
        {
            return new Extruder_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the exact OPC 40084-4 haul-off proxy.
        /// </summary>
        public HaulOff_InterfaceTypeClient HaulOff(NodeId node)
        {
            return new HaulOff_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the exact OPC 40084-5 melt-pump proxy.
        /// </summary>
        public MeltPump_InterfaceTypeClient MeltPump(NodeId node)
        {
            return new MeltPump_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the actual OPC 40084-6 filter proxy.
        /// </summary>
        public Filter_InterfaceTypeClient Filter(NodeId node)
        {
            return new Filter_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the exact OPC 40084-7 die proxy.
        /// </summary>
        public Die_InterfaceTypeClient Die(NodeId node)
        {
            return new Die_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the exact OPC 40084-8 pelletizer proxy.
        /// </summary>
        public Pelletizer_InterfaceTypeClient Pelletizer(NodeId node)
        {
            return new Pelletizer_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the exact OPC 40084-9 cutter proxy.
        /// </summary>
        public Cutter_InterfaceTypeClient Cutter(NodeId node)
        {
            return new Cutter_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the exact OPC 40084-10 calibrator proxy.
        /// </summary>
        public Calibrator_InterfaceTypeClient Calibrator(NodeId node)
        {
            return new Calibrator_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the OPC 40084-11 proxy with its real HaulOff inheritance.
        /// </summary>
        public Corrugator_InterfaceTypeClient Corrugator(NodeId node)
        {
            return new Corrugator_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Opens the exact OPC 40084-12 calender proxy.
        /// </summary>
        public Calender_InterfaceTypeClient Calender(NodeId node)
        {
            return new Calender_InterfaceTypeClient(Session, Require(node), Telemetry);
        }

        /// <summary>
        /// Discovers concrete extrusion devices, deduplicating Machinery/DI/component references.
        /// </summary>
        public IAsyncEnumerable<ExtrusionDeviceEntry> DiscoverAsync(
            NodeId root = default,
            int maximumNodes = 10000,
            CancellationToken cancellationToken = default)
        {
            if (maximumNodes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumNodes));
            }
            return DiscoverCoreAsync(root, maximumNodes, cancellationToken);
        }

        /// <summary>
        /// Performs bounded browsing after the public entrypoint validates its arguments.
        /// </summary>
        private async IAsyncEnumerable<ExtrusionDeviceEntry> DiscoverCoreAsync(
            NodeId root,
            int maximumNodes,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId expected = ExpandedNodeId.ToNodeId(Extrusion.ObjectTypeIds.ExtrusionDeviceType,
                Session.NamespaceUris);
            if (expected.IsNull)
            {
                yield break;
            }
            var pending = new Queue<NodeId>();
            var scheduled = new HashSet<NodeId>();
            var found = new HashSet<NodeId>();
            if (root.IsNull)
            {
                foreach (ExpandedNodeId folder in
                    (ArrayOf<ExpandedNodeId>)[Machinery.ObjectIds.Machines, Di.ObjectIds.DeviceSet])
                {
                    NodeId local = ExpandedNodeId.ToNodeId(folder, Session.NamespaceUris);
                    if (!local.IsNull && scheduled.Add(local))
                    {
                        pending.Enqueue(local);
                    }
                }
            }
            else
            {
                pending.Enqueue(root);
                scheduled.Add(root);
            }
            int visited = 0;
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++visited > maximumNodes)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                            "Extrusion discovery exceeded its configured node bound.");
                }
                ArrayOf<ReferenceDescription> references = await ExtrusionClientNodes.BrowseAsync(
                    Session, pending.Dequeue(), (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < references.Count; index++)
                {
                    ReferenceDescription reference = references[index];
                    NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    NodeId actualType = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                    if (node.IsNull || !scheduled.Add(node))
                    {
                        continue;
                    }
                    if (!actualType.IsNull &&
                        await Session.NodeCache.IsTypeOfAsync(actualType, expected,
                            cancellationToken).ConfigureAwait(false) &&
                        actualType != expected && found.Add(node))
                    {
                        yield return new ExtrusionDeviceEntry(node, reference.BrowseName, reference.DisplayName,
                            NodeId.ToExpandedNodeId(actualType, Session.NamespaceUris));
                    }
                    pending.Enqueue(node);
                }
            }
        }

        /// <summary>
        /// Rejects a null identity before constructing a generated proxy.
        /// </summary>
        private static NodeId Require(NodeId node)
        {
            return !node.IsNull ? node : throw new ArgumentNullException(nameof(node));
        }
    }
}
