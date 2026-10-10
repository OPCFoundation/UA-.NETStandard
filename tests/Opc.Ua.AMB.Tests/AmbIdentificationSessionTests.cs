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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Tests;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Drives the asset identification of a hosted server over a real client
    /// session: the advertised facet and units, and a client configuring the
    /// <c>AssetId</c>.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AmbIdentificationSessionTests
    {
        [Test]
        public async Task AClientSeesTheFacetAndConfiguresTheAssetIdAsync()
        {
            string state = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AmbIdentificationSessionTests),
                Guid.NewGuid().ToString("N"));
            DeviceState? device = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AmbIdentificationSessionTests),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement(options => options.UseFileSystemStores(state))
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> sensor = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Sensor",
                            "urn:acme:sensor:4711").ConfigureAwait(false);
                        device = sensor.Device;
                        await sensor.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            asset => asset.WithConfigurableAssetId("Sensor-1"),
                            context.CancellationToken).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            using var clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
            await clientFixture.LoadClientConfigurationAsync(
                Path.Combine(state, "client-pki")).ConfigureAwait(false);
            using ISession session = await clientFixture.ConnectAsync(
                new Uri(server.EndpointUrl),
                SecurityPolicies.None).ConfigureAwait(false);

            NodeId assetId = device!.AssetId!.NodeId;
            DataValue profiles = await session.ReadValueAsync(
                Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray,
                CancellationToken.None).ConfigureAwait(false);
            DataValue units = await session.ReadValueAsync(
                Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits,
                CancellationToken.None).ConfigureAwait(false);
            ReadResponse attributes = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = assetId, AttributeId = Attributes.UserAccessLevel }],
                CancellationToken.None).ConfigureAwait(false);
            DataValue accessLevel = attributes.Results[0];

            WriteResponse write = await session.WriteAsync(
                null,
                [
                    new WriteValue
                    {
                        NodeId = assetId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From("Sensor-42"))
                    },
                    new WriteValue
                    {
                        NodeId = assetId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(new string('x', 256)))
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            DataValue readBack = await session.ReadValueAsync(assetId, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(profiles.WrappedValue.TryGetValue(out ArrayOf<string> profileArray), Is.True);
                Assert.That(
                    profileArray.ToArray(),
                    Does.Contain("http://opcfoundation.org/UA-Profile/AMB/Server/BaseServer"));
                Assert.That(units.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> unitArray), Is.True);
                Assert.That(
                    unitArray.ToArray(),
                    Is.SupersetOf(new[]
                    {
                        new QualifiedName("AMB Asset Identification"),
                        new QualifiedName("AMB Configurable Asset Identification")
                    }));
                Assert.That(accessLevel.WrappedValue.TryGetValue(out byte level), Is.True);
                Assert.That(level & AccessLevels.CurrentWrite, Is.Not.Zero);
                Assert.That(StatusCode.IsGood(write.Results[0]), Is.True, write.Results[0].ToString());
                Assert.That(write.Results[1].Code, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(readBack.WrappedValue.TryGetValue(out string value), Is.True);
                Assert.That(value, Is.EqualTo("Sensor-42"));
            });
        }
    }
}
