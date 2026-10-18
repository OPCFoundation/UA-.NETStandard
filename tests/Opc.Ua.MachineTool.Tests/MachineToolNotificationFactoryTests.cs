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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies typed vendor alert factories, rollback and bounded accepted-publication retries.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("MachineTool")]
    public sealed class MachineToolNotificationFactoryTests
    {
        /// <summary>
        /// Vendor types retain their IDs and generated states; a later factory failure publishes no prefix.
        /// </summary>
        [Test]
        public async Task VendorFactoriesStageAtomicallyAndPreserveTypeIdentityAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var type = new BaseObjectTypeState
            {
                NodeId = new NodeId(Guid.NewGuid(), fixture.Context.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("VendorAlert", fixture.Context.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Vendor alert"),
                SuperTypeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.AlertType, fixture.Context.Context.NamespaceUris),
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            };
            await fixture.Context.Manager.AddPredefinedNodeAsync(type).ConfigureAwait(false);
            MachineToolAlert alert = MachineToolNotificationValidationTests.Alert();
            var provider = new MachineToolNotificationTests.NotificationProvider(
                new MachineToolNotificationSnapshot { Alerts = [alert] });
            bool fail = true;
            var created = new List<AlertState>();
            var options = new MachineToolNotificationOptions
            {
                Provider = provider,
                CreateAlertAsync = (context, parent, name, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (name.Name == "Third" && fail)
                    {
                        throw new ArgumentException("Factory failed.");
                    }
                    AlertState state = context.CreateInstanceOfAlertType(parent, name);
                    state.TypeDefinitionId = type.NodeId;
                    created.Add(state);
                    return new ValueTask<AlertState>(state);
                }
            };
            await using MachineToolRuntime<MachineToolState> runtime =
                await Builder(fixture, options).BuildAsync().ConfigureAwait(false);
            Assert.That(runtime.Notifications!.TryGetAlert(alert.Key, out AlertState? original), Is.True);
            Assert.That(original, Is.SameAs(created[0]));
            Assert.That(original!.TypeDefinitionId, Is.EqualTo(type.NodeId));
            provider.Snapshot = new MachineToolNotificationSnapshot
            {
                Revision = 1,
                Alerts = [alert with { Severity = 800 }, alert with { Key = "Second" }, alert with { Key = "Third" }]
            };
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await runtime.RefreshNotificationsAsync().ConfigureAwait(false));
            Assert.That(original.Severity!.Value, Is.EqualTo(500));
            Assert.That(runtime.Notifications.TryGetAlert("Second", out _), Is.False);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(created[1].NodeId), Is.Null);
            fail = false;
            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);
            Assert.That(runtime.Notifications.TryGetAlert("Third", out AlertState? third), Is.True);
            Assert.That(third!.TypeDefinitionId, Is.EqualTo(type.NodeId));
            Assert.That(original.Severity.Value, Is.EqualTo(800));
            Assert.That(runtime.Notifications.TryGetAlert("Absent", out _), Is.False);
            Assert.Throws<ArgumentNullException>(() => runtime.Notifications.TryGetAlert(null!, out _));
        }

        /// <summary>
        /// A failed sink retains the accepted EventId; the next update retries without creating a new identity.
        /// </summary>
        [Test]
        public async Task PublicationRetryPreservesAcceptedMessageIdentityAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var provider = new MachineToolNotificationTests.NotificationProvider(new MachineToolNotificationSnapshot());
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider).ConfigureAwait(false);
            ByteString firstId = default;
            ByteString retryId = default;
            bool fail = true;
            NodeStateReportEventAsyncHandler? previous = runtime.State.OnReportEventAsync;
            runtime.State.OnReportEventAsync = async (context, node, value, ct) =>
            {
                if (value is NotificationEventState message)
                {
                    if (fail)
                    {
                        firstId = message.EventId!.Value;
                        throw new InvalidOperationException("Sink rejected publication.");
                    }
                    retryId = message.EventId!.Value;
                }
                if (previous != null)
                {
                    await previous(context, node, value, ct).ConfigureAwait(false);
                }
            };
            provider.Snapshot = new MachineToolNotificationSnapshot
            {
                Revision = 1,
                Messages = [MachineToolNotificationValidationTests.Message(1)]
            };
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.RefreshNotificationsAsync().ConfigureAwait(false));
            Assert.That(runtime.Notifications!.Observation!.Revision, Is.EqualTo(1));
            fail = false;
            provider.Snapshot = new MachineToolNotificationSnapshot { Revision = 1 };
            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);
            Assert.That(firstId.IsEmpty, Is.False);
            Assert.That(retryId, Is.EqualTo(firstId));
            runtime.State.OnReportEventAsync = previous;
        }

        /// <summary>
        /// A factory cannot borrow an already-published condition from another machine.
        /// </summary>
        [Test]
        public async Task BorrowedFactoryConditionIsRejectedWithoutDeletingItsOwnerAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolAlert alert = MachineToolNotificationValidationTests.Alert();
            var provider = new MachineToolNotificationTests.NotificationProvider(
                new MachineToolNotificationSnapshot { Alerts = [alert] });
            await using MachineToolRuntime<MachineToolState> first =
                await MachineToolNotificationTests.BuildAsync(fixture, provider, "Owner").ConfigureAwait(false);
            Assert.That(first.Notifications!.TryGetAlert(alert.Key, out AlertState? existing), Is.True);
            var options = new MachineToolNotificationOptions
            {
                Provider = provider,
                CreateAlertAsync = (_, _, _, _) => new ValueTask<AlertState>(existing!)
            };
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Builder(fixture, options).BuildAsync().ConfigureAwait(false));
            Assert.That(fixture.Context.Manager.FindPredefinedNode(existing!.NodeId), Is.SameAs(existing));
            Assert.That(existing.ActiveState!.Id!.Value, Is.True);
        }

        /// <summary>
        /// Selects a factory on the normal public builder without replacing any shared registration.
        /// </summary>
        private static MachineToolBuilder<MachineToolState> Builder(
            MachineToolRuntimeFixture fixture, MachineToolNotificationOptions options)
        {
            return fixture.Context.AddMachineTool("Factory")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Factory test");
                    identity.SerialNumber = "Factory";
                    identity.ProductInstanceUri = "urn:notifications:factory";
                }).WithNotifications(options);
        }
    }
}
