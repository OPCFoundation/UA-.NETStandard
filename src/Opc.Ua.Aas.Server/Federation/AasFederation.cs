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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Aas.Server.Federation;
using Opc.Ua.XRegistry;

namespace Opc.Ua.Aas.Server.Federation
{
    /// <summary>
    /// Fail-closed egress policy for AAS federation resolution.
    /// </summary>
    public sealed class AasFederationEgressPolicy
    {
        /// <summary>
        /// Initializes a policy with safe defaults.
        /// </summary>
        public AasFederationEgressPolicy()
        {
        }

        /// <summary>
        /// Allowed URI schemes.
        /// </summary>
        public ISet<string> AllowedSchemes { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "https",
                "opc.tcp"
            };

        /// <summary>
        /// Exclusive host allowlist. Empty means every host not otherwise rejected may be used.
        /// </summary>
        public ISet<string> AllowedHosts { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Exclusive port allowlist. Empty means every explicit or default port may be used.
        /// </summary>
        public ISet<int> AllowedPorts { get; } = new HashSet<int>();

        /// <summary>
        /// Hosts allowed to resolve or connect to otherwise restricted address ranges.
        /// </summary>
        public ISet<string> TrustedRestrictedHosts { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maximum redirects followed during ResourceUrl resolution.
        /// </summary>
        public int MaxRedirects { get; set; } = 5;

        /// <summary>
        /// Maximum response bytes accepted after decompression.
        /// </summary>
        public int MaxDecompressedBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Per-operation timeout.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Credentials scoped to exact peer origins.
        /// </summary>
        public ArrayOf<AasFederationPeerCredential> PeerCredentials { get; set; } =
            new ArrayOf<AasFederationPeerCredential>();

        /// <summary>
        /// OPC UA peers keyed by ServerUri.
        /// </summary>
        public ArrayOf<AasOpcUaPeerPolicy> OpcUaPeers { get; set; } =
            new ArrayOf<AasOpcUaPeerPolicy>();
    }

    /// <summary>
    /// Credential scoped to one exact federation peer origin.
    /// </summary>
    public sealed class AasFederationPeerCredential
    {
        /// <summary>
        /// Initializes a scoped peer credential.
        /// </summary>
        public AasFederationPeerCredential(Uri peerOrigin, string authorizationHeader)
        {
            PeerOrigin = peerOrigin ?? throw new ArgumentNullException(nameof(peerOrigin));
            AuthorizationHeader = authorizationHeader ?? throw new ArgumentNullException(nameof(authorizationHeader));
        }

        /// <summary>
        /// Exact origin allowed to receive this credential.
        /// </summary>
        public Uri PeerOrigin { get; }

        /// <summary>
        /// Authorization header value for the peer origin only.
        /// </summary>
        public string AuthorizationHeader { get; }
    }

    /// <summary>
    /// Configured OPC UA federation peer identity.
    /// </summary>
    public sealed class AasOpcUaPeerPolicy
    {
        /// <summary>
        /// Initializes an OPC UA peer policy.
        /// </summary>
        public AasOpcUaPeerPolicy(string serverUri, Uri endpointUrl, string applicationUri)
        {
            ServerUri = serverUri ?? throw new ArgumentNullException(nameof(serverUri));
            EndpointUrl = endpointUrl ?? throw new ArgumentNullException(nameof(endpointUrl));
            ApplicationUri = applicationUri ?? throw new ArgumentNullException(nameof(applicationUri));
        }

        /// <summary>
        /// ExternalReference ServerUri.
        /// </summary>
        public string ServerUri { get; }

        /// <summary>
        /// Endpoint URL used to open the secure channel.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// Configured federation-peer ApplicationUri identity.
        /// </summary>
        public string ApplicationUri { get; }
    }

    /// <summary>
    /// Result of a fail-closed federation resolution.
    /// </summary>
    public readonly struct AasFederationResolutionResult : IEquatable<AasFederationResolutionResult>
    {
        /// <summary>
        /// Initializes a federation resolution result.
        /// </summary>
        public AasFederationResolutionResult(bool succeeded, ByteString content, string message)
        {
            Succeeded = succeeded;
            Content = content;
            Message = message ?? string.Empty;
        }

        /// <summary>
        /// Whether the resolution succeeded.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Bytes returned by a successful resolution. Empty on every failure.
        /// </summary>
        public ByteString Content { get; }

        /// <summary>
        /// Failure diagnostic.
        /// </summary>
        public string Message { get; }

        /// <inheritdoc/>
        public bool Equals(AasFederationResolutionResult other)
        {
            return Succeeded == other.Succeeded &&
                Content.Equals(other.Content) &&
                string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is AasFederationResolutionResult other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Succeeded ? 1 : 0;
                hash = (hash * 397) ^ Content.GetHashCode();
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Message);
                return hash;
            }
        }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static AasFederationResolutionResult Success(ByteString content)
        {
            return new AasFederationResolutionResult(true, content.Copy(), string.Empty);
        }

        /// <summary>
        /// Creates a failure result with no bytes.
        /// </summary>
        public static AasFederationResolutionResult Fail(string message)
        {
            return new AasFederationResolutionResult(false, ByteString.Empty, message);
        }

        /// <summary>
        /// Compares two federation results for equality.
        /// </summary>
        public static bool operator ==(
            AasFederationResolutionResult left,
            AasFederationResolutionResult right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two federation results for inequality.
        /// </summary>
        public static bool operator !=(
            AasFederationResolutionResult left,
            AasFederationResolutionResult right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Resolves DNS names for federation policy enforcement.
    /// </summary>
    public interface IAasFederationDnsResolver
    {
        /// <summary>
        /// Resolves the host to the addresses policy must check before connecting.
        /// </summary>
        ValueTask<ArrayOf<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
    }

    /// <summary>
    /// HTTP-like transport used by ResourceUrl resolution.
    /// </summary>
    public interface IAasFederationHttpTransport
    {
        /// <summary>
        /// Sends one request without ambient cookies, credentials, proxies or automatic redirects.
        /// </summary>
        ValueTask<AasFederationHttpResponse> SendAsync(
            AasFederationHttpRequest request,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Deferred response content reader.
    /// </summary>
    public interface IAasFederationContentReader
    {
        /// <summary>
        /// Reads response bytes after all redirect and connected-address checks have passed.
        /// </summary>
        ValueTask<ByteString> ReadAsync(int maxDecompressedBytes, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Federation transport request.
    /// </summary>
    public sealed class AasFederationHttpRequest
    {
        /// <summary>
        /// Initializes a transport request.
        /// </summary>
        public AasFederationHttpRequest(Uri uri, string? authorizationHeader)
        {
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
            AuthorizationHeader = authorizationHeader;
        }

        /// <summary>
        /// Request URI.
        /// </summary>
        public Uri Uri { get; }

        /// <summary>
        /// Peer-scoped authorization header, never ambient.
        /// </summary>
        public string? AuthorizationHeader { get; }
    }

    /// <summary>
    /// Federation transport response with deferred content.
    /// </summary>
    public sealed class AasFederationHttpResponse
    {
        /// <summary>
        /// Initializes a transport response.
        /// </summary>
        public AasFederationHttpResponse(
            int statusCode,
            IPAddress connectedAddress,
            Uri? redirectLocation,
            long? contentLength,
            IAasFederationContentReader contentReader)
        {
            StatusCode = statusCode;
            ConnectedAddress = connectedAddress ?? throw new ArgumentNullException(nameof(connectedAddress));
            RedirectLocation = redirectLocation;
            ContentLength = contentLength;
            ContentReader = contentReader ?? throw new ArgumentNullException(nameof(contentReader));
        }

        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Address actually connected to by the transport.
        /// </summary>
        public IPAddress ConnectedAddress { get; }

        /// <summary>
        /// Redirect location for 3xx responses.
        /// </summary>
        public Uri? RedirectLocation { get; }

        /// <summary>
        /// Optional response size before decompression.
        /// </summary>
        public long? ContentLength { get; }

        /// <summary>
        /// Deferred content reader.
        /// </summary>
        public IAasFederationContentReader ContentReader { get; }
    }

    /// <summary>
    /// Validates every federation URI, DNS result and connected address before bytes are read.
    /// </summary>
    public sealed class AasFederationEndpointValidator
    {
        /// <summary>
        /// Initializes a federation endpoint validator.
        /// </summary>
        public AasFederationEndpointValidator(AasFederationEgressPolicy? policy = null)
        {
            Policy = policy ?? new AasFederationEgressPolicy();
        }

        /// <summary>
        /// Policy enforced by the validator.
        /// </summary>
        public AasFederationEgressPolicy Policy { get; }

        /// <summary>
        /// Validates scheme, host and port before DNS or connection.
        /// </summary>
        public AasFederationResolutionResult ValidateUri(Uri uri)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            if (!uri.IsAbsoluteUri)
            {
                return AasFederationResolutionResult.Fail("Federation target must be an absolute URI.");
            }
            if (!Policy.AllowedSchemes.Contains(uri.Scheme))
            {
                return AasFederationResolutionResult.Fail("Federation target scheme is not allowed.");
            }
            if (Policy.AllowedHosts.Count > 0 && !Policy.AllowedHosts.Contains(uri.Host))
            {
                return AasFederationResolutionResult.Fail("Federation target host is not allowed.");
            }
            if (Policy.AllowedPorts.Count > 0 && !Policy.AllowedPorts.Contains(EffectivePort(uri)))
            {
                return AasFederationResolutionResult.Fail("Federation target port is not allowed.");
            }
            if (IsLocalHostName(uri.Host) && !Policy.TrustedRestrictedHosts.Contains(uri.Host))
            {
                return AasFederationResolutionResult.Fail("Federation target host is a localhost alias.");
            }
            if (IPAddress.TryParse(uri.Host, out IPAddress? literal))
            {
                return ValidateAddress(uri.Host, literal);
            }

            return AasFederationResolutionResult.Success(ByteString.Empty);
        }

        /// <summary>
        /// Validates a DNS result or connected address for a URI host.
        /// </summary>
        public AasFederationResolutionResult ValidateAddress(string host, IPAddress address)
        {
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }
            if (address is null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            IPAddress normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            if (IsRestrictedAddress(normalized) && !Policy.TrustedRestrictedHosts.Contains(host))
            {
                return AasFederationResolutionResult.Fail(
                    "Federation target address is restricted and is not explicitly trusted.");
            }

            return AasFederationResolutionResult.Success(ByteString.Empty);
        }

        internal static bool IsRestrictedAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }
            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
                address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None))
            {
                return true;
            }
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();
                if (bytes[0] == 0 || bytes[0] >= 240)
                {
                    return true;
                }
                if (bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 169 && bytes[1] == 254) ||
                    (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                    (bytes[0] == 127) ||
                    (bytes[0] >= 224 && bytes[0] <= 239) ||
                    (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) ||
                    (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) ||
                    (bytes[0] == 198 && bytes[1] == 18) ||
                    (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
                    (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113))
                {
                    return true;
                }

                return false;
            }
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                byte[] bytes = address.GetAddressBytes();
                if (address.IsIPv6LinkLocal || address.IsIPv6Multicast)
                {
                    return true;
                }
                if ((bytes[0] & 0xFE) == 0xFC)
                {
                    return true;
                }
                if (bytes[0] == 0x00 || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D &&
                    bytes[3] == 0xB8))
                {
                    return true;
                }
            }

            return false;
        }

        private static int EffectivePort(Uri uri)
        {
            if (!uri.IsDefaultPort)
            {
                return uri.Port;
            }
            if (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return 443;
            }
            if (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                return 80;
            }

            return uri.Port;
        }

        private static bool IsLocalHostName(string host)
        {
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "ip6-localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "ip6-loopback", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Resolves ResourceUrl federation links with fail-closed byte handling.
    /// </summary>
    public sealed class AasResourceUrlFederationResolver
    {
        /// <summary>
        /// Initializes a ResourceUrl resolver.
        /// </summary>
        public AasResourceUrlFederationResolver(
            AasFederationEgressPolicy? policy = null,
            IAasFederationDnsResolver? dnsResolver = null,
            IAasFederationHttpTransport? transport = null)
        {
            Policy = policy ?? new AasFederationEgressPolicy();
            m_validator = new AasFederationEndpointValidator(Policy);
            m_dnsResolver = dnsResolver ?? new DefaultAasFederationDnsResolver();
            m_transport = transport ?? new HttpClientAasFederationTransport();
        }

        /// <summary>
        /// Policy enforced by the resolver.
        /// </summary>
        public AasFederationEgressPolicy Policy { get; }

        /// <summary>
        /// Resolves a ResourceUrl external locator.
        /// </summary>
        public async ValueTask<AasFederationResolutionResult> ResolveAsync(
            Uri resourceUrl,
            CancellationToken cancellationToken = default)
        {
            if (resourceUrl is null)
            {
                throw new ArgumentNullException(nameof(resourceUrl));
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (Policy.Timeout > TimeSpan.Zero)
            {
                timeout.CancelAfter(Policy.Timeout);
            }

            Uri current = resourceUrl;
            for (int redirect = 0; redirect <= Policy.MaxRedirects; redirect++)
            {
                AasFederationResolutionResult validation = await ValidateBeforeConnectAsync(
                    current, timeout.Token).ConfigureAwait(false);
                if (!validation.Succeeded)
                {
                    return validation;
                }

                AasFederationHttpRequest request = new AasFederationHttpRequest(
                    current, CredentialFor(current));
                AasFederationHttpResponse response =
                    await m_transport.SendAsync(request, timeout.Token).ConfigureAwait(false);

                // Every hop opens a response whose content reader holds the
                // connection, and only the last hop is ever read, so each one
                // is released before the next is opened or the loop returns.
                try
                {
                    validation = m_validator.ValidateAddress(current.Host, response.ConnectedAddress);
                    if (!validation.Succeeded)
                    {
                        return validation;
                    }

                    if (IsRedirect(response.StatusCode))
                    {
                        if (response.RedirectLocation is null)
                        {
                            return AasFederationResolutionResult.Fail(
                                "Redirect response did not include a location.");
                        }
                        if (redirect == Policy.MaxRedirects)
                        {
                            return AasFederationResolutionResult.Fail(
                                "Federation redirect limit was exceeded.");
                        }

                        current = response.RedirectLocation.IsAbsoluteUri
                            ? response.RedirectLocation
                            : new Uri(current, response.RedirectLocation);
                        continue;
                    }

                    if (response.ContentLength.HasValue &&
                        response.ContentLength.Value > Policy.MaxDecompressedBytes)
                    {
                        return AasFederationResolutionResult.Fail(
                            "Federation response size bound was exceeded.");
                    }

                    if (response.StatusCode < 200 || response.StatusCode > 299)
                    {
                        return AasFederationResolutionResult.Fail(
                            "Federation target returned an unsuccessful status.");
                    }

                    ByteString content = await response.ContentReader
                        .ReadAsync(Policy.MaxDecompressedBytes, timeout.Token)
                        .ConfigureAwait(false);
                    if (content.Length > Policy.MaxDecompressedBytes)
                    {
                        return AasFederationResolutionResult.Fail(
                            "Federation decompressed response size bound was exceeded.");
                    }

                    return AasFederationResolutionResult.Success(content);
                }
                finally
                {
                    (response.ContentReader as IDisposable)?.Dispose();
                }
            }

            return AasFederationResolutionResult.Fail("Federation redirect limit was exceeded.");
        }

        private async ValueTask<AasFederationResolutionResult> ValidateBeforeConnectAsync(
            Uri uri,
            CancellationToken cancellationToken)
        {
            AasFederationResolutionResult validation = m_validator.ValidateUri(uri);
            if (!validation.Succeeded)
            {
                return validation;
            }

            ArrayOf<IPAddress> addresses = await m_dnsResolver.ResolveAsync(uri.Host, cancellationToken)
                .ConfigureAwait(false);
            if (addresses.Count == 0)
            {
                return AasFederationResolutionResult.Fail("Federation target DNS resolution returned no addresses.");
            }

            for (int ii = 0; ii < addresses.Count; ii++)
            {
                validation = m_validator.ValidateAddress(uri.Host, addresses[ii]);
                if (!validation.Succeeded)
                {
                    return validation;
                }
            }

            return AasFederationResolutionResult.Success(ByteString.Empty);
        }

        private string? CredentialFor(Uri uri)
        {
            Uri origin = new Uri(uri.GetLeftPart(UriPartial.Authority));
            for (int ii = 0; ii < Policy.PeerCredentials.Count; ii++)
            {
                AasFederationPeerCredential credential = Policy.PeerCredentials[ii];
                if (Uri.Compare(
                    credential.PeerOrigin,
                    origin,
                    UriComponents.SchemeAndServer,
                    UriFormat.SafeUnescaped,
                    StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return credential.AuthorizationHeader;
                }
            }

            return null;
        }

        private static bool IsRedirect(int statusCode)
        {
            return statusCode == 301 || statusCode == 302 || statusCode == 303 ||
                statusCode == 307 || statusCode == 308;
        }

        private readonly AasFederationEndpointValidator m_validator;
        private readonly IAasFederationDnsResolver m_dnsResolver;
        private readonly IAasFederationHttpTransport m_transport;
    }

    /// <summary>
    /// Reads remote OPC UA federation references after endpoint and identity validation.
    /// </summary>
    public sealed class AasOpcUaFederationResolver
    {
        /// <summary>
        /// Initializes an OPC UA federation resolver.
        /// </summary>
        public AasOpcUaFederationResolver(
            AasFederationEgressPolicy? policy,
            IAasFederationDnsResolver? dnsResolver,
            IAasOpcUaFederationClient client)
        {
            Policy = policy ?? new AasFederationEgressPolicy();
            m_validator = new AasFederationEndpointValidator(Policy);
            m_dnsResolver = dnsResolver ?? new DefaultAasFederationDnsResolver();
            m_client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Policy enforced by the resolver.
        /// </summary>
        public AasFederationEgressPolicy Policy { get; }

        /// <summary>
        /// Resolves an OPC UA ExternalReference using Annex E's remote path.
        /// </summary>
        public async ValueTask<AasFederationResolutionResult> ResolveAsync(
            AasOpcUaExternalReference externalReference,
            string localServerUri,
            CancellationToken cancellationToken = default)
        {
            if (externalReference is null)
            {
                throw new ArgumentNullException(nameof(externalReference));
            }
            if (localServerUri is null)
            {
                throw new ArgumentNullException(nameof(localServerUri));
            }
            if (string.IsNullOrEmpty(externalReference.ServerUri) ||
                string.Equals(externalReference.ServerUri, localServerUri, StringComparison.Ordinal))
            {
                return await m_client.ReadLocalAsync(externalReference.NodeId, cancellationToken)
                    .ConfigureAwait(false);
            }

            AasOpcUaPeerPolicy? peer = FindPeer(externalReference.ServerUri);
            if (peer is null)
            {
                return AasFederationResolutionResult.Fail("OPC UA federation peer is not configured.");
            }

            AasFederationResolutionResult validation = m_validator.ValidateUri(peer.EndpointUrl);
            if (!validation.Succeeded)
            {
                return validation;
            }

            ArrayOf<IPAddress> addresses = await m_dnsResolver
                .ResolveAsync(peer.EndpointUrl.Host, cancellationToken)
                .ConfigureAwait(false);
            for (int ii = 0; ii < addresses.Count; ii++)
            {
                validation = m_validator.ValidateAddress(peer.EndpointUrl.Host, addresses[ii]);
                if (!validation.Succeeded)
                {
                    return validation;
                }
            }

            AasOpcUaPeerIdentity identity = await m_client
                .DiscoverAsync(peer.EndpointUrl, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(identity.CertificateApplicationUri, peer.ApplicationUri, StringComparison.Ordinal) ||
                !string.Equals(identity.ServerApplicationUri, peer.ApplicationUri, StringComparison.Ordinal))
            {
                return AasFederationResolutionResult.Fail("OPC UA peer ApplicationUri identities do not agree.");
            }

            validation = m_validator.ValidateAddress(peer.EndpointUrl.Host, identity.ConnectedAddress);
            if (!validation.Succeeded)
            {
                return validation;
            }

            return await m_client.ReadRemoteAsync(peer.EndpointUrl, externalReference, cancellationToken)
                .ConfigureAwait(false);
        }

        private AasOpcUaPeerPolicy? FindPeer(string serverUri)
        {
            for (int ii = 0; ii < Policy.OpcUaPeers.Count; ii++)
            {
                AasOpcUaPeerPolicy peer = Policy.OpcUaPeers[ii];
                if (string.Equals(peer.ServerUri, serverUri, StringComparison.Ordinal))
                {
                    return peer;
                }
            }

            return null;
        }

        private readonly AasFederationEndpointValidator m_validator;
        private readonly IAasFederationDnsResolver m_dnsResolver;
        private readonly IAasOpcUaFederationClient m_client;
    }

    /// <summary>
    /// ExternalReference data needed by the Annex E OPC UA resolver.
    /// </summary>
    public sealed class AasOpcUaExternalReference
    {
        /// <summary>
        /// Initializes an OPC UA external reference.
        /// </summary>
        public AasOpcUaExternalReference(string serverUri, ExpandedNodeId nodeId)
        {
            ServerUri = serverUri ?? string.Empty;
            NodeId = nodeId;
        }

        /// <summary>
        /// ServerUri identifying the hosting endpoint.
        /// </summary>
        public string ServerUri { get; }

        /// <summary>
        /// Remote node identity including NamespaceUri and identifier.
        /// </summary>
        public ExpandedNodeId NodeId { get; }
    }

    /// <summary>
    /// Identity values discovered for an OPC UA peer.
    /// </summary>
    public sealed class AasOpcUaPeerIdentity
    {
        /// <summary>
        /// Initializes discovered OPC UA peer identity values.
        /// </summary>
        public AasOpcUaPeerIdentity(
            string certificateApplicationUri,
            string serverApplicationUri,
            IPAddress connectedAddress)
        {
            CertificateApplicationUri = certificateApplicationUri ??
                throw new ArgumentNullException(nameof(certificateApplicationUri));
            ServerApplicationUri = serverApplicationUri ??
                throw new ArgumentNullException(nameof(serverApplicationUri));
            ConnectedAddress = connectedAddress ?? throw new ArgumentNullException(nameof(connectedAddress));
        }

        /// <summary>
        /// ApplicationUri from the validated endpoint certificate.
        /// </summary>
        public string CertificateApplicationUri { get; }

        /// <summary>
        /// Server ApplicationUri returned by discovery.
        /// </summary>
        public string ServerApplicationUri { get; }

        /// <summary>
        /// Connected address for DNS-rebinding validation.
        /// </summary>
        public IPAddress ConnectedAddress { get; }
    }

    /// <summary>
    /// OPC UA client seam used by the federation resolver.
    /// </summary>
    public interface IAasOpcUaFederationClient
    {
        /// <summary>
        /// Discovers endpoint identity after endpoint certificate trust validation.
        /// </summary>
        ValueTask<AasOpcUaPeerIdentity> DiscoverAsync(Uri endpointUrl, CancellationToken cancellationToken);

        /// <summary>
        /// Reads the local entity when ServerUri is empty or local.
        /// </summary>
        ValueTask<AasFederationResolutionResult> ReadLocalAsync(
            ExpandedNodeId nodeId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Opens a secure channel and session, translates NamespaceUri, and reads the remote entity.
        /// </summary>
        ValueTask<AasFederationResolutionResult> ReadRemoteAsync(
            Uri endpointUrl,
            AasOpcUaExternalReference externalReference,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Identifier attributes retained by a local proxy for a remote AAS entity.
    /// </summary>
    public sealed class AasFederatedIdentifierAttribute
    {
        /// <summary>
        /// Initializes an identifier attribute.
        /// </summary>
        public AasFederatedIdentifierAttribute(string name, string value)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Attribute name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Attribute value.
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// Endpoint-independent AAS federation identity.
    /// </summary>
    public sealed class AasFederatedEntityIdentity
    {
        /// <summary>
        /// Initializes an endpoint-independent federation identity.
        /// </summary>
        public AasFederatedEntityIdentity(ArrayOf<AasFederatedIdentifierAttribute> attributes)
        {
            IdentifierAttributes = attributes;
            DerivedIdentifier = AasFederationIdentity.DeriveIdentifier(attributes);
        }

        /// <summary>
        /// Remote entity identifier attributes retained by the proxy.
        /// </summary>
        public ArrayOf<AasFederatedIdentifierAttribute> IdentifierAttributes { get; }

        /// <summary>
        /// Deterministic identifier derived from the attributes, never from an endpoint.
        /// </summary>
        public string DerivedIdentifier { get; }
    }

    /// <summary>
    /// Constructs endpoint-independent AAS federation identities.
    /// </summary>
    public static class AasFederationIdentity
    {
        /// <summary>
        /// Creates a proxy identity that retains the remote attributes and ignores local endpoint identity.
        /// </summary>
        public static AasFederatedEntityIdentity CreateProxyIdentity(
            ArrayOf<AasFederatedIdentifierAttribute> remoteAttributes,
            Uri localEndpoint)
        {
            if (localEndpoint is null)
            {
                throw new ArgumentNullException(nameof(localEndpoint));
            }

            return new AasFederatedEntityIdentity(Copy(remoteAttributes));
        }

        /// <summary>
        /// Derives the stable identifier from AAS identifier attributes.
        /// </summary>
        public static string DeriveIdentifier(ArrayOf<AasFederatedIdentifierAttribute> attributes)
        {
            var parts = new List<string>();
            for (int ii = 0; ii < attributes.Count; ii++)
            {
                parts.Add(attributes[ii].Name + "=" + attributes[ii].Value);
            }

            parts.Sort(StringComparer.Ordinal);
            return XRegistryIdentifier.FromSourceIdentity(string.Join("|", parts.ToArray()));
        }

        private static ArrayOf<AasFederatedIdentifierAttribute> Copy(
            ArrayOf<AasFederatedIdentifierAttribute> attributes)
        {
            var copy = new AasFederatedIdentifierAttribute[attributes.Count];
            for (int ii = 0; ii < attributes.Count; ii++)
            {
                copy[ii] = attributes[ii];
            }

            return new ArrayOf<AasFederatedIdentifierAttribute>(copy);
        }
    }

    internal sealed class DefaultAasFederationDnsResolver : IAasFederationDnsResolver
    {
        public async ValueTask<ArrayOf<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (IPAddress.TryParse(host, out IPAddress? literal))
            {
                return new ArrayOf<IPAddress>(new[] { literal });
            }

#if NET6_0_OR_GREATER
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
                .ConfigureAwait(false);
#else
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
#endif
            return new ArrayOf<IPAddress>(addresses);
        }
    }

    internal sealed class HttpClientAasFederationTransport : IAasFederationHttpTransport
    {
        public HttpClientAasFederationTransport()
            : this(CreateClient())
        {
        }

        public HttpClientAasFederationTransport(HttpClient client)
        {
            m_client = client ?? throw new ArgumentNullException(nameof(client));
        }

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the reader, and through it the response, transfers to the " +
                "caller, which disposes it once per redirect hop. CA2000 cannot model that transfer " +
                "through a return value. TODO: remove when CA2000 recognizes it.")]
        public async ValueTask<AasFederationHttpResponse> SendAsync(
            AasFederationHttpRequest request,
            CancellationToken cancellationToken)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.Uri);
            if (!string.IsNullOrEmpty(request.AuthorizationHeader))
            {
                httpRequest.Headers.TryAddWithoutValidation("Authorization", request.AuthorizationHeader);
            }

            HttpResponseMessage response = await m_client
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            Uri? location = response.Headers.Location;
            return new AasFederationHttpResponse(
                (int)response.StatusCode,
                GetConnectedAddress(httpRequest),
                location,
                response.Content.Headers.ContentLength,
                new HttpContentReader(response));
        }

        /// <summary>
        /// Reports the address the transport actually connected to.
        /// </summary>
        /// <remarks>
        /// Revalidating a second name resolution would prove nothing: a name
        /// that resolves to a permitted address twice can still have carried a
        /// different one into the socket, which is the whole of a DNS rebinding
        /// attack. Only the peer the connection reached says anything, so the
        /// connect callback records it and this reads it back. Where the
        /// platform cannot supply it, the address is reported as
        /// <see cref="IPAddress.None"/>, which the endpoint validator treats as
        /// restricted, so the request fails closed rather than passing on an
        /// approximation.
        /// </remarks>
        private static IPAddress GetConnectedAddress(HttpRequestMessage request)
        {
#if NET6_0_OR_GREATER
            if (request.Options.TryGetValue(s_connectedAddress, out IPAddress? address) &&
                address is not null)
            {
                return address;
            }
#endif
            return IPAddress.None;
        }

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Handler ownership is transferred to HttpClient with disposeHandler: true. " +
                "TODO: replace with a shared generalized egress transport.")]
        private static HttpClient CreateClient()
        {
#if NET6_0_OR_GREATER
            SocketsHttpHandler? socketsHandler = new SocketsHttpHandler();
            try
            {
                socketsHandler.AllowAutoRedirect = false;
                socketsHandler.UseCookies = false;
                socketsHandler.UseProxy = false;
                socketsHandler.Proxy = null;
                socketsHandler.Credentials = null;
                socketsHandler.SslOptions.CertificateRevocationCheckMode =
                    System.Security.Cryptography.X509Certificates.X509RevocationMode.Online;
                socketsHandler.ConnectCallback = ConnectAndRecordPeerAsync;
                HttpClient socketsClient = new HttpClient(socketsHandler, disposeHandler: true);
                socketsHandler = null;
                return socketsClient;
            }
            catch
            {
                socketsHandler?.Dispose();
                throw;
            }
#else
            HttpClientHandler? handler = new HttpClientHandler();
            try
            {
                handler.AllowAutoRedirect = false;
                handler.CheckCertificateRevocationList = true;
                handler.UseCookies = false;
                handler.UseDefaultCredentials = false;
                handler.PreAuthenticate = false;
                handler.Proxy = null;
                handler.UseProxy = false;
                HttpClient client = new HttpClient(handler, disposeHandler: true);
                handler = null;
                return client;
            }
            catch
            {
                handler?.Dispose();
                throw;
            }
#endif
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Connects and records the peer the socket actually reached.
        /// </summary>
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "The socket is owned by the NetworkStream that is returned, which the " +
                "handler disposes with the connection, and it is disposed here on the failure path. " +
                "TODO: remove when CA2000 recognizes ownsSocket.")]
        private static async ValueTask<Stream> ConnectAndRecordPeerAsync(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
                if (socket.RemoteEndPoint is IPEndPoint peer)
                {
                    context.InitialRequestMessage.Options.Set(s_connectedAddress, peer.Address);
                }
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static readonly HttpRequestOptionsKey<IPAddress?> s_connectedAddress =
            new("Opc.Ua.Aas.Federation.ConnectedAddress");
#endif


        private readonly HttpClient m_client;
    }

    internal sealed class HttpContentReader : IAasFederationContentReader, IDisposable
    {
        public HttpContentReader(HttpResponseMessage response)
        {
            m_response = response ?? throw new ArgumentNullException(nameof(response));
        }

        public async ValueTask<ByteString> ReadAsync(
            int maxDecompressedBytes,
            CancellationToken cancellationToken)
        {
            // The bound has to be enforced while reading rather than after.
            // Buffering the whole body first and measuring it afterwards lets a
            // hostile or compromised peer exhaust memory with a body that the
            // policy would have rejected, which is the opposite of a bounded
            // egress. One byte beyond the bound is enough to reject.
#if NET5_0_OR_GREATER
            using Stream stream = await m_response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
#else
            using Stream stream = await m_response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
#endif
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            long limit = (long)maxDecompressedBytes + 1;
            while (buffer.Length < limit)
            {
                int toRead = (int)Math.Min(chunk.Length, limit - buffer.Length);
#if NET5_0_OR_GREATER || NETSTANDARD2_1
                int read = await stream.ReadAsync(chunk.AsMemory(0, toRead), cancellationToken)
                    .ConfigureAwait(false);
#else
                int read = await stream.ReadAsync(chunk, 0, toRead, cancellationToken)
                    .ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    break;
                }
                buffer.Write(chunk, 0, read);
            }

            return ByteString.From(buffer.ToArray());
        }

        public void Dispose()
        {
            m_response.Dispose();
        }

        private readonly HttpResponseMessage m_response;
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers AAS federation services.
    /// </summary>
    public static class AasFederationServiceCollectionExtensions
    {
        /// <summary>
        /// Adds default federation ResourceUrl resolver services.
        /// </summary>
        public static IServiceCollection AddAasFederation(this IServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<AasFederationEgressPolicy>();
            services.TryAddSingleton<IAasFederationDnsResolver, DefaultAasFederationDnsResolver>();
            services.TryAddSingleton<IAasFederationHttpTransport, HttpClientAasFederationTransport>();
            services.TryAddSingleton<AasResourceUrlFederationResolver>();
            return services;
        }
    }
}
