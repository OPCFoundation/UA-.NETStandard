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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.PADIM.Client;
using Opc.Ua.PADIM.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.PADIM.Tests
{
    /// <summary>
    /// Exercises the standalone fluent host and typed client over a real UA session.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class PadimClientServerTests
    {
        /// <summary>
        /// Reads semantic values, reverses simulation, invokes a generated method and observes retained alarms.
        /// </summary>
        [Test]
        public async Task HostedDeviceSupportsTypedDiscoverySimulationAndGeneratedOperations()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            string endpoint = $"opc.tcp://localhost:{port.ToString(CultureInfo.InvariantCulture)}/Padim";
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "padim", Guid.NewGuid().ToString("N"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            DateTimeUtc timestamp = new(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            provider.Setup(p => p.ReadAsync("temperature", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From(21.5f), StatusCodes.Good, timestamp));
            int health = 0;
            provider.Setup(p => p.ReadAsync("health", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new DataValue(Variant.From(health), StatusCodes.Good, timestamp));
            PadimValueBinding? healthBinding = null;
            var writer = new Mock<IPadimWriteProvider>(MockBehavior.Strict);
            writer.Setup(p => p.WriteAsync(
                It.IsAny<string>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StatusCode(StatusCodes.Good));
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var operations = new Mock<IPadimOperationProvider>(MockBehavior.Strict);
            operations.Setup(p => p.ZeroPointAdjustmentAsync("temperature", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StatusCode(StatusCodes.Good));
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                options.ApplicationName = "PadimE2e";
                options.ApplicationUri = "urn:localhost:PadimE2e";
                options.ProductUri = "urn:OPCFoundation:PadimE2e";
                options.PkiRoot = Path.Combine(root, "server-pki");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(endpoint);
            }).AddPadim().ConfigurePadim(async context =>
            {
                var builder = await context.Manager.CreatePadimDeviceAsync(
                    new QualifiedName("Transmitter", context.Manager.InstanceNamespaceIndex));
                PadimDevice device = builder.BindPadim();
                healthBinding = device.WithHealthAlarms("health", provider.Object);
                AnalogSignalObjectState signal = await device.AddMeasurementAsync(
                    new QualifiedName("Temperature", context.Manager.InstanceNamespaceIndex),
                    PadimMeasurementKind.Temperature, "TT-001");
                device.BindValue(signal.AnalogSignal!, "temperature", provider.Object)
                    .WithSimulation(Variant.From(99f), writer.Object, authorizer.Object);
                device.WithZeroPointAdjustment(signal, "temperature", operations.Object, authorizer.Object);
                await device.RegisterAsync(context.CancellationToken);
                await device.RefreshAsync(context.CancellationToken);
                configured.TrySetResult(true);
            });
            await using ServiceProvider container = services.BuildServiceProvider();
            IHostedService host = container.GetServices<IHostedService>().Single();
            await host.StartAsync(timeout.Token);
            try
            {
                while (!configured.Task.IsCompleted)
                {
                    if (host is BackgroundService { ExecuteTask: { IsFaulted: true } failed })
                    {
                        await failed;
                    }
                    await Task.Delay(20, timeout.Token);
                }
                using var fixture = new ClientFixture(telemetry)
                {
                    SessionFactory = new ManagedSessionFactory(telemetry)
                };
                await fixture.LoadClientConfigurationAsync(Path.Combine(root, "client-pki"));
                using ISession session = await fixture.ConnectAsync(new Uri(endpoint), SecurityPolicies.None);
                var client = new PadimClient(session, telemetry);
                ArrayOf<PadimEntry> devices = await client.DiscoverDevicesAsync(timeout.Token);
                Assert.That(devices.Count, Is.EqualTo(1));
                ArrayOf<PadimEntry> signals = await client.DiscoverSignalsAsync(devices[0].NodeId, timeout.Token);
                Assert.That(signals.Count, Is.EqualTo(1));
                ArrayOf<PadimValue> values = await client.ReadValuesAsync(signals[0].NodeId, timeout.Token);
                PadimValue measurement = values.ToArray().Single(value => value.BrowseName.Name == "AnalogSignal");
                Assert.Multiple(() =>
                {
                    Assert.That(measurement.Sample.WrappedValue, Is.EqualTo(Variant.From(21.5f)));
                    Assert.That(measurement.Sample.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(measurement.Sample.SourceTimestamp, Is.EqualTo(timestamp));
                });
                ArrayOf<ExpandedNodeId> semantics = await client.ReadDictionaryEntriesAsync(
                    measurement.NodeId, timeout.Token);
                Assert.That(semantics.Count, Is.GreaterThan(0));
                for (int ii = 0; ii < semantics.Count; ii++)
                {
                    Assert.That(semantics[ii].NamespaceUri, Is.EqualTo(IRDI.Namespaces.IRDI));
                }
                PadimProcessValueClient analog = client.ProcessValue(measurement.NodeId);
                await using (var observation = client.ObserveValueAsync(measurement.NodeId, timeout.Token)
                    .GetAsyncEnumerator(timeout.Token))
                {
                    Assert.That(await observation.MoveNextAsync(), Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(observation.Current.Value.WrappedValue, Is.EqualTo(Variant.From(21.5f)));
                        Assert.That(observation.Current.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(observation.Current.Value.SourceTimestamp, Is.EqualTo(timestamp));
                    });
                }
                await analog.SimulationValue.WriteAsync(Variant.From(99f), timeout.Token);
                await analog.SimulationState.WriteAsync(true, timeout.Token);
                values = await client.ReadValuesAsync(signals[0].NodeId, timeout.Token);
                Assert.That(values.ToArray().Single(value => value.NodeId == measurement.NodeId).Sample.WrappedValue,
                    Is.EqualTo(Variant.From(99f)));
                await analog.SimulationState.WriteAsync(false, timeout.Token);
                values = await client.ReadValuesAsync(signals[0].NodeId, timeout.Token);
                Assert.That(values.ToArray().Single(value => value.NodeId == measurement.NodeId).Sample.WrappedValue,
                    Is.EqualTo(Variant.From(21.5f)));
                await client.ZeroPointAdjustmentAsync(signals[0].NodeId, timeout.Token);
                operations.VerifyAll();
                var managed = (ManagedSession)session;
                EventFilter filter = HealthEventFilter();
                await using (IAsyncEnumerator<EventNotification> events = managed.DefaultStreaming
                    .SubscribeEventsWhenReadyAsync(devices[0].NodeId, filter, async ct =>
                    {
                        health = 1;
                        await healthBinding!.RefreshAsync(ct);
                    }, ct: timeout.Token).GetAsyncEnumerator(timeout.Token))
                {
                    Assert.That(await events.MoveNextAsync(), Is.True);
                    ArrayOf<Variant> active = events.Current.Fields;
                    Assert.Multiple(() =>
                    {
                        Assert.That(active[0].TryGetValue(out NodeId _), Is.True);
                        Assert.That(active[1].TryGetValue(out ByteString _), Is.True);
                        Assert.That(active[2], Is.EqualTo(Variant.From(devices[0].NodeId)));
                        Assert.That(active[3], Is.EqualTo(Variant.From(true)));
                        Assert.That(active[4], Is.EqualTo(Variant.From(false)));
                        Assert.That(active[5], Is.EqualTo(Variant.From(true)));
                        Assert.That(active[6], Is.EqualTo(Variant.From(new StatusCode(StatusCodes.Good))));
                    });
                    active[0].TryGetValue(out NodeId conditionId);
                    active[1].TryGetValue(out ByteString eventId);
                    var alarms = new AlarmClient(session, telemetry);
                    await alarms.AcknowledgeAsync(conditionId, eventId, LocalizedText.From("Observed"), timeout.Token);
                    Assert.That(await events.MoveNextAsync(), Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(events.Current.Fields[3], Is.EqualTo(Variant.From(true)));
                        Assert.That(events.Current.Fields[4], Is.EqualTo(Variant.From(true)));
                        Assert.That(events.Current.Fields[5], Is.EqualTo(Variant.From(true)));
                    });
                    await using (IAsyncEnumerator<EventNotification> refresh = managed.DefaultStreaming
                        .SubscribeEventsWithRefreshAsync(devices[0].NodeId, filter, ct: timeout.Token)
                        .GetAsyncEnumerator(timeout.Token))
                    {
                        ArrayOf<Variant> retained = await NextConditionAsync(refresh, conditionId);
                        Assert.Multiple(() =>
                        {
                            Assert.That(retained[3], Is.EqualTo(Variant.From(true)));
                            Assert.That(retained[4], Is.EqualTo(Variant.From(true)));
                            Assert.That(retained[5], Is.EqualTo(Variant.From(true)));
                        });
                    }
                    health = 0;
                    await healthBinding!.RefreshAsync(timeout.Token);
                    ArrayOf<Variant> reversed = await NextConditionAsync(events, conditionId);
                    Assert.Multiple(() =>
                    {
                        Assert.That(reversed[3], Is.EqualTo(Variant.From(false)));
                        Assert.That(reversed[4], Is.EqualTo(Variant.From(true)));
                        Assert.That(reversed[5], Is.EqualTo(Variant.From(false)));
                    });
                }
                Assert.That(session.Connected, Is.True, "The PA-DIM client borrows rather than closes the session.");
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// Selects the standard condition identity, event identity, source and lifecycle fields.
        /// </summary>
        private static EventFilter HealthEventFilter()
        {
            return new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = Opc.Ua.Types.ObjectTypeIds.ConditionType,
                        AttributeId = Attributes.NodeId
                    },
                    Field(Opc.Ua.BrowseNames.EventId),
                    Field(Opc.Ua.BrowseNames.SourceNode),
                    Field(Opc.Ua.BrowseNames.ActiveState, Opc.Ua.BrowseNames.Id),
                    Field(Opc.Ua.BrowseNames.AckedState, Opc.Ua.BrowseNames.Id),
                    Field(Opc.Ua.BrowseNames.Retain),
                    Field(Opc.Ua.BrowseNames.Quality)
                ]
            };

            static SimpleAttributeOperand Field(string name, string? child = null)
            {
                return new SimpleAttributeOperand
                {
                    TypeDefinitionId = Opc.Ua.Types.ObjectTypeIds.AlarmConditionType,
                    BrowsePath = child == null
                        ? [new QualifiedName(name)]
                        : [new QualifiedName(name), new QualifiedName(child)],
                    AttributeId = Attributes.Value
                };
            }
        }

        /// <summary>
        /// Skips refresh markers while retaining the exact condition fields from the wire.
        /// </summary>
        private static async Task<ArrayOf<Variant>> NextConditionAsync(
            IAsyncEnumerator<EventNotification> events,
            NodeId conditionId)
        {
            while (await events.MoveNextAsync())
            {
                ArrayOf<Variant> fields = events.Current.Fields;
                if (fields.Count == 7 && fields[0].TryGetValue(out NodeId candidate) && candidate == conditionId)
                {
                    return fields;
                }
            }
            throw new InvalidOperationException("The condition stream ended before the expected condition.");
        }
    }
}
