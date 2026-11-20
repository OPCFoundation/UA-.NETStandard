/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Tests.Providers;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    public class SimulatedWotDiscoveryProviderTests
    {
        private readonly SimulatedWotDiscoveryProvider m_discovery = new();

        [Test]
        public async Task DiscoverReturnsCannedEndpoint()
        {
            System.Collections.Generic.IReadOnlyList<string> endpoints = await m_discovery
                .DiscoverAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoints, Has.Count.EqualTo(1));
            Assert.That(endpoints[0], Is.EqualTo(SimulatedWotDiscoveryProvider.CannedEndpoint));
        }

        [Test]
        public async Task TestSucceedsForCannedEndpoint()
        {
            (bool success, string status) = await m_discovery
                .TestAsync(SimulatedWotDiscoveryProvider.CannedEndpoint, CancellationToken.None).ConfigureAwait(false);

            Assert.That(success, Is.True);
            Assert.That(status, Is.EqualTo("Healthy"));
        }

        [Test]
        public async Task TestFailsForUnknownEndpoint()
        {
            (bool success, _) = await m_discovery
                .TestAsync("sim://nope", CancellationToken.None).ConfigureAwait(false);

            Assert.That(success, Is.False);
        }

        [Test]
        public async Task CreateThingDescriptionEmitsExpectedShape()
        {
            Server.ThingDescriptions.ThingDescription td = await m_discovery
                .CreateThingDescriptionAsync(
                    "asset-001",
                    SimulatedWotDiscoveryProvider.CannedEndpoint,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(td.Name, Is.EqualTo("asset-001"));
            Assert.That(td.Base, Is.EqualTo(SimulatedWotDiscoveryProvider.CannedEndpoint));
            Assert.That(td.Properties, Is.Not.Null);
            Assert.That(td.Properties!.ContainsKey("Voltage"), Is.True);
            Assert.That(td.Properties.ContainsKey("Status"), Is.True);
            Assert.That(td.Actions, Is.Not.Null);
            Assert.That(td.Actions!.ContainsKey("Reset"), Is.True);
        }
    }

    [TestFixture]
    [Category("WotCon")]
    public class SimulatedWotAssetProviderFactoryTests
    {
        private readonly SimulatedWotAssetProviderFactory m_factory = new();

        [Test]
        public void CanHandleAcceptsSimScheme()
        {
            var td = new Server.ThingDescriptions.ThingDescription
            {
                Base = "sim://opcua.test/wot/asset-001"
            };

            Assert.That(m_factory.CanHandle(td), Is.True);
        }

        [Test]
        public void CanHandleRejectsHttpScheme()
        {
            var td = new Server.ThingDescriptions.ThingDescription
            {
                Base = "http://example.com/thing"
            };

            Assert.That(m_factory.CanHandle(td), Is.False);
        }

        [Test]
        public void SupportedBindingsListsSimUri()
        {
            Assert.That(m_factory.SupportedBindings, Has.Member(SimulatedWotAssetProviderFactory.BindingUri));
        }
    }
}
