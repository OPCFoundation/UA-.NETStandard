/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server.DataChannels
{
    public sealed partial class ServerDataChannelProvider
    {
        /// <summary>
        /// Registers an actually implemented data endpoint. The availability callback must reflect
        /// listener/backend readiness; dispose the registration when that endpoint is withdrawn.
        /// QUIC requires its TLS application certificate; WebTransport requires its UASC application certificate.
        /// </summary>
        public IDisposable RegisterEndpoint(EndpointDescription endpoint, Func<bool> isAvailable)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (isAvailable == null)
            {
                throw new ArgumentNullException(nameof(isAvailable));
            }
            ValidateEndpoint(endpoint);
            var registration = new EndpointRegistration(this, (EndpointDescription)endpoint.Clone(), isAvailable);
            lock (m_gate)
            {
                ThrowIfDisposed();
                m_endpoints.Add(registration);
            }
            return registration;
        }

        /// <summary>
        /// Returns exact available profile matches without substituting another carrier or rewriting its URL.
        /// </summary>
        public ArrayOf<EndpointDescription> GetEndpoints(ArrayOf<string> profileUris = default)
        {
            EndpointRegistration[] candidates;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return [];
                }
                candidates = [.. m_endpoints];
            }
            List<EndpointDescription> endpoints = [];
            foreach (EndpointRegistration candidate in candidates)
            {
                if ((profileUris.Count == 0 ||
                    DataChannelSource.Contains(
                        profileUris, candidate.Description.TransportProfileUri ?? string.Empty)) &&
                    !candidate.IsDisposed && candidate.Available())
                {
                    endpoints.Add((EndpointDescription)candidate.Description.Clone());
                }
            }
            return [.. endpoints];
        }

        /// <summary>
        /// Projects only available endpoint profiles into model capabilities.
        /// </summary>
        public ArrayOf<string> GetSupportedTransportProfiles()
        {
            HashSet<string> profiles = new(StringComparer.Ordinal);
            foreach (EndpointDescription endpoint in GetEndpoints())
            {
                profiles.Add(endpoint.TransportProfileUri ?? string.Empty);
            }
            return [.. profiles];
        }

        /// <summary>
        /// Identifies the closed set of version-one carrier registrations.
        /// </summary>
        internal static bool IsDataProfile(string? profile)
        {
            return profile is DataChannelProfiles.DedicatedUasc or DataChannelProfiles.SharedUasc or
                DataChannelProfiles.Quic or DataChannelProfiles.WebTransport;
        }

        /// <summary>
        /// Rejects profiles, resources or security fields that would advertise a weaker or fictitious carrier.
        /// </summary>
        private void ValidateEndpoint(EndpointDescription endpoint)
        {
            if (!IsDataProfile(endpoint.TransportProfileUri) ||
                !Uri.TryCreate(endpoint.EndpointUrl, UriKind.Absolute, out Uri? uri) ||
                uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                endpoint.ServerCertificate.IsEmpty || endpoint.Server == null ||
                string.IsNullOrEmpty(endpoint.Server.ApplicationUri))
            {
                throw new ArgumentException("A complete authenticated data endpoint description is required.");
            }
            if (endpoint.TransportProfileUri == DataChannelProfiles.Quic)
            {
                if (endpoint.SecurityMode != MessageSecurityMode.None ||
                    endpoint.SecurityPolicyUri != SecurityPolicies.None || endpoint.UserIdentityTokens.Count != 0 ||
                    uri.Scheme != "opc.quic")
                {
                    throw new ArgumentException("Native QUIC descriptions represent TLS-only data endpoints, not Sessions.");
                }
            }
            else
            {
                UascDataChannelEndpoint.Validate(uri, endpoint, this, AllowIntegrity);
                if (endpoint.TransportProfileUri is DataChannelProfiles.DedicatedUasc or
                    DataChannelProfiles.WebTransport &&
                    endpoint.UserIdentityTokens.Count != 0)
                {
                    throw new ArgumentException("Management-only data endpoints cannot advertise Session authentication.");
                }
            }
            foreach (string discoveryUrl in endpoint.Server.DiscoveryUrls)
            {
                if (!Uri.TryCreate(discoveryUrl, UriKind.Absolute, out Uri? discovery) ||
                    UascDataChannelEndpoint.IsFlowResource(discovery) || discovery.Scheme is "opc.quic" or "opc.wt" ||
                    discovery.AbsoluteUri == uri.AbsoluteUri)
                {
                    throw new ArgumentException("A data endpoint is not a DiscoveryEndpoint.");
                }
            }
            using CertificateCollection chain = Utils.ParseCertificateChainBlob(
                endpoint.ServerCertificate, m_telemetry);
            if (chain.Count == 0 ||
                (m_server != null && endpoint.Server.ApplicationUri != m_server.ServerUris.GetString(0)))
            {
                throw new ArgumentException("The data endpoint must identify the participating application.");
            }
            try
            {
                UascDataChannelEndpoint.GetApplicationUri(chain[0], endpoint.Server.ApplicationUri);
            }
            catch (ServiceResultException exception)
            {
                throw new ArgumentException(
                    "The data certificate does not identify the advertised application.", nameof(endpoint), exception);
            }
        }

        /// <summary>
        /// Owns only one discovery registration, never a transport or a context.
        /// </summary>
        private sealed class EndpointRegistration(
            ServerDataChannelProvider owner,
            EndpointDescription description,
            Func<bool> available) : IDisposable
        {
            public EndpointDescription Description { get; } = description;
            public Func<bool> Available { get; } = available;
            public bool IsDisposed => Volatile.Read(ref m_disposed) != 0;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    lock (owner.m_gate)
                    {
                        owner.m_endpoints.Remove(this);
                    }
                }
            }

            private int m_disposed;
        }

        private readonly List<EndpointRegistration> m_endpoints = [];
    }
}
