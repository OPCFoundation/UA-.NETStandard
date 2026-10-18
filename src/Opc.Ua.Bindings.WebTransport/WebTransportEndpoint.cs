// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Globalization;
using System.Net;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Validates native-only endpoints and preserves exact CONNECT authority/path policy.
    /// </summary>
    internal static class WebTransportEndpoint
    {
        /// <summary>
        /// Rejects ambiguous/default paths, alternate transports, credentials and URI normalization tricks.
        /// </summary>
        internal static string Address(Uri endpoint, bool listen, bool numericListener = true)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "opc.wt" ||
                endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 ||
                endpoint.Host.Length == 0 || endpoint.AbsolutePath.Length == 0 ||
                endpoint.Port < 0 || (!listen && endpoint.Port == 0) ||
                (listen && numericListener && !IPAddress.TryParse(endpoint.DnsSafeHost, out _)))
            {
                throw new ArgumentException(
                    "An explicit opc.wt authority, UDP port and absolute resource are required.",
                    nameof(endpoint));
            }
            string original = endpoint.OriginalString;
            if (original.Contains('\\', StringComparison.Ordinal) ||
                original.Contains('\r', StringComparison.Ordinal) ||
                original.Contains('\n', StringComparison.Ordinal) ||
                !string.Equals(original, endpoint.AbsoluteUri, StringComparison.Ordinal))
            {
                throw new ArgumentException("The endpoint must be canonical and must not require URI normalization.",
                    nameof(endpoint));
            }
            string host = endpoint.DnsSafeHost;
            return host.Contains(':', StringComparison.Ordinal)
                ? $"[{host}]:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}"
                : $"{host}:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>
        /// Replaces an ephemeral listening authority with the backend's actual bound numeric endpoint.
        /// </summary>
        internal static Uri Bound(Uri endpoint, string address)
        {
            var bound = new Uri("opc.wt://" + address + endpoint.PathAndQuery);
            Address(bound, false);
            return bound;
        }

        /// <summary>
        /// Resolves only an ephemeral port while preserving the explicitly configured CONNECT authority.
        /// </summary>
        internal static Uri WithBoundPort(Uri endpoint, string address)
        {
            if (endpoint.Port != 0)
            {
                return endpoint;
            }
            IPEndPoint actual = IPEndPoint.Parse(address);
            return new UriBuilder(endpoint) { Port = actual.Port }.Uri;
        }

        /// <summary>
        /// Checks an exact HTTPS origin with no path, credentials, query, fragment or default-port rewriting.
        /// </summary>
        internal static bool IsCanonicalOrigin(string? origin)
        {
            if (string.IsNullOrEmpty(origin) ||
                !Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                uri.AbsolutePath != "/" || uri.Host.Length == 0)
            {
                return false;
            }
            return string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal);
        }
    }
}
