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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.PubSub.Server.SchemaRegistry;
using Opc.Ua.PubSub.SchemaRegistry;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.XRegistry;
using Quickstarts.ReferenceServer;


namespace Opc.Ua.PubSub.Server.Tests.SchemaRegistry
{
    /// <summary>
    /// A <see cref="ReferenceServer"/> that enables the optional in-server PubSub Schema Registry
    /// feature: it loads the Schema Registry companion NodeSet (from <c>Opc.Ua.PubSub</c>) through
    /// the <see cref="RuntimeNodeSetNodeManagerFactory"/> import path and attaches the fast-path,
    /// registration and federation node managers from <c>Opc.Ua.PubSub.Server</c>. The xRegistry
    /// node managers also load the compiled xRegistry base model as predefined nodes; this harness
    /// supplies the base NodeSet to the runtime importer so it can resolve Schema Registry supertypes.
    /// </summary>
    internal sealed class SchemaRegistryTestServer : ReferenceServer
    {
        /// <summary>The abstract xRegistry base companion namespace URI.</summary>
        public const string XRegistryNamespaceUri = XRegistryWellKnown.XRegistryNamespaceUri;

        /// <summary>The Schema Registry companion namespace URI.</summary>
        public const string SchemaRegistryNamespaceUri =
            SchemaRegistryWellKnown.SchemaRegistryNamespaceUri;

        /// <summary>Provisional NodeId of the <c>SchemaRegistryType</c> ObjectType.</summary>
        public const uint SchemaRegistryType = SchemaRegistryWellKnown.SchemaRegistryType;

        /// <summary>Provisional NodeId of the well-known <c>SchemaRegistry</c> object.</summary>
        public const uint SchemaRegistryObject = SchemaRegistryWellKnown.SchemaRegistryObject;

        /// <summary>
        /// Provisional NodeId of the <c>GetSchema</c> method materialized on the well-known
        /// <c>SchemaRegistry</c> object.
        /// </summary>
        public const uint SchemaRegistryGetSchemaMethod =
            SchemaRegistryWellKnown.SchemaRegistryGetSchemaMethod;

        /// <summary>
        /// Initializes the server and registers the runtime NodeSet factory for the Schema Registry
        /// companion NodeSet, plus the Schema Registry node managers.
        /// </summary>
        /// <param name="telemetry">Telemetry context forwarded to the base server.</param>
        public SchemaRegistryTestServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            string remoteServerUri = "urn:example:remote-schema-registry";
            var target = new XRegistryFederationTarget(
                new RegistryOriginDataType
                {
                    OriginUri = string.Empty,
                    ServerUri = remoteServerUri,
                    RegistryNodeId = new ExpandedNodeId("remote-registry", SchemaRegistryNamespaceUri)
                },
                remoteServerUri,
                new ExpandedNodeId(
                    new NodeId(SchemaContentIdProvider.Instance.ComputeContentId(
                        "avro",
                        SchemaRegistryOptions.FederatedSchemaDocument.Span)),
                    SchemaRegistryNamespaceUri),
                "/groups/federated/resources/federated-resource");
            var federationProvider = new Mock<IXRegistryFederationProvider>(MockBehavior.Strict);
            federationProvider.Setup(provider => provider.VerifyLogicalResourceAsync(
                target,
                SchemaRegistryOptions.RemoteEndpointUrl,
                It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            var options = new SchemaRegistryOptions
            {
                PublishFederationProxy = true,
                FederationTarget = target,
                FederationProvider = federationProvider.Object
            };

            var nodeSetOptions = new RuntimeNodeSetOptions
            {
                Sources = SchemaRegistryServerNodeSets.CreateSources(options)
            };

            AddNodeManager(new FastPathNodeManagerFactory(options));
            AddNodeManager(new SchemaRegistrationNodeManagerFactory(options));
            AddNodeManager(new FederationNodeManagerFactory(options));
            AddNodeManager(new RuntimeNodeSetNodeManagerFactory(nodeSetOptions));
        }
    }
}
