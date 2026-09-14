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

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// An immutable, explicitly trusted binding to a remote logical Resource. Configure this
    /// independently of document bytes, endpoint locators and metadata supplied by a remote peer.
    /// </summary>
    public sealed class XRegistryFederationTarget
    {
        /// <summary>
        /// Pins the origin, logical application, portable Resource NodeId and origin-relative Xid.
        /// </summary>
        /// <param name="originRegistry">The configured, exclusive origin identity.</param>
        /// <param name="serverUri">The trusted logical ApplicationUri, not an endpoint URL.</param>
        /// <param name="resourceNodeId">The logical Resource NodeId with a namespace URI and no server index.</param>
        /// <param name="resourceXid">The remote logical Resource Xid, without an exact Version suffix.</param>
        /// <exception cref="ArgumentNullException">The origin is null.</exception>
        /// <exception cref="ArgumentException">An identity is incomplete, non-portable or contradictory.</exception>
        public XRegistryFederationTarget(
            RegistryOriginDataType originRegistry,
            string serverUri,
            ExpandedNodeId resourceNodeId,
            string resourceXid)
        {
            if (originRegistry is null)
            {
                throw new ArgumentNullException(nameof(originRegistry));
            }
            RequireAbsoluteUri(serverUri, nameof(serverUri));
            RequirePortableNodeId(resourceNodeId, nameof(resourceNodeId));

            string originUri = originRegistry.OriginUri ??
                throw new ArgumentException("OriginUri must be present, even when empty.", nameof(originRegistry));
            string originServerUri = originRegistry.ServerUri ??
                throw new ArgumentException("ServerUri must be present, even when empty.", nameof(originRegistry));
            bool uriOrigin = originUri.Length != 0;
            if (uriOrigin)
            {
                RequireAbsoluteUri(originUri, nameof(originRegistry));
                if (originServerUri.Length != 0 || !originRegistry.RegistryNodeId.IsNull)
                {
                    throw new ArgumentException(
                        "Exactly one origin form must be authoritative.", nameof(originRegistry));
                }
            }
            else
            {
                RequireAbsoluteUri(originServerUri, nameof(originRegistry));
                RequirePortableNodeId(originRegistry.RegistryNodeId, nameof(originRegistry));
                if (!string.Equals(originServerUri, serverUri, StringComparison.Ordinal) ||
                    originRegistry.RegistryNodeId == resourceNodeId)
                {
                    throw new ArgumentException(
                        "The application/root origin must agree with the server and differ from the Resource.",
                        nameof(originRegistry));
                }
            }

            string[] parts = (resourceXid ?? string.Empty).Split('/');
            if (parts.Length != 5 || parts[0].Length != 0)
            {
                throw new ArgumentException("A concrete logical Resource Xid is required.", nameof(resourceXid));
            }
            for (int index = 1; index < parts.Length; index++)
            {
                if (parts[index].Length == 0 || parts[index] is "." or "..")
                {
                    throw new ArgumentException("The Resource Xid contains an empty or relative segment.",
                        nameof(resourceXid));
                }
                foreach (char character in parts[index])
                {
                    if (character is not ((>= 'A' and <= 'Z') or
                        (>= 'a' and <= 'z') or
                        (>= '0' and <= '9') or '_' or '.' or '-'))
                    {
                        throw new ArgumentException("The Resource Xid contains an invalid identifier.",
                            nameof(resourceXid));
                    }
                }
            }

            m_originUri = originUri;
            m_originServerUri = originServerUri;
            m_registryNodeId = originRegistry.RegistryNodeId;
            ServerUri = serverUri;
            ResourceNodeId = resourceNodeId;
            ResourceXid = resourceXid!;
            GroupXid = $"/{parts[1]}/{parts[2]}";
            GroupId = parts[2];
            ResourceId = parts[4];
        }

        /// <summary>
        /// Gets a copy of the pinned origin. Mutating this wire Structure cannot change the binding.
        /// </summary>
        public RegistryOriginDataType OriginRegistry => new()
        {
            OriginUri = m_originUri,
            ServerUri = m_originServerUri,
            RegistryNodeId = m_registryNodeId
        };

        /// <summary>
        /// Gets the trusted logical ApplicationUri used to resolve ServerArray references.
        /// </summary>
        public string ServerUri { get; }

        /// <summary>
        /// Gets the portable remote logical Resource NodeId, independent of content and endpoint.
        /// </summary>
        public ExpandedNodeId ResourceNodeId { get; }

        /// <summary>
        /// Gets the concrete origin-relative logical Resource Xid.
        /// </summary>
        public string ResourceXid { get; }

        /// <summary>
        /// Gets the origin-relative Xid of the owning Group.
        /// </summary>
        public string GroupXid { get; }

        /// <summary>
        /// Gets the remote Group's assigned identifier.
        /// </summary>
        public string GroupId { get; }

        /// <summary>
        /// Gets the remote Resource's assigned identifier, not the local proxy's identifier.
        /// </summary>
        public string ResourceId { get; }

        /// <summary>
        /// Compares observed origin metadata exactly against the configured binding.
        /// The observation never establishes trust by itself.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The observed origin does not match the trusted binding.
        /// </exception>
        public void VerifyOrigin(RegistryOriginDataType originRegistry)
        {
            if (originRegistry is null ||
                !string.Equals(m_originUri, originRegistry.OriginUri, StringComparison.Ordinal) ||
                !string.Equals(m_originServerUri, originRegistry.ServerUri, StringComparison.Ordinal) ||
                m_registryNodeId != originRegistry.RegistryNodeId)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The observed federation origin is not the trusted origin.");
            }
        }

        /// <summary>
        /// Validates a locator before a provider can use it. Authorization remains the provider's responsibility.
        /// </summary>
        /// <exception cref="ArgumentException">The endpoint is not a well-formed absolute URI.</exception>
        public static void ValidateEndpoint(string endpointUrl)
        {
            RequireAbsoluteUri(endpointUrl, nameof(endpointUrl));
            var uri = new Uri(endpointUrl, UriKind.Absolute);
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException(
                    "A locator must not contain credentials or a fragment.", nameof(endpointUrl));
            }
        }

        private static void RequireAbsoluteUri(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                char.IsWhiteSpace(value[0]) ||
                char.IsWhiteSpace(value[^1]) ||
                !Uri.IsWellFormedUriString(value, UriKind.Absolute) ||
                !Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                throw new ArgumentException("A well-formed absolute URI is required.", parameterName);
            }
        }

        private static void RequirePortableNodeId(ExpandedNodeId nodeId, string parameterName)
        {
            if (nodeId.IsNull || nodeId.NamespaceIndex != 0 || nodeId.ServerIndex != 0)
            {
                throw new ArgumentException("A non-null, URI-qualified NodeId without session indexes is required.",
                    parameterName);
            }
            RequireAbsoluteUri(nodeId.NamespaceUri!, parameterName);
        }

        private readonly string m_originUri;
        private readonly string m_originServerUri;
        private readonly ExpandedNodeId m_registryNodeId;
    }
}
