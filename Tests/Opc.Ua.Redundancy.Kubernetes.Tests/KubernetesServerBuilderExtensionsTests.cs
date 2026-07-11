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

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for the Kubernetes server builder fluent registration
    /// (<see cref="KubernetesServerBuilderExtensions"/>).
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class KubernetesServerBuilderExtensionsTests
    {
        [Test]
        public async Task UseKubernetesFeaturesRegisterResolvableServicesAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseKubernetes()
                .UseKubernetesLeaderElection(options => options.UseSharedStoreFallback = false)
                .UseKubernetesPeerDiscovery()
                .AddServerServiceLevel(new ConstantServiceLevelProvider())
                .UseKubernetesReadiness();

            await using ServiceProvider provider = services.BuildServiceProvider();

            // Outside a cluster the factory yields a not-in-cluster client and the
            // leader election falls back to a static non-leader, all without any IO.
            IKubernetesApiClient apiClient = provider.GetRequiredService<IKubernetesApiClient>();
            Assert.That(apiClient.IsInCluster, Is.False);
            Assert.That(provider.GetRequiredService<ILeaderElection>(), Is.Not.Null);
            Assert.That(provider.GetRequiredService<IKubernetesPeerDiscovery>(), Is.Not.Null);
            Assert.That(provider.GetRequiredService<KubernetesReadinessServer>(), Is.Not.Null);
            Assert.That(provider.GetServices<IServerStartupTask>(), Is.Not.Empty);
        }

        [Test]
        public async Task ReadinessTracksLeaderAwareServiceLevelAsync()
        {
            await using ServiceProvider leaderProvider = BuildProvider(NewLeaderApi());
            ILeaderElection leaderElection = leaderProvider.GetRequiredService<ILeaderElection>();
            IServiceLevelProvider leaderServiceLevelProvider = leaderProvider.GetRequiredService<IServiceLevelProvider>();
            KubernetesReadinessServer leaderReadiness = leaderProvider.GetRequiredService<KubernetesReadinessServer>();

            Assert.That(await leaderElection.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(leaderServiceLevelProvider.GetServiceLevel(), Is.EqualTo(ServiceLevels.Maximum));
            Assert.That(leaderReadiness.IsReady(), Is.True);

            await using ServiceProvider followerProvider = BuildProvider(NewFollowerApi());
            ILeaderElection followerElection = followerProvider.GetRequiredService<ILeaderElection>();
            IServiceLevelProvider followerServiceLevelProvider =
                followerProvider.GetRequiredService<IServiceLevelProvider>();
            KubernetesReadinessServer followerReadiness =
                followerProvider.GetRequiredService<KubernetesReadinessServer>();

            Assert.That(await followerElection.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            Assert.That(followerServiceLevelProvider.GetServiceLevel(), Is.EqualTo(ServiceLevels.DegradedMaximum));
            Assert.That(followerReadiness.IsReady(), Is.False);
        }

        [Test]
        public void BuilderExtensionsRejectNullBuilder()
        {
            Assert.That(
                () => ((IOpcUaServerBuilder)null!).UseKubernetes(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaServerBuilder)null!).UseKubernetesLeaderElection(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaServerBuilder)null!).UseKubernetesPeerDiscovery(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaServerBuilder)null!).UseKubernetesReadiness(),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task LeaderElectionUsesKubernetesLeaseWhenInClusterAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<IKubernetesApiClient>(x => x.IsInCluster));
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseKubernetesLeaderElection();

            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<ILeaderElection>(),
                Is.InstanceOf<KubernetesLeaseLeaderElection>());
        }

        [Test]
        public async Task LeaderElectionFallsBackToSharedStoreOutsideClusterAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseKubernetesLeaderElection(options => options.UseSharedStoreFallback = true);

            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<ILeaderElection>(),
                Is.InstanceOf<SharedStoreLeaseElection>());
        }

        private static ServiceProvider BuildProvider(IKubernetesApiClient apiClient)
        {
            var services = new ServiceCollection();
            var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 10, TimeSpan.Zero));
            services.AddSingleton(apiClient);
            services.AddSingleton<TimeProvider>(timeProvider);
            services.AddOpcUa()
                .AddServer(_ => { })
                .UseDistributedAddressSpace(options =>
                {
                    options.UseLeaderElection = true;
                    options.NodeId = "pod-a";
                    options.LeaseDuration = TimeSpan.FromSeconds(30);
                    options.RenewInterval = TimeSpan.FromSeconds(10);
                })
                .UseKubernetesLeaderElection(options =>
                {
                    options.LeaseName = "opcua";
                    options.LeaseDuration = TimeSpan.FromSeconds(30);
                    options.RenewInterval = TimeSpan.FromSeconds(10);
                    options.UseSharedStoreFallback = false;
                    options.Kubernetes.Namespace = "ns";
                    options.Kubernetes.NodeId = "pod-a";
                })
                .UseKubernetesReadiness(options =>
                {
                    options.Port = 18080;
                    options.ReadyMinimumServiceLevel = ServiceLevels.HealthyMinimum;
                });

            return services.BuildServiceProvider();
        }

        private static IKubernetesApiClient NewLeaderApi()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            KubernetesLease lease = HeldLease("pod-a", new DateTimeOffset(2026, 1, 1, 0, 0, 10, TimeSpan.Zero));
            api.SetupGet(x => x.IsInCluster).Returns(true);
            api.SetupSequence(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ReturnsAsync((KubernetesLease?)null)
                .ReturnsAsync(lease);
            api.Setup(x => x.CreateLeaseAsync("ns", It.IsAny<KubernetesLease>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, KubernetesLease lease, CancellationToken _) => lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);
            return api.Object;
        }

        private static IKubernetesApiClient NewFollowerApi()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(true);
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ReturnsAsync(HeldLease("pod-b", DateTimeOffset.UtcNow));
            return api.Object;
        }

        private static KubernetesLease HeldLease(string holder, DateTimeOffset renewTime)
        {
            return new KubernetesLease
            {
                Metadata = new KubernetesObjectMetadata
                {
                    Name = "opcua",
                    Namespace = "ns",
                    ResourceVersion = "1"
                },
                Spec = new KubernetesLeaseSpec
                {
                    HolderIdentity = holder,
                    LeaseDurationSeconds = 30,
                    AcquireTime = renewTime.ToString("O"),
                    RenewTime = renewTime.ToString("O"),
                    LeaseTransitions = 0
                }
            };
        }
    }
}
