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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies actual per-instance conformance contributions rather than namespace-presence claims.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("MachineTool")]
    public sealed class MachineToolCapabilityTests
    {
        /// <summary>
        /// Selected behavior is discoverable per instance, and removal withdraws only that instance's contribution.
        /// </summary>
        [Test]
        public async Task CapabilitiesDescribeBindingsAndWithdrawRemovedInstancesAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var notifications = new MachineToolNotificationTests.NotificationProvider(
                new MachineToolNotificationSnapshot());
            await using MachineToolRuntime<MachineToolState> first =
                await MachineToolNotificationTests.BuildAsync(fixture, notifications, "WithMessages")
                    .ConfigureAwait(false);
            await using MachineToolRuntime<MachineToolState> second = await fixture.Context.AddMachineTool("Mandatory")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Capabilities");
                    identity.SerialNumber = "Mandatory";
                    identity.ProductInstanceUri = "urn:capabilities:mandatory";
                }).BuildAsync().ConfigureAwait(false);
            const string errors = "MachineTool Notification - Errors and Alerts";
            Assert.That(first.Capabilities.ConformanceUnits.ToArray()!.Any(unit => unit.Name == errors), Is.True);
            Assert.That(second.Capabilities.ConformanceUnits.ToArray()!.Any(unit => unit.Name == errors), Is.False);
            Assert.That(fixture.Context.Manager.ConformanceUnits.ToArray()!.Any(unit => unit.Name == errors), Is.True);
            Assert.That(first.Capabilities.ConformanceUnits.ToArray()!.Any(unit =>
                unit.Name == "MachineTool Production Machinery Job Management"), Is.False);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "CapabilityClient").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
            Assert.That(session.NamespaceUris.GetIndex(MachineToolCapabilityNames.NamespaceUri), Is.GreaterThan(0),
                "The instance extension namespace must be published in the real server namespace table.");
            var children = new System.Collections.Generic.List<BaseInstanceState>();
            first.State.GetChildren(fixture.Context.Context, children);
            Assert.That(children.Find(child => child.BrowseName.Name == "Capabilities")!.ReferenceTypeId,
                Is.EqualTo(ReferenceTypeIds.HasComponent));
            MachineToolCapabilityReadSnapshot actual = await client.GetCapabilitiesAsync(first.NodeId)
                .ConfigureAwait(false);
            Assert.That(actual.DeclarationAvailable, Is.True);
            Assert.That(actual.Capabilities!.MachineId, Is.EqualTo(first.NodeId));
            Assert.That(actual.Capabilities.ConformanceUnits.ToArray()!.Any(unit => unit.Name == errors), Is.True);
            Assert.That(actual.Capabilities.Monitoring.PotentialMonitoredItems, Is.EqualTo(19),
                "Explicit empty notifications add Notification and Messages notifiers to the mandatory seventeen.");
            await first.DisposeAsync().ConfigureAwait(false);
            Assert.That(fixture.Context.Manager.ConformanceUnits.ToArray()!.Any(unit => unit.Name == errors), Is.False);
            Assert.That(second.Capabilities.ConformanceUnits.Count, Is.GreaterThan(0));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Optional absence remains distinct from malformed, denied or oversized per-instance declarations.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public async Task CapabilityReadsPreserveAbsenceAndRejectInvalidDeclarationsAsync(int scenario)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var provider = new MachineToolNotificationTests.NotificationProvider(new MachineToolNotificationSnapshot());
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider).ConfigureAwait(false);
            ushort ns = (ushort)fixture.Context.Context.NamespaceUris.GetIndex(MachineToolCapabilityNames.NamespaceUri);
            var declaration = (BaseObjectState)runtime.State.FindChild(fixture.Context.Context,
                new QualifiedName(MachineToolCapabilityNames.Capabilities, ns))!;
            string fieldName = scenario switch
            {
                4 => "ServerProfiles",
                5 => "MaximumSessions",
                6 => "SupportsAll",
                _ => "ConformanceUnits"
            };
            var variable = (BaseVariableState)declaration.FindChild(
                fixture.Context.Context, new QualifiedName(fieldName, ns))!;
            switch (scenario)
            {
                case 0:
                    runtime.State.RemoveChild(declaration);
                    await fixture.Context.Manager.DeleteNodeAsync(
                        fixture.Context.Manager.SystemContext, declaration.NodeId).ConfigureAwait(false);
                    break;
                case 1:
                    declaration.RemoveChild(variable);
                    await fixture.Context.Manager.DeleteNodeAsync(
                        fixture.Context.Manager.SystemContext, variable.NodeId).ConfigureAwait(false);
                    break;
                case 2:
                    variable.StatusCode = StatusCodes.BadUserAccessDenied;
                    break;
                default:
                    Variant value = scenario switch
                    {
                        4 => Variant.From(Enumerable.Repeat("profile", 13).ToArray().ToArrayOf()),
                        7 => Variant.From(new[] { new QualifiedName(new string('x', 70000), ns) }.ToArrayOf()),
                        _ => Variant.From("not the declared type")
                    };
                    variable.OnSimpleReadValueAsync = (_, _, _) =>
                        new ValueTask<AttributeSimpleReadResult>(
                            new AttributeSimpleReadResult(ServiceResult.Good, value));
                    break;
            }
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "CapabilityValidation").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
            if (scenario == 0)
            {
                MachineToolCapabilityReadSnapshot result = await client.GetCapabilitiesAsync(runtime.NodeId)
                    .ConfigureAwait(false);
                Assert.That(result.DeclarationAvailable, Is.False);
                Assert.That(result.Capabilities, Is.Null);
            }
            else
            {
                ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await client.GetCapabilitiesAsync(runtime.NodeId).ConfigureAwait(false)))!;
                StatusCode expected = scenario switch
                {
                    2 => StatusCodes.BadUserAccessDenied,
                    7 => StatusCodes.BadEncodingLimitsExceeded,
                    _ => StatusCodes.BadDecodingError
                };
                Assert.That(error.StatusCode, Is.EqualTo(expected));
            }
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
