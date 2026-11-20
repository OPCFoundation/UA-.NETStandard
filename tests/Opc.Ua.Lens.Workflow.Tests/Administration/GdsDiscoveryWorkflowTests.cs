// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
public sealed partial class GdsDiscoveryWorkflowTests
{

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
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
    [Category("LensDesktopWorkflow")]
    public Task QueryFilterAcceptTokenizesIndependentFieldsAndCancelLeavesTheOriginalUnchanged(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var original = new QueryServersFilter
            {
                ApplicationName = "Original name",
                ApplicationUri = "urn:original",
                ProductUri = "urn:old-product"
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
    [Category("LensDesktopWorkflow")]
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
