// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Covers the deprecated synchronous node manager API on purpose.

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Encoders;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// A <see cref="ReferenceServer"/> that additionally hosts the
    /// <see cref="ServerComplexTypesTestNodeManager"/> (runtime-loaded custom
    /// DataTypes with no compiled backing) and, once the server has built its
    /// stand-in encodeables, assigns a value of the runtime <c>TestPoint</c>
    /// structure to the <c>PointValue</c> variable. This exercises the server
    /// side complex type system end-to-end.
    /// </summary>
    internal sealed class ServerComplexTypesTestServer : ReferenceServer
    {
        /// <summary>
        /// Initializes the server and registers the complex type test node
        /// manager before the address space is created.
        /// </summary>
        /// <param name="telemetry">The telemetry context.</param>
        public ServerComplexTypesTestServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            AddNodeManager(new ServerComplexTypesTestNodeManagerFactory());
        }

        /// <inheritdoc/>
        protected override async ValueTask OnNodeManagerStartedAsync(
            IServerInternal server,
            CancellationToken cancellationToken = default)
        {
            // Build the stand-in encodeables for the runtime-loaded DataTypes.
            await base.OnNodeManagerStartedAsync(server, cancellationToken)
                .ConfigureAwait(false);

            // Now that the runtime stand-ins exist in the server factory,
            // populate the sample variables with values so a client can read
            // (and the server encode) them over the wire.
            await PopulateSampleValuesAsync(server, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Assigns a <c>TestPoint</c> structured value to the <c>PointValue</c>
        /// variable (using the server's runtime stand-in encodeable) and a
        /// <c>TestColor</c> enumeration value to the <c>ColorValue</c> variable.
        /// </summary>
        private static async ValueTask PopulateSampleValuesAsync(
            IServerInternal server,
            CancellationToken cancellationToken)
        {
            ushort namespaceIndex = (ushort)server.NamespaceUris
                .GetIndex(ServerComplexTypesTestNodeManager.NamespaceUri);

            var pointVariableId = new NodeId(
                ServerComplexTypesTestNodeManager.PointValueVariable,
                namespaceIndex);

            NodeState node = (await server.NodeManager
                .FindNodeInAddressSpaceAsync(pointVariableId, cancellationToken)
                .ConfigureAwait(false))!;
            if (node is BaseVariableState pointVariable)
            {
                var pointTypeId = NodeId.ToExpandedNodeId(
                    new NodeId(
                        ServerComplexTypesTestNodeManager.TestPointDataType,
                        namespaceIndex),
                    server.NamespaceUris);

                if (server.Factory.TryGetEncodeableType(pointTypeId, out IEncodeableType? pointType))
                {
                    IEncodeable body = pointType.CreateInstance();
                    if (body is Structure structure)
                    {
                        structure["X"] = new Variant(3);
                        structure["Y"] = new Variant(4);
                        structure["Name"] = new Variant("origin");
                    }

                    pointVariable.Value = new Variant(new ExtensionObject(body));
                    pointVariable.ClearChangeMasks(server.DefaultSystemContext, false);
                }
            }

            // TestPoint3D inherits X, Y and Name from TestPoint.
            NodeState point3DNode = (await server.NodeManager
                .FindNodeInAddressSpaceAsync(
                    new NodeId(ServerComplexTypesTestNodeManager.Point3DValueVariable, namespaceIndex),
                    cancellationToken)
                .ConfigureAwait(false))!;
            var point3DTypeId = NodeId.ToExpandedNodeId(
                new NodeId(ServerComplexTypesTestNodeManager.TestPoint3DDataType, namespaceIndex),
                server.NamespaceUris);
            if (point3DNode is BaseVariableState point3DVariable &&
                server.Factory.TryGetEncodeableType(point3DTypeId, out IEncodeableType? point3DType))
            {
                IEncodeable body = point3DType.CreateInstance();
                if (body is Structure structure)
                {
                    structure["X"] = new Variant(1);
                    structure["Y"] = new Variant(2);
                    structure["Name"] = new Variant("corner");
                    structure["Z"] = new Variant(5);
                }

                point3DVariable.Value = new Variant(new ExtensionObject(body));
                point3DVariable.ClearChangeMasks(server.DefaultSystemContext, false);
            }

            // TestColor.Green == 1; the wire form of an enumeration is Int32.
            var colorVariableId = new NodeId(
                ServerComplexTypesTestNodeManager.ColorValueVariable,
                namespaceIndex);

            NodeState colorNode = (await server.NodeManager
                .FindNodeInAddressSpaceAsync(colorVariableId, cancellationToken)
                .ConfigureAwait(false))!;
            if (colorNode is BaseVariableState colorVariable)
            {
                colorVariable.Value = new Variant(1);
                colorVariable.ClearChangeMasks(server.DefaultSystemContext, false);
            }
        }
    }
}
