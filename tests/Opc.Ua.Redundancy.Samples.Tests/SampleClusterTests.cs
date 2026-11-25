// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using RedundancySample;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Exercises the sample's actual certificate-store and mutual-TLS quorum wiring.
    /// </summary>
    [TestFixture]
    internal sealed class SampleClusterTests
    {
        /// <summary>
        /// Three distinct pinned leaf identities and provisioned private keys elect one real Raft leader.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task PinnedTlsRaftMembersElectLeaderAsync(CancellationToken ct)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "sample-tls-test", Guid.NewGuid().ToString("N"));
            string peerStore = Path.Combine(root, "peers");
            ulong[] ids = [10, 20, 30];
            int[] ports = TestPorts.GetFreePorts(ids.Length);
            string[] identityStores = ids.Select(id => Path.Combine(root, id.ToString(CultureInfo.InvariantCulture)))
                .ToArray();
            var clusters = new List<SampleCluster>();
            var replicas = new List<DefaultRaftConsensus>();
            try
            {
                var identities = new (string Thumbprint, string Pin)[ids.Length];
                for (int i = 0; i < ids.Length; i++)
                {
                    identities[i] = await ProvisionIdentityAsync(
                        ids[i], identityStores[i], peerStore, ct).ConfigureAwait(false);
                }

                for (int i = 0; i < ids.Length; i++)
                {
                    var settings = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["HA_RAFT_ID"] = ids[i].ToString(CultureInfo.InvariantCulture),
                        ["HA_RAFT_BIND"] = $"127.0.0.1:{ports[i]}",
                        ["HA_RAFT_PEERS"] = string.Join(",", Enumerable.Range(0, ids.Length)
                            .Where(peer => peer != i)
                            .Select(peer => $"{ids[peer]}|127.0.0.1:{ports[peer]}|{identities[peer].Pin}")),
                        ["HA_TLS_STORE_PATH"] = identityStores[i],
                        ["HA_TLS_PEER_STORE_PATH"] = peerStore,
                        ["HA_TLS_CERTIFICATE_THUMBPRINT"] = identities[i].Thumbprint
                    };
                    string? Read(string key) => settings.GetValueOrDefault(key);
                    var (id, bind, peers) = SampleCluster.ReadRaft(Read, "HA");
                    SampleCluster cluster = await SampleCluster.LoadAsync(
                        Read, "HA", peers.Select(peer => peer.Pin).ToArray(), ct).ConfigureAwait(false);
                    clusters.Add(cluster);
                    replicas.Add(cluster.CreateRaft(id, bind, peers));
                }

                await Task.WhenAll(replicas.Select(replica => replica.StartAsync(ct).AsTask()))
                    .ConfigureAwait(false);
                Assert.That(replicas.Count(replica => replica.IsLeader), Is.EqualTo(1));
            }
            finally
            {
                foreach (DefaultRaftConsensus replica in replicas)
                {
                    await replica.DisposeAsync().ConfigureAwait(false);
                }
                foreach (SampleCluster cluster in clusters)
                {
                    cluster.Dispose();
                }
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static async Task<(string Thumbprint, string Pin)> ProvisionIdentityAsync(
            ulong id,
            string localPath,
            string peerPath,
            CancellationToken ct)
        {
            using RSA privateKey = RSA.Create(2048);
            var request = new CertificateRequest($"CN=Sample Raft member {id}", privateKey, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")],
                critical: false));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            using X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(1));
            using Certificate local = Certificate.From(certificate);
            using Certificate peer = Certificate.FromRawData(certificate.RawData);
            ITelemetryContext telemetry = DefaultTelemetry.Create(_ => { });
            using var telemetryLifetime = telemetry as IDisposable;
            using ICertificateStore localStore =
                new CertificateStoreIdentifier(localPath, CertificateStoreType.Directory, noPrivateKeys: false)
                    .OpenStore(telemetry);
            using ICertificateStore peerStore =
                new CertificateStoreIdentifier(peerPath, CertificateStoreType.Directory).OpenStore(telemetry);
            await localStore.AddAsync(local, ct: ct).ConfigureAwait(false);
            await peerStore.AddAsync(peer, ct: ct).ConfigureAwait(false);
            return (certificate.Thumbprint, certificate.GetCertHashString(HashAlgorithmName.SHA256));
        }
    }
}
