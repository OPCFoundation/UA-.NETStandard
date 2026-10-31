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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.LADS.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.LADS.Tests.Client
{
    /// <summary>
    /// Covers the LADS client against a mocked session: construction, hosting,
    /// discovery, state reads and method calls through the generated proxies.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [Category("Client")]
    public sealed class LadsClientSessionTests
    {
        [Test]
        public void ConstructorRejectsMissingArguments()
        {
            Mock<ISession> session = CreateSession();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => _ = new LadsClient(null!, telemetry));
                Assert.Throws<ArgumentNullException>(() => _ = new LadsClient(session.Object, null!));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LadsClient(
                    session.Object, telemetry, new LadsClientOptions { QueueSize = 0 }));
                Assert.Throws<ArgumentNullException>(() => SessionLadsExtensions.GetLadsClient(null!, telemetry));
            });
        }

        [Test]
        public void SessionExtensionCreatesAClientWithOptions()
        {
            Mock<ISession> session = CreateSession();
            var options = new LadsClientOptions { MaximumFunctionDepth = 2 };

            LadsClient client = session.Object.GetLadsClient(NUnitTelemetryContext.Create(), options);
            LadsClient defaults = session.Object.GetLadsClient(NUnitTelemetryContext.Create());

            Assert.Multiple(() =>
            {
                Assert.That(client.Session, Is.SameAs(session.Object));
                Assert.That(client.Options, Is.SameAs(options));
                Assert.That(client.IsSupported, Is.True);
                Assert.That(defaults.Options, Is.SameAs(LadsClientOptions.Default));
                Assert.That(
                    session.Object.MessageContext.Factory.ContainsEncodeableType(DataTypeIds.KeyValueType),
                    Is.True);
            });
        }

        [Test]
        public void ServerWithoutLadsIsNotSupported()
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(s => s.MessageContext).Returns(context);

            var client = new LadsClient(session.Object, NUnitTelemetryContext.Create());

            Assert.Multiple(() =>
            {
                Assert.That(client.IsSupported, Is.False);
                ServiceResultException? exception = Assert.Throws<ServiceResultException>(
                    () => client.NamespaceIndexOf(Namespaces.LADS));
                Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            });
        }

        [Test]
        public void AddLadsClientRegistersAndResolvesTheFactory()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "LadsClientHosting";
                options.ApplicationUri = "urn:localhost:OPCFoundation:LadsClientHosting";
            });

            Assert.That(builder.AddLadsClient(), Is.SameAs(builder));

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<LadsClientFactory>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<LadsClient>>>(), Is.Not.Null);
                Assert.That(provider.GetService<LadsClientOptions>(), Is.Null);
                Assert.Throws<ArgumentNullException>(() => OpcUaLadsClientBuilderExtensions.AddLadsClient(null!));
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaLadsClientBuilderExtensions.AddLadsClient(null!, new LadsClientOptions()));
                Assert.Throws<ArgumentNullException>(() => builder.AddLadsClient((LadsClientOptions)null!));
                Assert.Throws<ArgumentNullException>(
                    () => builder.AddLadsClient((Func<LadsClientOptions, LadsClientOptions>)null!));
            });
        }

        [Test]
        public void AddLadsClientRegistersConfiguredOptions()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "LadsClientOptions";
                options.ApplicationUri = "urn:localhost:OPCFoundation:LadsClientOptions";
            });

            builder.AddLadsClient(options => options with { MaximumFunctionDepth = 3 });

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<LadsClientOptions>().MaximumFunctionDepth, Is.EqualTo(3));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => builder.AddLadsClient(new LadsClientOptions { FileChunkSize = 0 }));
            });
        }

        [Test]
        public async Task FactoryOpensASessionPerClientAsync()
        {
            int opened = 0;
            var options = new LadsClientOptions { MaximumBrowsePages = 2 };
            var factory = new LadsClientFactory(
                _ =>
                {
                    opened++;
                    return Task.FromResult<ManagedSession>(null!);
                },
                NUnitTelemetryContext.Create(),
                options);

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => _ = new LadsClientFactory(null!, NUnitTelemetryContext.Create()));
                Assert.Throws<ArgumentNullException>(
                    () => _ = new LadsClientFactory(_ => Task.FromResult<ManagedSession>(null!), null!));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LadsClientFactory(
                    _ => Task.FromResult<ManagedSession>(null!),
                    NUnitTelemetryContext.Create(),
                    new LadsClientOptions { MaximumBrowsePages = 0 }));
            });
            await Assert.ThatAsync(() => factory.CreateAsync(),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(opened, Is.EqualTo(1));
        }

        [Test]
        public async Task DevicesAreDiscoveredOnceAcrossDeviceSetAndMachinesAsync()
        {
            Mock<ISession> session = CreateSession();
            var nodeCache = new Mock<INodeCache>(MockBehavior.Strict);
            session.SetupGet(s => s.NodeCache).Returns(nodeCache.Object);
            ExpandedNodeId vendorType = new NodeId("VendorDeviceType", VendorIndex);
            ExpandedNodeId otherType = new NodeId("OtherType", VendorIndex);
            nodeCache.Setup(c => c.IsTypeOfAsync(
                    new NodeId("VendorDeviceType", VendorIndex), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));
            nodeCache.Setup(c => c.IsTypeOfAsync(
                    new NodeId("OtherType", VendorIndex), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(false));
            NodeId deviceSet = ToNodeId(session, Opc.Ua.Di.ObjectIds.DeviceSet);
            NodeId machines = ToNodeId(session, Opc.Ua.Machinery.ObjectIds.Machines);
            SetupBrowse(session, request =>
            {
                if (request.NodeId == deviceSet)
                {
                    return Browsed(
                        Device("Shared", "Shared device", ObjectTypeIds.LADSDeviceType),
                        Device("Other", "Not LADS", otherType),
                        Device("Vendor", "Vendor device", vendorType));
                }
                if (request.NodeId == machines)
                {
                    return Browsed(
                        Device("Shared", "Shared device", ObjectTypeIds.LADSDeviceType),
                        Device("MachineOnly", "Machine device", ObjectTypeIds.LADSDeviceType));
                }
                return new BrowseResult { StatusCode = StatusCodes.BadNodeIdUnknown };
            });
            LadsClient client = CreateClient(session);

            ArrayOf<LadsDevice> devices = await client.GetDevicesAsync().ConfigureAwait(false);
            LadsDevice? byDisplayName = await client.FindDeviceAsync("Machine device").ConfigureAwait(false);
            LadsDevice first = await client.GetFirstDeviceAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(devices.Count, Is.EqualTo(3));
                Assert.That(devices[0].Name, Is.EqualTo("Shared"));
                Assert.That(devices[0].DisplayName, Is.EqualTo("Shared device"));
                Assert.That(devices[1].Name, Is.EqualTo("Vendor"));
                Assert.That(devices[2].Name, Is.EqualTo("MachineOnly"));
                Assert.That(devices[0].Client, Is.SameAs(client));
                Assert.That(byDisplayName?.Name, Is.EqualTo("MachineOnly"));
                Assert.That(first.Name, Is.EqualTo("Shared"));
            });
            await Assert.ThatAsync(
                async () => await client.GetDeviceAsync("Missing").ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotFound));
            await Assert.ThatAsync(
                async () => await client.FindDeviceAsync(string.Empty).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task AServerWithoutDevicesHasNoFirstDeviceAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, _ => new BrowseResult { StatusCode = StatusCodes.BadNodeIdUnknown });
            LadsClient client = CreateClient(session);

            await Assert.ThatAsync(
                async () => await client.GetFirstDeviceAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task BrowseLimitsAreEnforcedAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, _ => Browsed(
                Device("A", "A", ObjectTypeIds.LADSDeviceType),
                Device("B", "B", ObjectTypeIds.LADSDeviceType)));
            LadsClient client = CreateClient(session, new LadsClientOptions { MaximumBrowseReferences = 1 });

            await Assert.ThatAsync(
                async () => await client.GetDevicesAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations));
        }

        [Test]
        public async Task FunctionalUnitsAndFunctionsAreBrowsedAndClassifiedAsync()
        {
            Mock<ISession> session = CreateSession();
            var nodeCache = new Mock<INodeCache>(MockBehavior.Strict);
            session.SetupGet(s => s.NodeCache).Returns(nodeCache.Object);
            nodeCache.Setup(c => c.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));
            var device = new NodeId("Device", VendorIndex);
            var unitSet = new NodeId("Device.FunctionalUnitSet", VendorIndex);
            var unit = new NodeId("Unit", VendorIndex);
            var functionSet = new NodeId("Unit.FunctionSet", VendorIndex);
            var temperature = new NodeId("Temperature", VendorIndex);
            var nestedSet = new NodeId("Temperature.FunctionSet", VendorIndex);
            var sensor = new NodeId("Sensor", VendorIndex);
            SetupTranslate(session, paths =>
            {
                var results = new BrowsePathResult[paths.Count];
                for (int ii = 0; ii < results.Length; ii++)
                {
                    NodeId start = paths[ii].StartingNode;
                    ArrayOf<RelativePathElement> elements = paths[ii].RelativePath.Elements;
                    string last = elements[elements.Count - 1].TargetName.Name!;
                    results[ii] = last switch
                    {
                        BrowseNames.FunctionalUnitSet when start == device => Resolved(unitSet),
                        BrowseNames.FunctionSet when start == unit => Resolved(functionSet),
                        BrowseNames.FunctionSet when start == temperature => Resolved(nestedSet),
                        _ => new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                    };
                }
                return results;
            });
            SetupBrowse(session, request =>
            {
                if (request.NodeId == unitSet)
                {
                    return Browsed(Reference(unit, "Unit", "Unit 1", ObjectTypeIds.FunctionalUnitType));
                }
                if (request.NodeId == functionSet)
                {
                    return Browsed(Reference(
                        temperature, "Temperature", "Temperature", ObjectTypeIds.AnalogControlFunctionType));
                }
                if (request.NodeId == nestedSet)
                {
                    return Browsed(Reference(
                        sensor, "Sensor", "Sensor", ObjectTypeIds.AnalogScalarSensorFunctionType));
                }
                return new BrowseResult { StatusCode = StatusCodes.BadNodeIdUnknown };
            });
            LadsClient client = CreateClient(session);

            LadsFunctionalUnit functionalUnit = await client.Device(device)
                .GetFunctionalUnitAsync("Unit 1").ConfigureAwait(false);
            ArrayOf<LadsFunction> all = await functionalUnit.GetFunctionsAsync().ConfigureAwait(false);
            ArrayOf<LadsFunction> top = await functionalUnit.GetFunctionsAsync(false).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(functionalUnit.NodeId, Is.EqualTo(unit));
                Assert.That(all.Count, Is.EqualTo(2));
                Assert.That(all[0].Kind, Is.EqualTo(LadsFunctionKind.AnalogControl));
                Assert.That(all[0].IsControl, Is.True);
                Assert.That(all[0].Path, Is.EqualTo("Temperature"));
                Assert.That(all[1].Kind, Is.EqualTo(LadsFunctionKind.AnalogScalarSensor));
                Assert.That(all[1].IsSensor, Is.True);
                Assert.That(all[1].Path, Is.EqualTo("Temperature/Sensor"));
                Assert.That(top.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task StopCallsTheFunctionalUnitStateMachineAsync()
        {
            Mock<ISession> session = CreateSession();
            var unit = new NodeId("Unit", VendorIndex);
            var state = new NodeId("Unit.FunctionalUnitState", VendorIndex);
            SetupTranslate(session, paths => [Resolved(state)]);
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls, Variant.Null);
            LadsClient client = CreateClient(session);

            await client.FunctionalUnit(unit).StopAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Has.Count.EqualTo(1));
                Assert.That(calls[0].ObjectId, Is.EqualTo(state));
                Assert.That(
                    calls[0].MethodId,
                    Is.EqualTo(ToNodeId(session, MethodIds.FunctionalStateMachineType_Stop)));
            });
        }

        [Test]
        public async Task StartProgramPassesTheRequestAndReturnsTheRunIdAsync()
        {
            Mock<ISession> session = CreateSession();
            var unit = new NodeId("Unit", VendorIndex);
            var state = new NodeId("Unit.FunctionalUnitState", VendorIndex);
            SetupTranslate(session, paths => [Resolved(state)]);
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls, Variant.From("run-1"));
            LadsClient client = CreateClient(session);

            string runId = await client.FunctionalUnit(unit).StartProgramAsync(
                LadsProgramRequest.For("Measure").WithProperty("Volume", "10").WithSample("Plate", "S1").WithJob("J"))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(runId, Is.EqualTo("run-1"));
                Assert.That(calls, Has.Count.EqualTo(1));
                Assert.That(calls[0].ObjectId, Is.EqualTo(state));
                Assert.That(calls[0].InputArguments.Count, Is.EqualTo(5));
                Assert.That(calls[0].InputArguments[0].TryGetValue(out string? template), Is.True);
                Assert.That(template, Is.EqualTo("Measure"));
                Assert.That(calls[0].InputArguments[2].TryGetValue(out string? job), Is.True);
                Assert.That(job, Is.EqualTo("J"));
            });
            await Assert.ThatAsync(
                async () => await client.StartProgramAsync(unit, null!).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task MethodsOnNodesWithoutTheStateMachineAreNotSupportedAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, paths => [new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }]);
            LadsClient client = CreateClient(session);
            var function = new NodeId("Function", VendorIndex);

            await Assert.ThatAsync(
                async () => await client.Function(function, LadsFunctionKind.Cover).OpenAsync()
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotSupported));
            await Assert.ThatAsync(
                async () => await client.StartFunctionAsync(function).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotSupported));
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => client.Function(NodeId.Null));
                Assert.Throws<ArgumentException>(() => client.Device(NodeId.Null));
                Assert.Throws<ArgumentException>(() => client.FunctionalUnit(NodeId.Null));
            });
        }

        [Test]
        public void ObservingWithoutAManagedSessionRequiresAStreamingSubscription()
        {
            Mock<ISession> session = CreateSession();
            LadsClient client = CreateClient(session);
            var streaming = new Mock<IStreamingSubscription>().Object;

            Assert.Multiple(() =>
            {
                Assert.That(client.StreamingOf(streaming), Is.SameAs(streaming));
                ServiceResultException? exception = Assert.Throws<ServiceResultException>(
                    () => client.StreamingOf(null));
                Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            });
        }

        private const ushort VendorIndex = 5;

        private static Mock<ISession> CreateSession()
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.AMB.Namespaces.AMB);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Namespaces.Machinery);
            context.NamespaceUris.GetIndexOrAppend(Namespaces.LADS);
            context.NamespaceUris.GetIndexOrAppend("urn:vendor");
            Assert.That(context.NamespaceUris.GetIndex("urn:vendor"), Is.EqualTo(VendorIndex));
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(s => s.MessageContext).Returns(context);
            return session;
        }

        private static LadsClient CreateClient(Mock<ISession> session, LadsClientOptions? options = null)
        {
            return new LadsClient(session.Object, NUnitTelemetryContext.Create(), options);
        }

        private static NodeId ToNodeId(Mock<ISession> session, ExpandedNodeId nodeId)
        {
            return ExpandedNodeId.ToNodeId(nodeId, session.Object.NamespaceUris);
        }

        private static ReferenceDescription Device(string name, string displayName, ExpandedNodeId typeDefinition)
        {
            return Reference(new NodeId(name, VendorIndex), name, displayName, typeDefinition);
        }

        private static ReferenceDescription Reference(
            NodeId nodeId,
            string name,
            string displayName,
            ExpandedNodeId typeDefinition)
        {
            return new ReferenceDescription
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName(name, VendorIndex),
                DisplayName = new LocalizedText(displayName),
                NodeClass = NodeClass.Object,
                TypeDefinition = typeDefinition
            };
        }

        private static BrowseResult Browsed(params ReferenceDescription[] references)
        {
            return new BrowseResult { References = references.ToArrayOf() };
        }

        private static BrowsePathResult Resolved(NodeId target)
        {
            return new BrowsePathResult
            {
                Targets = [new BrowsePathTarget { TargetId = target, RemainingPathIndex = uint.MaxValue }]
            };
        }

        private static void SetupTranslate(
            Mock<ISession> session,
            Func<ArrayOf<BrowsePath>, BrowsePathResult[]> response)
        {
            session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                    new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = response(paths).ToArrayOf()
                    }));
        }

        private static void SetupBrowse(Mock<ISession> session, Func<BrowseDescription, BrowseResult> response)
        {
            session.Setup(s => s.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> requests,
                    CancellationToken _) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    return new ValueTask<BrowseResponse>(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [response(requests[0])]
                    });
                });
        }

        private static void SetupCall(Mock<ISession> session, List<CallMethodRequest> calls, Variant output)
        {
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<CallMethodRequest> requests, CancellationToken _) =>
                {
                    foreach (CallMethodRequest request in requests)
                    {
                        calls.Add(request);
                    }
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments = output.IsNull ? [] : [output]
                            }
                        ]
                    });
                });
        }
    }
}
