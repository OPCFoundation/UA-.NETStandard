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
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
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
    [Test]
    public void RegisteredAppUsesTheFirstLocalizedNameAndSkipsOnlyEmptyListEntries()
    {
        var record = new ApplicationRecordDataType
        {
            ApplicationId = new NodeId("press", 2),
            ApplicationNames = [new LocalizedText("en", "Press"), new LocalizedText("de", "Presse")],
            ApplicationUri = "urn:press:A",
            ProductUri = "urn:press:sku",
            ApplicationType = ApplicationType.ClientAndServer,
            DiscoveryUrls = [null!, string.Empty, "opc.tcp://press.test", "  ", "https://press.test"],
            ServerCapabilities = [string.Empty, "DA", null!, "HA"]
        };

        RegisteredApp app = RegisteredApp.FromRecord(record);

        Assert.That(app.Record, Is.SameAs(record));
        Assert.That(app.ApplicationId, Is.EqualTo(new NodeId("press", 2)));
        Assert.That(app.Identifier, Is.EqualTo("ns=2;s=press"));
        Assert.That(app.ApplicationName, Is.EqualTo("Press"));
        Assert.That(app.ApplicationUri, Is.EqualTo("urn:press:A"));
        Assert.That(app.ProductUri, Is.EqualTo("urn:press:sku"));
        Assert.That(app.ApplicationType, Is.EqualTo("ClientAndServer"));
        Assert.That(app.DiscoveryUrls, Is.EqualTo("opc.tcp://press.test\n  \nhttps://press.test"));
        Assert.That(app.ServerCapabilities, Is.EqualTo("DA, HA"));
        Assert.That(record.DiscoveryUrls.Count, Is.EqualTo(5));
    }

    [Test]
    public void MissingNamesAndUnresolvedDescriptionsHaveDifferentIdentitySemantics()
    {
        var description = new ApplicationDescription
        {
            ApplicationName = LocalizedText.Null,
            ApplicationUri = "urn:unresolved:viewer",
            ProductUri = null!,
            ApplicationType = ApplicationType.Client,
            DiscoveryUrls = [string.Empty, "opc.tcp://viewer.test"]
        };
        RegisteredApp unresolved = RegisteredApp.FromDescription(description);
        Assert.That(unresolved.ApplicationName, Is.EqualTo("urn:unresolved:viewer"));
        Assert.That(unresolved.ApplicationId.IsNull, Is.True);
        Assert.That(unresolved.Record, Is.Null);
        Assert.That(unresolved.Identifier, Is.EqualTo("(unresolved)"));
        Assert.That(unresolved.ProductUri, Is.Empty);
        Assert.That(unresolved.ApplicationType, Is.EqualTo("Client"));
        Assert.That(unresolved.DiscoveryUrls, Is.EqualTo("opc.tcp://viewer.test"));
        Assert.That(unresolved.ServerCapabilities, Is.Empty);

        RegisteredApp unnamed = RegisteredApp.FromRecord(new ApplicationRecordDataType
        {
            ApplicationId = new NodeId(7600),
            ApplicationUri = null!,
            ProductUri = null!,
            ApplicationNames = [],
            DiscoveryUrls = [],
            ServerCapabilities = []
        });
        Assert.That(unnamed.ApplicationName, Is.EqualTo("(unnamed)"));
        Assert.That(unnamed.ApplicationUri, Is.Empty);
        Assert.That(unnamed.ProductUri, Is.Empty);
        Assert.That(unnamed.DiscoveryUrls, Is.Empty);
        Assert.That(unnamed.ServerCapabilities, Is.Empty);
        Assert.That(unnamed.ApplicationId, Is.EqualTo(new NodeId(7600)));
        Assert.That(unnamed.Identifier, Is.EqualTo("i=7600"));
    }

    [Test]
    public async Task SelectionDetailsAndDisconnectClearGroupAndApplicationStateButKeepTheSharedContext()
    {
        await using var context = new AdministrationWorkflowContext();
        RegisteredApplicationContext current = RegistrationTestData.Create();
        context.Workspace.Object.CurrentRegisteredApp = current;
        await using var plugin = new GdsManagementPlugin(context.Host);
        RegisteredApp app = Press();
        plugin.AllApps.Add(app);
        plugin.FilteredApps.Add(app);
        plugin.CertGroups.Add(new GdsCertGroupVm
        {
            GroupId = new NodeId(7611),
            DisplayName = "Application certificates"
        });
        plugin.SelectedApp = app;
        Assert.That(plugin.SelectedAppDetail, Does.Contain("Application Name: Press Alpha")
            .And.Contain("Application URI : urn:cell:A")
            .And.Contain("NodeId          : i=7601")
            .And.Contain("Discovery URLs:")
            .And.Contain("opc.tcp://endpoint-only.test:4840")
            .And.Contain("Server Capabilities: DA, HA"));
        plugin.SelectedApp = null;
        Assert.That(plugin.SelectedAppDetail, Is.EqualTo("No application selected."));
        Assert.That(plugin.CertGroups, Is.Empty);
        plugin.SelectedApp = app;

        await plugin.DisconnectCommand.ExecuteAsync(null).ConfigureAwait(false);

        Assert.That(plugin.AllApps, Is.Empty);
        Assert.That(plugin.FilteredApps, Is.Empty);
        Assert.That(plugin.CertGroups, Is.Empty);
        Assert.That(plugin.SelectedApp, Is.Null);
        Assert.That(plugin.SelectedAppDetail, Is.EqualTo("No application selected."));
        Assert.That(plugin.LastOperationResult, Is.EqualTo("Disconnected."));
        Assert.That(plugin.Status, Is.EqualTo("● Disconnected · Disconnected."));
        Assert.That(plugin.IsBusy, Is.False);
        Assert.That(context.Workspace.Object.CurrentRegisteredApp, Is.SameAs(current));
        Assert.That(context.Host.Session, Is.Null);
    }
}
