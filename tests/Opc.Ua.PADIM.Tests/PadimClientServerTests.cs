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
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.PADIM.Client;
using Opc.Ua.PADIM.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;

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
                .ReturnsAsync(StatusCodes.Good);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(p => p.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var operations = new Mock<IPadimOperationProvider>(MockBehavior.Strict);
            operations.Setup(p => p.ZeroPointAdjustmentAsync("temperature", It.IsAny<CancellationToken>()))
                .ReturnsAsync(StatusCodes.Good);
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
                PadimValue measurement = values.ToList().Single(value => value.BrowseName.Name == "AnalogSignal");
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
                Assert.That(values.ToList().Single(value => value.NodeId == measurement.NodeId).Sample.WrappedValue,
                    Is.EqualTo(Variant.From(99f)));
                await analog.SimulationState.WriteAsync(false, timeout.Token);
                values = await client.ReadValuesAsync(signals[0].NodeId, timeout.Token);
                Assert.That(values.ToList().Single(value => value.NodeId == measurement.NodeId).Sample.WrappedValue,
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
                        Assert.That(active[6], Is.EqualTo(Variant.From(StatusCodes.Good)));
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
        /// Discovers every hosted PADIM family and reads all bound primary and published interface values.
        /// </summary>
        [Test]
        public async Task HostedAllPublishedFamiliesDiscoverAndReadTypedFacetSamples()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            string endpoint = $"opc.tcp://localhost:{port.ToString(CultureInfo.InvariantCulture)}/PadimFacets";
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "padim-facets",
                Guid.NewGuid().ToString("N"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var samples = new PadimPublishedSampleSet();
            DateTimeUtc administrativeTimestamp =
                new(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            var administration = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            administration.Setup(provider => provider.ReadAsync("displayLanguage", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(Variant.From("en-US"), StatusCodes.Good, administrativeTimestamp));
            administration.Setup(provider => provider.ReadAsync("dateOfLastChange", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataValue(
                    Variant.From(administrativeTimestamp), StatusCodes.Good, administrativeTimestamp));
            var writer = new Mock<IPadimWriteProvider>(MockBehavior.Strict);
            var authorizer = new Mock<IPadimAuthorizer>(MockBehavior.Strict);
            var deviceStates = new Dictionary<PadimDeviceKind, PadimDevice>();
            var expectedSignals = new Dictionary<string, (NodeId NodeId, string TypeName)>(StringComparer.Ordinal);
            var expectedMeasurementTypes = new Dictionary<NodeId, string>();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                options.ApplicationName = "PadimFacetRuntime";
                options.ApplicationUri = "urn:localhost:PadimFacetRuntime";
                options.ProductUri = "urn:OPCFoundation:PadimFacetRuntime";
                options.PkiRoot = Path.Combine(root, "server-pki");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(endpoint);
            }).AddPadim().ConfigurePadim(async context =>
            {
                try
                {
                    foreach (PadimDeviceKind kind in PadimRuntimeTests.GetEnumValues<PadimDeviceKind>())
                    {
                        var builder = await context.Manager.CreatePadimDeviceAsync(
                            new QualifiedName($"FacetDevice{kind}", context.Manager.InstanceNamespaceIndex), kind);
                        PadimDevice device = builder.BindPadim();
                        SetIdentity(device);
                        deviceStates.Add(kind, device);
                        List<System.Xml.Linq.XElement> declarations =
                            PadimRuntimeTests.GroupDeclarations(device.State.TypeDefinitionId, "DeviceConditionSet");
                        Assert.That(declarations, Is.Not.Empty, $"{kind} has a published device condition set.");
                        await device.ConfigureDeviceConditionsAsync(group =>
                            PadimRuntimeTests.SelectPublishedMembers(device, group, declarations, samples));
                        if (kind == PadimDeviceKind.Device)
                        {
                            samples.Track(device,
                                device.WithHealthAlarms(samples.NextKey(), samples.Provider));
                        }
                        else
                        {
                            samples.Bind(device, device.State.DeviceHealth!);
                        }
                    }

                    PadimDevice catalog = deviceStates[PadimDeviceKind.Device];
                    catalog.WithAdministration(administration.Object, writer.Object, authorizer.Object);
                    foreach (PadimSignalKind kind in PadimRuntimeTests.GetEnumValues<PadimSignalKind>())
                    {
                        string browseName = $"FacetSignal{kind}";
                        SignalState signal = await catalog.AddSignalAsync(
                            new QualifiedName(browseName, catalog.Manager.InstanceNamespaceIndex), kind, $"TAG-{kind}");
                        string primaryName = PrimaryVariableName(kind);
                        var primary =
                            signal.FindChild(catalog.Context, ModelName(catalog, primaryName)) as BaseVariableState ??
                            throw new InvalidOperationException($"The {kind} primary value '{primaryName}' is absent.");
                        NodeId primaryType = PadimRuntimeTests.PublishedVariableTypeId(
                            catalog, signal.TypeDefinitionId, primaryName);
                        Assert.That(primary.TypeDefinitionId, Is.EqualTo(primaryType),
                            $"The {kind} primary variable retains its published type definition.");
                        PadimValueBinding primaryBinding = samples.Bind(catalog, primary);
                        if (kind == PadimSignalKind.Analog)
                        {
                            primaryBinding.WithSimulation(Variant.From(5f), writer.Object, authorizer.Object);
                        }
                        foreach ((string childName, NodeId dataType) in RequiredSignalChildren(kind))
                        {
                            BindRequiredChild(catalog, primary, childName, dataType, samples);
                        }
                        if (primary is PatMeasurementVariableState pat)
                        {
                            pat.AddPatMeasurandDescription(catalog.Context);
                            samples.Bind(catalog, pat.PatMeasurandDescription!);
                        }

                        expectedSignals.Add(browseName,
                            (signal.NodeId, PublishedSignalTypeName(kind)));

                        List<System.Xml.Linq.XElement> conditions =
                            PadimRuntimeTests.GroupDeclarations(signal.TypeDefinitionId, "SignalConditionSet");
                        if (conditions.Count != 0)
                        {
                            await catalog.ConfigureSignalConditionsAsync(signal, group =>
                                PadimRuntimeTests.SelectPublishedMembers(catalog, group, conditions, samples));
                        }
                        if (signal is AnalogSignalObjectState analog)
                        {
                            List<System.Xml.Linq.XElement> calibrations =
                                PadimRuntimeTests.GroupDeclarations(
                                    signal.TypeDefinitionId, "<SignalCalibrationIdentifier>");
                            if (calibrations.Count != 0)
                            {
                                await catalog.AddCalibrationAsync(
                                    analog,
                                    new QualifiedName($"Calibration{kind}", catalog.Manager.InstanceNamespaceIndex),
                                    group => PadimRuntimeTests.SelectPublishedMembers(
                                        catalog, group, calibrations, samples));
                            }
                        }
                    }

                    foreach (PadimMeasurementKind kind in PadimRuntimeTests.GetEnumValues<PadimMeasurementKind>())
                    {
                        string browseName = $"FacetMeasurement{kind}";
                        AnalogSignalObjectState signal = await catalog.AddMeasurementAsync(
                            new QualifiedName(browseName, catalog.Manager.InstanceNamespaceIndex),
                            kind,
                            $"PV-{kind}");
                        BaseVariableState primary = signal.AnalogSignal!;
                        string variableTypeName = MeasurementVariableTypeName(kind);
                        NodeId publishedVariableType = PadimRuntimeTests.PublishedTypeId(catalog, variableTypeName);
                        Assert.That(primary.TypeDefinitionId, Is.EqualTo(publishedVariableType),
                            $"{kind} retains the exact published measurement variable type.");
                        PadimValueBinding binding = samples.Bind(catalog, primary);
                        expectedSignals.Add(browseName, (signal.NodeId, "AnalogSignalType"));
                        expectedMeasurementTypes.Add(binding.Variable.NodeId, variableTypeName);
                        foreach ((string childName, NodeId dataType) in RequiredMeasurementChildren(kind))
                        {
                            BindRequiredChild(catalog, primary, childName, dataType, samples);
                        }
                        string[] optionalChildren = kind switch
                        {
                            PadimMeasurementKind.Temperature =>
                                ["SensorConnection", "SensorReference", "SensorClass"],
                            PadimMeasurementKind.Flow => ["FlowDirection"],
                            _ => []
                        };
                        foreach (string childName in optionalChildren)
                        {
                            await BindOptionalMeasurementChildAsync(
                                catalog, primary, childName, samples, context.CancellationToken);
                        }

                        if (kind == PadimMeasurementKind.Pat)
                        {
                            if (primary is not PatMeasurementVariableState pat)
                            {
                                throw new InvalidOperationException(
                                    "The PAT measurement did not use its generated state.");
                            }
                            pat.AddPatMeasurandDescription(catalog.Context);
                            Assert.That(pat.PatMeasurandDescription!.ValueRank, Is.EqualTo(ValueRanks.Scalar),
                                "PatMeasurandDescription omits ValueRank in the published NodeSet and is scalar.");
                            Assert.That(pat.PatMeasurandDescription.DataType,
                                Is.EqualTo(PadimRuntimeTests.PublishedNodeId(catalog, "ns=3;i=1275")));
                            samples.Bind(catalog, pat.PatMeasurandDescription);

                            pat.AddPatMatrixDescription(catalog.Context);
                            Assert.That(pat.PatMatrixDescription!.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                            samples.Bind(catalog, pat.PatMatrixDescription);
                        }
                    }

                    foreach (PadimDevice device in deviceStates.Values)
                    {
                        await device.RegisterAsync(context.CancellationToken);
                    }
                    foreach (PadimDevice device in deviceStates.Values)
                    {
                        await device.RefreshAsync(context.CancellationToken);
                    }
                    configured.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    configured.TrySetException(exception);
                    throw;
                }
            });
            await using ServiceProvider container = services.BuildServiceProvider();
            IHostedService host = container.GetServices<IHostedService>().Single();
            await host.StartAsync(timeout.Token);
            try
            {
                using (timeout.Token.Register(() => configured.TrySetCanceled()))
                {
                    await configured.Task.ConfigureAwait(false);
                }
                samples.AssertPublished();
                administration.Verify(
                    provider => provider.ReadAsync("displayLanguage", It.IsAny<CancellationToken>()), Times.Once);
                administration.Verify(
                    provider => provider.ReadAsync("dateOfLastChange", It.IsAny<CancellationToken>()), Times.Once);
                administration.VerifyNoOtherCalls();

                using var fixture = new ClientFixture(telemetry)
                {
                    SessionFactory = new ManagedSessionFactory(telemetry)
                };
                await fixture.LoadClientConfigurationAsync(Path.Combine(root, "client-pki"));
                using ISession session = await fixture.ConnectAsync(new Uri(endpoint), SecurityPolicies.None);
                var client = new PadimClient(session, telemetry);
                string[] expectedPadimProfiles = deviceStates.Values
                    .SelectMany(device => device.ServerProfiles.ToList())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                Assert.That(expectedPadimProfiles, Has.Length.EqualTo(56));
                DataValue profileReadback = await session.ReadValueAsync(
                    Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray, timeout.Token);
                Assert.That(profileReadback.WrappedValue.TryGetValue(out ArrayOf<string> advertisedProfiles), Is.True);
                Assert.That(
                    advertisedProfiles.ToList().Where(profile =>
                        profile.StartsWith("http://opcfoundation.org/UA-Profile/PADIM/Server/",
                            StringComparison.Ordinal)),
                    Is.EquivalentTo(expectedPadimProfiles),
                    "The live ServerProfileArray contains exactly the configured PA-DIM facets.");
                QualifiedName[] expectedPadimUnits = deviceStates.Values
                    .SelectMany(device => device.ConformanceUnits.ToList())
                    .Distinct()
                    .ToArray();
                DataValue unitReadback = await session.ReadValueAsync(
                    Opc.Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits, timeout.Token);
                Assert.That(unitReadback.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> advertisedUnits), Is.True);
                Assert.That(
                    advertisedUnits.ToList().Where(unit =>
                        unit.Name?.StartsWith("PA-DIM ", StringComparison.Ordinal) == true),
                    Is.EquivalentTo(expectedPadimUnits),
                    "The live ConformanceUnits variable contains the configured PA-DIM units.");
                QualifiedName[] independentlyExpectedUnits =
                [
                    new("PA-DIM Base"),
                    new("PA-DIM Nano Embedded")
                ];
                Assert.That(
                    advertisedUnits.ToList().Where(unit =>
                        unit.Name?.StartsWith("PA-DIM ", StringComparison.Ordinal) == true),
                    Is.SupersetOf(independentlyExpectedUnits),
                    "The fully configured fixture independently requires its PA-DIM base units.");
                QualifiedName[] independentlyExpectedMeasurementUnits =
                [
                    new("PA-DIM TemperatureMeasurementVariable"),
                    new("PA-DIM TemperatureMeasurementVariable Connection"),
                    new("PA-DIM TemperatureMeasurementVariable Reference"),
                    new("PA-DIM TemperatureMeasurementVariable SensorClass"),
                    new("PA-DIM FlowMeasurementVariable"),
                    new("PA-DIM FlowMeasurementVariable FlowDirection")
                ];
                Assert.That(
                    advertisedUnits.ToList().Where(unit =>
                        unit.Name?.StartsWith(
                            "PA-DIM TemperatureMeasurementVariable", StringComparison.Ordinal) == true ||
                        unit.Name?.StartsWith("PA-DIM FlowMeasurementVariable", StringComparison.Ordinal) == true),
                    Is.EquivalentTo(independentlyExpectedMeasurementUnits),
                    "The independent measurement-unit catalogue matches the fully configured optional members.");
                ArrayOf<PadimEntry> discoveredDevices = await client.DiscoverDevicesAsync(timeout.Token);
                Assert.That(discoveredDevices.Count, Is.EqualTo(deviceStates.Count));
                foreach (KeyValuePair<PadimDeviceKind, PadimDevice> device in deviceStates)
                {
                    string browseName = $"FacetDevice{device.Key}";
                    PadimEntry entry = discoveredDevices.ToList().Single(
                        candidate => candidate.BrowseName.Name == browseName);
                    Assert.That(entry.TypeDefinitionId, Is.EqualTo(PadimRuntimeTests.PublishedTypeId(
                        session.NamespaceUris, PublishedDeviceTypeName(device.Key))), browseName);
                    ArrayOf<PadimValue> identityValues = await client.ReadValuesAsync(entry.NodeId, timeout.Token);
                    string[] mandatoryIdentityNames =
                    [
                        "Manufacturer", "ManufacturerUri", "Model", "SerialNumber", "SoftwareRevision",
                        "HardwareRevision", "ProductCode", "ProductInstanceUri", "AssetId", "RevisionCounter"
                    ];
                    Assert.That(identityValues.ToList().Select(value => value.BrowseName.Name),
                        Is.SupersetOf(mandatoryIdentityNames), browseName);
                    foreach (string identityName in mandatoryIdentityNames)
                    {
                        PadimValue identityValue = identityValues.ToList().Single(
                            value => value.BrowseName.Name == identityName);
                        Assert.Multiple(() =>
                        {
                            Assert.That(identityValue.Sample.StatusCode, Is.EqualTo(StatusCodes.Good), identityName);
                            Assert.That(identityValue.Sample.WrappedValue.IsNull, Is.False, identityName);
                        });
                    }
                    PadimValue assetId = identityValues.ToList().Single(value => value.BrowseName.Name == "AssetId");
                    Assert.That(assetId.Sample.WrappedValue,
                        Is.EqualTo(Variant.From($"asset:{device.Value.State.NodeId}")), browseName);
                    Assert.That(device.Value.State.AssetId!.AccessLevel & AccessLevels.CurrentWrite, Is.Zero,
                        "Publishing configured identity must not enable unbound remote writes.");
                    if (device.Key == PadimDeviceKind.Device)
                    {
                        foreach ((string name, Variant value) in new[]
                        {
                            ("DisplayLanguage", Variant.From("en-US")),
                            ("DateOfLastChange", Variant.From(administrativeTimestamp))
                        })
                        {
                            PadimValue actual = identityValues.ToList().Single(
                                candidate => candidate.BrowseName.Name == name);
                            Assert.Multiple(() =>
                            {
                                Assert.That(actual.Sample.StatusCode, Is.EqualTo(StatusCodes.Good), name);
                                Assert.That(actual.Sample.WrappedValue, Is.EqualTo(value), name);
                            });
                        }
                    }
                    ArrayOf<PadimEntry> signals = await client.DiscoverSignalsAsync(entry.NodeId, timeout.Token);
                    if (device.Key == PadimDeviceKind.Device)
                    {
                        Assert.That(signals.Count, Is.EqualTo(expectedSignals.Count));
                        foreach (KeyValuePair<string, (NodeId NodeId, string TypeName)> expected in expectedSignals)
                        {
                            PadimEntry signal = signals.ToList().Single(
                                candidate => candidate.BrowseName.Name == expected.Key);
                            Assert.That(signal.NodeId, Is.EqualTo(MapNodeId(
                                device.Value, session.NamespaceUris, expected.Value.NodeId)), expected.Key);
                            Assert.That(signal.TypeDefinitionId, Is.EqualTo(
                                PadimRuntimeTests.PublishedTypeId(session.NamespaceUris, expected.Value.TypeName)),
                                expected.Key);
                            foreach (KeyValuePair<NodeId, string> measurement in expectedMeasurementTypes)
                            {
                                NodeId remappedMeasurementId = MapNodeId(
                                    device.Value, session.NamespaceUris, measurement.Key);
                                if (signal.NodeId == remappedMeasurementId)
                                {
                                    ArrayOf<ReferenceDescription> children = await client.BrowseChildrenAsync(
                                        signal.NodeId, timeout.Token);
                                    ReferenceDescription primary = children.ToList().Single(child =>
                                        child.BrowseName.Name == "AnalogSignal");
                                    Assert.That(ExpandedNodeId.ToNodeId(
                                        primary.TypeDefinition, session.NamespaceUris), Is.EqualTo(
                                            PadimRuntimeTests.PublishedTypeId(
                                                session.NamespaceUris, measurement.Value)), expected.Key);
                                }
                            }
                        }
                    }
                    else
                    {
                        Assert.That(signals, Is.Empty, browseName);
                    }
                }

                PadimDevice namespaceSource = deviceStates[PadimDeviceKind.Device];
                foreach (IGrouping<NodeId, PadimPublishedSample> parent in samples.Samples
                    .GroupBy(sample => sample.ParentId))
                {
                    NodeId parentId = MapNodeId(namespaceSource, session.NamespaceUris, parent.Key);
                    ArrayOf<PadimValue> values = await client.ReadValuesAsync(parentId, timeout.Token);
                    ArrayOf<ReferenceDescription> references = await client.BrowseChildrenAsync(
                        parentId, timeout.Token);
                    foreach (PadimPublishedSample expected in parent)
                    {
                        NodeId variableId = MapNodeId(namespaceSource, session.NamespaceUris, expected.NodeId);
                        PadimValue actual = values.ToList().Single(value => value.NodeId == variableId);
                        Assert.Multiple(() =>
                        {
                            Assert.That(actual.BrowseName.Name, Is.EqualTo(expected.BrowseName.Name), expected.Key);
                            Assert.That(actual.Sample.StatusCode, Is.EqualTo(expected.Sample.StatusCode), expected.Key);
                            Assert.That(actual.Sample.SourceTimestamp, Is.EqualTo(expected.Sample.SourceTimestamp),
                                expected.Key);
                        });
                        PadimPublishedSampleSet.AssertWireSample(
                            expected.Sample.WrappedValue, actual.Sample.WrappedValue, expected.Key);
                        ReferenceDescription reference = references.ToList().Single(candidate =>
                            ExpandedNodeId.ToNodeId(candidate.NodeId, session.NamespaceUris) == variableId);
                        Assert.That(ExpandedNodeId.ToNodeId(reference.TypeDefinition, session.NamespaceUris),
                            Is.EqualTo(MapNodeId(
                                namespaceSource, session.NamespaceUris, expected.TypeDefinitionId)), expected.Key);
                    }
                }

                foreach ((string typeName, string memberName) in new[]
                {
                    ("TemperatureMeasurementVariableType", "SensorConnection"),
                    ("TemperatureMeasurementVariableType", "SensorReference"),
                    ("TemperatureMeasurementVariableType", "SensorClass"),
                    ("FlowMeasurementVariableType", "FlowDirection")
                })
                {
                    NodeId measurementId = expectedMeasurementTypes.Single(
                        measurement => measurement.Value == typeName).Key;
                    PadimPublishedSample optionalSample = samples.Samples.Single(
                        sample => sample.ParentId == measurementId && sample.BrowseName.Name == memberName);
                    NodeId optionalNodeId = MapNodeId(namespaceSource, session.NamespaceUris, optionalSample.NodeId);
                    NodeId optionalParentId =
                        MapNodeId(namespaceSource, session.NamespaceUris, optionalSample.ParentId);
                    ArrayOf<PadimValue> optionalValues =
                        await client.ReadValuesAsync(optionalParentId, timeout.Token);
                    PadimValue actual = optionalValues.ToList().Single(value => value.NodeId == optionalNodeId);
                    ReadResponse metadata = await session.ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        [
                            new ReadValueId { NodeId = optionalNodeId, AttributeId = Attributes.DataType },
                            new ReadValueId { NodeId = optionalNodeId, AttributeId = Attributes.ValueRank }
                        ],
                        timeout.Token);
                    Assert.That(metadata.Results.Count, Is.EqualTo(2), memberName);
                    DataValue dataType = metadata.Results[0];
                    DataValue valueRank = metadata.Results[1];
                    Variant expectedValue = optionalSample.Binding.Variable is MultiStateValueDiscreteState enumeration
                        ? Variant.From(checked((uint)enumeration.EnumValues!.Value[0].Value))
                        : Variant.From(17u);
                    Assert.Multiple(() =>
                    {
                        Assert.That(dataType.StatusCode, Is.EqualTo(StatusCodes.Good), memberName);
                        Assert.That(dataType.WrappedValue, Is.EqualTo(Variant.From(Opc.Ua.DataTypeIds.UInt32)),
                            memberName);
                        Assert.That(valueRank.StatusCode, Is.EqualTo(StatusCodes.Good), memberName);
                        Assert.That(valueRank.WrappedValue, Is.EqualTo(Variant.From(ValueRanks.Any)), memberName);
                        Assert.That(actual.Sample.WrappedValue, Is.EqualTo(expectedValue), memberName);
                        Assert.That(actual.Sample.StatusCode,
                            Is.EqualTo(StatusCodes.UncertainSensorNotAccurate), memberName);
                    });
                }

                PadimPublishedSample patMeasurand = samples.Samples.Single(
                    sample => sample.BrowseName.Name == "PatMeasurandDescription" &&
                        sample.Binding.Variable.Parent is BaseInstanceState parent &&
                        parent.Parent is { } signal &&
                        signal.NodeId == expectedSignals["FacetMeasurementPat"].NodeId);
                Assert.That(patMeasurand.Sample.WrappedValue.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(patMeasurand.Sample.WrappedValue.TryGetStructure<ChemicalSubstance>(
                    out ChemicalSubstance? patSubstance), Is.True);
                Assert.That(patSubstance!.PatDictionary, Is.EqualTo(PatDictionaryEnum.CAS));
                PadimPublishedSample patMatrix = samples.Samples.Single(
                    sample => sample.BrowseName.Name == "PatMatrixDescription");
                Assert.That(patMatrix.Sample.WrappedValue.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(patMatrix.Sample.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> matrix), Is.True);
                Assert.That(matrix.Count, Is.EqualTo(1));
                await AssertDynamicMembersRequireRegistrationAsync(
                    namespaceSource, session, client, timeout.Token);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// Runtime contribution changes reach the live arrays and support lookup only through explicit publication.
        /// </summary>
        [Test]
        public async Task HostedRuntimeFacetChangesRequireExplicitPublication()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            string endpoint = $"opc.tcp://localhost:{port.ToString(CultureInfo.InvariantCulture)}/PadimPublication";
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "padim-publication",
                Guid.NewGuid().ToString("N"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var configured = new TaskCompletionSource<PadimDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                options.ApplicationName = "PadimPublication";
                options.ApplicationUri = "urn:localhost:PadimPublication";
                options.ProductUri = "urn:OPCFoundation:PadimPublication";
                options.PkiRoot = Path.Combine(root, "server-pki");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(endpoint);
            }).AddPadim().ConfigurePadim(async context =>
            {
                var builder = await context.Manager.CreatePadimDeviceAsync(
                    new QualifiedName("RuntimeDevice", context.Manager.InstanceNamespaceIndex));
                configured.TrySetResult(builder.BindPadim());
            });
            await using ServiceProvider container = services.BuildServiceProvider();
            IHostedService host = container.GetServices<IHostedService>().Single();
            await host.StartAsync(timeout.Token);
            try
            {
                PadimDevice device;
                using (timeout.Token.Register(() => configured.TrySetCanceled()))
                {
                    device = await configured.Task.ConfigureAwait(false);
                }
                var unit = new QualifiedName("PA-DIM TwoStateDiscreteVariable");
                using var fixture = new ClientFixture(telemetry)
                {
                    SessionFactory = new ManagedSessionFactory(telemetry)
                };
                await fixture.LoadClientConfigurationAsync(Path.Combine(root, "client-pki"));
                using ISession session = await fixture.ConnectAsync(new Uri(endpoint), SecurityPolicies.None);
                Assert.That(device.Manager.Server, Is.InstanceOf<ServerInternalData>());
                ConformanceUnitsManager catalog = ((ServerInternalData)device.Manager.Server).ConformanceUnitsManager;
                Assert.That(catalog, Is.Not.Null, "The connected host has completed conformance-manager startup.");
                await AssertPublicationAsync([], [], false);

                var samples = new PadimPublishedSampleSet();
                var signal = (TwoStateDiscreteSignalObjectState)await device.AddSignalAsync(
                    new QualifiedName("RuntimeSignal", device.Manager.InstanceNamespaceIndex),
                    PadimSignalKind.TwoStateDiscrete, "DI-RUNTIME", cancellationToken: timeout.Token);
                samples.Bind(device, signal.TwoStateDiscreteSignal!);
                await device.RegisterAsync(timeout.Token);
                await device.RefreshAsync(timeout.Token);
                string[] expectedProfiles = device.ServerProfiles.Span.ToArray();
                QualifiedName[] expectedUnits = device.ConformanceUnits.Span.ToArray();
                Assert.That(expectedProfiles,
                    Does.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TwoStateDiscreteSignal"));
                Assert.That(expectedUnits, Does.Contain(unit));
                await AssertPublicationAsync([], [], false);
                await catalog.PublishAsync(timeout.Token);
                await AssertPublicationAsync(expectedProfiles, expectedUnits, true);

                await device.DisposeAsync();
                Assert.That(device.ServerProfiles.Count, Is.Zero);
                Assert.That(device.ConformanceUnits.Count, Is.Zero);
                await AssertPublicationAsync(expectedProfiles, expectedUnits, true);
                await catalog.PublishAsync(timeout.Token);
                await AssertPublicationAsync([], [], false);

                async Task AssertPublicationAsync(string[] profiles, QualifiedName[] units, bool supported)
                {
                    (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) = await session.ReadValuesAsync(
                        [Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray,
                            Opc.Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits], timeout.Token);
                    Assert.That(values.Count, Is.EqualTo(2));
                    Assert.That(errors.Count, Is.EqualTo(2));
                    for (int ii = 0; ii < errors.Count; ii++)
                    {
                        Assert.That(ServiceResult.IsGood(errors[ii]), Is.True);
                        Assert.That(values[ii].StatusCode, Is.EqualTo(StatusCodes.Good));
                    }
                    Assert.That(values[0].WrappedValue.TryGetValue(out ArrayOf<string> liveProfiles), Is.True);
                    Assert.That(values[1].WrappedValue.TryGetValue(out ArrayOf<QualifiedName> liveUnits), Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(liveProfiles.ToList().Where(profile => profile.StartsWith(
                            "http://opcfoundation.org/UA-Profile/PADIM/Server/", StringComparison.Ordinal)),
                            Is.EquivalentTo(profiles));
                        Assert.That(liveUnits.ToList().Where(value =>
                            value.Name?.StartsWith("PA-DIM ", StringComparison.Ordinal) == true),
                            Is.EquivalentTo(units));
                        Assert.That(catalog.IsSupported(unit), Is.EqualTo(supported));
                    });
                }
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// Verifies that late-bound dynamic members contribute only after registration and are readable over UA.
        /// </summary>
        private static async Task AssertDynamicMembersRequireRegistrationAsync(
            PadimDevice namespaceSource, ISession session, PadimClient client, CancellationToken cancellationToken)
        {
            var builder = await namespaceSource.Manager.CreatePadimDeviceAsync(
                new QualifiedName("LateRegisteredDevice", namespaceSource.Manager.InstanceNamespaceIndex),
                cancellationToken: cancellationToken);
            PadimDevice device = builder.BindPadim();
            SetIdentity(device);
            device.State.DeviceHealth!.BrowseName =
                new QualifiedName("OriginalHealth", device.Manager.InstanceNamespaceIndex);
            var health = new BaseDataVariableState(device.State)
            {
                NodeId = new NodeId("LateHealth", device.Manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName(
                    "DeviceHealth", (ushort)device.Context.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi)),
                DataType = device.State.DeviceHealth.DataType,
                TypeDefinitionId = device.State.DeviceHealth.TypeDefinitionId,
                ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead
            };
            AnalogSignalObjectState signal = await device.AddMeasurementAsync(
                new QualifiedName("LateTemperature", device.Manager.InstanceNamespaceIndex),
                PadimMeasurementKind.Temperature, "TT-LATE", cancellationToken: cancellationToken);
            device.State.AddChild(health);
            signal.AnalogSignal!.BrowseName =
                new QualifiedName("OriginalMeasurement", device.Manager.InstanceNamespaceIndex);
            ushort padimNamespace = (ushort)device.Context.NamespaceUris.GetIndex(Namespaces.PADIM);
            TemperatureMeasurementVariableState primary =
                device.Context.CreateInstanceOfTemperatureMeasurementVariableType(
                    signal, new QualifiedName("AnalogSignal", padimNamespace));
            primary.NodeId = new NodeId("LatePrimary", device.Manager.InstanceNamespaceIndex);
            primary.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            signal.AddChild(primary);
            var samples = new PadimPublishedSampleSet();
            samples.Bind(device, health);
            samples.Bind(device, primary);
            samples.Bind(device, primary.SensorType!);
            Assert.Multiple(() =>
            {
                Assert.That(device.Manager.FindPredefinedNode(health.NodeId), Is.Null);
                Assert.That(device.Manager.FindPredefinedNode(primary.NodeId), Is.Null);
                Assert.That(device.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("PA-DIM Base")));
                Assert.That(device.ServerProfiles.ToArray(),
                    Does.Not.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TemperatureSignal"));
            });
            (ArrayOf<DataValue> _, ArrayOf<ServiceResult> errors) = await session.ReadValuesAsync(
                [MapNodeId(device, session.NamespaceUris, health.NodeId),
                    MapNodeId(device, session.NamespaceUris, primary.NodeId)], cancellationToken);
            Assert.That(errors.Count, Is.EqualTo(2));
            for (int ii = 0; ii < errors.Count; ii++)
            {
                Assert.That(errors[ii].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            await device.RegisterAsync(cancellationToken);
            await device.RefreshAsync(cancellationToken);
            Assert.Multiple(() =>
            {
                Assert.That(device.Manager.FindPredefinedNode(health.NodeId), Is.SameAs(health));
                Assert.That(device.Manager.FindPredefinedNode(primary.NodeId), Is.SameAs(primary));
                Assert.That(device.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("PA-DIM Base")));
                Assert.That(device.ServerProfiles.ToArray(),
                    Does.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TemperatureSignal"));
            });
            foreach (PadimPublishedSample expected in samples.Samples)
            {
                BaseVariableState variable = expected.Binding.Variable;
                NodeId parentId = MapNodeId(device, session.NamespaceUris, variable.Parent!.NodeId);
                NodeId variableId = MapNodeId(device, session.NamespaceUris, variable.NodeId);
                ArrayOf<ReferenceDescription> children = await client.BrowseChildrenAsync(parentId, cancellationToken);
                Assert.That(children.ToList().Any(child =>
                    ExpandedNodeId.ToNodeId(child.NodeId, session.NamespaceUris) == variableId), Is.True);
                (ArrayOf<DataValue> values, ArrayOf<ServiceResult> readErrors) =
                    await session.ReadValuesAsync([variableId], cancellationToken);
                Assert.That(ServiceResult.IsGood(readErrors[0]), Is.True);
                PadimPublishedSampleSet.AssertWireSample(
                    expected.Sample.WrappedValue, values[0].WrappedValue, expected.Key);
                Assert.That(values[0].StatusCode, Is.EqualTo(expected.Sample.StatusCode), expected.Key);
                Assert.That(values[0].SourceTimestamp, Is.EqualTo(expected.Sample.SourceTimestamp), expected.Key);
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
                        TypeDefinitionId = Opc.Ua.ObjectTypeIds.ConditionType,
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
                    TypeDefinitionId = Opc.Ua.ObjectTypeIds.AlarmConditionType,
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

        /// <summary>
        /// Maps a server-side NodeId by URI into the connected client's namespace table.
        /// </summary>
        private static NodeId MapNodeId(PadimDevice catalog, NamespaceTable clientNamespaces, NodeId nodeId)
        {
            string uri = catalog.Context.NamespaceUris.GetString(nodeId.NamespaceIndex) ??
                throw new InvalidOperationException($"Server namespace index {nodeId.NamespaceIndex} is absent.");
            int index = clientNamespaces.GetIndex(uri);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"Client namespace URI {uri} is absent.");
            return nodeId.WithNamespaceIndex((ushort)index);
        }

        /// <summary>
        /// Creates a PA-DIM model-qualified BrowseName using the device's runtime namespace table.
        /// </summary>
        private static QualifiedName ModelName(PadimDevice device, string name)
        {
            int index = device.Context.NamespaceUris.GetIndex(Namespaces.PADIM);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            return new QualifiedName(name, (ushort)index);
        }

        /// <summary>
        /// Supplies every mandatory PA-DIM and DI identity value used by the hosted facet fixture.
        /// </summary>
        private static void SetIdentity(PadimDevice device)
        {
            device.State.Manufacturer!.Value = LocalizedText.From("PA-DIM hosted fixture manufacturer");
            device.State.ManufacturerUri!.Value = "urn:fixture:manufacturer";
            device.State.Model!.Value = LocalizedText.From("PA-DIM hosted fixture");
            device.State.SerialNumber!.Value = device.State.NodeId.ToString();
            device.State.SoftwareRevision!.Value = "1.0";
            device.State.HardwareRevision!.Value = "1.0";
            device.State.ProductCode!.Value = "fixture";
            device.State.ProductInstanceUri!.Value = $"urn:fixture:{device.State.NodeId}";
            device.State.AddAssetId(device.Context).AssetId!.Value = $"asset:{device.State.NodeId}";
            device.State.AssetId!.StatusCode = StatusCodes.Good;
            device.State.RevisionCounter!.Value = 0;
        }

        /// <summary>
        /// Returns the generated primary variable BrowseName for a signal family.
        /// </summary>
        private static string PrimaryVariableName(PadimSignalKind kind)
        {
            return kind switch
            {
                PadimSignalKind.Control or PadimSignalKind.TwoStateControl or PadimSignalKind.MultiStateControl =>
                    "ControlSignal",
                PadimSignalKind.Discrete => "DiscreteSignal",
                PadimSignalKind.TwoStateDiscrete => "TwoStateDiscreteSignal",
                PadimSignalKind.MultiStateDiscrete => "MultiStateDiscreteSignal",
                _ => "AnalogSignal"
            };
        }

        /// <summary>
        /// Returns the published type BrowseName expected for each signal family.
        /// </summary>
        private static string PublishedSignalTypeName(PadimSignalKind kind)
        {
            return kind switch
            {
                PadimSignalKind.TwoStateControl => "TwoStateDiscreteControlSignalType",
                PadimSignalKind.MultiStateControl => "MultiStateDiscreteControlSignalType",
                _ => $"{kind}SignalType"
            };
        }

        /// <summary>
        /// Returns the exact PADIM object type BrowseName for a device family.
        /// </summary>
        private static string PublishedDeviceTypeName(PadimDeviceKind kind)
        {
            return kind == PadimDeviceKind.Device ? "PADIMType" : $"{kind}Type";
        }

        /// <summary>
        /// Returns the exact generated variable type BrowseName for a measurement family.
        /// </summary>
        private static string MeasurementVariableTypeName(PadimMeasurementKind kind)
        {
            return kind switch
            {
                PadimMeasurementKind.Analog => "AnalogSignalVariableType",
                PadimMeasurementKind.Temperature => "TemperatureMeasurementVariableType",
                PadimMeasurementKind.Pressure => "PressureMeasurementVariableType",
                PadimMeasurementKind.Flow => "FlowMeasurementVariableType",
                PadimMeasurementKind.Level => "LevelMeasurementVariableType",
                PadimMeasurementKind.Density => "ActualDensityVariableType",
                PadimMeasurementKind.Totalizer => "TotalizerVariableType",
                PadimMeasurementKind.MassFlow => "MassFlowRateVariableType",
                PadimMeasurementKind.ActualVolumeFlow => "ActualVolumeFlowRateVariableType",
                PadimMeasurementKind.NormalizedVolumeFlow => "NormalizedVolumeFlowRateVariableType",
                PadimMeasurementKind.Analytical => "AnalyticalMeasurementVariableType",
                PadimMeasurementKind.Pat => "PatMeasurementVariableType",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        /// <summary>
        /// Returns the mandatory generated values for each control signal specialization.
        /// </summary>
        private static (string Name, NodeId DataType)[] RequiredSignalChildren(PadimSignalKind kind)
        {
            return kind switch
            {
                PadimSignalKind.Control =>
                [
                    ("Setpoint", Opc.Ua.DataTypeIds.Float),
                    ("OperatingDirection", Opc.Ua.DataTypeIds.UInt32),
                    ("ActuatorType", Opc.Ua.DataTypeIds.UInt32)
                ],
                PadimSignalKind.TwoStateControl =>
                [
                    ("Setpoint", Opc.Ua.DataTypeIds.Boolean),
                    ("OperatingDirection", Opc.Ua.DataTypeIds.UInt32)
                ],
                PadimSignalKind.MultiStateControl =>
                [
                    ("Setpoint", Opc.Ua.DataTypeIds.UInt32)
                ],
                _ => []
            };
        }

        /// <summary>
        /// Returns the mandatory generated values for specialized measurement variables.
        /// </summary>
        private static (string Name, NodeId DataType)[] RequiredMeasurementChildren(PadimMeasurementKind kind)
        {
            return kind switch
            {
                PadimMeasurementKind.Temperature =>
                [
                    ("SensorType", Opc.Ua.DataTypeIds.UInt32)
                ],
                PadimMeasurementKind.Flow or PadimMeasurementKind.MassFlow or
                PadimMeasurementKind.ActualVolumeFlow or PadimMeasurementKind.NormalizedVolumeFlow =>
                [
                    ("LowFlowCutOff", Opc.Ua.DataTypeIds.Float)
                ],
                PadimMeasurementKind.Totalizer =>
                [
                    ("PulseValue", Opc.Ua.DataTypeIds.Number),
                    ("PulseWidth", Opc.Ua.DataTypeIds.Float)
                ],
                _ => []
            };
        }

        /// <summary>
        /// Acquires a mandatory generated child without converting its declared raw value type.
        /// </summary>
        private static void BindRequiredChild(
            PadimDevice device,
            BaseVariableState parent,
            string browseName,
            NodeId expectedDataType,
            PadimPublishedSampleSet samples)
        {
            BaseVariableState child = parent.FindChild(device.Context, ModelName(device, browseName)) as
                BaseVariableState ??
                throw new InvalidOperationException(
                    $"Mandatory primary child '{browseName}' is absent from {parent.BrowseName}.");
            Assert.That(child.DataType, Is.EqualTo(expectedDataType),
                $"{parent.BrowseName}/{browseName} retains its published DataType.");
            samples.Bind(device, child);
        }

        /// <summary>
        /// Materializes and binds an optional UInt32 measurement member declared by the PA-DIM NodeSet.
        /// </summary>
        private static async ValueTask BindOptionalMeasurementChildAsync(
            PadimDevice device,
            BaseVariableState parent,
            string browseName,
            PadimPublishedSampleSet samples,
            CancellationToken cancellationToken)
        {
            PadimValueBinding binding = await device.AddOptionalValueAsync(
                parent,
                ModelName(device, browseName),
                samples.NextKey(),
                samples.Provider,
                cancellationToken);
            BaseVariableState child = binding.Variable;
            Assert.Multiple(() =>
            {
                Assert.That(child.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UInt32), browseName);
                Assert.That(child.ValueRank, Is.EqualTo(ValueRanks.Any), browseName);
                Assert.That(child.TypeDefinitionId,
                    Is.EqualTo(Opc.Ua.VariableTypeIds.MultiStateDictionaryEntryDiscreteType), browseName);
            });
            samples.Track(device, binding);
        }
    }
}
