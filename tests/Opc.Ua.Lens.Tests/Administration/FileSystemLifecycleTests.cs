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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.FileSystem;

namespace UaLens.Tests.Administration;

[TestFixture]
public sealed class FileSystemLifecycleTests
{
    [Test]
    public async Task ReconnectAttachesConfiguredRootsOncePerSession()
    {
        await using var context = new AdministrationTestContext();
        await using var plugin = new FileSystemPlugin(context.Host);
        await RestoreAsync(plugin).ConfigureAwait(false);
        Mock<ISession> session = Session(context.Host.Telemetry, (_, _) => ValueTask.FromResult(Page()));

        await plugin.AttachConfiguredRootsAsync(session.Object, CancellationToken.None).ConfigureAwait(false);
        await plugin.AttachConfiguredRootsAsync(session.Object, CancellationToken.None).ConfigureAwait(false);
        Assert.That(plugin.Roots, Has.Count.EqualTo(3));
        Assert.That(context.Connection.Session, Is.Null);
        foreach (FsNode root in plugin.Roots)
        {
            Assert.That(root.Client?.Session, Is.SameAs(session.Object));
        }
        await plugin.AttachConfiguredRootsAsync(null, CancellationToken.None).ConfigureAwait(false);
        Assert.That(plugin.Roots, Is.Empty);
        AssertRoots(plugin);
        await plugin.AttachConfiguredRootsAsync(session.Object, CancellationToken.None).ConfigureAwait(false);
        Assert.That(plugin.Roots, Has.Count.EqualTo(3));
        session.Verify(s => s.BrowseAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
            It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedOrCancelledAttachmentPreservesConfigurationForRetry(bool cancel)
    {
        await using var context = new AdministrationTestContext();
        await using var plugin = new FileSystemPlugin(context.Host);
        await RestoreAsync(plugin).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deferred = new TaskCompletionSource<BrowseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool fail = true;
        Mock<ISession> session = Session(context.Host.Telemetry, (nodes, _) =>
        {
            if (!fail || nodes[0].NodeId == new NodeId("Docs", 2))
            {
                return ValueTask.FromResult(Page());
            }
            entered.TrySetResult();
            return new ValueTask<BrowseResponse>(deferred.Task);
        });
        Task attachment = plugin.AttachConfiguredRootsAsync(session.Object, cancellation.Token);
        await entered.Task.ConfigureAwait(false);
        if (cancel)
        {
            cancellation.Cancel();
            deferred.SetResult(Page());
            await Assert.ThatAsync(async () => await attachment.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        }
        else
        {
            deferred.SetException(new ServiceResultException(StatusCodes.BadCommunicationError));
            await attachment.ConfigureAwait(false);
        }
        Assert.That(plugin.Roots, Has.Count.EqualTo(2));
        AssertRoots(plugin);
        fail = false;
        await plugin.AttachConfiguredRootsAsync(session.Object, CancellationToken.None).ConfigureAwait(false);
        Assert.That(plugin.Roots, Has.Count.EqualTo(3));
        AssertRoots(plugin);
    }

    private static Mock<ISession> Session(
        ITelemetryContext telemetry,
        Func<ArrayOf<BrowseDescription>, CancellationToken, ValueTask<BrowseResponse>> browse)
    {
        ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
        messageContext.NamespaceUris.GetIndexOrAppend("urn:ualens:test:namespace-padding");
        messageContext.NamespaceUris.GetIndexOrAppend("urn:ualens:test:file-system");
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.SetupGet(s => s.MessageContext).Returns(messageContext);
        session.SetupGet(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
        session.Setup(s => s.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> nodes,
                CancellationToken token) => browse(nodes, token));
        return session;
    }

    private static BrowseResponse Page() => new()
    {
        ResponseHeader = new ResponseHeader(),
        Results =
        [
            new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References =
                [
                    new ReferenceDescription
                    {
                        NodeId = ObjectTypeIds.FileDirectoryType,
                        NodeClass = NodeClass.ObjectType
                    }
                ]
            }
        ]
    };

    private static Task RestoreAsync(FileSystemPlugin plugin)
    {
        var state = new FileSystemState();
        state.Roots.Add(new FileSystemState.RootSpec { NodeId = "ns=2;s=Docs", DisplayName = "Documents" });
        state.Roots.Add(new FileSystemState.RootSpec { NodeId = "ns=2;s=Logs", DisplayName = "Server logs" });
        return plugin.RestoreStateAsync(
            JsonSerializer.SerializeToElement(state, FileSystemStateJsonContext.Default.FileSystemState));
    }

    private static void AssertRoots(FileSystemPlugin plugin)
    {
        FileSystemState captured = plugin.CaptureState()
            .Deserialize(FileSystemStateJsonContext.Default.FileSystemState)!;
        Assert.That(captured.Roots, Has.Count.EqualTo(2));
        Assert.That(captured.Roots[0].NodeId, Is.EqualTo("ns=2;s=Docs"));
        Assert.That(captured.Roots[0].DisplayName, Is.EqualTo("Documents"));
        Assert.That(captured.Roots[1].NodeId, Is.EqualTo("ns=2;s=Logs"));
        Assert.That(captured.Roots[1].DisplayName, Is.EqualTo("Server logs"));
    }
}
