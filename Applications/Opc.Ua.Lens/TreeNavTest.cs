/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using UaLens.Connection;
using UaLens.Subscriptions;
using UaLens.Telemetry;
using UaLens.ViewModels;

namespace UaLens;

/// <summary>
/// Headless validator for the address-space tree's lazy-load behaviour and
/// the EventNotifier validation used by AddItemDialog.  Run with
/// <c>--testtree</c>.
/// </summary>
/// <remarks>
/// This test does NOT bring up Avalonia — it exercises BrowserViewModel
/// directly against a running OPC UA server. It guards the user-reported bug
/// where second-level nodes could not be expanded because their
/// <c>Children</c> collection was empty (no placeholder), so Avalonia's
/// TreeView never rendered an expand chevron.
/// </remarks>
internal static class TreeNavTest
{
    public static async Task<int> RunAsync(string endpoint)
    {
        Console.WriteLine("== TreeNavTest ==");
        Console.WriteLine($"   endpoint: {endpoint}");

        var logBuf = new Telemetry.LogRingBuffer(1024);
        var telemetry = new AppTelemetryContext(logBuf);
        var connection = new ConnectionService(telemetry);
        await using (connection.ConfigureAwait(false))
        {

            try
            {
                await connection.ConnectAsync(
                    new ConnectionOptions
                    {
                        EndpointUrl = endpoint,
                        UseSecurity = false,
                        Engine = SubscriptionEngineKind.ChannelV2
                    },
                    CancellationToken.None).ConfigureAwait(false);

                var browser = new BrowserViewModel(telemetry, connection);

                // ----- Step 1: trigger Reload manually (StateChanged would normally do it) -----
                ReloadRoots(browser);

                if (browser.Roots.Count != 1)
                {
                    Console.WriteLine($"FAIL: expected 1 root (RootFolder), got {browser.Roots.Count}");
                    return 2;
                }

                Console.WriteLine($"  roots loaded:                                {browser.Roots.Count}");
                Console.WriteLine($"  root[0] starts with placeholder:             {HasPlaceholderOnly(browser.Roots[0])}");

                // Root is auto-expanded in Reload — wait for its load to complete.
                NodeViewModel rootFolder = browser.Roots[0];

                await WaitForRealChildrenAsync(rootFolder, browser, "RootFolder").ConfigureAwait(false);

                Console.WriteLine($"  root[0] real children:                        {rootFolder.Children.Count}");

                // Note: with Aggregates-only browsing, RootFolder may legitimately
                // have zero children (Objects/Types/Views are linked via Organizes).
                // The interesting structural test is below: navigate to the Server
                // node directly which is rich in HasComponent children.
                int placeholderChildren = rootFolder.Children.Count(c => HasPlaceholderOnly(c));
                Console.WriteLine($"  RootFolder children that show a chevron:      {placeholderChildren} / {rootFolder.Children.Count}");
                if (rootFolder.Children.Count > 0 && placeholderChildren != rootFolder.Children.Count)
                {
                    Console.WriteLine("FAIL: some children of RootFolder have no placeholder — chevron will not render → cannot expand.");
                    return 2;
                }

                // ----- Step 2: build a Server (i=2253) test target via the
                // browser's normal LoadChildrenAsync path.  Server has rich
                // HasComponent children so it's a good Aggregates-target.
                var serverNode = new NodeViewModel(browser, NodeId.Null, ObjectIds.Server,
                    "◉ Server (test)", NodeClass.Object);
                // Trigger LoadChildrenAsync via IsExpanded.
                serverNode.IsExpanded = true;
                await WaitForRealChildrenAsync(serverNode, browser, "Server (test)").ConfigureAwait(false);
                Console.WriteLine($"  Server children loaded:                       {serverNode.Children.Count}");
                if (serverNode.Children.Count == 0)
                {
                    Console.WriteLine("FAIL: Server should have HasComponent children under Aggregates.");
                    return 2;
                }
                int serverWithChevron = serverNode.Children.Count(c => HasPlaceholderOnly(c));
                Console.WriteLine($"  Server children that show a chevron:          {serverWithChevron} / {serverNode.Children.Count}");
                if (serverWithChevron != serverNode.Children.Count)
                {
                    Console.WriteLine("FAIL: some Server children have no placeholder — chevron will not render → cannot expand.");
                    return 2;
                }

                // Expand a Server child to validate second-level expansion.
                NodeViewModel? secondLevel = serverNode.Children.FirstOrDefault(c => !c.IsPlaceholder);
                if (secondLevel is null)
                {
                    Console.WriteLine("FAIL: no second-level node available to test");
                    return 2;
                }

                Console.WriteLine($"  expanding second-level node: {secondLevel.NodeId} ({secondLevel.NodeClass})");
                secondLevel.IsExpanded = true;
                await WaitForRealChildrenAsync(secondLevel, browser, $"second-level {secondLevel.NodeId}").ConfigureAwait(false);

                int grand = secondLevel.Children.Count;
                int withPlaceholder = secondLevel.Children.Count(c => HasPlaceholderOnly(c));
                Console.WriteLine($"  grandchildren under {secondLevel.NodeId}:        {grand}");
                Console.WriteLine($"  grandchildren that themselves show a chevron: {withPlaceholder}");

                if (grand == 0)
                {
                    Console.WriteLine($"WARN: second-level node {secondLevel.NodeId} returned no grandchildren — picking another one if possible…");
                    NodeViewModel? alt = serverNode.Children.FirstOrDefault(c => !c.IsPlaceholder && !ReferenceEquals(c, secondLevel));
                    if (alt is not null)
                    {
                        alt.IsExpanded = true;
                        await WaitForRealChildrenAsync(alt, browser, $"alt second-level {alt.NodeId}").ConfigureAwait(false);
                        grand = alt.Children.Count;
                        Console.WriteLine($"  alt grandchildren under {alt.NodeId}:    {grand}");
                    }
                }

                // ----- Step 3: EventNotifier read for the Server object -----
                byte? notifier = await browser.GetEventNotifierAsync(ObjectIds.Server).ConfigureAwait(false);
                Console.WriteLine($"  Server EventNotifier:                         {(notifier.HasValue ? $"0x{notifier.Value:X2}" : "<null>")}");
                if (notifier is null)
                {
                    Console.WriteLine("FAIL: could not read Server EventNotifier.");
                    return 2;
                }
                bool subscribesToEvents = (notifier.Value & EventNotifiers.SubscribeToEvents) != 0;
                Console.WriteLine($"  SubscribeToEvents bit:                        {subscribesToEvents}");
                if (!subscribesToEvents)
                {
                    Console.WriteLine("FAIL: Server.EventNotifier should have SubscribeToEvents set on the reference server.");
                    return 2;
                }

                // ----- Step 4: dedup assertion — every expanded node's children
                // must have a unique NodeId.
                int dupTotal = CountDuplicateChildren(serverNode);
                foreach (NodeViewModel child in serverNode.Children)
                {
                    if (!child.IsPlaceholder)
                    {
                        dupTotal += CountDuplicateChildren(child);
                    }
                }
                Console.WriteLine($"  duplicate child NodeIds (Server+1 level):     {dupTotal}");
                if (dupTotal > 0)
                {
                    Console.WriteLine("FAIL: duplicate NodeIds detected — Aggregates dedup broken.");
                    return 2;
                }

                Console.WriteLine();
                Console.WriteLine("TREE NAV PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 2;
            }
        }
    }

    private static int CountDuplicateChildren(NodeViewModel n)
    {
        var seen = new HashSet<Opc.Ua.NodeId>();
        int dup = 0;
        foreach (NodeViewModel c in n.Children)
        {
            if (c.IsPlaceholder)
            {
                continue;
            }
            if (!seen.Add(c.NodeId))
            {
                dup++;
            }
        }
        return dup;
    }

    private static bool HasPlaceholderOnly(NodeViewModel n)
        => n.Children.Count == 1 && n.Children[0].IsPlaceholder;

    private static async Task WaitForRealChildrenAsync(NodeViewModel n, BrowserViewModel browser, string label)
    {
        // With no Avalonia App running, BrowserViewModel applies child updates
        // synchronously inline (PostToUiAsync detects null Application.Current).
        // The roots are loaded from Reload() via IsExpanded=true on a fire-and-forget
        // task, so we wait briefly for it to complete.
        for (int i = 0; i < 100 && HasPlaceholderOnly(n); i++)
        {
            await Task.Delay(30).ConfigureAwait(false);
        }
        Console.WriteLine($"   '{label}' has {n.Children.Count} real children, placeholder removed: {!HasPlaceholderOnly(n)}");
    }

    /// <summary>
    /// Mirror of <see cref="BrowserViewModel.Reload"/> — but the test runs without a
    /// real OPC UA <c>StateChanged</c> dispatch, so we trigger it directly.  This
    /// uses reflection to keep <c>Reload</c> private to the production code.
    /// </summary>
    private static void ReloadRoots(BrowserViewModel browser)
    {
        System.Reflection.MethodInfo? reload = typeof(BrowserViewModel)
            .GetMethod("Reload", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        reload?.Invoke(browser, null);
    }
}
