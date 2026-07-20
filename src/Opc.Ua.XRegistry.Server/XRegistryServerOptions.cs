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

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Configuration for the generic server-side xRegistry node managers: the registry companion
    /// namespace, the content-id provider that fingerprints resources, and optional seed/federation
    /// resources. A concrete registry (for example the PubSub Schema Registry) populates these with
    /// its own namespace, provider and seed documents.
    /// </summary>
    public class XRegistryServerOptions
    {
        /// <summary>
        /// The registry companion namespace URI the node managers claim. Defaults to the abstract
        /// xRegistry base namespace; a concrete registry overrides it with its own namespace.
        /// </summary>
        public string RegistryNamespaceUri { get; set; } = XRegistryWellKnown.XRegistryNamespaceUri;

        /// <summary>
        /// The provider that computes a resource's content-derived id and algorithm. Required when a
        /// seed/federation resource is published or a resource is registered.
        /// </summary>
        public IResourceContentIdProvider? ContentIdProvider { get; set; }

        /// <summary>When <c>true</c>, the fast-path manager pre-publishes <see cref="SeedDocument"/>.</summary>
        public bool PublishSeedResource { get; set; }

        /// <summary>The seed resource document published by the fast-path manager.</summary>
        public byte[]? SeedDocument { get; set; }

        /// <summary>The format of <see cref="SeedDocument"/>.</summary>
        public string SeedFormat { get; set; } = "avro";

        /// <summary>The BrowseName of the seeded fast-path resource node.</summary>
        public string SeedBrowseName { get; set; } = "FastPathResource";

        /// <summary>When <c>true</c>, the federation manager publishes a federated resource proxy.</summary>
        public bool PublishFederationProxy { get; set; }

        /// <summary>The document hosted by the remote registry (federated locally as a proxy).</summary>
        public byte[]? FederatedDocument { get; set; }

        /// <summary>The format of <see cref="FederatedDocument"/>.</summary>
        public string FederatedFormat { get; set; } = "avro";

        /// <summary>The remote registry's companion namespace URI carried by the proxy.</summary>
        public string RemoteRegistryNamespaceUri { get; set; } = XRegistryWellKnown.XRegistryNamespaceUri;

        /// <summary>The remote registry endpoint carried by the proxy's <c>ResourceUrl</c>.</summary>
        public string RemoteEndpointUrl { get; set; } = string.Empty;

        /// <summary>The remote server's index into the local <c>ServerArray</c>.</summary>
        public uint RemoteServerIndex { get; set; }

        /// <summary>The BrowseName of the federated resource proxy object.</summary>
        public string FederationProxyBrowseName { get; set; } = "FederatedResourceProxy";
    }
}
