/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Redundancy.K8s.Tests
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
    }
}
