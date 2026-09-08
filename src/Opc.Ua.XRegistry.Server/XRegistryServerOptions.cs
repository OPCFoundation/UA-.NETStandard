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

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Configuration for the generic server-side xRegistry node managers: the registry companion
    /// namespace, the content-id provider that fingerprints resources, and optional seed/federation
    /// resources. A concrete registry (for example the PubSub Schema Registry) populates these with
    /// its own namespace, provider and seed documents.
    /// </summary>
    public sealed class XRegistryServerOptions
    {
        /// <summary>
        /// Gets or sets whether native xRegistry change events are emitted.
        /// </summary>
        public bool EventsEnabled { get; set; }

        /// <summary>
        /// Gets or sets the stable absolute URL used as the xRegistry event <c>SourceUrl</c>.
        /// </summary>
        public string EventSourceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the domain registry's groups collection attribute name.
        /// </summary>
        public string GroupsAttributeName { get; set; } = "groups";

        /// <summary>
        /// Gets or sets the domain group's resources collection attribute name.
        /// </summary>
        public string ResourcesAttributeName { get; set; } = "resources";

        /// <summary>
        /// Gets or sets the domain resource's singular document attribute name.
        /// </summary>
        public string ResourceDocumentAttributeName { get; set; } = "resource";

        /// <summary>
        /// The registry companion namespace URI the node managers claim. Defaults to the abstract
        /// xRegistry base namespace; a concrete registry overrides it with its own namespace.
        /// </summary>
        public string RegistryNamespaceUri { get; set; } = XRegistryWellKnown.XRegistryNamespaceUri;

        /// <summary>
        /// The provider that computes an opaque document content key and algorithm. Required when a
        /// seed/federation lookup is published or registered Version content is committed.
        /// </summary>
        public IResourceContentIdProvider? ContentIdProvider { get; set; }

        /// <summary>
        /// The store that holds resource document bytes. Defaults to an in-process store; a
        /// high-availability deployment substitutes a shared one so documents survive a failover.
        /// </summary>
        public IXRegistryResourceStore ResourceStore { get; set; } = new InMemoryResourceStore();

        /// <summary>
        /// The BrowseName of the registry root Object the registration manager materializes.
        /// </summary>
        public string RegistryBrowseName { get; set; } = "Registry";

        /// <summary>
        /// The <c>RegistryId</c> published by the registry root Object.
        /// </summary>
        public string RegistryId { get; set; } = "urn:opcfoundation:xregistry";

        /// <summary>
        /// The <c>SpecVersion</c> published by the registry root Object.
        /// </summary>
        public string SpecVersion { get; set; } = "0.1.0";

        /// <summary>
        /// When <c>true</c>, the fast-path manager pre-publishes <see cref="SeedDocument"/>.
        /// </summary>
        public bool PublishSeedResource { get; set; }

        /// <summary>
        /// The seed resource document published by the fast-path manager.
        /// </summary>
        public ByteString SeedDocument { get; set; }

        /// <summary>
        /// The format of <see cref="SeedDocument"/>.
        /// </summary>
        public string SeedFormat { get; set; } = "avro";

        /// <summary>
        /// The BrowseName of the seeded fast-path resource node.
        /// </summary>
        public string SeedBrowseName { get; set; } = "FastPathResource";

        /// <summary>
        /// When <c>true</c>, the federation manager publishes a federated resource proxy.
        /// </summary>
        public bool PublishFederationProxy { get; set; }

        /// <summary>
        /// The document hosted by the remote registry (federated locally as a proxy).
        /// </summary>
        public ByteString FederatedDocument { get; set; }

        /// <summary>
        /// The format of <see cref="FederatedDocument"/>.
        /// </summary>
        public string FederatedFormat { get; set; } = "avro";

        /// <summary>
        /// The remote registry's companion namespace URI carried by the proxy.
        /// </summary>
        public string RemoteRegistryNamespaceUri { get; set; } = XRegistryWellKnown.XRegistryNamespaceUri;

        /// <summary>
        /// The remote registry endpoint carried by the proxy's <c>ResourceUrl</c>.
        /// </summary>
        public string RemoteEndpointUrl { get; set; } = string.Empty;

        /// <summary>
        /// The remote server's index into the local <c>ServerArray</c>.
        /// </summary>
        public uint RemoteServerIndex { get; set; }

        /// <summary>
        /// The BrowseName of the federated resource proxy object.
        /// </summary>
        public string FederationProxyBrowseName { get; set; } = "FederatedResourceProxy";

        /// <summary>
        /// The structural group id used for the federated proxy's xRegistry identity.
        /// </summary>
        public string FederationProxyGroupId { get; set; } = "federated";

        /// <summary>
        /// The structural resource id retained by the federated proxy.
        /// </summary>
        public string FederationProxyResourceId { get; set; } = "federated-resource";

        /// <summary>
        /// The structural version id retained by the federated proxy.
        /// </summary>
        public string FederationProxyVersionId { get; set; } = "1";

        /// <summary>
        /// The maximum number of concurrently open upload handles (CreateResource without Close).
        /// A safety valve against memory exhaustion from a remote caller; CreateResource is rejected
        /// with <c>BadTooManyOperations</c> when the limit is reached.
        /// </summary>
        public int MaxConcurrentUploads { get; set; } = 64;

        /// <summary>
        /// The maximum cumulative number of bytes buffered per upload handle. A safety valve against
        /// memory exhaustion; Write is rejected with <c>BadRequestTooLarge</c> beyond this size.
        /// </summary>
        public int MaxResourceBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// The maximum number of permanently registered resource nodes. A safety valve against
        /// address-space exhaustion; Close is rejected with <c>BadTooManyOperations</c> at the limit.
        /// </summary>
        public int MaxRegisteredResources { get; set; } = 4096;

        /// <summary>
        /// Whether reading a resource also requires a <c>SignAndEncrypt</c> secure channel.
        /// </summary>
        /// <remarks>
        /// Registry <b>writes</b> always require <c>SignAndEncrypt</c> and this option cannot relax
        /// that: a resource document and its content lookup are integrity-critical, so a
        /// mutation over a channel that is only signed — or not protected at all — is rejected with
        /// <c>BadSecurityModeInsufficient</c>. Reads are permitted on any secure channel by default,
        /// because a registry is usually a public catalogue; set this to <c>true</c> when the
        /// documents themselves are confidential.
        /// </remarks>
        public bool RequireEncryptionForReads { get; set; }

        /// <summary>
        /// Validates event configuration when event support is enabled.
        /// </summary>
        /// <exception cref="System.ArgumentException">
        /// The event source URL is not absolute or a domain attribute name is missing.
        /// </exception>
        public void Validate()
        {
            if (!EventsEnabled)
            {
                return;
            }
            if (!System.Uri.TryCreate(EventSourceUrl, System.UriKind.Absolute, out _))
            {
                throw new System.ArgumentException(
                    "EventSourceUrl must be an absolute URI when xRegistry events are enabled.",
                    nameof(EventSourceUrl));
            }
            EnsureName(GroupsAttributeName, nameof(GroupsAttributeName));
            EnsureName(ResourcesAttributeName, nameof(ResourcesAttributeName));
            EnsureName(ResourceDocumentAttributeName, nameof(ResourceDocumentAttributeName));

            static void EnsureName(string value, string name)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new System.ArgumentException(
                        $"{name} is required when xRegistry events are enabled.",
                        name);
                }
            }
        }
    }
}
