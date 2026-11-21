// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the load-direction <c>StrongEligibility</c> composition with the hybrid consistency store.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class LoadDirectionStrongRoutingTests
    {
        [Test]
        public async Task StrongEligibilityRoutesEligibilityKeyspacesToRaftAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .UseRedundancyConsistency()
                .UseServerLoadDirection(options =>
                {
                    options.BalancingEndpointUrl = "opc.tcp://ha:4840";
                    options.StrongEligibility = true;
                });
            await using ServiceProvider provider = services.BuildServiceProvider();

            var store = (HybridSharedKeyValueStore)provider.GetRequiredService<ISharedKeyValueStore>();

            Assert.That(store.IsStrongKey("svc/urn:a"), Is.True, "the health eligibility keyspace is linearizable");
            Assert.That(store.IsStrongKey("endpoint/urn:a"), Is.True, "the endpoint directory is linearizable");
            Assert.That(store.IsStrongKey("load/urn:a"), Is.False, "the high-churn load weight stays eventual");
            Assert.That(store.IsStrongKey("nonce/x"), Is.True, "the default strong keyspaces are preserved");
            Assert.That(store.IsStrongKey("node/1"), Is.False, "bulk keys stay eventual");
        }

        [Test]
        public async Task DefaultLeavesEligibilityKeyspacesEventualAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .UseRedundancyConsistency()
                .UseServerLoadDirection(options => options.BalancingEndpointUrl = "opc.tcp://ha:4840");
            await using ServiceProvider provider = services.BuildServiceProvider();

            var store = (HybridSharedKeyValueStore)provider.GetRequiredService<ISharedKeyValueStore>();

            Assert.That(store.IsStrongKey("svc/urn:a"), Is.False, "eligibility stays eventual by default");
            Assert.That(store.IsStrongKey("endpoint/urn:a"), Is.False);
            Assert.That(store.IsStrongKey("nonce/x"), Is.True, "the default strong keyspaces are preserved");
        }
    }
}
