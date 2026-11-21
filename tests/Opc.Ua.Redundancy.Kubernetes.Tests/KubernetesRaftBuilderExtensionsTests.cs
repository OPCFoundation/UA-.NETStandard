// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for <see cref="KubernetesRaftBuilderExtensions"/> — the multi-node RaftCs registration for a
    /// Kubernetes StatefulSet.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class KubernetesRaftBuilderExtensionsTests
    {
        [Test]
        public async Task RegistersRaftCsConsensusForStatefulSetAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseKubernetesRaftConsensus(options =>
                {
                    // Set the pod name explicitly so the test does not depend on the
                    // machine name; ordinal 1 → Raft node id 2 of a 3-node cluster.
                    options.PodName = "opcua-ha-1";
                    options.HeadlessServiceName = "opcua-ha-headless";
                    options.ReplicaCount = 3;
                    options.UseDurableStorage = false;
                });

            await using ServiceProvider provider = services.BuildServiceProvider();

            IRaftConsensus consensus = provider.GetRequiredService<IRaftConsensus>();
            Assert.That(consensus, Is.InstanceOf<DefaultRaftConsensus>());
        }

        [Test]
        public async Task RegistersDurableFileStorageWithFqdnPeersAsync()
        {
            string dir = Path.Combine(Path.GetTempPath(), "opcua-raft-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var services = new ServiceCollection();
                services.AddOpcUa()
                    .AddServer(_ => { })
                    .UseKubernetesRaftConsensus(options =>
                    {
                        options.PodName = "opcua-ha-0";
                        options.HeadlessServiceName = "opcua-ha-headless";
                        options.Namespace = "opcua"; // fully-qualified peer DNS
                        options.ReplicaCount = 3;
                        options.UseDurableStorage = true; // FileRaftStorage on a temp dir
                        options.StoragePath = dir;
                    });

                // Resolve (builds the WAL + bootstraps the ConfState) then dispose
                // (closes the WAL) before deleting the directory.
                {
                    await using ServiceProvider provider = services.BuildServiceProvider();
                    Assert.That(
                        provider.GetRequiredService<IRaftConsensus>(),
                        Is.InstanceOf<DefaultRaftConsensus>());
                }

                Assert.That(Directory.Exists(dir), Is.True, "the file WAL directory was created");
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Test]
        public void RejectsNullBuilderAndInvalidOptions()
        {
            Assert.That(
                () => ((IOpcUaServerBuilder)null!).UseKubernetesRaftConsensus(),
                Throws.ArgumentNullException);

            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });

            Assert.That(
                () => builder.UseKubernetesRaftConsensus(o => o.ReplicaCount = 3),
                Throws.ArgumentException, "HeadlessServiceName is required");
            Assert.That(
                () => builder.UseKubernetesRaftConsensus(o => o.HeadlessServiceName = "h"),
                Throws.ArgumentException, "ReplicaCount must be >= 1");
        }

        [Test]
        public void ResolvingRejectsNonOrdinalPodName()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseKubernetesRaftConsensus(options =>
                {
                    options.PodName = "no-ordinal-here";
                    options.HeadlessServiceName = "h";
                    options.ReplicaCount = 3;
                    options.UseDurableStorage = false;
                });

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                () => provider.GetRequiredService<IRaftConsensus>(),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ResolvingRejectsOutOfRangeOrdinal()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseKubernetesRaftConsensus(options =>
                {
                    options.PodName = "opcua-ha-5";
                    options.HeadlessServiceName = "h";
                    options.ReplicaCount = 3;
                    options.UseDurableStorage = false;
                });

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                () => provider.GetRequiredService<IRaftConsensus>(),
                Throws.InvalidOperationException);
        }
    }
}
