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
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using Opc.Ua;
using UaLens.Subscriptions;

namespace UaLens.Tests.Desktop;

internal sealed class ControlledSubscriptionAdapter
{
    public ControlledSubscriptionAdapter()
    {
        Mock.SetupGet(a => a.Events).Returns(Input.Reader);
        Mock.SetupGet(a => a.Counters).Returns(Counters);
        Mock.SetupGet(a => a.Items).Returns(() => new ArrayOf<MonitoredItemConfig>(Items.ToArray()));
        Mock.SetupGet(a => a.CurrentPublishingInterval).Returns(() => Configuration.PublishingInterval);
        Mock.SetupGet(a => a.CurrentKeepAliveCount).Returns(() => Configuration.KeepAliveCount);
        Mock.SetupGet(a => a.CurrentLifetimeCount).Returns(() => Configuration.LifetimeCount);
        Mock.SetupGet(a => a.HasWorkerPool).Returns(true);
        Mock.Setup(a => a.ApplySubscriptionAsync(It.IsAny<SubscriptionConfig>(), It.IsAny<CancellationToken>()))
            .Returns((SubscriptionConfig config, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                Configuration = config;
                Applied.Add(config);
                return Task.CompletedTask;
            });
        Mock.Setup(a => a.AddItemAsync(It.IsAny<MonitoredItemConfig>(), It.IsAny<CancellationToken>()))
            .Returns((MonitoredItemConfig config, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                Added.Add(config);
                m_nextId = Math.Max(m_nextId, config.Id);
                int id = config.Id > 0 ? config.Id : checked(++m_nextId);
                if (!m_usedIds.Add(id) || Items.Exists(item => item.Id == id))
                {
                    throw new ArgumentException("The document item identity is already in use.", nameof(config));
                }
                MonitoredItemConfig assigned = config with { Id = id };
                Items.Add(assigned);
                return Task.FromResult(assigned.Id);
            });
        Mock.Setup(a => a.ConfigureItemAsync(It.IsAny<MonitoredItemConfig>(), It.IsAny<CancellationToken>()))
            .Returns((MonitoredItemConfig config, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                Configured.Add(config);
                int index = Items.FindIndex(item => item.Id == config.Id);
                Items[index] = config;
                return Task.CompletedTask;
            });
        Mock.Setup(a => a.SetMonitoringModeAsync(
                It.IsAny<int>(), It.IsAny<MonitoringMode>(), It.IsAny<CancellationToken>()))
            .Returns((int id, MonitoringMode mode, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                int index = Items.FindIndex(item => item.Id == id);
                if (index >= 0)
                {
                    Items[index] = Items[index] with { MonitoringMode = mode };
                }
                return Task.CompletedTask;
            });
        Mock.Setup(a => a.RemoveItemAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int id, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                Removed.Add(id);
                Items.RemoveAll(item => item.Id == id);
                return Task.CompletedTask;
            });
        Mock.Setup(a => a.DisposeAsync()).Returns(() =>
        {
            DisposeCount++;
            Input.Writer.TryComplete();
            return ValueTask.CompletedTask;
        });
    }

    public Mock<ISubscriptionAdapter> Mock { get; } = new();
    public ISubscriptionAdapter Object => Mock.Object;
    public Channel<NotificationEvent> Input { get; } = Channel.CreateUnbounded<NotificationEvent>();
    public SubscriptionCounters Counters { get; } = new();
    public SubscriptionConfig Configuration { get; private set; } = new();
    public List<MonitoredItemConfig> Items { get; } = [];
    public List<MonitoredItemConfig> Added { get; } = [];
    public List<MonitoredItemConfig> Configured { get; } = [];
    public List<int> Removed { get; } = [];
    public List<SubscriptionConfig> Applied { get; } = [];
    public int DisposeCount { get; private set; }

    private readonly HashSet<int> m_usedIds = [];
    private int m_nextId;
}
