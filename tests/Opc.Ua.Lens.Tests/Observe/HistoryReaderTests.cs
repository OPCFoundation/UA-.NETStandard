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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Historian;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class HistoryReaderTests
{
    [TestCase("1234.5", 1234.5)]
    [TestCase("1,234.5", 1234.5)]
    public void HistoryRowPreservesInvariantNumericStrings(string text, double expected)
    {
        var row = new HistoryRow(s_timestamp, s_timestamp, Variant.From(text), StatusCodes.Good);
        Assert.That(row.IsNumeric, Is.True);
        Assert.That(row.Numeric, Is.EqualTo(expected));
        Assert.That(row.DisplayValue, Is.EqualTo(text));
        Assert.That(row.DisplayStatus, Is.EqualTo("Good"));
    }

    [Test]
    public void HistoryRowRejectsArraysAndNonFiniteNumericValues()
    {
        var array = new HistoryRow(
            s_timestamp, s_timestamp, new Variant(s_numericArray), StatusCodes.Good);
        var nonFinite = new HistoryRow(
            s_timestamp, s_timestamp, Variant.From(double.NaN), StatusCodes.Good);
        Assert.That(array.IsNumeric, Is.False);
        Assert.That(nonFinite.IsNumeric, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AnnotationCursorFailureOrCancellationReleasesIndependently(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        bool released = false;
        var nextEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = new TaskCompletionSource<HistoryReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ISession> session = Session((release, nodes, token) =>
        {
            if (release)
            {
                Assert.That(nodes[0].NodeId, Is.EqualTo(s_annotations));
                Assert.That(nodes[0].ContinuationPoint, Is.EqualTo(s_cursor));
                Assert.That(token, Is.Not.EqualTo(cancellation.Token));
                Assert.That(token.CanBeCanceled, Is.True);
                Assert.That(token.IsCancellationRequested, Is.False);
                released = true;
                return ValueTask.FromResult(Page());
            }
            if (nodes[0].NodeId == s_variable)
            {
                return ValueTask.FromResult(PrimaryPage());
            }
            if (nodes[0].ContinuationPoint.IsNull)
            {
                return ValueTask.FromResult(Page(s_cursor));
            }
            nextEntered.SetResult();
            return new ValueTask<HistoryReadResponse>(next.Task);
        });
        var reader = new HistoryReader(session.Object);
        List<HistoryRow> rows = await ReadAsync(reader, cancellation.Token).ConfigureAwait(false);
        Assert.That(rows, Has.Count.EqualTo(1));
        bool published = false;
        async Task ReadAnnotationsAndPublishAsync()
        {
            await reader.AttachAnnotationsAsync(s_variable, rows, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            published = true;
        }
        Task attach = ReadAnnotationsAndPublishAsync();
        await nextEntered.Task.ConfigureAwait(false);
        if (cancel)
        {
            cancellation.Cancel();
            next.SetCanceled(cancellation.Token);
            await Assert.ThatAsync(() => attach,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(published, Is.False);
        }
        else
        {
            next.SetException(new ServiceResultException(StatusCodes.BadHistoryOperationUnsupported));
            await attach.ConfigureAwait(false);
            Assert.That(published, Is.True);
        }
        Assert.That(released, Is.True);
        Assert.That(rows[0].Annotation, Is.Null);
    }

    [Test]
    public async Task UnsupportedAnnotationsPreserveSuccessfulPrimaryRead()
    {
        Mock<ISession> session = Session((_, nodes, _) => ValueTask.FromResult(
            nodes[0].NodeId == s_variable
                ? PrimaryPage()
                : new HistoryReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new HistoryReadResult { StatusCode = StatusCodes.BadHistoryOperationUnsupported }]
                }));
        var reader = new HistoryReader(session.Object);
        List<HistoryRow> rows = await ReadAsync(reader, CancellationToken.None).ConfigureAwait(false);
        await reader.AttachAnnotationsAsync(s_variable, rows, CancellationToken.None).ConfigureAwait(false);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Value.TryGetValue(out int value), Is.True);
        Assert.That(value, Is.EqualTo(42));
        Assert.That(rows[0].Annotation, Is.Null);
    }

    [Test]
    public async Task CancellationDuringAnnotationResolutionIsNotOptionalFeatureFailure()
    {
        Mock<ISession> session = Session((_, _, _) => ValueTask.FromResult(PrimaryPage()));
        using var cancellation = new CancellationTokenSource();
        session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, ArrayOf<BrowsePath> _, CancellationToken token) =>
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled<TranslateBrowsePathsToNodeIdsResponse>(token);
            });
        var reader = new HistoryReader(session.Object);
        List<HistoryRow> rows = await ReadAsync(reader, cancellation.Token).ConfigureAwait(false);
        await Assert.ThatAsync(() => reader.AttachAnnotationsAsync(
                s_variable, rows, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
    }

    [Test]
    public async Task PrimaryFailureAlsoReleasesItsCursor()
    {
        int reads = 0;
        bool released = false;
        Mock<ISession> session = Session((release, nodes, _) =>
        {
            if (release)
            {
                released = nodes[0].ContinuationPoint == s_cursor;
                return ValueTask.FromResult(Page());
            }
            if (++reads == 1)
            {
                return ValueTask.FromResult(Page(s_cursor));
            }
            throw new ServiceResultException(StatusCodes.BadCommunicationError);
        });
        await Assert.ThatAsync(() => ReadAsync(new HistoryReader(session.Object), CancellationToken.None),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
        Assert.That(released, Is.True);
    }

    [Test]
    public async Task CleanupDeadlineDoesNotReplaceTheOriginalReadFailure()
    {
        int reads = 0;
        bool releaseRequested = false;
        var release = new TaskCompletionSource<HistoryReadResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ISession> session = Session((cleanup, _, token) =>
        {
            if (cleanup)
            {
                releaseRequested = true;
                Assert.That(token.CanBeCanceled, Is.True);
                return new ValueTask<HistoryReadResponse>(release.Task);
            }
            if (++reads == 1)
            {
                return ValueTask.FromResult(Page(s_cursor));
            }
            throw new ServiceResultException(StatusCodes.BadCommunicationError);
        });
        try
        {
            var reader = new HistoryReader(session.Object, TimeSpan.Zero);
            await Assert.ThatAsync(() => ReadAsync(reader, CancellationToken.None),
                Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(releaseRequested, Is.True);
            Assert.That(release.Task.IsCompleted, Is.False);
        }
        finally
        {
            release.TrySetResult(Page());
        }
    }

    private static Task<List<HistoryRow>> ReadAsync(HistoryReader reader, CancellationToken token) =>
        reader.ReadRawAsync(s_variable, s_timestamp, s_timestamp.AddMinutes(1), false, false, 0, token);

    private static HistoryReadResponse PrimaryPage() => new()
    {
        ResponseHeader = new ResponseHeader(),
        Results =
        [
            new HistoryReadResult
            {
                HistoryData = new ExtensionObject(new HistoryData
                {
                    DataValues = [new DataValue(Variant.From(42), StatusCodes.Good, s_timestamp)]
                })
            }
        ]
    };

    private static HistoryReadResponse Page(ByteString cursor = default) => new()
    {
        ResponseHeader = new ResponseHeader(),
        Results = [new HistoryReadResult { ContinuationPoint = cursor }]
    };

    private static Mock<ISession> Session(
        Func<bool, ArrayOf<HistoryReadValueId>, CancellationToken, ValueTask<HistoryReadResponse>> read)
    {
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
        session.Setup(s => s.HistoryReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ExtensionObject>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<bool>(), It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, ExtensionObject _, TimestampsToReturn _, bool release,
                ArrayOf<HistoryReadValueId> nodes, CancellationToken token) => read(release, nodes, token));
        session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new TranslateBrowsePathsToNodeIdsResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results =
                [
                    new BrowsePathResult
                    {
                        Targets = [new BrowsePathTarget { TargetId = s_annotations }]
                    }
                ]
            }));
        return session;
    }

    private static readonly NodeId s_variable = new("variable", 2);
    private static readonly ArrayOf<int> s_numericArray = [1, 2];
    private static readonly NodeId s_annotations = new("annotations", 2);
    private static readonly ByteString s_cursor = new(new byte[] { 9, 8, 7 });
    private static readonly DateTime s_timestamp = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
}
