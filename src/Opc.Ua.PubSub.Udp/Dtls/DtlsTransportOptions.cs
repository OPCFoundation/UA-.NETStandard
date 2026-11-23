// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// IConfiguration-bindable DTLS transport settings for Part 14 §7.3.2.4.
    /// </summary>
    public sealed class DtlsTransportOptions
    {
        /// <summary>
        /// Default profile preferred when neither the endpoint nor configuration name a profile and
        /// no other enabled profile is selected at runtime.
        /// </summary>
        public const string DefaultProfileName = "ECC_nistP256_AesGcm";

        /// <summary>
        /// Optional preferred DTLS profile name from the Part 14 DTLS profile matrix. When set and the
        /// profile is enabled and supported by the runtime it is selected; otherwise the first
        /// enabled and supported profile is chosen at runtime. Cipher suites/profiles are never pinned
        /// by configuration: this is only a preference, not a hard requirement.
        /// </summary>
        public string? PreferredProfileName { get; set; }

        /// <summary>
        /// Profile names disabled at configuration time even if the runtime supports them. Matching is
        /// case-insensitive and selection fails closed when all supported profiles are disabled.
        /// </summary>
        public ISet<string> DisabledProfiles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maximum DTLS handshake datagram size before RFC 9147 handshake fragmentation is required.
        /// </summary>
        public int MaxHandshakeDatagramSize { get; set; } = 1200;

        /// <summary>
        /// Initial retransmission timeout for RFC 9147 handshake flights.
        /// </summary>
        public TimeSpan InitialRetransmissionTimeout { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum retransmission timeout for RFC 9147 handshake flights.
        /// </summary>
        public TimeSpan MaxRetransmissionTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Enables DTLS 1.3 stateless HelloRetryRequest cookies for listeners.
        /// </summary>
        public bool RequireHelloRetryRequestCookie { get; set; } = true;

        /// <summary>
        /// Local ECC certificates with private keys used for CertificateVerify. Multiple certificates
        /// may be registered; the handshake selects the certificate whose ECDsa named curve matches the
        /// negotiated profile certificate curve, similar to how secure channels register an application
        /// certificate per certificate type.
        /// </summary>
        /// <remarks>
        /// These handles are <b>borrowed</b>: the caller that registers a <see cref="Certificate"/>
        /// retains ownership and is responsible for disposing it. The DTLS stack does not dispose the
        /// registered handles; it takes an independent reference (via <see cref="Certificate.AddRef"/>)
        /// for the duration of any handshake that uses them.
        /// </remarks>
        public IList<Certificate> LocalCertificates { get; } = [];

        /// <summary>
        /// Local certificate identifiers resolved from the configured certificate manager or store
        /// registry when a DTLS context is created. Resolved private-key certificates are merged with
        /// <see cref="LocalCertificates"/> before the handshake selects the certificate whose ECDsa
        /// named curve matches the negotiated profile certificate curve.
        /// </summary>
        public IList<CertificateIdentifier> LocalCertificateIdentifiers { get; } = [];

        /// <summary>
        /// Optional direct-construction peer certificate validator.
        /// </summary>
        public ICertificateValidatorEx? PeerCertificateValidator { get; set; }

        /// <summary>
        /// Requires DTLS 1.3 mutual authentication. When <see langword="true"/> (the default) the
        /// server includes a CertificateRequest in its flight, the client answers with its
        /// Certificate and CertificateVerify, and the server validates the client chain through the
        /// same fail-closed certificate validator used for the server certificate. Mutual
        /// authentication requires a configured peer certificate validator on the server and a local
        /// certificate on the client; otherwise the handshake fails closed.
        /// </summary>
        /// <remarks>
        /// The DTLS server (the subscriber) accepts a single association and pins the peer that
        /// completes the handshake. Without client authentication any host that can reach the port
        /// first becomes that peer, locks the real publisher out and, on a connection with writers,
        /// receives its data. Set this to <see langword="false"/> only when the publisher is
        /// authenticated by other means (for example SKS-managed message security) and the network
        /// is trusted.
        /// </remarks>
        public bool RequireClientCertificate { get; set; } = true;
    }
}
