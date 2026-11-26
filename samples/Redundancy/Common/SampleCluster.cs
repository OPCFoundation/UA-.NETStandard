// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Crdt.Transport;
using Opc.Ua;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using Raft;
using Raft.Transport;

namespace RedundancySample
{
    /// <summary>
    /// Resolves explicitly pinned cluster members and owns the local TLS identity.
    /// </summary>
    internal sealed class SampleCluster : IDisposable
    {
        private SampleCluster(Certificate local, X509Certificate2 certificate)
        {
            m_local = local;
            m_certificate = certificate;
        }

        /// <summary>
        /// Loads the cluster identity from an OPC UA certificate store and resolves exact SHA-256 leaf pins.
        /// </summary>
        public static async Task<SampleCluster> LoadAsync(
            Func<string, string?> read,
            string prefix,
            IReadOnlyCollection<string> pins,
            CancellationToken ct = default)
        {
            var identifier = new CertificateIdentifier
            {
                StoreType = read($"{prefix}_TLS_STORE_TYPE") ?? CertificateStoreType.Directory,
                StorePath = Require(read, $"{prefix}_TLS_STORE_PATH"),
                Thumbprint = Require(read, $"{prefix}_TLS_CERTIFICATE_THUMBPRINT")
            };
            ITelemetryContext telemetry = DefaultTelemetry.Create(_ => { });
            using var telemetryLifetime = telemetry as IDisposable;
            using ICertificateStore store = new CertificateStoreIdentifier(
                Require(read, $"{prefix}_TLS_PEER_STORE_PATH"),
                read($"{prefix}_TLS_PEER_STORE_TYPE") ?? CertificateStoreType.Directory).OpenStore(telemetry);
            using CertificateCollection certificates = await store.EnumerateAsync(ct).ConfigureAwait(false);
            Certificate? local = await CertificateIdentifierResolver.LoadPrivateKeyAsync(
                    identifier,
                    telemetry: telemetry,
                    ct: ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"{prefix}: the cluster TLS private key was not found.");
            X509Certificate2? certificate = null;
            SampleCluster? cluster = null;
            try
            {
                certificate = local.AsX509Certificate2();
                if (!certificate.HasPrivateKey || !IsCurrent(certificate))
                {
                    throw new InvalidOperationException($"{prefix}: the TLS certificate needs a valid private key.");
                }

                string localPin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
                cluster = new SampleCluster(local, certificate);
                local = null;
                certificate = null;
                foreach (string pin in pins)
                {
                    if (pin.Equals(localPin, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"{prefix}: the local TLS identity cannot be a peer.");
                    }

                    if (cluster.m_peers.ContainsKey(pin))
                    {
                        continue;
                    }

                    bool found = false;
                    foreach (Certificate candidate in certificates)
                    {
                        if (candidate.HasPrivateKey)
                        {
                            continue;
                        }
                        using X509Certificate2 peer = candidate.AsX509Certificate2();
                        if (pin.Equals(peer.GetCertHashString(HashAlgorithmName.SHA256),
                            StringComparison.OrdinalIgnoreCase) && IsCurrent(peer))
                        {
                            cluster.m_peers.Add(pin, new X509Certificate2(peer));
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        throw new InvalidOperationException($"{prefix}: a pinned TLS peer certificate was not found.");
                    }
                }

                SampleCluster result = cluster;
                cluster = null;
                return result;
            }
            finally
            {
                cluster?.Dispose();
                certificate?.Dispose();
                local?.Dispose();
            }
        }

        /// <summary>
        /// Reads an explicit odd, static Raft membership before creating any transport.
        /// </summary>
        public static (ulong Id, IPEndPoint Bind, List<(ulong Id, IPEndPoint Address, string Pin)> Peers) ReadRaft(
            Func<string, string?> read,
            string prefix)
        {
            string idKey = $"{prefix}_RAFT_ID";
            if (!ulong.TryParse(Require(read, idKey), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) ||
                id == 0)
            {
                throw new FormatException($"{idKey} must be a nonzero unsigned member ID.");
            }

            IPEndPoint bind = ParseEndpoint(Require(read, $"{prefix}_RAFT_BIND"), allowWildcard: true);
            var memberIds = new HashSet<ulong> { id };
            var addresses = new HashSet<IPEndPoint> { bind };
            var pins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var peers = new List<(ulong, IPEndPoint, string)>();
            string peersKey = $"{prefix}_RAFT_PEERS";
            foreach (string entry in ReadList(read(peersKey)))
            {
                string[] fields = entry.Split('|', StringSplitOptions.TrimEntries);
                if (fields.Length != 3 ||
                    !ulong.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong peerId) ||
                    peerId == 0 || !memberIds.Add(peerId) || !IsSha256Pin(fields[2]))
                {
                    throw new FormatException($"{peersKey} must contain unique ID|IP:port|SHA256 entries.");
                }
                IPEndPoint endpoint = ParseEndpoint(fields[1]);
                if (!addresses.Add(endpoint) || !pins.Add(fields[2]))
                {
                    throw new FormatException($"{peersKey} must use distinct endpoints and certificates.");
                }
                peers.Add((peerId, endpoint, fields[2]));
            }
            string membersKey = $"{prefix}_RAFT_MEMBERS";
            if (memberIds.Count < 3 || memberIds.Count % 2 == 0 ||
                (read(membersKey) is string count &&
                    (!int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out int configured) ||
                        configured != memberIds.Count)))
            {
                throw new FormatException($"{peersKey} must define an odd cluster of at least three members.");
            }
            return (id, bind, peers);
        }

        /// <summary>
        /// Reads the closed gossip routing and certificate allowlist.
        /// </summary>
        public static List<(IPEndPoint Address, string Pin)> ReadGossip(
            Func<string, string?> read,
            string prefix,
            IPEndPoint bind)
        {
            string key = $"{prefix}_GOSSIP_PEERS";
            var addresses = new HashSet<IPEndPoint> { bind };
            var pins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var peers = new List<(IPEndPoint, string)>();
            foreach (string entry in ReadList(Require(read, key)))
            {
                string[] fields = entry.Split('|', StringSplitOptions.TrimEntries);
                if (fields.Length != 2 || !IsSha256Pin(fields[1]))
                {
                    throw new FormatException($"{key} must contain IP:port|SHA256 entries.");
                }
                IPEndPoint endpoint = ParseEndpoint(fields[0]);
                if (!addresses.Add(endpoint) || !pins.Add(fields[1]))
                {
                    throw new FormatException($"{key} must use distinct endpoints and certificates.");
                }
                peers.Add((endpoint, fields[1]));
            }
            if (peers.Count == 0)
            {
                throw new FormatException($"{key} must name at least one pinned peer.");
            }
            return peers;
        }

        /// <summary>
        /// Creates consensus over the pinned mutual-TLS Raft transport, using volatile demo storage.
        /// </summary>
        public DefaultRaftConsensus CreateRaft(
            ulong id,
            IPEndPoint bind,
            List<(ulong Id, IPEndPoint Address, string Pin)> peers)
        {
            const int maxRaftFrameBytes = 16 * 1024 * 1024;
            var members = new List<ulong>(peers.Count + 1) { id };
            var tlsPeers = new List<TlsRaftPeer>(peers.Count);
            foreach ((ulong peerId, IPEndPoint endpoint, string pin) in peers)
            {
                members.Add(peerId);
                tlsPeers.Add(new TlsRaftPeer(peerId, endpoint, m_peers[pin]));
            }

            // Consensus owns the transport; the enclosing sample owns the certificates.
#pragma warning disable CA2000
            return DefaultRaftConsensus.CreateCluster(
                id,
                new ArrayOf<ulong>(members.ToArray()),
                new TlsRaftTransport(id, bind, m_certificate, tlsPeers,
                    new TlsRaftTransportOptions { MaxFrameLength = maxRaftFrameBytes }),
                new RaftNodeOptions
                {
                    TickInterval = TimeSpan.FromMilliseconds(50),
                    MaxIncomingMessageBytes = maxRaftFrameBytes
                },
                config =>
                {
                    config.ElectionTick = 10;
                    config.PreVote = true;
                    config.CheckQuorum = true;
                },
                TimeSpan.FromSeconds(30));
#pragma warning restore CA2000
        }

        /// <summary>
        /// Uses mutual TLS and authenticates only the exact leaf certificates listed for this gossip fabric.
        /// </summary>
        public GossipTlsOptions CreateGossipTls(IEnumerable<string> pins)
        {
            var trusted = new HashSet<string>(pins, StringComparer.OrdinalIgnoreCase);
            return new GossipTlsOptions
            {
                ServerCertificate = m_certificate,
                ClientCertificates = [m_certificate],
                RequireClientCertificate = true,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = (_, peer, _, errors) =>
                    peer is X509Certificate2 cert &&
                    (errors & SslPolicyErrors.RemoteCertificateNotAvailable) == 0 &&
                    IsCurrent(cert) &&
                    trusted.Contains(cert.GetCertHashString(HashAlgorithmName.SHA256))
            };
        }

        /// <summary>
        /// Returns a required, nonempty configuration value without exposing it in errors.
        /// </summary>
        public static string Require(Func<string, string?> read, string key)
        {
            string? value = read(key);
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException($"Set {key} before starting this topology.")
                : value.Trim();
        }

        /// <summary>
        /// Splits a static member list.
        /// </summary>
        public static string[] ReadList(string? value) =>
            value?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        /// <summary>
        /// Parses numeric routes, never accepting unpinned DNS discovery or wildcard peer addresses.
        /// </summary>
        public static IPEndPoint ParseEndpoint(string value, bool allowWildcard = false)
        {
            if (!IPEndPoint.TryParse(value, out IPEndPoint? endpoint) || endpoint.Port == 0 ||
                (!allowWildcard && (endpoint.Address.Equals(IPAddress.Any) ||
                    endpoint.Address.Equals(IPAddress.IPv6Any))))
            {
                throw new FormatException("Cluster endpoints must be numeric IP:port addresses with nonzero ports.");
            }
            return endpoint;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (X509Certificate2 peer in m_peers.Values)
            {
                peer.Dispose();
            }
            m_certificate.Dispose();
            m_local.Dispose();
        }

        private static bool IsSha256Pin(string pin) => pin.Length == 64 && pin.AsSpan().IndexOfAnyExcept(
            "0123456789abcdefABCDEF".AsSpan()) < 0;

        private static bool IsCurrent(X509Certificate2 cert)
        {
            DateTime now = DateTime.UtcNow;
            return cert.NotBefore.ToUniversalTime() <= now && now <= cert.NotAfter.ToUniversalTime();
        }

        private readonly Certificate m_local;
        private readonly X509Certificate2 m_certificate;
        private readonly Dictionary<string, X509Certificate2> m_peers = new(StringComparer.OrdinalIgnoreCase);
    }
}
