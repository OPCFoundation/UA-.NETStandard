// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.ViewModels;

[TestFixture]
[NonParallelizable]
public sealed partial class BrowserViewModelWorkflowTests
{

    [Test]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task CanceledOrUnchangedViewSelectionDoesNotDiscardExistingTreeButNewKindReloads()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            BrowserViewModel browser = context.Browser;
            var root = new NodeViewModel(browser, NodeId.Null, ObjectIds.RootFolder, "Retained root", NodeClass.Object);
            browser.Roots.Add(root);
            await browser.SetViewKindAsync(BrowseViewKind.Objects, default).ConfigureAwait(true);
            Assert.That(browser.Roots.Single(), Is.SameAs(root));
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(true);

            Assert.That(() => browser.SetViewKindAsync(BrowseViewKind.DataTypes, cancellation.Token),
                Throws.InstanceOf<System.OperationCanceledException>());

            Assert.That(browser.CurrentViewKind, Is.EqualTo(BrowseViewKind.Objects));
            Assert.That(browser.Roots.Single(), Is.SameAs(root));
            await browser.SetViewKindAsync(BrowseViewKind.DataTypes, default).ConfigureAwait(true);
            Assert.That(browser.CurrentViewKind, Is.EqualTo(BrowseViewKind.DataTypes));
            Assert.That(browser.Roots, Is.Empty);
            Assert.That(context.ConfigurationsCreated, Is.Zero);
        });
    }
}
