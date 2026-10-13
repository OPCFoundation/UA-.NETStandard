/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Extrusion.Client;
using Opc.Ua.PlasticsRubber.Client;

namespace Opc.Ua.Extrusion.Tests
{
    /// <summary>
    /// Port-free discovery, generated composition, hosting and public event workflows.
    /// </summary>
    [TestFixture]
    public sealed partial class ExtrusionClientWorkflowTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task ExtrusionFacadePreservesConcretePartsAndClientSpecificGuardsAsync(bool supported)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(includeExtrusion: supported, token: cancellation.Token);
            var extruder = peer.Client.Extruder(ExtrusionClientPeer.DeviceId);
            var filter = peer.Client.Filter(ExtrusionClientPeer.FilterId);
            Assert.Multiple(() =>
            {
                Assert.That(extruder.GetType(), Is.EqualTo(typeof(Extruder.Extruder_InterfaceTypeClient)));
                Assert.That(filter.GetType(), Is.EqualTo(typeof(Filter.Filter_InterfaceTypeClient)));
                Assert.That(extruder.ObjectId, Is.EqualTo(ExtrusionClientPeer.DeviceId));
                Assert.That(filter.ObjectId, Is.EqualTo(ExtrusionClientPeer.FilterId));
                Assert.That(extruder.Session, Is.SameAs(peer.Session.Object));
                Assert.That(filter.Session, Is.SameAs(peer.Session.Object));
                Assert.That(extruder.Telemetry, Is.SameAs(peer.Telemetry));
                Assert.That(filter.Telemetry, Is.SameAs(peer.Telemetry));
                Assert.That(peer.Client.Session, Is.SameAs(peer.Session.Object));
                Assert.That(peer.Client.IsSupported, Is.EqualTo(supported));
            });
            Assert.That(Assert.Throws<ArgumentNullException>(() => peer.Client.Extruder(NodeId.Null))!.ParamName,
                Is.EqualTo("node"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => peer.Client.Filter(NodeId.Null))!.ParamName,
                Is.EqualTo("node"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new ExtrusionClient(null!,
                peer.Telemetry))!.ParamName,
                Is.EqualTo("session"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new ExtrusionClient(peer.Session.Object,
                null!))!.ParamName,
                Is.EqualTo("telemetry"));
            if (supported)
            {
                NodeId present = peer.NextNode();
                peer.ExpectBrowse(extruder.ObjectId, 0, ExtrusionClientPeer.Variable(present,
                    peer.General("IsPresent")));
                peer.ExpectRead(present, ExtrusionClientPeer.Sample(Variant.From(true)));
                ExtrusionClientPeer.AssertReading(await extruder.IsPresentVariable().ReadAsync(cancellation.Token)
                    .ConfigureAwait(false), true, true, StatusCodes.Good);
            }
            else
            {
                Assert.That(await ExtrusionClientPeer.CollectAsync(peer.Client.DiscoverAsync(cancellationToken:
                    cancellation.Token))
                    .ConfigureAwait(false), Is.Empty);
                ExtrusionClientPeer.AssertFailure(async () =>
                    await extruder.IsPresentVariable().ReadAsync(cancellation.Token).ConfigureAwait(false),
                        StatusCodes.BadNotFound);
                peer.VerifyNoServices();
            }
            NodeId pressure = peer.NextNode();
            peer.ExpectBrowse(filter.ObjectId, 0,
                ExtrusionClientPeer.Variable(pressure, peer.FilterName("BackflushPressure")));
            peer.ExpectRead(pressure, ExtrusionClientPeer.Sample(Variant.From(19.375),
                StatusCodes.UncertainInitialValue));
            ExtrusionClientPeer.AssertReading(await filter.BackflushPressureVariable().ReadAsync(cancellation.Token)
                .ConfigureAwait(false), 19.375, true, StatusCodes.UncertainInitialValue);
            peer.Verify();
        }

        [Test]
        public async Task ExtrusionDiscoveryTraversesAbstractAndUnknownNodesAndPreservesSubtypesAsync()
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId machines = peer.Type(Machinery.ObjectIds.Machines);
            NodeId devices = peer.Type(Di.ObjectIds.DeviceSet);
            NodeId abstractNode = peer.NextNode();
            NodeId unknown = peer.NextNode();
            NodeId vendor = peer.NextNode();
            NodeId unrelated = peer.NextNode();
            NodeId expected = peer.Type(ObjectTypeIds.ExtrusionDeviceType);
            NodeId extruderType = peer.Type(Extruder.ObjectTypeIds.Extruder_InterfaceType);
            NodeId filterType = peer.Type(Filter.ObjectTypeIds.Filter_InterfaceType);
            var vendorType = new NodeId("VendorExtruder", 2);
            var wrongType = new NodeId("OtherContainer", 2);
            ReferenceDescription extruder = ExtrusionClientPeer.Reference(
                ExtrusionClientPeer.DeviceId, extruderType, new QualifiedName("EX-27", 1));
            ReferenceDescription filter = ExtrusionClientPeer.Reference(
                ExtrusionClientPeer.FilterId, filterType, new QualifiedName("Filter-43", 1));
            ReferenceDescription abstractReference = ExtrusionClientPeer.Reference(
                abstractNode, expected, new QualifiedName("Abstract", 1));
            peer.ExpectBrowse(machines, (uint)NodeClass.Object, abstractReference,
                ExtrusionClientPeer.Reference(unknown, new ExpandedNodeId("Type", "urn:unmapped"),
                    new QualifiedName("Unknown", 1)),
                extruder, extruder,
                ExtrusionClientPeer.Reference(NodeId.Null, expected, new QualifiedName("Invalid", 1)),
                ExtrusionClientPeer.Reference(new ExpandedNodeId("Remote", "urn:unmapped"), expected,
                    new QualifiedName("Remote", 1)));
            peer.ExpectBrowse(devices, (uint)NodeClass.Object, extruder, abstractReference, filter,
                ExtrusionClientPeer.Reference(machines, expected, new QualifiedName("RootCycle", 1)));
            peer.ExpectBrowse(abstractNode, (uint)NodeClass.Object, filter, abstractReference,
                ExtrusionClientPeer.Reference(machines, expected, new QualifiedName("BackToRoot", 1)));
            peer.ExpectBrowse(unknown, (uint)NodeClass.Object,
                ExtrusionClientPeer.Reference(vendor, vendorType, new QualifiedName("Vendor-9", 2)),
                ExtrusionClientPeer.Reference(unrelated, wrongType, new QualifiedName("Other", 1)));
            peer.ExpectBrowse(ExtrusionClientPeer.DeviceId, (uint)NodeClass.Object);
            peer.ExpectBrowse(ExtrusionClientPeer.FilterId, (uint)NodeClass.Object);
            peer.ExpectBrowse(vendor, (uint)NodeClass.Object);
            peer.ExpectBrowse(unrelated, (uint)NodeClass.Object,
                ExtrusionClientPeer.Reference(vendor, vendorType, new QualifiedName("VendorAgain", 1)),
                ExtrusionClientPeer.Reference(unknown, NodeId.Null, new QualifiedName("UnknownAgain", 1)));
            peer.ExpectType(expected, expected, true);
            peer.ExpectType(extruderType, expected, true);
            peer.ExpectType(filterType, expected, true);
            peer.ExpectType(vendorType, expected, true);
            peer.ExpectType(wrongType, expected, false);

            List<ExtrusionDeviceEntry> found = await ExtrusionClientPeer.CollectAsync(
                peer.Client.DiscoverAsync(root: NodeId.Null, maximumNodes: 8, cancellationToken: cancellation.Token))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(found.Select(value => value.NodeId),
                    Is.EqualTo(new[] { ExtrusionClientPeer.DeviceId, ExtrusionClientPeer.FilterId, vendor }));
                Assert.That(found.Select(value => value.TypeDefinition), Is.EqualTo(new[]
                {
                    Extruder.ObjectTypeIds.Extruder_InterfaceType,
                    Filter.ObjectTypeIds.Filter_InterfaceType,
                    new ExpandedNodeId("VendorExtruder", "urn:extrusion-tests:vendor")
                }));
                Assert.That(found.Select(value => value.BrowseName), Is.EqualTo(new[]
                {
                    new QualifiedName("EX-27", 1), new QualifiedName("Filter-43", 1), new QualifiedName("Vendor-9", 2)
                }));
                Assert.That(found[0].DisplayName, Is.EqualTo(new LocalizedText("en", "Display EX-27")));
                Assert.That(found[1].DisplayName, Is.EqualTo(new LocalizedText("en", "Display Filter-43")));
                Assert.That(found[2].DisplayName, Is.EqualTo(new LocalizedText("en", "Display Vendor-9")));
            });
            peer.Verify();
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task ExtrusionDiscoveryEnforcesDequeuedBoundsThroughFullEnumerationAsync(int maximum)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId root = peer.NextNode();
            if (maximum < 1)
            {
                ArgumentOutOfRangeException? failure = Assert.Throws<ArgumentOutOfRangeException>(
                    () => peer.Client.DiscoverAsync(root, maximum, cancellation.Token));
                Assert.That(failure!.ParamName, Is.EqualTo("maximumNodes"));
                peer.VerifyNoServices();
                return;
            }
            NodeId actual = peer.Type(Extruder.ObjectTypeIds.Extruder_InterfaceType);
            NodeId expected = peer.Type(ObjectTypeIds.ExtrusionDeviceType);
            ReferenceDescription child = ExtrusionClientPeer.Reference(
                ExtrusionClientPeer.DeviceId, actual, new QualifiedName("Extruder", 1));
            peer.ExpectBrowse(root, (uint)NodeClass.Object, child, child,
                ExtrusionClientPeer.Reference(root, expected, new QualifiedName("Self", 1)));
            peer.ExpectType(actual, expected, true);
            if (maximum > 1)
            {
                peer.ExpectBrowse(ExtrusionClientPeer.DeviceId, (uint)NodeClass.Object);
            }
            var found = new List<ExtrusionDeviceEntry>();
            async Task EnumerateAsync()
            {
                await foreach (ExtrusionDeviceEntry value in peer.Client.DiscoverAsync(root, maximum,
                    cancellation.Token)
                    .ConfigureAwait(false))
                {
                    found.Add(value);
                }
            }
            if (maximum == 1)
            {
                ExtrusionClientPeer.AssertFailure(EnumerateAsync, StatusCodes.BadEncodingLimitsExceeded);
            }
            else
            {
                await EnumerateAsync().ConfigureAwait(false);
            }
            Assert.That(found.Select(value => value.NodeId), Is.EqualTo(new[] { ExtrusionClientPeer.DeviceId }),
                "The discovered child is yielded before its later dequeue can exceed the bound.");
            Assert.That(found[0].TypeDefinition, Is.EqualTo(Extruder.ObjectTypeIds.Extruder_InterfaceType));
            peer.Verify();
        }

        [TestCase("Denied")]
        [TestCase("PreCancelled")]
        [TestCase("CancelType")]
        [TestCase("MissingRoots")]
        [TestCase("Unsupported")]
        public async Task ExtrusionDiscoveryPropagatesBrowseFailuresAndCancellationAsync(string shape)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(includeExtrusion: shape != "Unsupported",
                includeRoots: shape != "MissingRoots", token: cancellation.Token);
            NodeId root = shape is "MissingRoots" or "Unsupported" ? NodeId.Null : peer.NextNode();
            if (shape == "Denied")
            {
                peer.ExpectBrowseResults(root, (uint)NodeClass.Object,
                    [new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied }]);
            }
            else if (shape == "PreCancelled")
            {
                cancellation.Cancel();
            }
            else if (shape == "CancelType")
            {
                NodeId actual = peer.Type(Extruder.ObjectTypeIds.Extruder_InterfaceType);
                peer.ExpectBrowse(root, (uint)NodeClass.Object, ExtrusionClientPeer.Reference(
                    ExtrusionClientPeer.DeviceId, actual, new QualifiedName("Extruder", 1)));
                peer.ExpectType(actual, peer.Type(ObjectTypeIds.ExtrusionDeviceType), true,
                    beforeReturn: cancellation.Cancel);
            }
            if (shape == "Denied")
            {
                ExtrusionClientPeer.AssertFailure(async () => await ExtrusionClientPeer.CollectAsync(
                    peer.Client.DiscoverAsync(root, cancellationToken: cancellation.Token)).ConfigureAwait(false),
                    StatusCodes.BadUserAccessDenied);
            }
            else if (shape is "PreCancelled" or "CancelType")
            {
                OperationCanceledException? failure = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await ExtrusionClientPeer.CollectAsync(peer.Client.DiscoverAsync(
                        root, cancellationToken: cancellation.Token)).ConfigureAwait(false));
                Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
            }
            else
            {
                Assert.That(await ExtrusionClientPeer.CollectAsync(
                    peer.Client.DiscoverAsync(root, cancellationToken: cancellation.Token)).ConfigureAwait(false),
                        Is.Empty);
            }
            if (shape is "MissingRoots" or "Unsupported" or "PreCancelled")
            {
                peer.VerifyNoServices();
            }
            peer.Verify();
        }

        [TestCase("ItemState", "Present")]
        [TestCase("ItemState", "MissingGroup")]
        [TestCase("ItemState", "MissingChild")]
        [TestCase("ItemState", "BrowseDenied")]
        [TestCase("ItemState", "TranslationFailure")]
        [TestCase("OperationMode", "Present")]
        [TestCase("OperationMode", "MissingGroup")]
        [TestCase("OperationMode", "MissingChild")]
        [TestCase("ProductionDatasets", "Present")]
        [TestCase("ProductionDatasets", "Missing")]
        [TestCase("ProductionDatasets", "TranslationDenied")]
        [TestCase("ResetMaintenance", "Present")]
        [TestCase("ResetMaintenance", "Missing")]
        [TestCase("ResetMaintenance", "CallDenied")]
        [TestCase("Jobs", "Present")]
        [TestCase("Jobs", "Missing")]
        [TestCase("Jobs", "CallDenied")]
        public async Task ExtrusionOptionalWorkflowsReturnTypedResultsOrMissingCapabilityAsync(string workflow,
            string outcome)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId owner = workflow == "Jobs" ? new NodeId("line-6", 1) : ExtrusionClientPeer.DeviceId;
            NodeId child = peer.NextNode();
            NodeId state = peer.NextNode();
            QualifiedName childName = workflow switch
            {
                "ItemState" or "OperationMode" => peer.Name(Machinery.Namespaces.Machinery, "MachineryBuildingBlocks"),
                "ProductionDatasets" => peer.General("ProductionDatasetManagement"),
                "ResetMaintenance" => peer.General("Maintenance"),
                _ => peer.Name(Line.Namespaces.ExtrusionLine, "JobGroups")
            };
            var transportFailure = new ServiceResultException(StatusCodes.BadCommunicationError);
            peer.ExpectChild(owner, childName, outcome is "MissingGroup" or "Missing" ? NodeId.Null : child,
                outcome == "TranslationDenied" ? StatusCodes.BadUserAccessDenied : StatusCodes.Good,
                outcome == "TranslationFailure" ? transportFailure : null);
            if (workflow is "ItemState" or "OperationMode" && outcome is not ("MissingGroup" or "TranslationFailure"))
            {
                QualifiedName stateName = peer.Name(Machinery.Namespaces.Machinery,
                    workflow == "ItemState" ? "MachineryItemState" : "MachineryOperationMode");
                peer.ExpectBrowseResults(child, 0,
                [
                    new BrowseResult
                    {
                        StatusCode = outcome == "BrowseDenied" ? StatusCodes.BadUserAccessDenied : StatusCodes.Good,
                        References = outcome == "MissingChild" ? [] :
                            [ExtrusionClientPeer.Reference(state, NodeId.Null, stateName)]
                    }
                ]);
            }
            if (workflow is "ResetMaintenance" or "Jobs" && outcome != "Missing")
            {
                peer.ExpectCall(child, peer.Type(workflow == "Jobs"
                    ? Line.MethodIds.JobGroupsType_StartJobGroupById
                    : PlasticsRubber.MethodIds.MaintenanceType_Reset),
                    workflow == "Jobs" ? [Variant.From("Group-West-43")] : [],
                    outcome == "CallDenied" ? StatusCodes.BadUserAccessDenied : StatusCodes.Good);
            }
            async Task InvokeAsync()
            {
                switch (workflow)
                {
                    case "ItemState":
                        var item = await
                            peer.Client.Extruder(owner).ItemStateAsync(cancellation.Token).ConfigureAwait(false);
                        AssertProxy(item, typeof(ExtrusionMachineryItemState_StateMachineTypeClient), state, peer);
                        break;
                    case "OperationMode":
                        var mode = await
                            peer.Client.Extruder(owner).OperationModeAsync(cancellation.Token).ConfigureAwait(false);
                        AssertProxy(mode, typeof(Machinery.MachineryOperationModeStateMachineTypeClient), state, peer);
                        break;
                    case "ProductionDatasets":
                        ProductionDatasetClient datasets = await peer.Client.Extruder(owner)
                            .ProductionDatasetsAsync(cancellation.Token).ConfigureAwait(false);
                        Assert.That(datasets.Session, Is.SameAs(peer.Session.Object));
                        AssertProxy(datasets.Proxy, typeof(PlasticsRubber.ProductionDatasetManagementTypeClient),
                            child, peer);
                        break;
                    case "ResetMaintenance":
                        await
                            peer.Client.Extruder(owner).ResetMaintenanceAsync(cancellation.Token).ConfigureAwait(false);
                        break;
                    case "Jobs":
                        Line.JobGroupsTypeClient jobs = await peer.Client.Line(owner).JobsAsync(cancellation.Token)
                            .ConfigureAwait(false);
                        AssertProxy(jobs, typeof(Line.JobGroupsTypeClient), child, peer);
                        await jobs.StartJobGroupByIdAsync("Group-West-43", cancellation.Token).ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(workflow));
                }
            }
            if (outcome == "Present")
            {
                await InvokeAsync().ConfigureAwait(false);
            }
            else if (outcome == "TranslationFailure")
            {
                Assert.That(Assert.ThrowsAsync<ServiceResultException>(
                    async () => await InvokeAsync().ConfigureAwait(false)), Is.SameAs(transportFailure));
                Assert.That(transportFailure.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
            }
            else
            {
                ExtrusionClientPeer.AssertFailure(InvokeAsync, outcome is "BrowseDenied" or "CallDenied"
                    ? StatusCodes.BadUserAccessDenied : StatusCodes.BadNotFound);
            }
            peer.Verify();
        }

        [TestCase("ItemState")]
        [TestCase("OperationMode")]
        [TestCase("ProductionDatasets")]
        [TestCase("ResetMaintenance")]
        [TestCase("Jobs")]
        public void ExtrusionOptionalWorkflowsRejectNullOwners(string workflow)
        {
            var peer = new ExtrusionClientPeer();
            ArgumentNullException? failure = Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                switch (workflow)
                {
                    case "ItemState":
                        await ExtrusionClientWorkflows.ItemStateAsync(null!).ConfigureAwait(false);
                        break;
                    case "OperationMode":
                        await ExtrusionClientWorkflows.OperationModeAsync(null!).ConfigureAwait(false);
                        break;
                    case "ProductionDatasets":
                        await ExtrusionClientWorkflows.ProductionDatasetsAsync(null!).ConfigureAwait(false);
                        break;
                    case "ResetMaintenance":
                        await ExtrusionClientWorkflows.ResetMaintenanceAsync(null!).ConfigureAwait(false);
                        break;
                    case "Jobs":
                        await ExtrusionClientWorkflows.JobsAsync(null!).ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(workflow));
                }
            });
            Assert.That(failure!.ParamName, Is.EqualTo(workflow == "Jobs" ? "line" : "device"));
            peer.VerifyNoServices();
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ExtrusionHostingPreservesSingletonRegistrations(bool existingFactory, bool existingDelegate)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer();
            var defaultFailure = new InvalidOperationException("Default session factory.");
            var retainedFailure = new InvalidOperationException("Caller-provided session factory.");
            var delegateFailure = new InvalidOperationException("Caller-provided typed factory.");
            int defaultCalls = 0;
            int retainedCalls = 0;
            int delegateCalls = 0;
            Task<ManagedSession> DefaultSession(CancellationToken token)
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                defaultCalls++;
                return Task.FromException<ManagedSession>(defaultFailure);
            }
            var retainedFactory = new ExtrusionClientFactory(token =>
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                retainedCalls++;
                return Task.FromException<ManagedSession>(retainedFailure);
            }, peer.Telemetry);
            Func<CancellationToken, Task<ExtrusionClient>> retainedDelegate = token =>
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                delegateCalls++;
                return Task.FromException<ExtrusionClient>(delegateFailure);
            };
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(peer.Telemetry);
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(DefaultSession);
            if (existingFactory)
            {
                services.AddSingleton(retainedFactory);
            }
            if (existingDelegate)
            {
                services.AddSingleton(retainedDelegate);
            }
            var builder = new Mock<IOpcUaClientBuilder>(MockBehavior.Strict);
            builder.SetupGet(value => value.Services).Returns(services);
            Assert.That(builder.Object.AddExtrusionClient(), Is.SameAs(builder.Object));
            Assert.That(builder.Object.AddExtrusionClient(), Is.SameAs(builder.Object));
            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                ExtrusionClientFactory factory = provider.GetRequiredService<ExtrusionClientFactory>();
                Func<CancellationToken, Task<ExtrusionClient>> create =
                    provider.GetRequiredService<Func<CancellationToken, Task<ExtrusionClient>>>();
                Assert.Multiple(() =>
                {
                    Assert.That(services.Count(value => value.ServiceType == typeof(ExtrusionClientFactory)),
                        Is.EqualTo(1));
                    Assert.That(services.Count(value => value.ServiceType == typeof(Func<CancellationToken,
                        Task<ExtrusionClient>>)),
                        Is.EqualTo(1));
                    Assert.That(services.Single(value => value.ServiceType == typeof(ExtrusionClientFactory)).Lifetime,
                        Is.EqualTo(ServiceLifetime.Singleton));
                    Assert.That(services.Single(value => value.ServiceType == typeof(Func<CancellationToken,
                        Task<ExtrusionClient>>)).Lifetime,
                        Is.EqualTo(ServiceLifetime.Singleton));
                    Assert.That(provider.GetRequiredService<ExtrusionClientFactory>(), Is.SameAs(factory));
                    Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<ExtrusionClient>>>(),
                        Is.SameAs(create));
                    Assert.That(defaultCalls + retainedCalls + delegateCalls, Is.Zero,
                        "Registration and resolution must not connect.");
                });
                if (existingFactory)
                {
                    Assert.That(factory, Is.SameAs(retainedFactory));
                }
                if (existingDelegate)
                {
                    Assert.That(create, Is.SameAs(retainedDelegate));
                }
                Assert.That(Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await create(cancellation.Token).ConfigureAwait(false)),
                    Is.SameAs(existingDelegate ? delegateFailure : existingFactory ? retainedFailure : defaultFailure));
            }
            Assert.Multiple(() =>
            {
                Assert.That(defaultCalls, Is.EqualTo(!existingFactory && !existingDelegate ? 1 : 0));
                Assert.That(retainedCalls, Is.EqualTo(existingFactory && !existingDelegate ? 1 : 0));
                Assert.That(delegateCalls, Is.EqualTo(existingDelegate ? 1 : 0));
            });
            builder.VerifyGet(value => value.Services, Times.Exactly(4));
            builder.VerifyNoOtherCalls();
            peer.VerifyNoServices();
            peer.Verify();
        }

        [TestCase("Failure")]
        [TestCase("Cancelled")]
        [TestCase("PreCancelled")]
        [TestCase("NullSession")]
        public async Task ExtrusionFactoryValidatesDependenciesAndForwardsTokensAndFailuresAsync(string outcome)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer();
            var expected = new InvalidOperationException("Session acquisition was rejected.");
            int calls = 0;
            Task<ManagedSession> Sessions(CancellationToken token)
            {
                calls++;
                Assert.That(token, Is.EqualTo(cancellation.Token));
                Assert.That(token.IsCancellationRequested, Is.EqualTo(outcome == "PreCancelled"));
                if (outcome == "Cancelled")
                {
                    cancellation.Cancel();
                }
                return outcome switch
                {
                    "Cancelled" or "PreCancelled" => Task.FromCanceled<ManagedSession>(token),
                    "NullSession" => Task.FromResult<ManagedSession>(null!),
                    _ => Task.FromException<ManagedSession>(expected)
                };
            }
            Assert.That(Assert.Throws<ArgumentNullException>(() => new ExtrusionClientFactory(null!,
                peer.Telemetry))!.ParamName,
                Is.EqualTo("sessions"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new ExtrusionClientFactory(Sessions,
                null!))!.ParamName,
                Is.EqualTo("telemetry"));
            Assert.That(Assert.Throws<ArgumentNullException>(() =>
                OpcUaExtrusionClientExtensions.AddExtrusionClient(null!))!.ParamName, Is.EqualTo("builder"));
            Assert.That(calls, Is.Zero);
            var factory = new ExtrusionClientFactory(Sessions, peer.Telemetry);
            if (outcome == "PreCancelled")
            {
                cancellation.Cancel();
            }
            if (outcome == "NullSession")
            {
                await Assert.ThatAsync(() => factory.CreateAsync(cancellation.Token),
                    Throws.TypeOf<ArgumentNullException>().With.Property(nameof(ArgumentNullException.ParamName))
                        .EqualTo("session")).ConfigureAwait(false);
            }
            else if (outcome is "Cancelled" or "PreCancelled")
            {
                await Assert.ThatAsync(() => factory.CreateAsync(cancellation.Token),
                    Throws.TypeOf<TaskCanceledException>()
                        .With.Property(nameof(TaskCanceledException.CancellationToken))
                        .EqualTo(cancellation.Token)).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => factory.CreateAsync(cancellation.Token),
                    Throws.Exception.SameAs(expected)).ConfigureAwait(false);
            }
            Assert.That(calls, Is.EqualTo(1));
            peer.VerifyNoServices();
            peer.Verify();
        }

        [Test]
        public async Task ExtrusionFacadesRetainCallerOwnedSessionAfterContainerDisposalAsync()
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            var services = new ServiceCollection();
            services.AddSingleton(peer.Session.Object);
            services.AddSingleton(peer.Telemetry);
            services.AddSingleton<ExtrusionClient>();
            Extruder.Extruder_InterfaceTypeClient extruder;
            Filter.Filter_InterfaceTypeClient filter;
            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                ExtrusionClient client = provider.GetRequiredService<ExtrusionClient>();
                Assert.That(provider.GetRequiredService<ExtrusionClient>(), Is.SameAs(client));
                extruder = client.Extruder(ExtrusionClientPeer.DeviceId);
                filter = peer.Client.Filter(ExtrusionClientPeer.FilterId);
                AssertProxy(extruder, typeof(Extruder.Extruder_InterfaceTypeClient), ExtrusionClientPeer.DeviceId,
                    peer);
                AssertProxy(filter, typeof(Filter.Filter_InterfaceTypeClient), ExtrusionClientPeer.FilterId, peer);
            }
            NodeId present = peer.NextNode();
            NodeId pressure = peer.NextNode();
            peer.ExpectBrowse(ExtrusionClientPeer.DeviceId, 0,
                ExtrusionClientPeer.Variable(present, peer.General("IsPresent")));
            peer.ExpectBrowse(ExtrusionClientPeer.FilterId, 0,
                ExtrusionClientPeer.Variable(pressure, peer.FilterName("BackflushPressure")));
            peer.ExpectRead(present, ExtrusionClientPeer.Sample(Variant.From(true)));
            peer.ExpectRead(pressure, ExtrusionClientPeer.Sample(Variant.From(21.875)));
            ExtrusionClientPeer.AssertReading(await extruder.IsPresentVariable().ReadAsync(cancellation.Token)
                .ConfigureAwait(false), true, true, StatusCodes.Good);
            ExtrusionClientPeer.AssertReading(await filter.BackflushPressureVariable().ReadAsync(cancellation.Token)
                .ConfigureAwait(false), 21.875, true, StatusCodes.Good);
            peer.Verify();
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ExtrusionDiscoveryUsesTheRemainingResolvableDefaultRootAsync(bool machinery)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(includeRoots: false, token: cancellation.Token);
            peer.Context.NamespaceUris.GetIndexOrAppend(machinery
                ? Machinery.Namespaces.Machinery : Di.Namespaces.OpcUaDi);
            NodeId root = peer.Type(machinery ? Machinery.ObjectIds.Machines : Di.ObjectIds.DeviceSet);
            NodeId type = peer.Type(Extruder.ObjectTypeIds.Extruder_InterfaceType);
            peer.ExpectBrowse(root, (uint)NodeClass.Object, ExtrusionClientPeer.Reference(
                ExtrusionClientPeer.DeviceId, type, new QualifiedName("OnlyDevice", 1)));
            peer.ExpectBrowse(ExtrusionClientPeer.DeviceId, (uint)NodeClass.Object);
            peer.ExpectType(type, peer.Type(ObjectTypeIds.ExtrusionDeviceType), true);
            List<ExtrusionDeviceEntry> values = await ExtrusionClientPeer.CollectAsync(
                peer.Client.DiscoverAsync(cancellationToken: cancellation.Token)).ConfigureAwait(false);
            Assert.That(values.Select(value => value.NodeId), Is.EqualTo(new[] { ExtrusionClientPeer.DeviceId }));
            Assert.That(values[0].TypeDefinition, Is.EqualTo(Extruder.ObjectTypeIds.Extruder_InterfaceType));
            peer.Verify();
        }

        private static void AssertProxy(ObjectTypeClient proxy, Type expectedType, NodeId expectedId,
            ExtrusionClientPeer peer)
        {
            Assert.Multiple(() =>
            {
                Assert.That(proxy.GetType(), Is.EqualTo(expectedType));
                Assert.That(proxy.ObjectId, Is.EqualTo(expectedId));
                Assert.That(proxy.Session, Is.SameAs(peer.Session.Object));
                Assert.That(proxy.Telemetry, Is.SameAs(peer.Telemetry));
            });
        }
    }
}
