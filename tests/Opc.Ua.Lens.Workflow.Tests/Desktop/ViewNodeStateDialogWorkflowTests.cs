/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[NonParallelizable]
public sealed partial class ViewNodeStateDialogWorkflowTests
{

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task NullOrDisconnectedNodeDisplaysSentinelWithoutRead(bool nullNode)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var dialog = new ViewNodeStateDialog(context.Browser, context.Connection,
                nullNode ? NodeId.Null : new NodeId(1234u, 2));
            Task shown = dialog.ShowDialog(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "HeaderBlock").Text,
                    Is.EqualTo(nullNode ? "NodeId: (none)" : "NodeId: ns=2;i=1234"));
                Assert.That(DesktopInteraction.Control<TreeView>(dialog, "StateTree").Items
                    .Cast<NodeStateItem>().Select(item => item.Header),
                    Is.EqualTo(s_nullOrDisconnectedNodeDisplaysSentinelWithoutReadExpected));
                Assert.That(context.ConfigurationsCreated, Is.Zero);
                Assert.That(context.Discoveries, Is.Empty);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CloseButton"));
                await shown.ConfigureAwait(true);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static readonly string[] s_nullOrDisconnectedNodeDisplaysSentinelWithoutReadExpected =
    [
        "(disconnected or null node)",
    ];
}
