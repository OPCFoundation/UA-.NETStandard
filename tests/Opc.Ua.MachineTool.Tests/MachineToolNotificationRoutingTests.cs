/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies the MachineTool Event Propagation CU for native parent and alternate hierarchical notifier paths.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("MachineTool")]
    public sealed class MachineToolNotificationRoutingTests
    {
        /// <summary>
        /// An event must reach every inverse notifier path, but a shared ancestor receives it exactly once.
        /// </summary>
        [Test]
        public async Task InverseNotifierDiamondDeliversEachEventOnceAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var provider = new MachineToolNotificationTests.NotificationProvider(new MachineToolNotificationSnapshot());
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider).ConfigureAwait(false);
            var alternate = new BaseObjectState(runtime.State)
            {
                NodeId = new NodeId(Guid.NewGuid(), fixture.Context.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("OperatorPanel", fixture.Context.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Operator panel"),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType,
                EventNotifier = EventNotifiers.SubscribeToEvents
            };
            runtime.State.AddChild(alternate);
            await fixture.Context.Manager.AddPredefinedNodeAsync(alternate).ConfigureAwait(false);
            MessagesState messages = runtime.Notifications!.State;
            messages.AddNotifier(fixture.Context.Context, ReferenceTypeIds.HasNotifier, true, alternate);
            alternate.AddNotifier(fixture.Context.Context, ReferenceTypeIds.HasNotifier, false, messages);
            int roots = 0;
            int notifications = 0;
            int panels = 0;
            int sources = 0;
            runtime.State.EventReported += (_, _, e) =>
            {
                if (e is NotificationEventState)
                {
                    roots++;
                }
            };
            runtime.State.Notification!.EventReported += (_, _, e) =>
            {
                if (e is NotificationEventState)
                {
                    notifications++;
                }
            };
            alternate.EventReported += (_, _, e) =>
            {
                if (e is NotificationEventState)
                {
                    panels++;
                }
            };
            messages.EventReported += (_, _, e) =>
            {
                if (e is NotificationEventState)
                {
                    sources++;
                }
            };
            provider.Snapshot = new MachineToolNotificationSnapshot
            {
                Revision = 1,
                Messages =
                [
                    new MachineToolMessage
                    {
                        Sequence = 1, Identifier = "M1", Message = new LocalizedText("Ready"),
                        Time = new DateTime(2026, 7, 8, 9, 10, 11, DateTimeKind.Utc)
                    }
                ]
            };

            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);

            Assert.That(sources, Is.EqualTo(1));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(panels, Is.EqualTo(1));
            Assert.That(roots, Is.EqualTo(1));
        }
    }
}
