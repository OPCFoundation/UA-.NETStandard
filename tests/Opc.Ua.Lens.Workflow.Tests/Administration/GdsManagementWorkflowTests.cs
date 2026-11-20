/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds;
using UaLens.Plugins.Gds;
using UaLens.Plugins.GdsManagement;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[NonParallelizable]
public sealed partial class GdsManagementWorkflowTests
{

    [TestCase("  PRESS ALPHA ", 1)]
    [TestCase("URN:CELL:A", 1)]
    [TestCase("PRESS:SKU", 1)]
    [TestCase(" client ", 2)]
    [TestCase(" hA ", 1)]
    [TestCase("endpoint-only", 0)]
    [TestCase("  ", 3)]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task FilterMatchesTrimmedCaseInsensitiveDisplayFieldsButNotDiscoveryUrls(string query, int matching)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new GdsManagementPlugin(context.Host);
            RegisteredApp first = Press();
            RegisteredApp second = RegisteredApp.FromRecord(new ApplicationRecordDataType
            {
                ApplicationId = new NodeId(7602),
                ApplicationNames = [new LocalizedText("Viewer Beta")],
                ApplicationUri = "urn:viewer:beta",
                ProductUri = "urn:viewer:sku",
                ApplicationType = ApplicationType.Client,
                ServerCapabilities = ["RDA"]
            });
            plugin.AllApps.Add(first);
            plugin.AllApps.Add(second);
            plugin.FilteredApps.Add(new RegisteredApp { ApplicationId = new NodeId(7999) });
            NodeId[] expected = matching switch
            {
                1 => [new NodeId(7601)],
                2 => [new NodeId(7602)],
                3 => [new NodeId(7601), new NodeId(7602)],
                _ => []
            };
            await DesktopInteraction.CollectionChangedAsync(plugin.FilteredApps,
                () => plugin.FilteredApps.Select(a => a.ApplicationId).SequenceEqual(expected),
                () => plugin.FilterText = query).ConfigureAwait(true);

            Assert.That(plugin.FilteredApps.Select(a => a.ApplicationId), Is.EqualTo(expected));
            Assert.That(plugin.AllApps.Select(a => a.ApplicationName),
                Is.EqualTo(s_filterMatchesTrimmedCaseInsensitiveDisplayFieldsButNotDiscoveExpected));
            Assert.That(first.DiscoveryUrls, Is.EqualTo("opc.tcp://endpoint-only.test:4840"));
            Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
            Assert.That(context.ConnectionContext.Discoveries, Is.Empty);
        });
    }

    [Test]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task RegistrationPrefillsFromCurrentContextRatherThanSelectedQueryRowAndCancelDoesNotConnect()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            RegisteredApplicationContext current = RegistrationTestData.Create();
            context.Workspace.Object.CurrentRegisteredApp = current;
            await using var plugin = new GdsManagementPlugin(context.Host);
            RegisteredApp selected = Press();
            plugin.AllApps.Add(selected);
            plugin.SelectedApp = selected;
            Task operation = Task.CompletedTask;
            RegisterApplicationDialog dialog = await DesktopInteraction.OpenedAsync<RegisterApplicationDialog>(
                () => operation = plugin.RegisterApplicationCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text, Is.EqualTo("Assembly line"));
                Assert.That(DesktopInteraction.Control<TextBox>(dialog, "UriBox").Text,
                    Is.EqualTo("urn:fixture:assembly"));
                Assert.That(DesktopInteraction.Control<ComboBox>(dialog, "RegistrationTypeBox").SelectedIndex,
                    Is.EqualTo(2));
                Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
                DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text = "Discarded registration";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                await operation.ConfigureAwait(true);
                Assert.That(plugin.LastOperationResult, Is.EqualTo("Register cancelled."));
                Assert.That(plugin.Status, Is.EqualTo("● Disconnected · Register cancelled."));
                Assert.That(plugin.SelectedApp, Is.SameAs(selected));
                Assert.That(plugin.AllApps.Single(), Is.SameAs(selected));
                Assert.That(context.Workspace.Object.CurrentRegisteredApp, Is.SameAs(current));
                Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
                Assert.That(context.ConnectionContext.Discoveries, Is.Empty);
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task UnregisterCancelOrCloseKeepsTheResolvedApplicationAndStatesTheDestructiveScope(bool close)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new GdsManagementPlugin(context.Host);
            RegisteredApp selected = Press();
            plugin.AllApps.Add(selected);
            plugin.SelectedApp = selected;
            Task operation = Task.CompletedTask;
            Window dialog = await DesktopInteraction.OpenedAsync<Window>(
                () => operation = plugin.UnregisterApplicationCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                Assert.That(((DockPanel)dialog.Content!).Children.OfType<TextBlock>().Single().Text,
                    Does.Contain("Press Alpha").And.Contain("issued-certificate history")
                        .And.Contain("cannot be undone"));
                Button cancel = dialog.GetLogicalDescendants().OfType<Button>()
                    .Single(b => Equals(b.Content, "Cancel"));
                Assert.That(cancel.IsDefault, Is.True);
                Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
                if (close)
                {
                    dialog.Close();
                }
                else
                {
                    DesktopInteraction.Click(cancel);
                }
                await operation.ConfigureAwait(true);
                Assert.That(plugin.LastOperationResult, Is.EqualTo("Unregister cancelled."));
                Assert.That(plugin.AllApps.Single().ApplicationId, Is.EqualTo(new NodeId(7601)));
                Assert.That(plugin.SelectedApp, Is.SameAs(selected));
                Assert.That(plugin.IsBusy, Is.False);
                Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }

    private static readonly string[] s_filterMatchesTrimmedCaseInsensitiveDisplayFieldsButNotDiscoveExpected =
    [
        "Press Alpha",
        "Viewer Beta",
    ];
}
