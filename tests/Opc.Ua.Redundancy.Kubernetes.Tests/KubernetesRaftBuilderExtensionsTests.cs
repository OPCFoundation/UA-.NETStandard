// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Raft.Storage.File;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Tests for the Kubernetes StatefulSet Raft registration.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class KubernetesRaftBuilderExtensionsTests
    {
        [Test]
        [NonParallelizable]
        public async Task StatefulSetReplicasStartWithPinnedTlsAndPersistMembershipAsync()
        {
            string wal = CreateWalPath();
            using X509Certificate2 firstCertificate = CreateCertificate();
            using X509Certificate2 secondCertificate = CreateCertificate();
            (int firstPort, int secondPort) = ReservePorts();
            var certificates = new[] { firstCertificate, secondCertificate };
            int resolutions = 0;
            string? oldPodIp = Environment.GetEnvironmentVariable("POD_IP");
            Environment.SetEnvironmentVariable("POD_IP", "127.0.0.1");
            try
            {
                var services = new ServiceCollection[2];
                for (int ordinal = 0; ordinal < services.Length; ordinal++)
                {
                    int localOrdinal = ordinal;
                    services[ordinal] = new ServiceCollection();
                    services[ordinal].AddOpcUa()
                        .AddServer(_ => { })
                        .UseRedundancyConsistency(RedundancyConsistencyMode.Strong)
                        .UseKubernetesRaftConsensus(options =>
                        {
                            options.PodName = localOrdinal == 0 ? "opcua-ha-0" : "opcua-ha-1";
                            options.HeadlessServiceName = "opcua-ha-headless";
                            options.Namespace = "opcua";
                            options.ReplicaCount = 2;
                            options.RaftPort = localOrdinal == 0 ? firstPort : secondPort;
                            options.BindAddress = localOrdinal == 0 ? $"tcp://127.0.0.1:{firstPort}" : null;
                            options.StoragePath = Path.Combine(wal, options.PodName);
                            options.ResolveMemberCertificateAsync = async (_, id, isLocal, ct) =>
                            {
                                await Task.Yield();
                                ct.ThrowIfCancellationRequested();
                                Interlocked.Increment(ref resolutions);
                                Assert.That(isLocal, Is.EqualTo(id == (ulong)localOrdinal + 1));
                                X509Certificate2 certificate = certificates[(int)id - 1];
                                return isLocal
                                    ? new X509Certificate2(certificate)
                                    : X509CertificateLoader.LoadCertificate(certificate.RawData);
                            };
                            options.ResolvePeerEndPointAsync = (_, id, endpoint, _) =>
                            {
                                string expectedHost = id == 1
                                    ? "opcua-ha-0.opcua-ha-headless.opcua.svc"
                                    : "opcua-ha-1.opcua-ha-headless.opcua.svc";
                                Assert.That(endpoint.Host, Is.EqualTo(expectedHost));
                                return ValueTask.FromResult(
                                    new IPEndPoint(IPAddress.Loopback, id == 1 ? firstPort : secondPort));
                            };
                        });
                }

                {
                    await using ServiceProvider firstProvider = services[0].BuildServiceProvider();
                    await using ServiceProvider secondProvider = services[1].BuildServiceProvider();
                    IRaftConsensus first = firstProvider.GetRequiredService<IRaftConsensus>();
                    IRaftConsensus second = secondProvider.GetRequiredService<IRaftConsensus>();
                    Assert.That(first, Is.InstanceOf<IRaftConsensusLocality>());
                    Assert.That(((IRaftConsensusLocality)first).IsProcessLocal, Is.False);
                    Assert.That(resolutions, Is.Zero, "DI resolution must not load certificates");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await Task.WhenAll(
                        StartServerRaftAsync(firstProvider, timeout.Token).AsTask(),
                        StartServerRaftAsync(secondProvider, timeout.Token).AsTask());
                    Assert.That(resolutions, Is.EqualTo(4));
                    Assert.That(first.IsLeader ^ second.IsLeader, Is.True);
                }

                using var firstWal = new FileRaftStorage(new FileRaftStorageOptions(Path.Combine(wal, "opcua-ha-0")));
                using var secondWal = new FileRaftStorage(new FileRaftStorageOptions(Path.Combine(wal, "opcua-ha-1")));
                Assert.That(firstWal.InitialState().ConfState.Voters, Is.EqualTo(new ulong[] { 1, 2 }));
                Assert.That(secondWal.InitialState().ConfState.Voters, Is.EqualTo(new ulong[] { 1, 2 }));
            }
            finally
            {
                Environment.SetEnvironmentVariable("POD_IP", oldPodIp);
                if (Directory.Exists(wal))
                {
                    Directory.Delete(wal, true);
                }
            }
        }

        [Test]
        public async Task StartupRejectsMissingPeerCertificateAsync()
        {
            var services = new ServiceCollection();
            string wal = CreateWalPath();
            try
            {
                services.AddOpcUa()
                    .AddServer(_ => { })
                    .UseKubernetesRaftConsensus(options =>
                    {
                        ConfigureNetwork(options, wal);
                        options.ResolveMemberCertificateAsync = (_, _, isLocal, _) =>
                            ValueTask.FromResult(isLocal ? CreateCertificate() : null!);
                    });

                await using ServiceProvider provider = services.BuildServiceProvider();
                await Assert.ThatAsync(
                    async () => await StartServerRaftAsync(provider),
                    Throws.InvalidOperationException.With.Message.Contains("member 2"));
            }
            finally
            {
                if (Directory.Exists(wal))
                {
                    Directory.Delete(wal, true);
                }
            }
        }

        [Test]
        public async Task ExplicitSingleNodeInProcessTestBackendStartsAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseKubernetesRaftConsensus(options =>
                {
                    options.ReplicaCount = 1;
                    options.UseInProcessTestTransport = true;
                });

            await using ServiceProvider provider = services.BuildServiceProvider();
            await StartServerRaftAsync(provider);
            IRaftConsensus consensus = provider.GetRequiredService<IRaftConsensus>();
            Assert.That(consensus.IsLeader, Is.True);
            Assert.That(consensus, Is.InstanceOf<IRaftConsensusLocality>());
            Assert.That(((IRaftConsensusLocality)consensus).IsProcessLocal, Is.True);
        }

        [Test]
        public void InProcessTestBackendRejectsKubernetes()
        {
            var services = new ServiceCollection();
            var api = new Mock<IKubernetesApiClient>();
            api.SetupGet(client => client.IsInCluster).Returns(true);
            services.AddSingleton(api.Object);
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseKubernetesRaftConsensus(options =>
                {
                    options.ReplicaCount = 1;
                    options.UseInProcessTestTransport = true;
                });

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(() => provider.GetRequiredService<IRaftConsensus>(), Throws.InvalidOperationException);
        }

        [Test]
        public void RejectsImplicitNetworkCredentialsOrVolatileStorage()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });

            Assert.That(
                () => builder.UseKubernetesRaftConsensus(options =>
                {
                    options.HeadlessServiceName = "raft";
                    options.ReplicaCount = 1;
                }),
                Throws.ArgumentException.With.Message.Contains("ResolveMemberCertificateAsync"));
            Assert.That(
                () => builder.UseKubernetesRaftConsensus(options =>
                {
                    ConfigureNetwork(options, "unused");
                    options.UseDurableStorage = false;
                }),
                Throws.ArgumentException.With.Message.Contains("WAL"));
            Assert.That(
                () => builder.UseKubernetesRaftConsensus(options =>
                {
                    options.ReplicaCount = 2;
                    options.UseInProcessTestTransport = true;
                }),
                Throws.ArgumentException.With.Message.Contains("one replica"));
        }

        [TestCase("no-ordinal-here")]
        [TestCase("opcua-ha-5")]
        public void ResolvingRejectsInvalidOrdinal(string podName)
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseKubernetesRaftConsensus(options =>
                {
                    ConfigureNetwork(options, "unused");
                    options.PodName = podName;
                });

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(() => provider.GetRequiredService<IRaftConsensus>(), Throws.InvalidOperationException);
        }

        private static ValueTask StartServerRaftAsync(IServiceProvider provider, CancellationToken ct = default)
        {
            IServerPreStartupTask task = provider.GetServices<IServerPreStartupTask>().Single();
            return task.OnServerStartingAsync(Mock.Of<IServerContext>(), ct);
        }

        private static void ConfigureNetwork(KubernetesRaftConsensusOptions options, string wal)
        {
            options.PodName = "opcua-ha-0";
            options.HeadlessServiceName = "opcua-ha-headless";
            options.ReplicaCount = 2;
            options.BindAddress = "127.0.0.1";
            options.StoragePath = wal;
            options.ResolveMemberCertificateAsync = (_, _, _, _) => ValueTask.FromResult(CreateCertificate());
        }

        private static (int First, int Second) ReservePorts()
        {
            using var first = new TcpListener(IPAddress.Loopback, 0);
            using var second = new TcpListener(IPAddress.Loopback, 0);
            first.Start();
            second.Start();
            return (((IPEndPoint)first.LocalEndpoint).Port, ((IPEndPoint)second.LocalEndpoint).Port);
        }

        private static string CreateWalPath()
        {
            return Path.GetFullPath(Path.Combine("bin", "kube-raft-tests", Guid.NewGuid().ToString("N")));
        }

        private static X509Certificate2 CreateCertificate()
        {
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Raft Test", key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new(Oids.ServerAuthentication), new(Oids.ClientAuthentication) },
                critical: false));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            using X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-1), now.AddHours(1));
            byte[] pkcs12 = certificate.Export(X509ContentType.Pkcs12);
            try
            {
                return X509CertificateLoader.LoadPkcs12(pkcs12, null);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs12);
            }
        }
    }
}
