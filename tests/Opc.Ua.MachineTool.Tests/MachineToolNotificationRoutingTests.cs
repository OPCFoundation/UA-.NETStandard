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
