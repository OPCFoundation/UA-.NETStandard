// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.ViewModels;

[TestFixture]
public sealed class ReferencesViewModelTests
{
    [Test]
    public async Task OfflineSelectionAndClearReplaceTheEntireReferencePresentation()
    {
        await using var context = new DesktopConnectionContext();
        using var model = new ReferencesViewModel(
            context.Telemetry, context.Connection, InlineWorkspaceDispatcher.Instance);
        await model.LoadAsync(new NodeId("Boiler", 2), NodeClass.Object).ConfigureAwait(false);
        Assert.That(model.Header, Is.EqualTo("◉ ns=2;s=Boiler  (Object)"));
        Assert.That(model.Rows.Single(), Is.EqualTo(
            new ReferenceRow("·", "(disconnected)", string.Empty, string.Empty, string.Empty)));

        await model.LoadAsync(new NodeId("Temperature", 2), NodeClass.Variable).ConfigureAwait(false);

        Assert.That(model.Header, Is.EqualTo("○ ns=2;s=Temperature  (Variable)"));
        Assert.That(model.Rows.Single().ReferenceType, Is.EqualTo("(disconnected)"));
        Assert.That(model.Rows[0].TargetNodeId, Is.Empty);
        model.Clear();
        Assert.That(model.Header, Is.EqualTo("(no node selected)"));
        Assert.That(model.Rows, Is.Empty);
        Assert.That(context.ConfigurationsCreated, Is.Zero);
    }
}
