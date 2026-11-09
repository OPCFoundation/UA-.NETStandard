/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using Opc.Ua.Client;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Deterministic OPC 10000-21 model and client tests in the AOT test project.
    /// Source inclusion alone does not verify trimming or NativeAOT publication.
    /// </summary>
    public sealed class OnboardingModelAotTests
    {
        private const string kOnboardingNamespaceUri = "http://opcfoundation.org/UA/Onboarding/";

        [Test]
        public async Task GeneratedTicketTypesResolveAndEncodeLiteralOnboardingWireContractsAsync()
        {
            using var harness = new OnboardingHarness();
            IServiceMessageContext messageContext = harness.Session.MessageContext;
            int onboardingNamespaceIndex =
                messageContext.NamespaceUris.GetIndex(kOnboardingNamespaceUri);
            await Assert.That(onboardingNamespaceIndex).IsGreaterThan(0);
            await Assert.That(
                messageContext.NamespaceUris.GetString((uint)onboardingNamespaceIndex))
                .IsEqualTo(kOnboardingNamespaceUri);

            var deviceEncodingId = new ExpandedNodeId(1441u, kOnboardingNamespaceUri);
            await Assert.That(
                harness.Session.Factory.TryGetEncodeableType(
                    deviceEncodingId, out IEncodeableType? deviceEncodeableType))
                .IsTrue();
            await Assert.That(deviceEncodeableType!.Type)
                .IsEqualTo(typeof(DeviceIdentityTicketType));

            var compositeEncodingId = new ExpandedNodeId(1442u, kOnboardingNamespaceUri);
            await Assert.That(
                harness.Session.Factory.TryGetEncodeableType(
                    compositeEncodingId, out IEncodeableType? compositeEncodeableType))
                .IsTrue();
            await Assert.That(compositeEncodeableType!.Type)
                .IsEqualTo(typeof(CompositeIdentityTicketType));

            var deviceTicket = new DeviceIdentityTicketType
            {
                ManufacturerName = "Acme",
                ModelName = "Pressure Sensor",
                ModelVersion = "3.2",
                HardwareRevision = "board-C",
                SoftwareRevision = "firmware-8",
                SerialNumber = "PS-0042",
                ManufactureDate = new DateTime(2024, 3, 5, 6, 7, 8, DateTimeKind.Utc),
                Authorities = [],
                ProductInstanceUri = "urn:acme:pressure-sensor:PS-0042"
            };
            ExtensionObject decodedDeviceExtension =
                EncodeAndDecode(messageContext, deviceTicket);

            await Assert.That(decodedDeviceExtension.TypeId).IsEqualTo(deviceEncodingId);
            await Assert.That(
                decodedDeviceExtension.TryGetValue<DeviceIdentityTicketType>(
                    out DeviceIdentityTicketType? decodedDevice))
                .IsTrue();
            await Assert.That(decodedDevice!.ManufacturerName).IsEqualTo("Acme");
            await Assert.That(decodedDevice.ModelName).IsEqualTo("Pressure Sensor");
            await Assert.That(decodedDevice.ModelVersion).IsEqualTo("3.2");
            await Assert.That(decodedDevice.HardwareRevision).IsEqualTo("board-C");
            await Assert.That(decodedDevice.SoftwareRevision).IsEqualTo("firmware-8");
            await Assert.That(decodedDevice.SerialNumber).IsEqualTo("PS-0042");
            await Assert.That(decodedDevice.ManufactureDate)
                .IsEqualTo(new DateTime(2024, 3, 5, 6, 7, 8, DateTimeKind.Utc));
            await Assert.That(decodedDevice.Authorities.Count).IsEqualTo(0);
            await Assert.That(decodedDevice.ProductInstanceUri)
                .IsEqualTo("urn:acme:pressure-sensor:PS-0042");

            var compositeTicket = new CompositeIdentityTicketType
            {
                ManufacturerName = "Acme",
                ModelName = "Assembly Line",
                ModelVersion = "5.1",
                HardwareRevision = "line-D",
                SoftwareRevision = "controller-12",
                SerialNumber = "LINE-0007",
                ManufactureDate = new DateTime(2023, 11, 12, 13, 14, 15, DateTimeKind.Utc),
                Authorities = [],
                CompositeInstanceUri = "urn:acme:line:LINE-0007",
                Devices =
                [
                    "urn:acme:pressure-sensor:PS-0042",
                    "urn:acme:valve:V-0018"
                ],
                Composites = []
            };
            ExtensionObject decodedCompositeExtension =
                EncodeAndDecode(messageContext, compositeTicket);

            await Assert.That(decodedCompositeExtension.TypeId).IsEqualTo(compositeEncodingId);
            await Assert.That(
                decodedCompositeExtension.TryGetValue<CompositeIdentityTicketType>(
                    out CompositeIdentityTicketType? decodedComposite))
                .IsTrue();
            await Assert.That(decodedComposite!.ManufacturerName).IsEqualTo("Acme");
            await Assert.That(decodedComposite.ModelName).IsEqualTo("Assembly Line");
            await Assert.That(decodedComposite.ModelVersion).IsEqualTo("5.1");
            await Assert.That(decodedComposite.HardwareRevision).IsEqualTo("line-D");
            await Assert.That(decodedComposite.SoftwareRevision).IsEqualTo("controller-12");
            await Assert.That(decodedComposite.SerialNumber).IsEqualTo("LINE-0007");
            await Assert.That(decodedComposite.ManufactureDate)
                .IsEqualTo(new DateTime(2023, 11, 12, 13, 14, 15, DateTimeKind.Utc));
            await Assert.That(decodedComposite.Authorities.Count).IsEqualTo(0);
            await Assert.That(decodedComposite.CompositeInstanceUri)
                .IsEqualTo("urn:acme:line:LINE-0007");
            await Assert.That(decodedComposite.Devices.Count).IsEqualTo(2);
            await Assert.That(decodedComposite.Devices[0])
                .IsEqualTo("urn:acme:pressure-sensor:PS-0042");
            await Assert.That(decodedComposite.Devices[1])
                .IsEqualTo("urn:acme:valve:V-0018");
            await Assert.That(decodedComposite.Composites.Count).IsEqualTo(0);
        }

        [Test]
        public async Task CompositeTicketEncodingUsesDeclaredFieldOrderAndDistinctArraysAsync()
        {
            using var harness = new OnboardingHarness();
            var ticket = new CompositeIdentityTicketType
            {
                ManufacturerName = "Manufacturer",
                ModelName = "Model",
                ModelVersion = "Version",
                HardwareRevision = "Hardware",
                SoftwareRevision = "Software",
                SerialNumber = "Serial",
                ManufactureDate = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                Authorities = [],
                CompositeInstanceUri = "urn:assembly:root",
                Devices = ["urn:device:first", "urn:device:second"],
                Composites = ["urn:assembly:child"]
            };
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, harness.Session.MessageContext, true))
            {
                ticket.Encode(encoder);
            }

            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, harness.Session.MessageContext, true);
            // Observe the wire using primitive readers, not the generated decoder:
            // symmetric field swaps in generated Encode/Decode must not pass.
            await Assert.That(decoder.ReadString(null)).IsEqualTo("Manufacturer");
            await Assert.That(decoder.ReadString(null)).IsEqualTo("Model");
            await Assert.That(decoder.ReadString(null)).IsEqualTo("Version");
            await Assert.That(decoder.ReadString(null)).IsEqualTo("Hardware");
            await Assert.That(decoder.ReadString(null)).IsEqualTo("Software");
            await Assert.That(decoder.ReadString(null)).IsEqualTo("Serial");
            await Assert.That(decoder.ReadDateTime(null))
                .IsEqualTo(new DateTimeUtc(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
            await Assert.That(decoder.ReadInt32(null)).IsEqualTo(0);
            await Assert.That(decoder.ReadString(null)).IsEqualTo("urn:assembly:root");
            ArrayOf<string?> devices = decoder.ReadStringArray(null);
            ArrayOf<string?> composites = decoder.ReadStringArray(null);
            await Assert.That(devices.Count).IsEqualTo(2);
            await Assert.That(devices[0]).IsEqualTo("urn:device:first");
            await Assert.That(devices[1]).IsEqualTo("urn:device:second");
            await Assert.That(composites.Count).IsEqualTo(1);
            await Assert.That(composites[0]).IsEqualTo("urn:assembly:child");
            await Assert.That(stream.Position).IsEqualTo(stream.Length);
        }

        [Test]
        public async Task GeneratedOnboardingProxiesConstructWithoutDispatchAsync()
        {
            using var harness = new OnboardingHarness();
            var registrarNodeId = new NodeId("registrar-17", 2);
            var deviceNodeId = new NodeId("device-42", 3);

            var registrarClient = new OnboardingClient(
                harness.Session, registrarNodeId, harness.Telemetry);
            var deviceClient = new ProvisionableDeviceTypeClient(
                harness.Session, deviceNodeId, harness.Telemetry);

            await Assert.That(registrarClient.Session).IsSameReferenceAs(harness.Session);
            await Assert.That(registrarClient.RegistrarNodeId).IsEqualTo(registrarNodeId);
            await Assert.That(registrarClient.ObjectId).IsEqualTo(registrarNodeId);
            await Assert.That(deviceClient.Session).IsSameReferenceAs(harness.Session);
            await Assert.That(deviceClient.ObjectId).IsEqualTo(deviceNodeId);
            await Assert.That(harness.Channel.ServiceRequestCount).IsEqualTo(0);
        }

        private static ExtensionObject EncodeAndDecode(
            IServiceMessageContext messageContext,
            IEncodeable ticket)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, messageContext, true))
            {
                encoder.WriteExtensionObject("Ticket", new ExtensionObject(ticket));
            }

            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, messageContext, true);
            return decoder.ReadExtensionObject("Ticket");
        }

        /// <summary>
        /// Creates a Session whose local transport rejects every service dispatch.
        /// </summary>
        private sealed class OnboardingHarness : IDisposable
        {
            public OnboardingHarness()
            {
                Telemetry = DefaultTelemetry.Create(static _ => { });
                Channel = new FailClosedTransportChannel(Telemetry);
                Session = new Session(
                    Channel,
                    new ApplicationConfiguration(Telemetry)
                    {
                        ClientConfiguration = new ClientConfiguration()
                    },
                    new ConfiguredEndpoint(
                        null,
                        Channel.EndpointDescription,
                        Channel.EndpointConfiguration));

                Session.NamespaceUris.GetIndexOrAppend(kOnboardingNamespaceUri);
                Session.MessageContext.NamespaceUris.GetIndexOrAppend(kOnboardingNamespaceUri);
                Session.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
                Session.MessageContext.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            }

            public ITelemetryContext Telemetry { get; }

            public FailClosedTransportChannel Channel { get; }

            public Session Session { get; }

            public void Dispose()
            {
                Session.Dispose();
            }
        }

        /// <summary>
        /// Implements only the local transport surface needed to construct a Session.
        /// Any unexpected OPC UA service request is recorded and rejected immediately.
        /// </summary>
        private sealed class FailClosedTransportChannel : ITransportChannel
        {
            public FailClosedTransportChannel(ITelemetryContext telemetry)
            {
                MessageContext = ServiceMessageContext.Create(telemetry);
            }

            public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.None;

            public EndpointDescription EndpointDescription { get; } = new()
            {
                EndpointUrl = "opc.tcp://onboarding-aot.invalid:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };

            public EndpointConfiguration EndpointConfiguration { get; } = new();

            public byte[] ChannelThumbprint => [];

            public byte[] ClientChannelCertificate => [];

            public byte[] ServerChannelCertificate => [];

            public IServiceMessageContext MessageContext { get; }

            public int OperationTimeout { get; set; } = 10000;

            public int ServiceRequestCount { get; private set; }

            public ValueTask ReconnectAsync(
                ITransportWaitingConnection? connection = null,
                CancellationToken ct = default)
            {
                throw new InvalidOperationException(
                    "Onboarding AOT tests do not establish transport connections.");
            }

            public ValueTask<IServiceResponse> SendRequestAsync(
                IServiceRequest request,
                CancellationToken ct = default)
            {
                ServiceRequestCount++;
                throw new InvalidOperationException(
                    $"Unexpected service dispatch in onboarding AOT test: {request.GetType().Name}.");
            }

            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                return default;
            }

            public void Dispose()
            {
            }
        }
    }
}
