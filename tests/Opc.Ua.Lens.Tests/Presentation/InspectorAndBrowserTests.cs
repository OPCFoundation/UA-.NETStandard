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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Telemetry;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Presentation;

[TestFixture]
public sealed class InspectorAndBrowserTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task AttributeWorkerCompletionPublishesOnlyThroughDispatcher(bool fail)
    {
        var dispatcher = new GuardedDispatcher();
        var pending = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        ArrayOf<ReadValueId> requested = [];
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.Setup(value => value.ReadAsync(
            null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .Callback<RequestHeader?, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                (_, _, _, ids, _) => requested = ids)
            .Returns(new ValueTask<ReadResponse>(pending.Task));
        var model = new NodeAttributesViewModel(Telemetry(), () => session.Object, dispatcher);
        model.Rows.CollectionChanged += (_, _) => dispatcher.VerifyAccess();
        Task loading = Task.CompletedTask;
        await dispatcher.InvokeAsync(() =>
        {
            loading = model.LoadAsync(new NodeId("node", 0), NodeClass.Object);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        await Task.Run(() =>
        {
            if (fail)
            {
                pending.SetException(new ServiceResultException(StatusCodes.BadTimeout));
            }
            else
            {
                pending.SetResult(new ReadResponse
                {
                    Results = requested.ToList().Select(_ => new DataValue(Variant.From("value"))).ToArray()
                });
            }
        }).ConfigureAwait(false);
        await loading.ConfigureAwait(false);
        Assert.That(model.Rows, Has.Count.EqualTo(fail ? 1 : requested.Count));
        Assert.That(model.Rows[0].Name, fail ? Is.EqualTo("(read failed)") : Is.Not.EqualTo("(read failed)"));
        await dispatcher.InvokeAsync(() =>
        {
            model.Dispose();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AttributeOldCompletionCannotReplaceNewSelectionOrClear(bool clear)
    {
        var dispatcher = new GuardedDispatcher();
        var first = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.SetupSequence(value => value.ReadAsync(
            null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ReadResponse>(first.Task))
            .ThrowsAsync(new ServiceResultException(StatusCodes.BadUserAccessDenied));
        var model = new NodeAttributesViewModel(Telemetry(), () => session.Object, dispatcher);
        model.Rows.CollectionChanged += (_, _) => dispatcher.VerifyAccess();
        Task old = Task.CompletedTask;
        await dispatcher.InvokeAsync(() =>
        {
            old = model.LoadAsync(new NodeId("old", 0), NodeClass.Object);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        await dispatcher.InvokeAsync(() => model.LoadAsync(new NodeId("new", 0), NodeClass.Object))
            .ConfigureAwait(false);
        await dispatcher.InvokeAsync(() =>
        {
            if (clear)
            {
                model.Clear();
            }
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        first.SetException(new ServiceResultException(StatusCodes.BadTimeout));
        await old.ConfigureAwait(false);
        Assert.That(model.Rows, Has.Count.EqualTo(clear ? 0 : 1));
        if (!clear)
        {
            Assert.That(model.Rows[0].Value, Does.Contain("BadUserAccessDenied"));
            Assert.That(model.Header, Does.Contain("new"));
        }
        await dispatcher.InvokeAsync(() =>
        {
            model.Dispose();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task ReferenceWorkerReadCannotPublishOutsideDispatcherOrAfterClear(bool fail, bool clear)
    {
        var dispatcher = new GuardedDispatcher();
        var pending = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = BrowseSession();
        session.Setup(value => value.BrowseAsync(
            null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowseResponse
            {
                Results = [new BrowseResult { References = [Reference("child")] }]
            });
        session.Setup(value => value.ReadAsync(
            null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ReadResponse>(pending.Task));
        var model = new ReferencesViewModel(Telemetry(), () => session.Object, dispatcher);
        model.Rows.CollectionChanged += (_, _) => dispatcher.VerifyAccess();
        Task loading = Task.CompletedTask;
        await dispatcher.InvokeAsync(() =>
        {
            loading = model.LoadAsync(new NodeId("node", 0), NodeClass.Object);
            if (clear)
            {
                model.Clear();
            }
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        await Task.Run(() =>
        {
            if (fail)
            {
                pending.SetException(new ServiceResultException(StatusCodes.BadTimeout));
            }
            else
            {
                pending.SetResult(new ReadResponse { Results = [new DataValue(Variant.From(new QualifiedName("type")))] });
            }
        }).ConfigureAwait(false);
        await loading.ConfigureAwait(false);
        Assert.That(model.Rows, Has.Count.EqualTo(clear ? 0 : 1));
        if (!clear)
        {
            Assert.That(model.Rows[0].ReferenceType, Is.EqualTo(fail ? "(browse failed)" : "type"));
        }
        await dispatcher.InvokeAsync(() =>
        {
            model.Dispose();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedBrowseCanRetryWithoutRebuildingSuccessfulBranches(bool badResult)
    {
        var session = BrowseSession();
        var response = new BrowseResponse { Results = [new BrowseResult { References = [Reference("child")] }] };
        var sequence = session.SetupSequence(value => value.BrowseAsync(
            null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()));
        if (badResult)
        {
            sequence.ReturnsAsync(new BrowseResponse
            {
                Results = [new BrowseResult { StatusCode = StatusCodes.BadTimeout }]
            });
        }
        else
        {
            sequence.ThrowsAsync(new ServiceResultException(StatusCodes.BadTimeout));
        }
        sequence.ReturnsAsync(response);
        var model = new BrowserViewModel(Telemetry(), () => session.Object, InlineWorkspaceDispatcher.Instance)
        {
            CurrentViewKind = BrowseViewKind.ObjectTypes
        };
        var node = new NodeViewModel(model, NodeId.Null, ObjectIds.ObjectTypesFolder, "types", NodeClass.Object);
        await model.LoadChildrenAsync(node).ConfigureAwait(false);
        Assert.That(node.ChildrenLoaded, Is.False);
        Assert.That(node.HasLoadError, Is.True);
        Assert.That(node.HasItems, Is.True);
        await node.RetryCommand.ExecuteAsync(null).ConfigureAwait(false);
        Assert.That(node.ChildrenLoaded, Is.True);
        Assert.That(node.HasLoadError, Is.False);
        Assert.That(node.Children.Single().Text, Is.EqualTo("child"));
        NodeViewModel child = node.Children[0];
        await model.LoadChildrenAsync(node).ConfigureAwait(false);
        Assert.That(node.Children[0], Is.SameAs(child));
        session.Verify(value => value.BrowseAsync(
            null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ContinuationFailureKeepsPartialResultAndReleasesCursorBeforeRetry(bool badResult)
    {
        ByteString cursor = new(new byte[] { 1, 2 });
        var session = BrowseSession();
        session.SetupSequence(value => value.BrowseAsync(
            null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowseResponse
            {
                Results = [new BrowseResult { References = [Reference("first")], ContinuationPoint = cursor }]
            })
            .ReturnsAsync(new BrowseResponse
            {
                Results = [new BrowseResult { References = [Reference("first"), Reference("second")] }]
            });
        if (badResult)
        {
            session.Setup(value => value.BrowseNextAsync(
                null, false, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = [new BrowseResult { StatusCode = StatusCodes.BadTimeout }]
                });
        }
        else
        {
            session.Setup(value => value.BrowseNextAsync(
                null, false, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadTimeout));
        }
        session.Setup(value => value.BrowseNextAsync(
            null, true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowseNextResponse());
        var model = new BrowserViewModel(Telemetry(), () => session.Object, InlineWorkspaceDispatcher.Instance)
        {
            CurrentViewKind = BrowseViewKind.ObjectTypes
        };
        var node = new NodeViewModel(model, NodeId.Null, ObjectIds.ObjectTypesFolder, "types", NodeClass.Object);
        await model.LoadChildrenAsync(node).ConfigureAwait(false);
        Assert.That(node.ChildrenLoaded, Is.False);
        Assert.That(node.LoadError, Does.StartWith("Incomplete"));
        Assert.That(node.Children.Single().Text, Is.EqualTo("first"));
        session.Verify(value => value.BrowseNextAsync(
            null, true, It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == cursor),
            It.Is<CancellationToken>(token => !token.IsCancellationRequested)), Times.Once);
        await model.LoadChildrenAsync(node).ConfigureAwait(false);
        Assert.That(node.Children.Select(child => child.Text), Is.EqualTo(s_expectedChildren));
        Assert.That(node.ChildrenLoaded, Is.True);
    }

    [Test]
    public async Task PendingBrowseIsSingleFlightAndSuccessfulEmptyNodeIsNotFailure()
    {
        var pending = new TaskCompletionSource<BrowseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = BrowseSession();
        session.Setup(value => value.BrowseAsync(
            null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<BrowseResponse>(pending.Task));
        var model = new BrowserViewModel(Telemetry(), () => session.Object, InlineWorkspaceDispatcher.Instance)
        {
            CurrentViewKind = BrowseViewKind.ObjectTypes
        };
        var node = new NodeViewModel(model, NodeId.Null, ObjectIds.ObjectTypesFolder, "types", NodeClass.Object);
        Task first = model.LoadChildrenAsync(node);
        await model.LoadChildrenAsync(node).ConfigureAwait(false);
        Assert.That(node.IsLoading, Is.True);
        pending.SetResult(new BrowseResponse { Results = [new BrowseResult()] });
        await first.ConfigureAwait(false);
        Assert.That(node.IsLoading, Is.False);
        Assert.That(node.ChildrenLoaded, Is.True);
        Assert.That(node.HasItems, Is.False);
        Assert.That(node.HasLoadError, Is.False);
        session.Verify(value => value.BrowseAsync(
            null, null, 0, It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AppTelemetryContext Telemetry() => new(new LogRingBuffer());

    private static Mock<ISession> BrowseSession()
    {
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.SetupGet(value => value.NamespaceUris).Returns(new NamespaceTable());
        return session;
    }

    private static ReferenceDescription Reference(string name) => new()
    {
        NodeId = new ExpandedNodeId(name, 0),
        ReferenceTypeId = ReferenceTypeIds.HasComponent,
        DisplayName = new LocalizedText(name),
        BrowseName = new QualifiedName(name),
        NodeClass = NodeClass.Object
    };

    private sealed class GuardedDispatcher : IWorkspaceDispatcher
    {
        public void VerifyAccess()
        {
            Assert.That(s_depth, Is.GreaterThan(0), "Bound state was changed outside its dispatcher.");
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            s_depth++;
            Task result;
            try
            {
                result = action();
            }
            finally
            {
                s_depth--;
            }
            return result;
        }

        [ThreadStatic]
        private static int s_depth;
    }

    private static readonly string[] s_expectedChildren = ["first", "second"];
}
