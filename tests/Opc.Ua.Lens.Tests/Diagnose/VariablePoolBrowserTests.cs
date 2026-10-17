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
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Tests.Companions;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class VariablePoolBrowserTests
{
    [Test]
    public async Task PageTwoVariableAndFolderDescendantsMatchSinglePagePool()
    {
        var paged = Tree(true);
        var single = Tree(false);
        var browser = new VariablePoolBrowser(paged.Telemetry);
        VariablePoolDiscovery first = await browser.BrowseAsync(
            paged.Session.Object, s_root, CancellationToken.None).ConfigureAwait(false);
        VariablePoolDiscovery second = await browser.BrowseAsync(
            single.Session.Object, s_root, CancellationToken.None).ConfigureAwait(false);
        Assert.That(first.IncompleteReason, Is.Null);
        Assert.That(first.Variables.ToList(), Is.EquivalentTo(second.Variables.ToList()));
        Assert.That(first.Variables.ToList().Select(value => value.NodeId).ToArray(),
            Is.EquivalentTo(new[] { s_first, s_second, s_descendant }));
        Assert.That(paged.Released, Has.Count.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedOrCancelledBrowseNextReleasesCursor(bool cancel)
    {
        var session = Tree(true);
        using var cancellation = new CancellationTokenSource();
        bool independentCleanup = false;
        session.NextHandler = (release, _, token) =>
        {
            if (release)
            {
                independentCleanup = !token.IsCancellationRequested && token != cancellation.Token;
                return new BrowseNextResponse { ResponseHeader = new ResponseHeader(), Results = [] };
            }
            if (cancel)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            throw new ServiceResultException(StatusCodes.BadCommunicationError);
        };
        var browser = new VariablePoolBrowser(session.Telemetry);
        if (cancel)
        {
            await Assert.ThatAsync(() => browser.BrowseAsync(
                    session.Session.Object, s_root, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        }
        else
        {
            VariablePoolDiscovery result = await browser.BrowseAsync(
                session.Session.Object, s_root, cancellation.Token).ConfigureAwait(false);
            Assert.That(result.IncompleteReason, Does.Contain("Browse failed"));
            Assert.That(result.Variables, Has.Count.EqualTo(1));
        }
        Assert.That(independentCleanup, Is.True);
        Assert.That(session.Released.Contains(s_cursor), Is.True);
    }

    [Test]
    public async Task NodeBudgetReportsPartialPoolAndReleasesCursor()
    {
        var session = Tree(true);
        session.NextHandler = (_, _, _) => new BrowseNextResponse
        {
            ResponseHeader = new ResponseHeader(),
            Results = [new BrowseResult { References = [Reference(s_second)], ContinuationPoint = s_cursor }]
        };
        var browser = new VariablePoolBrowser(session.Telemetry, maxNodes: 2);
        VariablePoolDiscovery result = await browser.BrowseAsync(
            session.Session.Object, s_root, CancellationToken.None).ConfigureAwait(false);
        Assert.That(result.IncompleteReason, Does.Contain("node budget"));
        Assert.That(result.Variables, Has.Count.EqualTo(1));
        Assert.That(session.Released.Contains(s_cursor), Is.True);
    }

    [Test]
    public async Task DepthBudgetReportsIncompleteDiscovery()
    {
        var session = Tree(false);
        var browser = new VariablePoolBrowser(session.Telemetry, maxDepth: 1);
        VariablePoolDiscovery result = await browser.BrowseAsync(
            session.Session.Object, s_root, CancellationToken.None).ConfigureAwait(false);
        Assert.That(result.IncompleteReason, Does.Contain("depth limit"));
        Assert.That(result.Variables.ToList().Select(value => value.NodeId).ToArray(),
            Does.Not.Contain(s_descendant));
    }

    private static IndustrialCompanionTestSession Tree(bool paged)
    {
        var session = new IndustrialCompanionTestSession();
        session.BrowseHandler = (description, _) =>
        {
            if (description.NodeId == s_root)
            {
                return paged
                    ? new BrowseResult { References = [Reference(s_first)], ContinuationPoint = s_cursor }
                    : new BrowseResult
                    {
                        References = [Reference(s_first), Reference(s_second), Reference(s_folder, NodeClass.Object)]
                    };
            }
            return description.NodeId == s_folder
                ? new BrowseResult { References = [Reference(s_descendant), Reference(s_second)] }
                : new BrowseResult();
        };
        session.NextHandler = (_, _, _) => new BrowseNextResponse
        {
            ResponseHeader = new ResponseHeader(),
            Results =
            [
                new BrowseResult
                {
                    References = [Reference(s_second), Reference(s_folder, NodeClass.Object)]
                }
            ]
        };
        return session;
    }

    private static ReferenceDescription Reference(NodeId nodeId, NodeClass nodeClass = NodeClass.Variable) => new()
    {
        NodeId = nodeId,
        NodeClass = nodeClass,
        DisplayName = new LocalizedText(nodeId.ToString())
    };

    private static readonly NodeId s_root = new("root", 2);
    private static readonly NodeId s_first = new("first", 2);
    private static readonly NodeId s_second = new("second", 2);
    private static readonly NodeId s_folder = new("folder", 2);
    private static readonly NodeId s_descendant = new("descendant", 2);
    private static readonly ByteString s_cursor = new(new byte[] { 1, 2, 3 });
}
