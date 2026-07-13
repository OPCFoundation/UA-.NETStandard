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

            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(pointVariableId, cancellationToken)
                .ConfigureAwait(false);
            if (node is BaseVariableState pointVariable)
            {
                var pointTypeId = NodeId.ToExpandedNodeId(
                    new NodeId(
                        ServerComplexTypesTestNodeManager.TestPointDataType,
                        namespaceIndex),
                    server.NamespaceUris);

                if (server.Factory.TryGetEncodeableType(pointTypeId, out IEncodeableType pointType))
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

            // TestColor.Green == 1; the wire form of an enumeration is Int32.
            var colorVariableId = new NodeId(
                ServerComplexTypesTestNodeManager.ColorValueVariable,
                namespaceIndex);

            NodeState colorNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(colorVariableId, cancellationToken)
                .ConfigureAwait(false);
            if (colorNode is BaseVariableState colorVariable)
            {
                colorVariable.Value = new Variant(1);
                colorVariable.ClearChangeMasks(server.DefaultSystemContext, false);
            }
        }
    }
}
