/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Unit tests for the parts of <see cref="ServerStatusMonitor"/> that do
    /// not need a live session: the V2 notification dispatch and the interval
    /// clamping the classic path applies. The engine paths themselves are
    /// covered end to end by <see cref="ClientTest"/> and <see cref="PushTest"/>,
    /// which exercise the classic engine against a real server.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerStatusMonitorTests
    {
        [Test]
        public async Task RaisesForTheMonitoredServerStatusItemAsync()
        {
            var raised = new List<ServerStatusChangedEventArgs>();
            ServerStatusMonitor monitor = CreateMonitor(raised.Add);

            await NotifyAsync(
                monitor,
                new DataValueChange(
                    new StubMonitoredItem(ServerStatusMonitor.ServerStatusItemName),
                    new DataValue(new ExtensionObject(
                        new ServerStatusDataType { State = ServerState.Running })),
                    null)).ConfigureAwait(false);

            Assert.That(raised, Has.Count.EqualTo(1));
            Assert.That(raised[0].Status, Is.Not.Null);
            Assert.That(raised[0].Status.State, Is.EqualTo(ServerState.Running));
        }

        [Test]
        public async Task IgnoresAChangeWithoutAMonitoredItemAsync()
        {
            // A change that carries no monitored item cannot be attributed to
            // this monitor, so it must not surface as a server status.
            var raised = new List<ServerStatusChangedEventArgs>();
            ServerStatusMonitor monitor = CreateMonitor(raised.Add);

            await NotifyAsync(
                monitor,
                new DataValueChange(
                    null,
                    new DataValue(new ExtensionObject(new ServerStatusDataType())),
                    null)).ConfigureAwait(false);

            Assert.That(raised, Is.Empty);
        }

        [Test]
        public async Task IgnoresAChangeFromAnotherItemAsync()
        {
            var raised = new List<ServerStatusChangedEventArgs>();
            ServerStatusMonitor monitor = CreateMonitor(raised.Add);

            await NotifyAsync(
                monitor,
                new DataValueChange(
                    new StubMonitoredItem("SomethingElse"),
                    new DataValue(new ExtensionObject(new ServerStatusDataType())),
                    null)).ConfigureAwait(false);

            Assert.That(raised, Is.Empty);
        }

        [Test]
        public async Task ASubscriberThatThrowsDoesNotBreakDispatchAsync()
        {
            // The publish pipeline is shared with everything else the client
            // does on that session, so a bad handler must not escape.
            ServerStatusMonitor monitor = CreateMonitor(
                _ => throw new InvalidOperationException("subscriber"));

            Assert.DoesNotThrowAsync(
                () => NotifyAsync(
                    monitor,
                    new DataValueChange(
                        new StubMonitoredItem(ServerStatusMonitor.ServerStatusItemName),
                        new DataValue(new ExtensionObject(new ServerStatusDataType())),
                        null)).AsTask());

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public void ToMillisecondsKeepsAnOrdinaryInterval()
        {
            Assert.That(
                ServerStatusMonitor.ToMilliseconds(TimeSpan.FromSeconds(5)),
                Is.EqualTo(5000));
        }

        [Test]
        public void ToMillisecondsClampsANegativeIntervalToZero()
        {
            // The option is documented as an interval, so a negative value is a
            // misconfiguration rather than the -1 sentinel of the service.
            Assert.That(
                ServerStatusMonitor.ToMilliseconds(TimeSpan.FromSeconds(-1)),
                Is.Zero);
        }

        [Test]
        public void ToMillisecondsClampsAnOversizedIntervalToIntMaxValue()
        {
            Assert.That(
                ServerStatusMonitor.ToMilliseconds(TimeSpan.FromDays(365)),
                Is.EqualTo(int.MaxValue));
        }

        private static ServerStatusMonitor CreateMonitor(
            Action<ServerStatusChangedEventArgs> onServerStatusChanged)
        {
            return new ServerStatusMonitor(
                new GdsClientOptions(),
                NullLogger<ServerStatusMonitorTests>.Instance,
                onServerStatusChanged);
        }

        private static ValueTask NotifyAsync(
            ServerStatusMonitor monitor,
            params DataValueChange[] changes)
        {
            return monitor.OnDataChangeNotificationAsync(
                null!,
                sequenceNumber: 1,
                publishTime: DateTime.UtcNow,
                notification: changes,
                publishStateMask: PublishState.None,
                stringTable: []);
        }

        /// <summary>
        /// Minimal <see cref="IMonitoredItem"/> carrying only the name the
        /// dispatch filter reads.
        /// </summary>
        private sealed class StubMonitoredItem : IMonitoredItem
        {
            public StubMonitoredItem(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public uint Order => 0;
            public uint ServerId => 0;
            public bool Created => true;
            public ServiceResult Error => ServiceResult.Good;
            public MonitoringFilterResult? FilterResult => null;
            public MonitoringMode CurrentMonitoringMode => MonitoringMode.Reporting;
            public TimeSpan CurrentSamplingInterval => TimeSpan.FromSeconds(1);
            public uint CurrentQueueSize => 1;
            public uint ClientHandle => 1;
            public IEnumerable<IMonitoredItem> TriggeringItems => [];
            public IEnumerable<IMonitoredItem> TriggeredItems => [];

            public ValueTask ConditionRefreshAsync(CancellationToken ct = default)
            {
                return default;
            }
        }
    }
}
