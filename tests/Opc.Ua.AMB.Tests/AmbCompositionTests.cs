/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Pumps;
using Opc.Ua.Pumps.Server.Builders;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Composes the Asset Management Basics sidecar with a companion
    /// specification that owns the Device Integration address space itself.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AmbCompositionTests
    {
        [Test]
        public async Task APumpOfThePumpsManagerBecomesAnAssetAsync()
        {
            IAssetHandle? handle = null;
            PumpState? pump = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(APumpOfThePumpsManagerBecomesAnAssetAsync),
                builder => builder
                    .AddPumps()
                    .AddAssetManagement()
                    .ConfigurePumps(async context =>
                    {
                        IPumpBuilder created = await context.Manager.CreatePumpAsync(
                            new QualifiedName("Pump_1", context.Manager.InstanceNamespaceIndex),
                            context.CancellationToken).ConfigureAwait(false);
                        created.WithNameplate(new PumpNameplate
                        {
                            NodeId = NodeId.Null,
                            Manufacturer = new LocalizedText("Acme Pumps"),
                            SerialNumber = "SN-001",
                            ProductInstanceUri = "urn:acme:pump:1"
                        });
                        pump = created.Pump;

                        // OPC 40223 keeps the nameplate in the 2:Identification
                        // group, and a pump is a topology element, not a device.
                        ITopologyElementBuilder<PumpState> element =
                            context.DiContext.TopologyElement<PumpState>(created.NodeId);
                        handle = await context.GetRequiredService<IAssetManagement>().RegisterAssetAsync(
                            element.Node,
                            asset => asset.WithConfigurableAssetId("P-1"),
                            context.CancellationToken).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            Assert.That(handle, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(handle!.NodeId, Is.EqualTo(pump!.NodeId));
                Assert.That(handle.ProductInstanceUri, Is.EqualTo("urn:acme:pump:1"));
                Assert.That(handle.AssetId, Is.EqualTo("P-1"));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Asset Identification")));
                Assert.That(
                    server.Manager.ServerProfiles.ToArray(),
                    Does.Contain("http://opcfoundation.org/UA-Profile/AMB/Server/BaseServer"));
            });
        }
    }
}
