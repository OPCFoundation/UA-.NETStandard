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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using UaLens.Connection;
using UaLens.Plugins.GdsDiscovery;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class DiscoveryProviderWorkflowTests
{
    [TestCase((int)DiscoveryRootKind.LocalMachine, false)]
    [TestCase((int)DiscoveryRootKind.LocalMachine, true)]
    [TestCase((int)DiscoveryRootKind.LocalNetwork, false)]
    [TestCase((int)DiscoveryRootKind.GlobalDiscovery, false)]
    [TestCase((int)DiscoveryRootKind.GlobalDiscovery, true)]
    public Task DiscoveryRootsProjectActualProvidersWithoutOpeningAPrimarySession(int kind, bool empty)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            using var files = new TemporaryCertificateStores();
            var lds = new Mock<ILocalDiscoveryServerClient>();
            var gds = new Mock<IGlobalDiscoveryServerClient>();
            ArrayOf<ApplicationDescription> applications = empty ? [] :
            [
                new ApplicationDescription
                {
                    ApplicationName = new LocalizedText("Machine server"),
                    ApplicationUri = "urn:fixture:machine", DiscoveryUrls = [kEndpoint]
                }
            ];
            ArrayOf<ServerOnNetwork> network = empty ? [] :
            [
                new ServerOnNetwork { ServerName = "Network server", DiscoveryUrl = kEndpoint }
            ];
            lds.Setup(client => client.FindServersAsync(
                It.IsAny<string>(), null, CancellationToken.None)).ReturnsAsync(applications);
            lds.Setup(client => client.FindServersOnNetworkAsync(0, 100, CancellationToken.None))
                .ReturnsAsync((network, DateTimeUtc.MinValue));
            gds.Setup(client => client.QueryServersAsync(
                100, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ArrayOf<string>>(), CancellationToken.None)).ReturnsAsync(network);
            lds.Setup(client => client.DisposeAsync()).Returns(ValueTask.CompletedTask);
            gds.Setup(client => client.DisposeAsync()).Returns(ValueTask.CompletedTask);
            await using var plugin = new GdsDiscoveryPlugin(
                context.Host, _ => lds.Object, _ => gds.Object, Path.Combine(files.Root, "favorites.json"));
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            DiscoveryNode root = plugin.Roots.Single(root => root.RootKind == (DiscoveryRootKind)kind);
            plugin.SelectedNode = root;
            await plugin.RefreshSelectedRootAsync().ConfigureAwait(true);
            Assert.That(root.Children, Is.Not.Empty);
            Assert.That(root.Children[0].Display,
                Is.EqualTo(empty ? "(no servers)" : kind == (int)DiscoveryRootKind.LocalMachine
                    ? "Machine server" : "Network server"));
            if (!empty)
            {
                Assert.That(root.Children[0].EndpointUrl, Is.EqualTo(kEndpoint));
            }
            if ((DiscoveryRootKind)kind == DiscoveryRootKind.LocalNetwork)
            {
                Assert.That(root.Children[^1].Display, Is.EqualTo("mDNS hosts (this machine)"));
                Assert.That(root.Children[^1].Children[0].Display, Does.Contain("Listing local network interfaces"));
            }
            Assert.That(context.ConnectionContext.Connection.CurrentSession, Is.Null);
            Assert.That(plugin.Status, Does.Contain("entry/entries"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task FavoriteWorkflowPersistsOnlyOwnedStateAndProjectsEndpointSecurity(bool malformed)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            using var files = new TemporaryCertificateStores();
            string path = Path.Combine(files.Root, "favorites.json");
            if (malformed)
            {
                await File.WriteAllTextAsync(path, "{broken").ConfigureAwait(true);
            }
            else
            {
                await FavoritesStore.SaveAsync(s_saved, path: path).ConfigureAwait(true);
            }
            var lds = new Mock<ILocalDiscoveryServerClient>();
            lds.Setup(client => client.GetEndpointsAsync(kEndpoint, CancellationToken.None)).ReturnsAsync(
                (ArrayOf<EndpointDescription>)
                [
                    new(kEndpoint) {
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None },
                    new(kEndpoint)
                    {
                        SecurityMode = MessageSecurityMode.SignAndEncrypt,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ]);
            lds.Setup(client => client.DisposeAsync()).Returns(ValueTask.CompletedTask);
            await using var plugin = new GdsDiscoveryPlugin(context.Host, _ => lds.Object,
                _ => throw new AssertionException("No GDS query is needed."), path);
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            if (malformed)
            {
                Assert.That(plugin.Status, Does.StartWith("● Favourites could not be read:"));
                Assert.That(await File.ReadAllTextAsync(path).ConfigureAwait(true), Is.EqualTo("{broken"));
                return;
            }
            DiscoveryNode custom = plugin.Roots.Single(root => root.RootKind == DiscoveryRootKind.CustomDiscovery);
            Assert.That(custom.Children.Single().IsFavorite, Is.True);
            await DesktopInteraction.ModelChangedAsync(plugin, () => plugin.Endpoints.Count == 2, () =>
            {
                plugin.SelectedNode = custom.Children[0];
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            Assert.That(plugin.Endpoints[1].SecurityMode, Is.EqualTo("SignAndEncrypt"));
            Assert.That(plugin.Endpoints[1].SecurityProfile, Is.EqualTo("Basic256Sha256"));
            plugin.SelectedEndpoint = plugin.Endpoints[1];
            plugin.ConnectSelectedToConnectionPane();
            Assert.That(context.Workspace.Object.EndpointUrl, Is.EqualTo(kEndpoint));
            Assert.That(context.ConnectionContext.Connection.CurrentSession, Is.Null);
            await plugin.AddCurrentToFavouritesAsync().ConfigureAwait(true);
            Assert.That(plugin.Status, Does.Contain("already in favourites"));
            await plugin.RemoveFromFavouritesAsync().ConfigureAwait(true);
            Assert.That(custom.Children, Is.Empty);
            Assert.That(await FavoritesStore.LoadAsync(path: path).ConfigureAwait(true), Is.Empty);
            await plugin.AddCurrentToFavouritesAsync().ConfigureAwait(true);
            Assert.That(await FavoritesStore.LoadAsync(path: path).ConfigureAwait(true), Is.EqualTo(s_saved));
        });
    }

    [Test]
    public Task LateEndpointResponseCannotOverwriteTheNewlySelectedServer()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            using var files = new TemporaryCertificateStores();
            var lds = new Mock<ILocalDiscoveryServerClient>();
            var old = new TaskCompletionSource<ArrayOf<EndpointDescription>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lds.Setup(client => client.GetEndpointsAsync("opc.tcp://old.test:4840", CancellationToken.None))
                .Returns(() =>
                {
                    entered.TrySetResult();
                    return new ValueTask<ArrayOf<EndpointDescription>>(old.Task);
                });
            lds.Setup(client => client.GetEndpointsAsync(kEndpoint, CancellationToken.None))
                .ReturnsAsync((ArrayOf<EndpointDescription>)[new(kEndpoint)]);
            lds.Setup(client => client.DisposeAsync()).Returns(ValueTask.CompletedTask);
            await using var plugin = new GdsDiscoveryPlugin(context.Host, _ => lds.Object,
                _ => throw new AssertionException("No GDS query is needed."), Path.Combine(
                    files.Root,
                    "favorites.json"));
            plugin.SelectedNode = new DiscoveryNode { Endpoint = new EndpointDescription("opc.tcp://old.test:4840") };
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            await DesktopInteraction.ModelChangedAsync(plugin, () => plugin.Endpoints.Count == 1, () =>
            {
                plugin.SelectedNode = new DiscoveryNode { Endpoint = new EndpointDescription(kEndpoint) };
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            old.SetResult([new EndpointDescription("opc.tcp://old.test:4840")]);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.That(plugin.Endpoints.Single().Url, Is.EqualTo(kEndpoint));
        });
    }

    private const string kEndpoint = "opc.tcp://fixture.test:4840";
    private static readonly string[] s_saved = [kEndpoint];
}
