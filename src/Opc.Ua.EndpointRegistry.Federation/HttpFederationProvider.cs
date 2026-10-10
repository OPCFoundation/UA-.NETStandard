/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Federation
{
    /// <summary>A configured pair of metadata and independent provider-observation routes.</summary>
    public sealed class HttpFederationRoute
    {
        /// <summary>
        /// Pins one source to exact HTTPS routes on one authority. A schema fragment is not an association.
        /// </summary>
        public HttpFederationRoute(RegistryEntityReferenceDataType source, string observationLocator)
        {
            m_source = (RegistryEntityReferenceDataType)source.Clone();
            Key = new FederationSourceKey(source);
            MetadataUri = Https(source.Locator!);
            ObservationUri = Https(observationLocator);
            if (MetadataUri.Authority != ObservationUri.Authority ||
                MetadataUri.OriginalString == ObservationUri.OriginalString ||
                source.HasNativeTarget || !source.NativeTarget.IsNull)
            {
                throw new ArgumentException("HTTP metadata needs a distinct same-authority observation and no UA target.");
            }
        }

        /// <summary>Gets the pinned source key.</summary>
        public FederationSourceKey Key { get; }

        /// <summary>Gets a copy of the authorized source.</summary>
        public RegistryEntityReferenceDataType Source => (RegistryEntityReferenceDataType)m_source.Clone();

        /// <summary>Gets the exact metadata route.</summary>
        public Uri MetadataUri { get; }

        /// <summary>Gets the independent provider-observation route.</summary>
        public Uri ObservationUri { get; }

        private static Uri Https(string value)
        {
            var uri = new Uri(value, UriKind.Absolute);
            if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            {
                throw new ArgumentException("An authorized HTTPS route without user information or fragment is required.");
            }
            return uri;
        }

        private readonly RegistryEntityReferenceDataType m_source;
    }

    /// <summary>
    /// Bounded outbound HTTP at the server provider boundary, using a supplied authenticated HttpClient.
    /// Its transport must have automatic redirects disabled; responses and final routes are checked again.
    /// No route is taken from a Message resolution request.
    /// </summary>
    public sealed partial class HttpFederationProvider
    {
        /// <summary>Creates a configured provider without issuing any HTTP request.</summary>
        public HttpFederationProvider(
            HttpClient client,
            FederationTrustBinding binding,
            ArrayOf<HttpFederationRoute> routes,
            ITelemetryContext telemetry,
            int maxBytes = 4 * 1024 * 1024,
            TimeSpan? timeout = null)
        {
            m_client = client ?? throw new ArgumentNullException(nameof(client));
            m_binding = binding ?? throw new ArgumentNullException(nameof(binding));
            m_logger = (telemetry ?? throw new ArgumentNullException(nameof(telemetry)))
                .CreateLogger<HttpFederationProvider>();
            m_maxBytes = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
            m_timeout = timeout ?? TimeSpan.FromSeconds(30);
            if (m_timeout <= TimeSpan.Zero || m_timeout.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            if (binding.ApplicationUri.Length != 0)
            {
                throw new ArgumentException("An HTTP provider cannot invent an OPC UA application/root binding.");
            }
            foreach (HttpFederationRoute route in routes)
            {
                if (!route.Key.Origin.Equals(binding.Origin) ||
                    !binding.Authorizes(route.MetadataUri.OriginalString) ||
                    !binding.Authorizes(route.ObservationUri.OriginalString) ||
                    !m_routes.TryAdd(route.Key, route))
                {
                    throw new ArgumentException("Each HTTP route needs an unambiguous independently authorized source.");
                }
            }
        }

        /// <summary>
        /// Explicitly preloads one configured key; only configured routes are dereferenced.
        /// Origin/Xid/Version constraints come from the separate authenticated provider observation.
        /// </summary>
        public async ValueTask<FederationResolutionCache> PreloadAsync(
            FederationResolutionCache cache,
            FederationSourceKey key,
            CancellationToken cancellationToken = default)
        {
            if (cache is null)
            {
                throw new ArgumentNullException(nameof(cache));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_routes.TryGetValue(key, out HttpFederationRoute? route))
            {
                throw new ArgumentException("No configured outbound route exists for this source.");
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(m_timeout);
            RegistryObjectValueDataType manifest = Object(await GetAsync(route.ObservationUri, deadline.Token)
                .ConfigureAwait(false));
            string origin = String(manifest, "OriginUri");
            string xid = String(manifest, "Xid");
            string version = String(manifest, "VersionId");
            uint epoch = UInt32(manifest, "Epoch");
            bool hasDocument = Boolean(manifest, "HasDocument");
            uint maxVersions = UInt32(manifest, "MaxVersions");
            if (manifest.Members.Count != 6)
            {
                throw new ArgumentException("Incomplete or unexpected provider observation fields.");
            }
            RegistryEntityReferenceDataType source = route.Source;
            var evidence = new FederationMetadataObservation(
                new RegistryEntityReferenceDataType { OriginUri = origin },
                route.MetadataUri.OriginalString, string.Empty, null, null, ExpandedNodeId.Null,
                xid, version, hasDocument, maxVersions);
            new FederationMetadataSelector().Select(source, m_binding, evidence);
            RegistryObjectValueDataType metadata = Object(await GetAsync(route.MetadataUri, deadline.Token)
                .ConfigureAwait(false));
            FederationResolutionCache updated = cache.WithObservation(m_binding, evidence,
                new EndpointRegistryMessageObservation
                {
                    Source = source,
                    Metadata = metadata,
                    Epoch = epoch,
                    VersionId = version
                });
            LogPreloaded(m_logger, updated.Count);
            return updated;
        }

        private async ValueTask<RegistryValueDataType> GetAsync(Uri uri, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json");
            using HttpResponseMessage response = await m_client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 ||
                response.RequestMessage?.RequestUri?.OriginalString != uri.OriginalString)
            {
                throw new ArgumentException("Redirects or a changed HTTP authority/route are not authorized.");
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType != "application/json" ||
                response.Content.Headers.ContentLength > m_maxBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "Provider response content type or advertised size is outside the configured bounds.");
            }
#if NET8_0_OR_GREATER
            using Stream input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
#else
            using CancellationTokenRegistration abort = ct.Register(response.Dispose);
            using Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            using var output = new MemoryStream();
            var buffer = new byte[Math.Min(m_maxBytes, 8192)];
            while (true)
            {
#if NETFRAMEWORK
                int read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
#else
                int read = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    break;
                }
                if (output.Length + read > m_maxBytes)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "Provider response exceeded the configured streaming size limit.");
                }
                output.Write(buffer, 0, read);
            }
            ct.ThrowIfCancellationRequested();
            return RegistryValues.Parse(output.ToArray());
        }

        private static RegistryObjectValueDataType Object(RegistryValueDataType value) =>
            value is RegistryObjectValueDataType map && map.Kind == 5
                ? map : throw new ArgumentException("A provider JSON object is required.");

        private static RegistryValueDataType Member(RegistryObjectValueDataType map, string name)
        {
            foreach (RegistryMemberDataType member in map.Members)
            {
                if (member.Name == name)
                {
                    return member.Value;
                }
            }
            throw new ArgumentException("Missing independent provider observation: " + name);
        }

        private static string String(RegistryObjectValueDataType map, string name) =>
            Member(map, name) is RegistryStringValueDataType { Kind: 2, Value: not null } text
                ? text.Value : throw new ArgumentException("A String observation is required: " + name);

        private static bool Boolean(RegistryObjectValueDataType map, string name) =>
            Member(map, name) is RegistryBooleanValueDataType { Kind: 1 } flag
                ? flag.Value : throw new ArgumentException("A Boolean observation is required: " + name);

        private static uint UInt32(RegistryObjectValueDataType map, string name)
        {
            RegistryValueDataType value = Member(map, name);
            if (value is not RegistryNumberValueDataType { Kind: 3, IsInteger: true } ||
                !uint.TryParse(Encoding.UTF8.GetString(RegistryValues.ToJson(value).ToArray()),
                    NumberStyles.None, CultureInfo.InvariantCulture, out uint result))
            {
                throw new ArgumentException("A UInt32 observation is required: " + name);
            }
            return result;
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Validated HTTP federation cache contains {Count} sources.")]
        private static partial void LogPreloaded(ILogger logger, int count);

        private readonly HttpClient m_client;
        private readonly FederationTrustBinding m_binding;
        private readonly int m_maxBytes;
        private readonly TimeSpan m_timeout;
        private readonly ILogger m_logger;
        private readonly Dictionary<FederationSourceKey, HttpFederationRoute> m_routes = [];
    }
}
