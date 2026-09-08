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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Diagnostics;
using UaLens.Plugins.Gds;
using UaLens.Subscriptions;
using UaLens.Telemetry;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Workspace;

[TestFixture]
public sealed class PluginRegistryTests
{
    [Test]
    public void CatalogContainsEveryShippedKindWithUniqueCommandsAndShortcuts()
    {
        List<PluginRegistration> registrations = PluginRegistry.All.ToList();
        Assert.That(registrations, Has.Count.EqualTo(12));
        Assert.That(
            registrations.Select(registration => registration.Kind),
            Is.EquivalentTo(Enum.GetValues<PluginKind>()));
        var commands = new CommandRegistry(
            [.. registrations.Select(registration => new CommandDescriptor(
                registration.CommandId,
                CommandScope.Application,
                registration.DisplayName,
                registration.InputGesture,
                new RelayCommand(() => { })))]);

        Assert.That(commands.All.Count, Is.EqualTo(12));
        foreach (PluginRegistration registration in registrations)
        {
            Assert.That(PluginRegistry.For(registration.Kind), Is.SameAs(registration));
            Assert.That(registration.Description, Is.Not.Empty);
            Assert.That(registration.Factory, Is.Not.Null);
            Assert.That(registration.Glyph.All(char.IsAsciiLetterOrDigit), Is.True);
        }
        Assert.That(
            registrations.Select(registration => registration.Group).Distinct(),
            Is.EquivalentTo(Enum.GetValues<ToolGroup>()));
    }

    [Test]
    public void ConnectionRequirementsDistinguishLocalPrimaryAndSecondaryWork()
    {
        Assert.That(
            PluginRegistry.For(PluginKind.CertificateManager).ConnectionScope, Is.EqualTo(ToolConnectionScope.Local));
        Assert.That(
            PluginRegistry.For(PluginKind.GdsDiscovery).ConnectionScope, Is.EqualTo(ToolConnectionScope.Local));
        Assert.That(
            PluginRegistry.For(PluginKind.Subscription).ConnectionScope, Is.EqualTo(ToolConnectionScope.Primary));
        Assert.That(
            PluginRegistry.For(PluginKind.GdsManagement).ConnectionScope, Is.EqualTo(ToolConnectionScope.Secondary));
        Assert.That(
            PluginRegistry.For(PluginKind.GdsPush).ConnectionScope, Is.EqualTo(ToolConnectionScope.Secondary));
    }

    [Test]
    public async Task SubscriptionFactoryCreatesUsableConfigurationWithoutAConnectionOrView()
    {
        var telemetry = new AppTelemetryContext(new LogRingBuffer(capacity: 32));
        var connection = new ConnectionService(telemetry);
        await using (connection.ConfigureAwait(false))
        {
            var context = new TestPluginWorkspace();
            var host = new PluginHost(context, connection, new BrowserViewModel(telemetry, connection), telemetry);
            IPlugin plugin = PluginRegistry.For(PluginKind.Subscription).Factory(host);
            await using (plugin.ConfigureAwait(false))
            {
                Assert.That(plugin, Is.TypeOf<SubscriptionViewModel>());
                var monitor = (SubscriptionViewModel)plugin;
                monitor.Subscription = new SubscriptionConfig { PublishingInterval = TimeSpan.FromMilliseconds(250) };
                monitor.DisplayModeIndex = 1;
                monitor.Items.Add(new MonitoredItemConfig
                {
                    NodeId = new NodeId(1234u),
                    DisplayName = "Configured offline"
                });

                Assert.That(monitor.IsBound, Is.False);
                Assert.That(monitor.Adapter, Is.Null);
                Assert.That(monitor.Subscription.PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
                Assert.That(monitor.Items.Single().NodeId, Is.EqualTo(new NodeId(1234u)));
                Assert.That(SubscriptionDocumentState.Capture(monitor).DisplayModeIndex, Is.EqualTo(1));
                Assert.That(host.Session, Is.Null);
                Assert.That(connection.IsConnected, Is.False);
            }
        }
    }

    [Test]
    public void LegacyImportKeepsNodeAndSubscriptionIntentWithoutServerHandles()
    {
        var saved = new SessionFile.TabSnapshot
        {
            Title = "Saved monitor",
            PublishingInterval = new SessionFile.TimeSpanMs(200),
            KeepAliveCount = 7,
            LifetimeCount = 100,
            PublishingEnabled = false,
            Items =
            [
                new SessionFile.ItemSnapshot
                {
                    NodeId = "ns=2;s=Temperature",
                    DisplayName = "Temperature",
                    SamplingInterval = new SessionFile.TimeSpanMs(50),
                    QueueSize = 9,
                    DiscardOldest = false,
                    MonitoringMode = (byte)MonitoringMode.Sampling
                }
            ]
        };

        SubscriptionDocumentState imported = SubscriptionDocumentState.Import(saved);
        Assert.That(imported.Title, Is.EqualTo("Saved monitor"));
        Assert.That(imported.Subscription.PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(200)));
        Assert.That(imported.Subscription.PublishingEnabled, Is.False);
        Assert.That(imported.Subscription.KeepAliveCount, Is.EqualTo(7));
        Assert.That(imported.Items.Count, Is.EqualTo(1));
        Assert.That(imported.Items[0].Id, Is.Zero);
        Assert.That(imported.Items[0].NodeId, Is.EqualTo(new NodeId("Temperature", 2)));
        Assert.That(imported.Items[0].QueueSize, Is.EqualTo(9));
        Assert.That(imported.Items[0].DiscardOldest, Is.False);
        Assert.That(imported.Items[0].MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
        SessionFile.TabSnapshot exported = imported.Export();
        Assert.That(exported.Items[0].NodeId, Is.EqualTo("ns=2;s=Temperature"));
        Assert.That(exported.Items[0].SamplingInterval.Milliseconds, Is.EqualTo(50));
    }

    private sealed class TestPluginWorkspace : ObservableObject, IPluginWorkspace
    {
        public IPlugin? ActiveDocument => null;
        public string EndpointUrl { get; set; } = string.Empty;
        public RegisteredApplicationContext? CurrentRegisteredApp { get; set; }
        public NodeViewModel? SelectedNode => null;
        public bool IsAddressSpaceVisible => false;
        public ResourceMonitorHost? ResourceMonitor => null;

        public Task<IPlugin> OpenToolAsync(
            PluginKind kind,
            EndpointDescription? discoveryEndpoint = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This catalog test does not open cooperating tools.");
        }
    }
}
