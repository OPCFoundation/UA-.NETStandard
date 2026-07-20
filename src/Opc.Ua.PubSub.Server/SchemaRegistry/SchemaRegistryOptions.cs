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

using System.Diagnostics.CodeAnalysis;
using Opc.Ua.PubSub.SchemaRegistry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.PubSub.Server.SchemaRegistry
{
    /// <summary>
    /// Options for the optional, dependency-injectable in-server PubSub Schema Registry feature. The
    /// defaults reproduce the well-known Schema Registry companion namespace and seed a demonstration
    /// schema and federation proxy; a host may override the namespace or disable the seeds.
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public sealed class SchemaRegistryOptions
    {
        /// <summary>The seed schema published by the fast-path manager when enabled.</summary>
        internal static readonly byte[] SeedSchemaDocument = System.Text.Encoding.UTF8.GetBytes(
            "{\"type\":\"record\",\"name\":\"FastPath\",\"fields\":[]}");

        /// <summary>The document federated from a remote registry when enabled.</summary>
        internal static readonly byte[] FederatedSchemaDocument = System.Text.Encoding.UTF8.GetBytes(
            "{\"type\":\"record\",\"name\":\"Federated\",\"fields\":[]}");

        /// <summary>The remote registry endpoint carried by the federation proxy's <c>ResourceUrl</c>.</summary>
        internal const string RemoteEndpointUrl = "opc.tcp://remote-registry.example:4840";

        /// <summary>The remote server's index into the local <c>ServerArray</c>.</summary>
        internal const uint RemoteServerIndex = 1;

        /// <summary>The Schema Registry companion namespace URI.</summary>
        public string SchemaRegistryNamespaceUri { get; set; } =
            SchemaRegistryWellKnown.SchemaRegistryNamespaceUri;

        /// <summary>The abstract xRegistry base companion namespace URI.</summary>
        public string XRegistryNamespaceUri { get; set; } =
            XRegistryWellKnown.XRegistryNamespaceUri;

        /// <summary>
        /// When <c>true</c> (default) the fast-path node manager pre-publishes a seed schema so a
        /// fresh server can resolve at least one content-addressed schema before any registration.
        /// </summary>
        public bool PublishSeedSchema { get; set; } = true;

        /// <summary>
        /// When <c>true</c> (default) the federation node manager publishes a proxy for a schema
        /// hosted by a remote registry, proving the federation model.
        /// </summary>
        public bool PublishFederationProxy { get; set; } = true;

        /// <summary>
        /// Builds the generic <see cref="XRegistryServerOptions"/> that drive the xRegistry node
        /// managers with the Schema Registry namespace, content-id provider and seed documents.
        /// </summary>
        /// <returns>The mapped generic server options.</returns>
        internal XRegistryServerOptions ToServerOptions()
        {
            return new XRegistryServerOptions
            {
                RegistryNamespaceUri = SchemaRegistryNamespaceUri,
                ContentIdProvider = SchemaContentIdProvider.Instance,
                PublishSeedResource = PublishSeedSchema,
                SeedDocument = SeedSchemaDocument,
                SeedFormat = "avro",
                SeedBrowseName = "FastPathSchema",
                PublishFederationProxy = PublishFederationProxy,
                FederatedDocument = FederatedSchemaDocument,
                FederatedFormat = "avro",
                RemoteRegistryNamespaceUri = SchemaRegistryNamespaceUri,
                RemoteEndpointUrl = RemoteEndpointUrl,
                RemoteServerIndex = RemoteServerIndex,
                FederationProxyBrowseName = "FederatedSchemaProxy"
            };
        }
    }
}
