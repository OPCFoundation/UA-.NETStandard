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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Default-deny HTTP transport for document dependencies, not a WoT binding executor.
    /// </summary>
    internal static class WotDependencyHttp
    {
        /// <summary>Dedicated client name; no credentials or unrelated client headers are inherited.</summary>
        internal const string ClientName = "Opc.Ua.Mcp.WotDocuments";

        /// <summary>
        /// Normalizes a host-configured origin and rejects paths, credentials, queries, fragments and non-HTTPS.
        /// </summary>
        internal static string ParseOrigin(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 4096 ||
                !Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                uri.Host.Contains('*', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Dependency origins must be exact HTTPS scheme/host/port origins.", nameof(text));
            }
            return Origin(uri);
        }

        /// <summary>
        /// Applies both the exact-origin allowlist and the existing WoT literal-address policy.
        /// </summary>
        internal static Uri RequireAllowed(Uri uri, WotMcpOptions options)
        {
            ArgumentNullException.ThrowIfNull(uri);
            if (!uri.IsAbsoluteUri || uri.AbsoluteUri.Length > 4096 ||
                uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new UnauthorizedAccessException("Only credential-free HTTPS dependency URIs are permitted.");
            }
            string origin = Origin(uri);
            bool allowed = false;
            foreach (string entry in options.AllowedDependencyOrigins)
            {
                if (string.Equals(ParseOrigin(entry), origin, StringComparison.OrdinalIgnoreCase))
                {
                    allowed = true;
                    break;
                }
            }
            if (!allowed)
            {
                throw new UnauthorizedAccessException("The dependency origin is not on the host allowlist.");
            }
            ServiceResult result = WotEndpointValidator.Validate(
                uri.AbsoluteUri, AddressPolicy(options), out Uri? normalized);
            if (ServiceResult.IsBad(result) || normalized is null)
            {
                throw new UnauthorizedAccessException("The dependency address is prohibited by host network policy.");
            }
            if (IPAddress.TryParse(normalized.IdnHost, out IPAddress? address) && !IsAddressAllowed(address, options))
            {
                throw new UnauthorizedAccessException("The dependency address is prohibited by host network policy.");
            }
            return new UriBuilder(normalized) { Fragment = string.Empty }.Uri;
        }

        /// <summary>
        /// Builds a handler that cannot bypass origin/address policy through proxies or redirects.
        /// </summary>
        internal static SocketsHttpHandler CreateHandler(WotMcpOptions options)
        {
            WotMcpOptions policy = options.Snapshot();
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                Credentials = null,
                MaxConnectionsPerServer = 2,
                AutomaticDecompression = DecompressionMethods.None,
                SslOptions = { CertificateRevocationCheckMode = X509RevocationMode.Online },
                ConnectCallback = (context, ct) => ConnectAsync(context, policy, ct)
            };
        }

        /// <summary>
        /// Reuses WoT's address classification for the exact address subsequently passed to Socket.ConnectAsync.
        /// </summary>
        internal static bool IsAddressAllowed(IPAddress address, WotMcpOptions options)
        {
            ArgumentNullException.ThrowIfNull(address);
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
                address.Equals(IPAddress.Broadcast) || address.IsIPv6Multicast ||
                (address.IsIPv6SiteLocal && !options.AllowPrivateDependencyAddresses) ||
                (address.AddressFamily == AddressFamily.InterNetwork &&
                    address.GetAddressBytes()[0] is 0 or >= 224))
            {
                return false;
            }
            var uri = new UriBuilder(Uri.UriSchemeHttps, address.ToString());
            return ServiceResult.IsGood(
                WotEndpointValidator.Validate(uri.Uri.AbsoluteUri, AddressPolicy(options), out _));
        }

        /// <summary>
        /// Omits user information, query and fragment from dependency provenance.
        /// </summary>
        internal static string SafeReference(string reference)
        {
            if (Uri.TryCreate(reference, UriKind.Absolute, out Uri? uri))
            {
                return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
            }
            if (reference.Contains("://", StringComparison.Ordinal))
            {
                return "[invalid document address]";
            }
            int suffix = reference.IndexOfAny(['?', '#']);
            return suffix < 0 ? reference : reference[..suffix];
        }

        /// <summary>
        /// Resolves once, checks each address, and connects to that same address without a DNS TOCTOU window.
        /// </summary>
        private static async ValueTask<Stream> ConnectAsync(
            SocketsHttpConnectionContext context, WotMcpOptions options, CancellationToken ct)
        {
            Uri requestUri = context.InitialRequestMessage.RequestUri ??
                throw new HttpRequestException("The dependency request has no URI.");
            _ = RequireAllowed(requestUri, options);
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
            SocketException? failure = null;
            foreach (IPAddress address in addresses)
            {
                if (!IsAddressAllowed(address, options))
                {
                    continue;
                }
                Socket? socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct)
                        .ConfigureAwait(false);
                    var stream = new NetworkStream(socket, ownsSocket: true);
                    socket = null;
                    return stream;
                }
                catch (SocketException exception)
                {
                    failure = exception;
                }
                finally
                {
                    socket?.Dispose();
                }
            }
            if (failure is not null)
            {
                throw new HttpRequestException("No approved dependency address accepted the connection.", failure);
            }
            throw new HttpRequestException("DNS returned no address permitted by the dependency network policy.");
        }

        /// <summary>
        /// Keeps host-list comparison separate from literal and resolved-address validation.
        /// </summary>
        private static WotEndpointPolicy AddressPolicy(WotMcpOptions options)
        {
            var policy = new WotEndpointPolicy
            {
                AllowLoopback = false,
                AllowPrivateAddresses = options.AllowPrivateDependencyAddresses
            };
            policy.AllowedSchemes.Clear();
            policy.AllowedSchemes.Add(Uri.UriSchemeHttps);
            return policy;
        }

        /// <summary>
        /// Uses the IDNA host and effective port so Unicode/default-port aliases compare identically.
        /// </summary>
        private static string Origin(Uri uri)
        {
            string host = WotEndpointValidator.ToAsciiHost(uri);
            if (uri.HostNameType == UriHostNameType.IPv6)
            {
                host = "[" + host + "]";
            }
            return $"{uri.Scheme}://{host}:{uri.Port}";
        }
    }
}
