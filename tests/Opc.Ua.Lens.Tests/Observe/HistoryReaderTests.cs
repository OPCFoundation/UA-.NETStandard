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
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Historian;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class HistoryReaderTests
{
    private static readonly DateTime s_start = new(2026, 3, 4, 5, 6, 7, 123, DateTimeKind.Utc);
    private static readonly NodeId s_node = new("Boiler.Temperature", 2);

    [TestCase("null", "", false, 0)]
    [TestCase("integer", "-17", true, -17)]
    [TestCase("double", "12.75", true, 12.75)]
    [TestCase("numericText", "4.25", true, 4.25)]
    [TestCase("text", "boiler ready", false, 0)]
    [TestCase("boolean", "True", true, 1)]
    [TestCase("nan", "NaN", false, 0)]
    [TestCase("infinity", "Infinity", false, 0)]
    public void HistoryRowPreservesStatusTimestampsAndNumericProjection(
        string kind, string display, bool numeric, double number)
    {
        Variant value = kind switch
        {
            "integer" => new Variant(-17),
            "double" => new Variant(12.75),
            "numericText" => new Variant("4.25"),
            "text" => new Variant("boiler ready"),
            "boolean" => new Variant(true),
            "nan" => new Variant(double.NaN),
            "infinity" => new Variant(double.PositiveInfinity),
            _ => Variant.Null
        };
        var row = new HistoryRow(s_start, s_start.AddSeconds(2), value, StatusCodes.BadOutOfRange);

        Assert.That(row.DisplayTimestamp, Is.EqualTo("2026-03-04T05:06:07.123Z"));
        Assert.That(row.ServerTimestamp, Is.EqualTo(s_start.AddSeconds(2)));
        Assert.That(row.Value, Is.EqualTo(value));
        Assert.That(row.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
        Assert.That(row.DisplayStatus, Is.EqualTo("BadOutOfRange"));
        Assert.That(row.DisplayValue, Is.EqualTo(display));
        Assert.That(row.IsNumeric, Is.EqualTo(numeric));
        Assert.That(row.Numeric, numeric ? Is.EqualTo(number) : Is.NaN);
    }

    [TestCase("", "校正済み", "校正済み")]
    [TestCase("operator", "校正済み", "校正済み  —  operator")]
    [TestCase("operator", "", "  —  operator")]
    public void AnnotationChangesNotifyBothProjectionsOnlyWhenReferenceChanges(
        string author, string message, string display)
    {
        var row = new HistoryRow(s_start, s_start, new Variant(24), 0x01230000);
        var changes = new List<string?>();
        row.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        var annotation = new Annotation { UserName = author, Message = message, AnnotationTime = s_start };

        row.Annotation = annotation;
        row.Annotation = annotation;

        Assert.That(row.DisplayAnnotation, Is.EqualTo(display));
        Assert.That(changes, Is.EqualTo(s_annotationChangesNotifyBothProjectionsOnlyWhenReferenceChangeExpected));
        Assert.That(row.DisplayStatus, Is.EqualTo("0x01230000"));
        row.Annotation = null;
        Assert.That(row.DisplayAnnotation, Is.Empty);
        Assert.That(row.Numeric, Is.EqualTo(24));
        Assert.That(changes, Is.EqualTo(s_annotationChangesNotifyBothProjectionsOnlyWhenReferenceChangeExpected2));
    }

    [TestCase(false, false, 0u)]
    [TestCase(false, true, 1u)]
    [TestCase(true, false, 25u)]
    [TestCase(true, true, 0u)]
    public async Task RawAndModifiedReadsSendExactDetailsAndConcatenatePages(
        bool modified, bool bounds, uint limit)
    {
        var driver = new HistoryProtocolTestDriver();
        using var cancellation = new CancellationTokenSource();
        driver.Read = _ => new ValueTask<HistoryReadResponse>(driver.Reads.Count == 1
            ? HistoryProtocolTestDriver.Page(
                [Value(12.5, 0, StatusCodes.Good), Value(-3, 1, StatusCodes.BadOutOfRange)],
                ByteString.From([0x31, 0xC4]), modified)
            : HistoryProtocolTestDriver.Page([Value(19, 2, StatusCodes.Uncertain)], modified: modified));

        List<HistoryRow> rows = await new HistoryReader(driver.Session.Object).ReadRawAsync(
            s_node, s_start, s_start.AddMinutes(5), bounds, modified, limit, cancellation.Token)
            .ConfigureAwait(false);

        AssertRows(rows);
        Assert.That(driver.Reads, Has.Count.EqualTo(2));
        for (int i = 0; i < driver.Reads.Count; i++)
        {
            HistoryProtocolTestDriver.ReadCall call = driver.Reads[i];
            AssertRead(call, cancellation.Token);
            var details = WorkflowAssertions.GetEncodeable<ReadRawModifiedDetails>(call.Details);
            Assert.That(details, Is.TypeOf<ReadRawModifiedDetails>());
            Assert.That((DateTime)details.StartTime, Is.EqualTo(s_start));
            Assert.That((DateTime)details.EndTime, Is.EqualTo(s_start.AddMinutes(5)));
            Assert.That(details.NumValuesPerNode, Is.EqualTo(limit));
            Assert.That(details.ReturnBounds, Is.EqualTo(bounds));
            Assert.That(details.IsReadModified, Is.EqualTo(modified));
            Assert.That(call.Nodes[0].ContinuationPoint.ToArray(),
                Is.EqualTo(i == 0 ? Array.Empty<byte>() : new byte[] { 0x31, 0xC4 }));
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public async Task ProcessedReadPreservesAggregateRangeAndIntervalAcrossPages(int pages)
    {
        var driver = new HistoryProtocolTestDriver();
        using var cancellation = new CancellationTokenSource();
        driver.Read = _ => new ValueTask<HistoryReadResponse>(HistoryProtocolTestDriver.Page(
            pages == 0 ? [] : [Value(driver.Reads.Count * 10, driver.Reads.Count, StatusCodes.Good)],
            driver.Reads.Count < pages ? ByteString.From([(byte)driver.Reads.Count]) : default));
        var aggregate = new NodeId(Objects.AggregateFunction_Maximum);

        List<HistoryRow> rows = await new HistoryReader(driver.Session.Object).ReadProcessedAsync(
            s_node, aggregate, s_start, s_start.AddHours(1), 2500, cancellation.Token).ConfigureAwait(false);

        Assert.That(rows.Select(row => row.Numeric), Is.EqualTo(Enumerable.Range(1, pages).Select(i => i * 10)));
        Assert.That(driver.Reads, Has.Count.EqualTo(Math.Max(1, pages)));
        for (int i = 0; i < driver.Reads.Count; i++)
        {
            HistoryProtocolTestDriver.ReadCall call = driver.Reads[i];
            AssertRead(call, cancellation.Token);
            var details = WorkflowAssertions.GetEncodeable<ReadProcessedDetails>(call.Details);
            Assert.That(details.AggregateType.ToArray(), Is.EqualTo(new[] { aggregate }));
            Assert.That(details.ProcessingInterval, Is.EqualTo(2500));
            Assert.That((DateTime)details.StartTime, Is.EqualTo(s_start));
            Assert.That((DateTime)details.EndTime, Is.EqualTo(s_start.AddHours(1)));
            Assert.That(details.AggregateConfiguration.UseServerCapabilitiesDefaults, Is.False);
            Assert.That(call.Nodes[0].ContinuationPoint.ToArray(),
                Is.EqualTo(i == 0 ? Array.Empty<byte>() : new[] { (byte)i }));
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    public async Task AtTimeReadPreservesRequestedTimestampOrder(int count)
    {
        DateTime[] requested = new[] { s_start.AddSeconds(8), s_start, s_start, s_start.AddSeconds(3) }
            .Take(count).ToArray();
        var driver = new HistoryProtocolTestDriver();
        using var cancellation = new CancellationTokenSource();
        driver.Read = _ => new ValueTask<HistoryReadResponse>(driver.Reads.Count == 1
            ? HistoryProtocolTestDriver.Page([Value(-7, 0, StatusCodes.Good)], ByteString.From([1]))
            : HistoryProtocolTestDriver.Page([Value(3, 8, StatusCodes.BadNoData)]));

        List<HistoryRow> rows = await new HistoryReader(driver.Session.Object)
            .ReadAtTimeAsync(s_node, requested, cancellation.Token).ConfigureAwait(false);

        Assert.That(rows.Select(row => row.Numeric), Is.EqualTo(new[] { -7d, 3d }));
        Assert.That(rows[1].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
        Assert.That(rows[1].SourceTimestamp, Is.EqualTo(s_start.AddSeconds(8)));
        Assert.That(driver.Reads, Has.Count.EqualTo(2));
        foreach (HistoryProtocolTestDriver.ReadCall call in driver.Reads)
        {
            AssertRead(call, cancellation.Token);
            var details = WorkflowAssertions.GetEncodeable<ReadAtTimeDetails>(call.Details);
            Assert.That(details.ReqTimes.IsNull, Is.False);
            Assert.That(details.ReqTimes.ToList().Select(time => (DateTime)time), Is.EqualTo(requested));
            Assert.That(details.UseSimpleBounds, Is.True);
        }
    }

    [TestCase("emptyResults")]
    [TestCase("emptyData")]
    [TestCase("nullPayload")]
    [TestCase("unrelatedPayload")]
    [TestCase("emptyContinuation")]
    public async Task ReadWithoutContinuationDoesNotIssueRelease(string responseKind)
    {
        var driver = new HistoryProtocolTestDriver();
        HistoryReadResponse response = responseKind switch
        {
            "emptyResults" => new HistoryReadResponse(),
            "nullPayload" => new HistoryReadResponse { Results = [new HistoryReadResult()] },
            "unrelatedPayload" => new HistoryReadResponse
            {
                Results = [new HistoryReadResult { HistoryData = new ExtensionObject(new Annotation()) }]
            },
            "emptyContinuation" => HistoryProtocolTestDriver.Page([], ByteString.From(Array.Empty<byte>())),
            _ => HistoryProtocolTestDriver.Page([])
        };
        driver.Read = _ => new ValueTask<HistoryReadResponse>(response);

        List<HistoryRow> rows = await ReadModeAsync(new HistoryReader(driver.Session.Object), "raw", default)
            .ConfigureAwait(false);

        Assert.That(rows, Is.Empty);
        Assert.That(driver.Reads, Has.Count.EqualTo(1));
        Assert.That(driver.Reads[0].Release, Is.False);
        Assert.That(driver.Reads[0].Nodes[0].ContinuationPoint.IsNull, Is.True);
    }

    [TestCase("raw")]
    [TestCase("modified")]
    [TestCase("processed")]
    [TestCase("atTime")]
    public async Task PreCanceledPrimaryReadSendsNoRequest(string mode)
    {
        var driver = new HistoryProtocolTestDriver();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);

        await Assert.ThatAsync(() => ReadModeAsync(new HistoryReader(driver.Session.Object), mode, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(driver.Reads, Is.Empty);
        Assert.That(driver.Translations, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BadStatusAndServiceFaultPreserveOriginalErrorWithoutInventingRelease(bool serviceFault)
    {
        var driver = new HistoryProtocolTestDriver();
        var failure = new ServiceResultException(StatusCodes.BadCommunicationError, "history link lost");
        driver.Read = _ => driver.Reads.Count == 1
            ? new ValueTask<HistoryReadResponse>(HistoryProtocolTestDriver.Page(
                [Value(9, 0, StatusCodes.Good)], ByteString.From([2, 3])))
            : serviceFault
                ? ValueTask.FromException<HistoryReadResponse>(failure)
                : new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                {
                    Results = [new HistoryReadResult { StatusCode = StatusCodes.BadHistoryOperationUnsupported }]
                });

        if (serviceFault)
        {
            await Assert.ThatAsync(() => ReadModeAsync(new HistoryReader(driver.Session.Object), "raw", default),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
        }
        else
        {
            await Assert.ThatAsync(() => ReadModeAsync(new HistoryReader(driver.Session.Object), "raw", default),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadHistoryOperationUnsupported))
                .ConfigureAwait(false);
        }
        Assert.That(driver.Reads, Has.Count.EqualTo(2));
        Assert.That(driver.Reads.All(call => !call.Release), Is.True);
        Assert.That(driver.Reads[1].Nodes[0].ContinuationPoint.ToArray(), Is.EqualTo(new byte[] { 2, 3 }));
    }

    [TestCase("raw", false)]
    [TestCase("modified", true)]
    [TestCase("processed", false)]
    [TestCase("atTime", true)]
    public async Task CancelWithOutstandingContinuationReleasesOnceWithoutMaskingCancellation(
        string mode, bool failRelease)
    {
        var driver = new HistoryProtocolTestDriver();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HistoryReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new OperationCanceledException("canceled history page", cancellation.Token);
        driver.Read = call =>
        {
            if (call.Release)
            {
                return failRelease
                    ? ValueTask.FromException<HistoryReadResponse>(new InvalidOperationException("release failed"))
                    : new ValueTask<HistoryReadResponse>(new HistoryReadResponse());
            }
            if (driver.Reads.Count == 1)
            {
                return new ValueTask<HistoryReadResponse>(HistoryProtocolTestDriver.Page(
                    [Value(4, 0, StatusCodes.Good)], ByteString.From([0xA1, 0x09])));
            }
            entered.SetResult();
            return new ValueTask<HistoryReadResponse>(pending.Task);
        };

        Task<List<HistoryRow>> read = ReadModeAsync(new HistoryReader(driver.Session.Object), mode, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        pending.SetException(failure);
        await Assert.ThatAsync(() => read, Throws.Exception.SameAs(failure)).ConfigureAwait(false);

        Assert.That(driver.Reads, Has.Count.EqualTo(3));
        HistoryProtocolTestDriver.ReadCall release = driver.Reads[2];
        Assert.That(release.Release, Is.True);
        Assert.That(release.Token, Is.EqualTo(CancellationToken.None));
        Assert.That(release.Timestamps, Is.EqualTo(TimestampsToReturn.Both));
        Assert.That(release.Nodes.Single().NodeId, Is.EqualTo(s_node));
        Assert.That(release.Nodes[0].ContinuationPoint.ToArray(), Is.EqualTo(new byte[] { 0xA1, 0x09 }));
        Assert.That(release.Details, Is.EqualTo(driver.Reads[0].Details));
        Assert.That(driver.Reads[1].Token, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public async Task CancellationBetweenPagesReleasesTheMostRecentContinuation()
    {
        var driver = new HistoryProtocolTestDriver();
        using var cancellation = new CancellationTokenSource();
        driver.Read = call =>
        {
            if (call.Release)
            {
                return new ValueTask<HistoryReadResponse>(new HistoryReadResponse());
            }
            if (driver.Reads.Count == 2)
            {
                cancellation.Cancel();
            }
            return new ValueTask<HistoryReadResponse>(HistoryProtocolTestDriver.Page(
                [Value(driver.Reads.Count, 0, StatusCodes.Good)], ByteString.From([(byte)driver.Reads.Count])));
        };

        await Assert.ThatAsync(() => ReadModeAsync(
            new HistoryReader(driver.Session.Object), "atTime", cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(driver.Reads, Has.Count.EqualTo(3));
        Assert.That(driver.Reads[2].Release, Is.True);
        Assert.That(driver.Reads[2].Nodes[0].ContinuationPoint.ToArray(), Is.EqualTo(new byte[] { 2 }));
        Assert.That(driver.Reads[2].Token.CanBeCanceled, Is.False);
    }

    [Test]
    public async Task AnnotationLookupUsesMappedPropertyAndPaddedRange()
    {
        var driver = new HistoryProtocolTestDriver();
        using var cancellation = new CancellationTokenSource();
        var property = new NodeId("Boiler.Annotations", 2);
        driver.Translate = _ => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
            new TranslateBrowsePathsToNodeIdsResponse
            {
                Results = [new BrowsePathResult
                {
                    Targets = [new BrowsePathTarget
                    {
                        TargetId = ExpandedNodeId.Parse("nsu=urn:history:test:plant;s=Boiler.Annotations"),
                        RemainingPathIndex = uint.MaxValue
                    }]
                }]
            });
        var late = new Annotation { Message = "late point", UserName = "A", AnnotationTime = s_start.AddDays(1) };
        var early = new Annotation { Message = "early point", UserName = "B", AnnotationTime = s_start.AddDays(2) };
        HistoryRow[] rows =
        [
            new(s_start.AddSeconds(8), s_start, new Variant(11), StatusCodes.Good),
            new(s_start, s_start, new Variant(7), StatusCodes.Good)
        ];
        driver.Read = _ => new ValueTask<HistoryReadResponse>(driver.Reads.Count == 1
            ? HistoryProtocolTestDriver.Page([AnnotationValue(late, s_start.AddSeconds(8))], ByteString.From([4, 5]))
            : HistoryProtocolTestDriver.Page([AnnotationValue(early, s_start)]));

        await new HistoryReader(driver.Session.Object).AttachAnnotationsAsync(s_node, rows, cancellation.Token)
            .ConfigureAwait(false);

        Assert.That(rows.Select(row => row.DisplayAnnotation),
            Is.EqualTo(s_annotationLookupUsesMappedPropertyAndPaddedRangeExpected));
        Assert.That(
            rows.Select(row => row.Numeric),
            Is.EqualTo(s_annotationLookupUsesMappedPropertyAndPaddedRangeExpected2));
        AssertTranslation(driver.Translations.Single(), cancellation.Token);
        Assert.That(driver.Reads, Has.Count.EqualTo(2));
        for (int i = 0; i < driver.Reads.Count; i++)
        {
            HistoryProtocolTestDriver.ReadCall call = driver.Reads[i];
            Assert.That(call.Nodes.Single().NodeId, Is.EqualTo(property));
            Assert.That(call.Timestamps, Is.EqualTo(TimestampsToReturn.Source));
            Assert.That(call.Token, Is.EqualTo(cancellation.Token));
            Assert.That(call.Release, Is.False);
            var details = WorkflowAssertions.GetEncodeable<ReadRawModifiedDetails>(call.Details);
            Assert.That((DateTime)details.StartTime, Is.EqualTo(s_start.AddMilliseconds(-1)));
            Assert.That((DateTime)details.EndTime, Is.EqualTo(s_start.AddSeconds(8).AddMilliseconds(1)));
            Assert.That(details.ReturnBounds, Is.False);
            Assert.That(details.IsReadModified, Is.False);
            Assert.That(details.NumValuesPerNode, Is.Zero);
            Assert.That(call.Nodes[0].ContinuationPoint.ToArray(),
                Is.EqualTo(i == 0 ? Array.Empty<byte>() : new byte[] { 4, 5 }));
        }
    }

    [TestCase("single")]
    [TestCase("array")]
    public async Task AnnotationsMatchDataValueSourceTimestampNotAnnotationTime(string representation)
    {
        var driver = AnnotationDriver();
        var annotation = new Annotation
        {
            Message = "校正", UserName = "engineer", AnnotationTime = s_start.AddSeconds(9)
        };
        var orphan = new Annotation { Message = "not a matching data point", AnnotationTime = s_start };
        Variant wrapped = representation == "single"
            ? new Variant(new ExtensionObject(annotation))
            : new Variant((ArrayOf<ExtensionObject>)
                [new ExtensionObject(new Argument()), new ExtensionObject(annotation), new ExtensionObject(orphan)]);
        HistoryRow[] rows =
        [
            new(s_start, s_start, new Variant(10), StatusCodes.Good),
            new(s_start.AddSeconds(9), s_start, new Variant(20), StatusCodes.Good)
        ];
        driver.Read = _ => new ValueTask<HistoryReadResponse>(HistoryProtocolTestDriver.Page(
        [
            new DataValue(wrapped, StatusCodes.Good, s_start, s_start),
            AnnotationValue(orphan, s_start.AddSeconds(30))
        ]));

        await new HistoryReader(driver.Session.Object).AttachAnnotationsAsync(s_node, rows, default)
            .ConfigureAwait(false);

        Assert.That(rows[0].Annotation!.Message, Is.EqualTo("校正"));
        Assert.That(rows[0].Annotation!.AnnotationTime, Is.EqualTo((DateTimeUtc)s_start.AddSeconds(9)));
        Assert.That(rows[0].DisplayAnnotation, Is.EqualTo("校正  —  engineer"));
        Assert.That(rows[1].Annotation, Is.Null);
        Assert.That(
            rows.Select(row => row.Numeric),
            Is.EqualTo(s_annotationsMatchDataValueSourceTimestampNotAnnotationTimeExpected));
        Assert.That(driver.Reads, Has.Count.EqualTo(1));
    }

    [TestCase("null")]
    [TestCase("string")]
    [TestCase("unrelated")]
    [TestCase("emptyArray")]
    [TestCase("unrelatedArray")]
    public async Task UnsupportedAnnotationPayloadDoesNotOverwriteExistingAnnotation(string representation)
    {
        Variant payload = representation switch
        {
            "string" => new Variant("not an annotation"),
            "unrelated" => new Variant(new ExtensionObject(new Argument())),
            "emptyArray" => new Variant(ArrayOf<ExtensionObject>.Empty),
            "unrelatedArray" => new Variant((ArrayOf<ExtensionObject>)[new ExtensionObject(new Argument())]),
            _ => Variant.Null
        };
        var previous = new Annotation { Message = "retained" };
        var row = new HistoryRow(s_start, s_start, new Variant(35), StatusCodes.Good) { Annotation = previous };
        var driver = AnnotationDriver();
        driver.Read = _ => new ValueTask<HistoryReadResponse>(HistoryProtocolTestDriver.Page(
            [new DataValue(payload, StatusCodes.Good, s_start, s_start)]));

        await new HistoryReader(driver.Session.Object).AttachAnnotationsAsync(s_node, [row], default)
            .ConfigureAwait(false);

        Assert.That(row.Annotation, Is.SameAs(previous));
        Assert.That(row.DisplayAnnotation, Is.EqualTo("retained"));
        Assert.That(row.Numeric, Is.EqualTo(35));
        Assert.That(driver.Reads, Has.Count.EqualTo(1));
    }

    [TestCase("empty")]
    [TestCase("bad")]
    [TestCase("noTargets")]
    [TestCase("nullTarget")]
    [TestCase("failure")]
    [TestCase("canceled")]
    public async Task AnnotationTranslationFailuresPreservePrimaryRowsAndSendNoHistory(string outcome)
    {
        var driver = new HistoryProtocolTestDriver();
        using var cancellation = new CancellationTokenSource();
        driver.Translate = _ => outcome switch
        {
            "empty" => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                new TranslateBrowsePathsToNodeIdsResponse()),
            "bad" => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(new TranslateBrowsePathsToNodeIdsResponse
            {
                Results = [new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }]
            }),
            "noTargets" => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                new TranslateBrowsePathsToNodeIdsResponse { Results = [new BrowsePathResult()] }),
            "nullTarget" => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                HistoryProtocolTestDriver.Property(NodeId.Null)),
            "canceled" => ValueTask.FromException<TranslateBrowsePathsToNodeIdsResponse>(
                new OperationCanceledException(cancellation.Token)),
            _ => ValueTask.FromException<TranslateBrowsePathsToNodeIdsResponse>(
                new ServiceResultException(StatusCodes.BadCommunicationError))
        };
        var row = new HistoryRow(s_start, s_start.AddSeconds(1), new Variant(65), StatusCodes.Uncertain);

        await new HistoryReader(driver.Session.Object).AttachAnnotationsAsync(s_node, [row], cancellation.Token)
            .ConfigureAwait(false);

        Assert.That(row.Numeric, Is.EqualTo(65));
        Assert.That(row.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
        Assert.That(row.Annotation, Is.Null);
        Assert.That(driver.Reads, Is.Empty);
        AssertTranslation(driver.Translations.Single(), cancellation.Token);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NullVariableOrEmptyRowsSkipAllAnnotationServices(bool emptyRows)
    {
        var driver = new HistoryProtocolTestDriver();
        var row = new HistoryRow(s_start, s_start, new Variant(7), StatusCodes.Good);

        await new HistoryReader(driver.Session.Object).AttachAnnotationsAsync(
            emptyRows ? s_node : NodeId.Null, emptyRows ? [] : [row], default).ConfigureAwait(false);

        Assert.That(driver.Translations, Is.Empty);
        Assert.That(driver.Reads, Is.Empty);
        Assert.That(row.Numeric, Is.EqualTo(7));
        Assert.That(row.Annotation, Is.Null);
    }

    [TestCase("bad", true)]
    [TestCase("empty", true)]
    [TestCase("fault", false)]
    [TestCase("canceled", false)]
    public async Task OptionalAnnotationReadFailureDistinguishesPartialResultsFromAbandonedRead(
        string outcome, bool retainsPartial)
    {
        var driver = AnnotationDriver();
        var annotation = new Annotation { Message = "first page", UserName = "operator", AnnotationTime = s_start };
        driver.Read = _ => driver.Reads.Count == 1
            ? new ValueTask<HistoryReadResponse>(HistoryProtocolTestDriver.Page(
                [AnnotationValue(annotation, s_start)], ByteString.From([8])))
            : outcome switch
            {
                "empty" => new ValueTask<HistoryReadResponse>(new HistoryReadResponse()),
                "bad" => new ValueTask<HistoryReadResponse>(new HistoryReadResponse
                {
                    Results = [new HistoryReadResult { StatusCode = StatusCodes.BadNoData }]
                }),
                "canceled" => ValueTask.FromException<HistoryReadResponse>(new OperationCanceledException()),
                _ => ValueTask.FromException<HistoryReadResponse>(new ServiceResultException(StatusCodes.BadTimeout))
            };
        var row = new HistoryRow(s_start, s_start, new Variant(18), StatusCodes.Good);

        await new HistoryReader(driver.Session.Object).AttachAnnotationsAsync(s_node, [row], default)
            .ConfigureAwait(false);

        Assert.That(row.Annotation, retainsPartial ? Is.SameAs(annotation) : Is.Null);
        Assert.That(row.Numeric, Is.EqualTo(18));
        Assert.That(driver.Reads, Has.Count.EqualTo(2));
        Assert.That(driver.Reads.All(call => !call.Release), Is.True);
    }

    [Test]
    public async Task AnnotationsUseExactSourceTicksAndLastDuplicateRowAndValueWin()
    {
        var driver = AnnotationDriver();
        HistoryRow[] rows =
        [
            new(s_start, s_start, new Variant(10), StatusCodes.Good),
            new(s_start, s_start.AddSeconds(1), new Variant(11), StatusCodes.Uncertain),
            new(s_start.AddMilliseconds(1), s_start, new Variant(12), StatusCodes.Good)
        ];
        var original = new Annotation { Message = "first annotation", AnnotationTime = s_start.AddDays(1) };
        var replacement = new Annotation { Message = "last annotation", AnnotationTime = s_start.AddDays(2) };
        driver.Read = _ => new ValueTask<HistoryReadResponse>(HistoryProtocolTestDriver.Page(
        [
            AnnotationValue(original, s_start),
            AnnotationValue(replacement, s_start),
            AnnotationValue(new Annotation { Message = "one tick away" }, s_start.AddTicks(1))
        ]));

        await new HistoryReader(driver.Session.Object).AttachAnnotationsAsync(s_node, rows, default)
            .ConfigureAwait(false);

        Assert.That(rows[0].Annotation, Is.Null);
        Assert.That(rows[1].Annotation, Is.SameAs(replacement));
        Assert.That(rows[1].DisplayAnnotation, Is.EqualTo("last annotation"));
        Assert.That(rows[2].Annotation, Is.Null);
        Assert.That(
            rows.Select(row => row.Numeric),
            Is.EqualTo(s_annotationsUseExactSourceTicksAndLastDuplicateRowAndValueWinExpected));
        Assert.That(rows[1].StatusCode, Is.EqualTo(StatusCodes.Uncertain));
        Assert.That(driver.Reads, Has.Count.EqualTo(1));
    }

    private static HistoryProtocolTestDriver AnnotationDriver()
    {
        return new HistoryProtocolTestDriver
        {
            Translate = _ => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                HistoryProtocolTestDriver.Property(new NodeId("Annotations", 2)))
        };
    }

    private static DataValue Value(double number, int seconds, StatusCode status)
    {
        return new DataValue(new Variant(number), status, s_start.AddSeconds(seconds), s_start.AddSeconds(seconds + 1));
    }

    private static DataValue AnnotationValue(Annotation annotation, DateTime source)
    {
        return new DataValue(
            new Variant(new ExtensionObject(annotation)), StatusCodes.Good, source, source.AddSeconds(1));
    }

    private static void AssertRead(HistoryProtocolTestDriver.ReadCall call, CancellationToken token)
    {
        Assert.That(call.Header, Is.Null);
        Assert.That(call.Timestamps, Is.EqualTo(TimestampsToReturn.Both));
        Assert.That(call.Release, Is.False);
        Assert.That(call.Token, Is.EqualTo(token));
        Assert.That(call.Nodes, Has.Length.EqualTo(1));
        Assert.That(call.Nodes[0].NodeId, Is.EqualTo(s_node));
        Assert.That(call.Nodes[0].IndexRange, Is.Null.Or.Empty);
    }

    internal static void AssertTranslation(
        HistoryProtocolTestDriver.TranslateCall call, CancellationToken token)
    {
        Assert.That(call.Header, Is.Null);
        Assert.That(call.Token, Is.EqualTo(token));
        Assert.That(call.Paths, Has.Length.EqualTo(1));
        Assert.That(call.Paths[0].StartingNode, Is.EqualTo(s_node));
        Assert.That(call.Paths[0].RelativePath.Elements.Count, Is.EqualTo(1));
        RelativePathElement element = call.Paths[0].RelativePath.Elements[0];
        Assert.That(element.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
        Assert.That(element.TargetName, Is.EqualTo(new QualifiedName(BrowseNames.Annotations)));
        Assert.That(element.IsInverse, Is.False);
        Assert.That(element.IncludeSubtypes, Is.False);
    }

    private static void AssertRows(IReadOnlyList<HistoryRow> rows)
    {
        Assert.That(rows.Select(row => row.Numeric), Is.EqualTo(new[] { 12.5, -3, 19 }));
        Assert.That(rows.Select(row => row.StatusCode), Is.EqualTo(new StatusCode[]
        {
            StatusCodes.Good, StatusCodes.BadOutOfRange, StatusCodes.Uncertain
        }));
        Assert.That(rows.Select(row => row.SourceTimestamp),
            Is.EqualTo(new[] { s_start, s_start.AddSeconds(1), s_start.AddSeconds(2) }));
        Assert.That(rows.Select(row => row.ServerTimestamp),
            Is.EqualTo(new[] { s_start.AddSeconds(1), s_start.AddSeconds(2), s_start.AddSeconds(3) }));
    }

    private static Task<List<HistoryRow>> ReadModeAsync(HistoryReader reader, string mode, CancellationToken token)
    {
        return mode switch
        {
            "processed" => reader.ReadProcessedAsync(
                s_node, new NodeId(Objects.AggregateFunction_Average), s_start, s_start.AddMinutes(1), 1000, token),
            "atTime" => reader.ReadAtTimeAsync(s_node, [s_start, s_start.AddSeconds(5)], token),
            _ => reader.ReadRawAsync(s_node, s_start, s_start.AddMinutes(1), true, mode == "modified", 15, token)
        };
    }

    private static readonly string[] s_annotationChangesNotifyBothProjectionsOnlyWhenReferenceChangeExpected =
    [
        "Annotation",
        "DisplayAnnotation",
    ];
    private static readonly string[] s_annotationChangesNotifyBothProjectionsOnlyWhenReferenceChangeExpected2 =
    [
        "Annotation",
        "DisplayAnnotation",
        "Annotation",
        "DisplayAnnotation",
    ];
    private static readonly string[] s_annotationLookupUsesMappedPropertyAndPaddedRangeExpected =
    [
        "late point  —  A",
        "early point  —  B",
    ];
    private static readonly double[] s_annotationLookupUsesMappedPropertyAndPaddedRangeExpected2 =
    [
        11d,
        7d,
    ];
    private static readonly double[] s_annotationsMatchDataValueSourceTimestampNotAnnotationTimeExpected =
    [
        10d,
        20d,
    ];
    private static readonly double[] s_annotationsUseExactSourceTicksAndLastDuplicateRowAndValueWinExpected =
    [
        10d,
        11d,
        12d,
    ];
}
