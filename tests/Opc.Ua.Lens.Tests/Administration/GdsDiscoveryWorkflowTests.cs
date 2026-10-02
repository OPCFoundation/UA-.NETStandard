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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.GdsDiscovery;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.Administration;

[TestFixture]
[NonParallelizable]
public sealed class GdsDiscoveryWorkflowTests
{
    [Test]
    public async Task ConnectSelectionRoutesTheDisplayedEndpointWithoutDiscoveringOrConnecting()
    {
        await using var context = new AdministrationWorkflowContext();
        context.Workspace.Object.EndpointUrl = "opc.tcp://old.test:4840";
        await using var plugin = new GdsDiscoveryPlugin(context.Host);
        EndpointDescription endpoint = Endpoint();
        plugin.SelectedEndpoint = Row(endpoint);

        plugin.ConnectSelectedToConnectionPane();

        Assert.That(context.Workspace.Object.EndpointUrl, Is.EqualTo("opc.tcp://displayed.test:4841"));
        Assert.That(plugin.Status, Is.EqualTo("● opc.tcp://displayed.test:4841 → Connection pane."));
        Assert.That(plugin.Roots.Select(r => r.RootKind), Is.EqualTo(new DiscoveryRootKind?[]
        {
            DiscoveryRootKind.LocalMachine, DiscoveryRootKind.LocalNetwork,
            DiscoveryRootKind.GlobalDiscovery, DiscoveryRootKind.CustomDiscovery
        }));
        Assert.That(plugin.Roots.All(r => r.Children.Count == 0 && !r.IsExpanded), Is.True);
        Assert.That(plugin.SelectedNode, Is.Null);
        Assert.That(plugin.Endpoints, Is.Empty);
        Assert.That(context.ConnectionContext.Discoveries, Is.Empty);
        Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
        context.Workspace.Verify(w => w.OpenToolAsync(
            It.IsAny<PluginKind>(), It.IsAny<EndpointDescription?>(), It.IsAny<CancellationToken>()), Times.Never);
        plugin.SelectedEndpoint = null;
        plugin.ConnectSelectedToConnectionPane();
        Assert.That(plugin.Status, Is.EqualTo("● Pick a server or endpoint first."));
        Assert.That(context.Workspace.Object.EndpointUrl, Is.EqualTo("opc.tcp://displayed.test:4841"));
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task OpenAsRoutesTheExactSecureDescriptorToTheRequestedTool(bool management)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new GdsDiscoveryPlugin(context.Host);
            EndpointDescription endpoint = Endpoint();
            plugin.SelectedEndpoint = Row(endpoint);
            PluginKind kind = management ? PluginKind.GdsManagement : PluginKind.GdsPush;
            var target = new Mock<IPlugin>(MockBehavior.Strict);
            context.Workspace.Setup(w => w.OpenToolAsync(kind, endpoint, CancellationToken.None))
                .ReturnsAsync(target.Object);
            MenuItem item = plugin.ContributeMenuItems().Single(i =>
                Equals(i.Header, management ? "Open as _Management…" : "Open as _Push…"));

            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            context.Workspace.Verify(w => w.OpenToolAsync(kind, endpoint, CancellationToken.None), Times.Once);
            Assert.That(context.Workspace.Object.EndpointUrl, Is.EqualTo("opc.tcp://wire.test:4842"));
            Assert.That(endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(endpoint.UserIdentityTokens[0].PolicyId, Is.EqualTo("operator-certificate"));
            Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
            Assert.That(context.ConnectionContext.Discoveries, Is.Empty);
            plugin.SelectedEndpoint = null;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(plugin.Status, Is.EqualTo("● Pick a server or endpoint first."));
            context.Workspace.Verify(w => w.OpenToolAsync(kind, endpoint, CancellationToken.None), Times.Once);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task QueryFilterAcceptTokenizesIndependentFieldsAndCancelLeavesTheOriginalUnchanged(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var original = new QueryServersFilter
            {
                ApplicationName = "Original name", ApplicationUri = "urn:original", ProductUri = "urn:old-product"
            };
            original.ServerCapabilities.Add("OriginalCapability");
            var dialog = new QueryServersFilterDialog(original);
            Task<QueryServersFilter?> prompt = dialog.ShowDialog<QueryServersFilter?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                Assert.That(DesktopInteraction.Control<TextBox>(dialog, "CapabilitiesBox").Text,
                    Is.EqualTo("OriginalCapability"));
                DesktopInteraction.Control<TextBox>(dialog, "AppNameBox").Text = "  Assembly*  ";
                DesktopInteraction.Control<TextBox>(dialog, "AppUriBox").Text = "  ";
                DesktopInteraction.Control<TextBox>(dialog, "ProductUriBox").Text = "  urn:fixture:product  ";
                DesktopInteraction.Control<TextBox>(dialog, "CapabilitiesBox").Text = " DA,  , HA,,DA ";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(
                    dialog, accept ? "OkButton" : "CancelButton"));
                QueryServersFilter? result = await prompt.ConfigureAwait(true);
                if (accept)
                {
                    Assert.That(result!.ApplicationName, Is.EqualTo("Assembly*"));
                    Assert.That(result.ApplicationUri, Is.Null);
                    Assert.That(result.ProductUri, Is.EqualTo("urn:fixture:product"));
                    Assert.That(
                        result.ServerCapabilities,
                        Is.EqualTo(s_queryFilterAcceptTokenizesIndependentFieldsAndCancelLeavesTheExpected));
                    Assert.That(result, Is.Not.SameAs(original));
                }
                else
                {
                    Assert.That(result, Is.Null);
                }
                Assert.That(original.ApplicationName, Is.EqualTo("Original name"));
                Assert.That(original.ApplicationUri, Is.EqualTo("urn:original"));
                Assert.That(original.ProductUri, Is.EqualTo("urn:old-product"));
                Assert.That(
                    original.ServerCapabilities,
                    Is.EqualTo(s_queryFilterAcceptTokenizesIndependentFieldsAndCancelLeavesTheExpected2));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task EmptyQueryFilterAcceptsAnUnrestrictedQueryWithoutManufacturingCapabilityTokens()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new QueryServersFilterDialog(new QueryServersFilter());
            Task<QueryServersFilter?> prompt = dialog.ShowDialog<QueryServersFilter?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "CapabilitiesBox").Text = "  ";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                QueryServersFilter result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.ApplicationName, Is.Null);
                Assert.That(result.ApplicationUri, Is.Null);
                Assert.That(result.ProductUri, Is.Null);
                Assert.That(result.ServerCapabilities, Is.Empty);
                Assert.That(dialog.IsVisible, Is.False);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    private static EndpointDescription Endpoint()
    {
        return new EndpointDescription
        {
            EndpointUrl = "opc.tcp://wire.test:4842",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Certificate) { PolicyId = "operator-certificate" }],
            Server = new ApplicationDescription { ApplicationUri = "urn:fixture:server" }
        };
    }

    private static DiscoveryEndpointRow Row(EndpointDescription endpoint)
    {
        return new DiscoveryEndpointRow
        {
            Url = "opc.tcp://displayed.test:4841", SecurityMode = "SignAndEncrypt",
            SecurityProfile = "Basic256Sha256", Endpoint = endpoint
        };
    }

    private static readonly string[] s_queryFilterAcceptTokenizesIndependentFieldsAndCancelLeavesTheExpected =
    [
        "DA",
        "HA",
        "DA",
    ];
    private static readonly string[] s_queryFilterAcceptTokenizesIndependentFieldsAndCancelLeavesTheExpected2 =
    [
        "OriginalCapability",
    ];
}
