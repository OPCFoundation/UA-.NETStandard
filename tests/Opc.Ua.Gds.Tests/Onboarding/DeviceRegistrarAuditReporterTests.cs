/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies sensitive event-type permissions and concrete software-status audit payloads.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DeviceRegistrarAuditReporterTests
    {
        /// <summary>
        /// The injected adapter sends both native event types through the bound server's restricted reporter.
        /// </summary>
        [Test]
        public async Task HostedAdapterReportsThroughBoundModelAndRejectsAfterDetach()
        {
            (SessionSystemContext context, ArrayOf<NodeState> nodes) = CreateModel();
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.Auditing).Returns(true);
            var events = new List<IFilterTarget>();
            server.Setup(value => value.ReportEventAsync(
                context, It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Callback<ISystemContext, IFilterTarget, CancellationToken>((_, target, _) => events.Add(target))
                .Returns(default(ValueTask));
            var reporter = new DeviceRegistrarAuditReporter(server.Object, context, nodes);
            var adapter = new HostedDeviceRegistrarAuditReporter();
            OnboardingIdentitySelection identity = CreateSelection("signed-ticket", "signed-composite");
            ServiceResultException? before = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.ReportIdentityAcceptedAsync(context, identity).ConfigureAwait(false));
            Assert.That(before!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            adapter.Attach(reporter);

            await adapter.ReportIdentityAcceptedAsync(context, identity).ConfigureAwait(false);
            await adapter.ReportSoftwareStatusAsync(context, identity.ProductInstanceUri, true, "2.0")
                .ConfigureAwait(false);
            adapter.Detach(reporter);
            ServiceResultException? after = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.ReportSoftwareStatusAsync(context, identity.ProductInstanceUri, true, "2.0")
                    .ConfigureAwait(false));

            Assert.That(after!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0], Is.InstanceOf<DeviceIdentityAcceptedAuditEventState>());
            Assert.That(events[1], Is.InstanceOf<DeviceSoftwareUpdatedAuditEventState>());
            Assert.That(((DeviceIdentityAcceptedAuditEventState)events[0]).Ticket!.Value, Is.EqualTo("signed-ticket"));
        }

        /// <summary>
        /// A live owner cannot be overwritten, and a stale shutdown cannot detach a replacement owner.
        /// </summary>
        [Test]
        public async Task HostedAdapterRejectsCompetingOwnersAndIgnoresStaleDetach()
        {
            var first = new Mock<IDeviceRegistrarAuditReporter>(MockBehavior.Strict);
            var second = new Mock<IDeviceRegistrarAuditReporter>(MockBehavior.Strict);
            var context = new SystemContext(NUnitTelemetryContext.Create());
            second.Setup(value => value.ReportSoftwareStatusAsync(
                context, "urn:device:test", false, "3.0", CancellationToken.None)).Returns(default(ValueTask));
            var adapter = new HostedDeviceRegistrarAuditReporter();
            Assert.That(() => adapter.Attach(null!), Throws.ArgumentNullException);
            Assert.That(() => adapter.Attach(adapter), Throws.ArgumentException);
            adapter.Attach(first.Object);
            Assert.That(() => adapter.Attach(second.Object), Throws.InvalidOperationException);
            Assert.That(() => adapter.Attach(first.Object), Throws.InvalidOperationException);
            adapter.Detach(first.Object);
            adapter.Attach(second.Object);
            adapter.Detach(first.Object);

            await adapter.ReportSoftwareStatusAsync(context, "urn:device:test", false, "3.0").ConfigureAwait(false);

            first.VerifyNoOtherCalls();
            second.Verify(value => value.ReportSoftwareStatusAsync(
                context, "urn:device:test", false, "3.0", CancellationToken.None), Times.Once);
            second.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Cancellation prevents dispatch, while a live reporter's failure remains observable to its caller.
        /// </summary>
        [Test]
        public async Task HostedAdapterPreservesReporterFailureAndHonorsCancellation()
        {
            var reporter = new Mock<IDeviceRegistrarAuditReporter>(MockBehavior.Strict);
            var context = new SystemContext(NUnitTelemetryContext.Create());
            OnboardingIdentitySelection identity = CreateSelection("signed-ticket", string.Empty);
            var failure = new ServiceResultException(StatusCodes.BadCommunicationError);
            reporter.Setup(value => value.ReportIdentityAcceptedAsync(
                context, identity, CancellationToken.None)).Throws(failure);
            var adapter = new HostedDeviceRegistrarAuditReporter();
            adapter.Attach(reporter.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await adapter.ReportIdentityAcceptedAsync(context, identity, cancellation.Token).ConfigureAwait(false));
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.ReportIdentityAcceptedAsync(context, identity).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            reporter.Verify(value => value.ReportIdentityAcceptedAsync(
                context, identity, CancellationToken.None), Times.Once);
            reporter.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Identity acceptance retains the exact signed envelopes and selected certificate in the native event.
        /// </summary>
        [TestCase("")]
        [TestCase("signed-composite")]
        public async Task IdentityAcceptanceReportsSignedEnvelopesAndSelectedCertificate(string composite)
        {
            (SessionSystemContext context, ArrayOf<NodeState> nodes) = CreateModel();
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.Auditing).Returns(true);
            AuditEventState? reported = null;
            server.Setup(s => s.ReportEventAsync(context, It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Callback<ISystemContext, IFilterTarget, CancellationToken>((_, e, _) => reported = (AuditEventState)e)
                .Returns(default(ValueTask));
            var reporter = new DeviceRegistrarAuditReporter(server.Object, context, nodes);
            OnboardingIdentitySelection selection = CreateSelection("signed-ticket", composite);

            await reporter.ReportIdentityAcceptedAsync(context, selection).ConfigureAwait(false);

            Assert.That(reported, Is.InstanceOf<DeviceIdentityAcceptedAuditEventState>());
            var identityEvent = (DeviceIdentityAcceptedAuditEventState)reported!;
            Assert.That(identityEvent.ProductInstanceUri!.Value, Is.EqualTo(selection.ProductInstanceUri));
            Assert.That(identityEvent.Certificate!.Value, Is.EqualTo(selection.SelectedIdentity));
            Assert.That(identityEvent.Ticket!.Value, Is.EqualTo("signed-ticket"));
            Assert.That(identityEvent.Composite!.Value, Is.EqualTo(composite));
            Assert.That(identityEvent.SourceNode!.Value, Is.EqualTo(Opc.Ua.ObjectIds.Server));
            server.Verify(s => s.ReportEventAsync(
                context, It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A missing signed envelope or disabled auditing cannot produce an incomplete identity event.
        /// </summary>
        [TestCase(true, "")]
        [TestCase(false, "signed-ticket")]
        public async Task IdentityAcceptanceRejectsIncompleteEvidenceOrDisabledAuditing(
            bool auditing, string envelope)
        {
            StatusCode expected = auditing ? StatusCodes.BadConfigurationError : StatusCodes.BadInvalidState;
            (SessionSystemContext context, ArrayOf<NodeState> nodes) = CreateModel();
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.Auditing).Returns(auditing);
            var reporter = new DeviceRegistrarAuditReporter(server.Object, context, nodes);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await reporter.ReportIdentityAcceptedAsync(context, CreateSelection(envelope, string.Empty))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            server.Verify(s => s.ReportEventAsync(
                It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SoftwareStatusReportsGeneratedAuditEventWithNativePayload(bool status)
        {
            (SessionSystemContext context, ArrayOf<NodeState> nodes) = CreateModel();
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.Auditing).Returns(true);
            AuditEventState? reported = null;
            server.Setup(s => s.ReportEventAsync(context, It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Callback<ISystemContext, IFilterTarget, CancellationToken>((_, e, _) => reported = (AuditEventState)e)
                .Returns(default(ValueTask));
            var reporter = new DeviceRegistrarAuditReporter(server.Object, context, nodes);
            await reporter.ReportSoftwareStatusAsync(
                context, "urn:device:test", status, "2.0").ConfigureAwait(false);
            Assert.That(reported, Is.InstanceOf<DeviceSoftwareUpdatedAuditEventState>());
            var softwareEvent = (DeviceSoftwareUpdatedAuditEventState)reported!;
            Assert.That(softwareEvent.ProductInstanceUri!.Value, Is.EqualTo("urn:device:test"));
            Assert.That(softwareEvent.SoftwareRevision!.Value, Is.EqualTo("2.0"));
            Assert.That(softwareEvent.Status!.Value, Is.EqualTo(status));
            Assert.That(softwareEvent.SourceNode!.Value, Is.EqualTo(Opc.Ua.ObjectIds.Server));
            server.Verify(s => s.ReportEventAsync(
                context, It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void RegistrationEventHierarchyIncludingCustomSubtypesAllowsOnlySecurityAdminReceivers()
        {
            (SessionSystemContext context, ArrayOf<NodeState> nodes) = CreateModel();
            NodeId baseId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceRegistrationAuditEventType, context.NamespaceUris);
            NodeId softwareId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceSoftwareUpdatedAuditEventType, context.NamespaceUris);
            var customType = new BaseObjectTypeState
            {
                NodeId = new NodeId("custom-event", 3),
                SuperTypeId = softwareId,
                RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = Opc.Ua.ObjectIds.WellKnownRole_AuthenticatedUser,
                        Permissions = (uint)PermissionType.ReceiveEvents
                    }
                ]
            };
            NodeState[] modelNodes = nodes.ToArray() ?? throw new AssertionException("Missing model nodes.");
            NodeState[] extendedNodes = [.. modelNodes, customType];
            ArrayOf<NodeState> extended = extendedNodes.ToArrayOf();
            _ = new DeviceRegistrarAuditReporter(new Mock<IServerInternal>().Object, context, extended);
            BaseObjectTypeState[] restricted = extendedNodes.OfType<BaseObjectTypeState>()
                .Where(t => t.NodeId == baseId || t.SuperTypeId == baseId ||
                    t.NodeId == customType.NodeId).ToArray();
            Assert.That(restricted, Has.Length.GreaterThanOrEqualTo(4));
            foreach (BaseObjectTypeState type in restricted)
            {
                Assert.That(type.RolePermissions.Count, Is.EqualTo(1));
                Assert.That(
                    type.RolePermissions[0].RoleId, Is.EqualTo(Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin));
                Assert.That(
                    type.RolePermissions[0].Permissions & (uint)PermissionType.ReceiveEvents, Is.Not.Zero);
                Assert.That(type.UserRolePermissions.Count, Is.EqualTo(1));
                Assert.That(
                    type.UserRolePermissions[0].RoleId, Is.EqualTo(Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin));
            }
        }

        /// <summary>
        /// The supported event dispatcher delivers whole registration events only to SecurityAdmin sessions,
        /// including native identity/software events and custom descendants, and still enforces the source ACL.
        /// </summary>
        [Test]
        public async Task RegistrationEventsReachOnlyAuthorizedSessionsThroughNativeEventAuthorization(
            [Values(0, 1, 2, 3, 4)] int kind, [Values(true, false)] bool sourceAllowsAdmin)
        {
            (SessionSystemContext context, ArrayOf<NodeState> model) = CreateModel();
            NodeId baseId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceRegistrationAuditEventType, context.NamespaceUris);
            NodeId identityId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceIdentityAcceptedAuditEventType, context.NamespaceUris);
            NodeId softwareId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceSoftwareUpdatedAuditEventType, context.NamespaceUris);
            ushort onboardingIndex = identityId.NamespaceIndex;
            var customType = new BaseObjectTypeState
            {
                NodeId = new NodeId("custom-registration-event", onboardingIndex),
                SuperTypeId = identityId
            };
            var descendantType = new BaseObjectTypeState
            {
                NodeId = new NodeId("custom-registration-descendant", onboardingIndex),
                SuperTypeId = customType.NodeId
            };
            NodeState[] modelNodes = model.ToArray() ?? throw new AssertionException("Missing model nodes.");
            ArrayOf<NodeState> nodes = [.. modelNodes, customType, descendantType];
            var source = new BaseObjectState(null)
            {
                NodeId = Opc.Ua.ObjectIds.Server,
                EventNotifier = EventNotifiers.SubscribeToEvents
            };
            if (!sourceAllowsAdmin)
            {
                source.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = Opc.Ua.ObjectIds.WellKnownRole_Observer,
                        Permissions = (uint)PermissionType.ReceiveEvents
                    }
                ];
            }
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var server = new Mock<IServerInternal>();
            var master = new Mock<IMasterNodeManager>();
            server.SetupGet(s => s.Auditing).Returns(true);
            server.SetupGet(s => s.Telemetry).Returns(telemetry);
            server.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            server.SetupGet(s => s.ServerUris).Returns(context.ServerUris);
            server.SetupGet(s => s.TypeTree).Returns(new TypeTable(context.NamespaceUris));
            server.SetupGet(s => s.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(s => s.NodeManager).Returns(master.Object);
            server.SetupGet(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            await using var manager = new TestableAsyncCustomNodeManager(
                server.Object, new ApplicationConfiguration(), false, NullLogger.Instance,
                Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding, Opc.Ua.Namespaces.OpcUa);
            foreach (NodeState node in nodes)
            {
                manager.PredefinedNodes[node.NodeId] = node;
            }
            manager.PredefinedNodes[source.NodeId] = source;
            master.Setup(m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns<NodeId, CancellationToken>((id, _) =>
                {
                    if (manager.PredefinedNodes.TryGetValue(id, out NodeState? node))
                    {
                        return new ValueTask<(object? Handle, IAsyncNodeManager? Manager)>(
                            (new NodeHandle(id, node), manager));
                    }
                    return new ValueTask<(object? Handle, IAsyncNodeManager? Manager)>((null, null));
                });
            Mock<IEventMonitoredItem> admin = CreateReceiver(1, Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin);
            Mock<IEventMonitoredItem> authenticated = CreateReceiver(
                2, Opc.Ua.ObjectIds.WellKnownRole_AuthenticatedUser);
            Mock<IEventMonitoredItem> anonymous = CreateReceiver(3, Opc.Ua.ObjectIds.WellKnownRole_Anonymous);
            Mock<IEventMonitoredItem> observer = CreateReceiver(4, Opc.Ua.ObjectIds.WellKnownRole_Observer);
            IList<IEventMonitoredItem> receivers =
                [authenticated.Object, admin.Object, anonymous.Object, observer.Object];
            IFilterTarget? reported = null;
            server.Setup(s => s.ReportEventAsync(context, It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext _, IFilterTarget occurrence, CancellationToken ct) =>
                {
                    reported = occurrence;
                    return EventManager.ReportEventAsync(occurrence, manager, receivers, ct);
                });
            var reporter = new DeviceRegistrarAuditReporter(server.Object, context, nodes);
            NodeId expectedType;
            if (kind == 1)
            {
                expectedType = identityId;
                await reporter.ReportIdentityAcceptedAsync(
                    context, CreateSelection("unredacted-signed-ticket", "unredacted-composite"))
                    .ConfigureAwait(false);
            }
            else if (kind == 2)
            {
                expectedType = softwareId;
                await reporter.ReportSoftwareStatusAsync(
                    context, "urn:test:device", true, "unredacted-revision").ConfigureAwait(false);
            }
            else
            {
                expectedType = kind == 0 ? baseId : kind == 3 ? customType.NodeId : descendantType.NodeId;
                var occurrence = new BaseEventState(null);
                occurrence.Initialize(
                    context, null, EventSeverity.Min, LocalizedText.From("Registration changed."));
                occurrence.EventType!.Value = expectedType;
                occurrence.SetChildValue(context, global::Opc.Ua.BrowseNames.SourceNode, source.NodeId, false);
                await server.Object.ReportEventAsync(context, occurrence).ConfigureAwait(false);
            }
            Assert.That(reported, Is.Not.Null);
            Assert.That(((BaseEventState)reported!).EventType!.Value, Is.EqualTo(expectedType));
            admin.Verify(item => item.QueueEvent(reported!),
                sourceAllowsAdmin ? Times.Once() : Times.Never());
            authenticated.Verify(item => item.QueueEvent(It.IsAny<IFilterTarget>()), Times.Never);
            anonymous.Verify(item => item.QueueEvent(It.IsAny<IFilterTarget>()), Times.Never);
            observer.Verify(item => item.QueueEvent(It.IsAny<IFilterTarget>()), Times.Never);
            if (reported is DeviceIdentityAcceptedAuditEventState identityEvent)
            {
                Assert.That(identityEvent.Ticket!.Value, Is.EqualTo("unredacted-signed-ticket"));
                Assert.That(identityEvent.Composite!.Value, Is.EqualTo("unredacted-composite"));
            }
            server.Verify(s => s.ReportAuditEvent(
                It.IsAny<ISystemContext>(), It.IsAny<AuditEventState>()), Times.Never);
        }

        [Test]
        public void MissingGeneratedAuditTypeCannotFallBackToPublicEvent()
        {
            (SessionSystemContext context, _) = CreateModel();
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                new DeviceRegistrarAuditReporter(new Mock<IServerInternal>().Object, context, []));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Omitting any required event declaration rejects initialization before it changes the remaining ACLs.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void IncompleteRegistrationEventHierarchyCannotBeRestrictedPartially(int missing)
        {
            (SessionSystemContext context, ArrayOf<NodeState> nodes) = CreateModel();
            ExpandedNodeId[] required =
            [
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceRegistrationAuditEventType,
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceIdentityAcceptedAuditEventType,
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceSoftwareUpdatedAuditEventType
            ];
            NodeId removedId = ExpandedNodeId.ToNodeId(required[missing], context.NamespaceUris);
            NodeState[] modelNodes = nodes.ToArray() ?? throw new AssertionException("Missing model nodes.");
            NodeState[] remaining = modelNodes.Where(node => node.NodeId != removedId).ToArray();
            BaseObjectTypeState[] types = remaining.OfType<BaseObjectTypeState>().ToArray();
            ArrayOf<RolePermissionType>[] before = types.Select(type => type.RolePermissions).ToArray();

            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                new DeviceRegistrarAuditReporter(
                    new Mock<IServerInternal>().Object, context, remaining.ToArrayOf()));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            for (int i = 0; i < types.Length; i++)
            {
                Assert.That(types[i].RolePermissions, Is.EqualTo(before[i]));
            }
        }

        /// <summary>
        /// The registrar cannot acknowledge its audit operation before asynchronous delivery has completed.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReportingAwaitsDeliveryAndPropagatesDispatcherFailure(bool identityAcceptance)
        {
            (SessionSystemContext context, ArrayOf<NodeState> nodes) = CreateModel();
            var dispatch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.Auditing).Returns(true);
            server.Setup(s => s.ReportEventAsync(
                context, It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(dispatch.Task));
            var reporter = new DeviceRegistrarAuditReporter(server.Object, context, nodes);
            ValueTask pending = identityAcceptance
                ? reporter.ReportIdentityAcceptedAsync(context, CreateSelection("ticket", "composite"))
                : reporter.ReportSoftwareStatusAsync(context, "urn:test:device", true, "revision");
            Assert.That(pending.IsCompleted, Is.False);
            var failure = new InvalidOperationException("event dispatch unavailable");
            dispatch.SetException(failure);

            InvalidOperationException? actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await pending.ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BroadenedPermissionsPreventAuditDelivery(bool identityAcceptance)
        {
            (SessionSystemContext context, ArrayOf<NodeState> nodes) = CreateModel();
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.Auditing).Returns(true);
            var reporter = new DeviceRegistrarAuditReporter(server.Object, context, nodes);
            NodeId softwareId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.ObjectTypeIds.DeviceSoftwareUpdatedAuditEventType, context.NamespaceUris);
            NodeState[] modelNodes = nodes.ToArray() ?? throw new AssertionException("Missing model nodes.");
            BaseObjectTypeState softwareType = modelNodes.OfType<BaseObjectTypeState>()
                .Single(t => t.NodeId == softwareId);
            softwareType.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = Opc.Ua.ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)PermissionType.ReceiveEvents
                }
            ];
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (identityAcceptance)
                {
                    await reporter.ReportIdentityAcceptedAsync(
                        context, CreateSelection("signed-ticket", string.Empty)).ConfigureAwait(false);
                }
                else
                {
                    await reporter.ReportSoftwareStatusAsync(
                        context, "urn:device:test", true, "2.0").ConfigureAwait(false);
                }
            });
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            server.Verify(s => s.ReportEventAsync(
                It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task DisabledAuditingCannotSilentlyAcknowledgeRequiredEvent()
        {
            (SessionSystemContext context, ArrayOf<NodeState> nodes) = CreateModel();
            var server = new Mock<IServerInternal>();
            var reporter = new DeviceRegistrarAuditReporter(server.Object, context, nodes);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await reporter.ReportSoftwareStatusAsync(
                    context, "urn:device:test", true, "2.0").ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            server.Verify(s => s.ReportEventAsync(
                It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationPreventsAuditDelivery(bool identityAcceptance)
        {
            (SessionSystemContext context, ArrayOf<NodeState> nodes) = CreateModel();
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.Auditing).Returns(true);
            var reporter = new DeviceRegistrarAuditReporter(server.Object, context, nodes);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                if (identityAcceptance)
                {
                    await reporter.ReportIdentityAcceptedAsync(
                        context, CreateSelection("signed-ticket", string.Empty), cancellation.Token)
                        .ConfigureAwait(false);
                }
                else
                {
                    await reporter.ReportSoftwareStatusAsync(
                        context, "urn:device:test", true, "2.0", cancellation.Token).ConfigureAwait(false);
                }
            });
            server.Verify(s => s.ReportEventAsync(
                It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Supplies validation evidence independently of envelope parsing.
        /// </summary>
        private static OnboardingIdentitySelection CreateSelection(string ticket, string composite)
        {
            return new OnboardingIdentitySelection(
                ByteString.From(new byte[] { 1, 2, 3 }), new DeviceIdentityTicketType(),
                "urn:device:test", ticket, composite);
        }

        /// <summary>
        /// Creates a monitored session whose already-granted effective role is used by native permission checks.
        /// </summary>
        private static Mock<IEventMonitoredItem> CreateReceiver(uint id, NodeId role)
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.GrantedRoleIds).Returns([role]);
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(id));
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
            session.SetupGet(value => value.PreferredLocales).Returns([]);
            var receiver = new Mock<IEventMonitoredItem>();
            receiver.SetupGet(value => value.Id).Returns(id);
            receiver.SetupGet(value => value.Session).Returns(session.Object);
            return receiver;
        }

        private static (SessionSystemContext Context, ArrayOf<NodeState> Nodes) CreateModel()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            messageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            var context = new SessionSystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory,
                TypeTable = new TypeTable(messageContext.NamespaceUris)
            };
            NodeStateCollection nodes = new NodeStateCollection().AddOpcUaOnboarding(context);
            return (context, nodes.ToArray().ToArrayOf());
        }
    }
}
