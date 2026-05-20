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
                .DiscoverAsync(CancellationToken.None);

            Assert.That(endpoints, Has.Count.EqualTo(1));
            Assert.That(endpoints[0], Is.EqualTo(SimulatedWotDiscoveryProvider.CannedEndpoint));
        }

        [Test]
        public async Task TestSucceedsForCannedEndpoint()
        {
            (bool success, string status) = await m_discovery
                .TestAsync(SimulatedWotDiscoveryProvider.CannedEndpoint, CancellationToken.None);

            Assert.That(success, Is.True);
            Assert.That(status, Is.EqualTo("Healthy"));
        }

        [Test]
        public async Task TestFailsForUnknownEndpoint()
        {
            (bool success, _) = await m_discovery
                .TestAsync("sim://nope", CancellationToken.None);

            Assert.That(success, Is.False);
        }

        [Test]
        public async Task CreateThingDescriptionEmitsExpectedShape()
        {
            Opc.Ua.WotCon.Server.ThingDescriptions.ThingDescription td = await m_discovery
                .CreateThingDescriptionAsync(
                    "asset-001",
                    SimulatedWotDiscoveryProvider.CannedEndpoint,
                    CancellationToken.None);

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
            var td = new Opc.Ua.WotCon.Server.ThingDescriptions.ThingDescription
            {
                Base = "sim://opcua.test/wot/asset-001"
            };

            Assert.That(m_factory.CanHandle(td), Is.True);
        }

        [Test]
        public void CanHandleRejectsHttpScheme()
        {
            var td = new Opc.Ua.WotCon.Server.ThingDescriptions.ThingDescription
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
