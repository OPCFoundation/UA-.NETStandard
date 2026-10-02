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
using UaLens.Plugins.Historian;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class HistoryUpdaterTests
{
    private static readonly DateTime s_time = new(2026, 3, 4, 5, 6, 7, 123, DateTimeKind.Utc);
    private static readonly NodeId s_node = new("Boiler.Temperature", 2);

    [TestCase(PerformUpdateType.Insert)]
    [TestCase(PerformUpdateType.Update)]
    [TestCase(PerformUpdateType.Replace)]
    public async Task ValueUpdatesEncodeExactOperationAndDataValue(PerformUpdateType operation)
    {
        var driver = GoodDriver();
        using var cancellation = new CancellationTokenSource();
        var value = new DataValue(new Variant("校正 42°C"), StatusCodes.Uncertain, s_time, s_time.AddSeconds(2));
        var updater = new HistoryUpdater(driver.Session.Object);

        HistoryUpdateOutcome outcome = await (operation switch
        {
            PerformUpdateType.Insert => updater.InsertAsync(s_node, value, cancellation.Token),
            PerformUpdateType.Replace => updater.ReplaceAsync(s_node, value, cancellation.Token),
            _ => updater.InsertReplaceAsync(s_node, value, cancellation.Token)
        }).ConfigureAwait(false);

        Assert.That(outcome.IsGood, Is.True);
        Assert.That(outcome.Summarise(), Is.EqualTo("Good (1/1 ok)"));
        var details = WorkflowAssertions.GetEncodeable<UpdateDataDetails>(AssertUpdate(driver, cancellation.Token));
        Assert.That(details.NodeId, Is.EqualTo(s_node));
        Assert.That(details.PerformInsertReplace, Is.EqualTo(operation));
        Assert.That(details.UpdateValues.Count, Is.EqualTo(1));
        DataValue sent = details.UpdateValues[0];
        Assert.That(sent.WrappedValue, Is.EqualTo(new Variant("校正 42°C")));
        Assert.That(sent.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
        Assert.That((DateTime)sent.SourceTimestamp, Is.EqualTo(s_time));
        Assert.That((DateTime)sent.ServerTimestamp, Is.EqualTo(s_time.AddSeconds(2)));
        Assert.That(driver.Translations, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RemoveUsesOneInstantRawRangeRatherThanDeleteAtTimes(bool local)
    {
        var driver = GoodDriver();
        using var cancellation = new CancellationTokenSource();
        DateTime timestamp = local ? s_time.ToLocalTime() : s_time;

        HistoryUpdateOutcome outcome = await new HistoryUpdater(driver.Session.Object)
            .RemoveAsync(s_node, timestamp, cancellation.Token).ConfigureAwait(false);

        var details = WorkflowAssertions.GetEncodeable<DeleteRawModifiedDetails>(
            AssertUpdate(driver, cancellation.Token));
        Assert.That(details.NodeId, Is.EqualTo(s_node));
        Assert.That(details.IsDeleteModified, Is.False);
        Assert.That((DateTime)details.StartTime, Is.EqualTo(s_time));
        Assert.That((DateTime)details.EndTime, Is.EqualTo(s_time));
        Assert.That(outcome.OperationResults, Is.EqualTo(new StatusCode[] { StatusCodes.Good }));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    public async Task DeleteAtTimesPreservesExactTimestampScopeOrderAndDuplicates(int count)
    {
        DateTime[] times = new[] { s_time.AddSeconds(5).ToLocalTime(), s_time, s_time, s_time.AddSeconds(1) }
            .Take(count).ToArray();
        var driver = GoodDriver();
        using var cancellation = new CancellationTokenSource();

        HistoryUpdateOutcome outcome = await new HistoryUpdater(driver.Session.Object)
            .DeleteAtTimesAsync(s_node, times, cancellation.Token).ConfigureAwait(false);

        var details = WorkflowAssertions.GetEncodeable<DeleteAtTimeDetails>(AssertUpdate(driver, cancellation.Token));
        Assert.That(details.NodeId, Is.EqualTo(s_node));
        Assert.That(details.ReqTimes.IsNull, Is.False);
        Assert.That(details.ReqTimes.ToList().Select(time => (DateTime)time),
            Is.EqualTo(new[] { s_time.AddSeconds(5), s_time, s_time, s_time.AddSeconds(1) }.Take(count)));
        Assert.That(outcome.IsGood, Is.True);
        Assert.That(driver.Reads, Is.Empty);
    }

    [TestCase(false, -1)]
    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(true, -1)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    public async Task RawAndModifiedDeleteEncodeDistinctRangeFlagsWithoutChangingBounds(bool modified, int endOffset)
    {
        var driver = GoodDriver();
        using var cancellation = new CancellationTokenSource();
        var updater = new HistoryUpdater(driver.Session.Object);
        DateTime end = s_time.AddTicks(endOffset);

        HistoryUpdateOutcome outcome = await (modified
            ? updater.DeleteModifiedAsync(s_node, s_time.ToLocalTime(), end.ToLocalTime(), cancellation.Token)
            : updater.DeleteRawAsync(s_node, s_time.ToLocalTime(), end.ToLocalTime(), cancellation.Token))
            .ConfigureAwait(false);

        var details = WorkflowAssertions.GetEncodeable<DeleteRawModifiedDetails>(
            AssertUpdate(driver, cancellation.Token));
        Assert.That(details.NodeId, Is.EqualTo(s_node));
        Assert.That(details.IsDeleteModified, Is.EqualTo(modified));
        Assert.That((DateTime)details.StartTime, Is.EqualTo(s_time));
        Assert.That((DateTime)details.EndTime, Is.EqualTo(end));
        Assert.That(outcome.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
    }

    [Test]
    public async Task AnnotationUpdateTargetsTranslatedPropertyWithExactPayload()
    {
        var driver = GoodDriver();
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
        var annotation = new Annotation
        {
            UserName = "engineer α", Message = "校正後\n42°C", AnnotationTime = s_time.AddDays(1)
        };

        HistoryUpdateOutcome outcome = await new HistoryUpdater(driver.Session.Object)
            .UpdateAnnotationAsync(s_node, s_time.ToLocalTime(), annotation, cancellation.Token).ConfigureAwait(false);

        HistoryReaderTests.AssertTranslation(driver.Translations.Single(), cancellation.Token);
        var details = WorkflowAssertions.GetEncodeable<UpdateDataDetails>(AssertUpdate(driver, cancellation.Token));
        Assert.That(details.NodeId, Is.EqualTo(property));
        Assert.That(details.PerformInsertReplace, Is.EqualTo(PerformUpdateType.Update));
        Assert.That(details.UpdateValues.Count, Is.EqualTo(1));
        DataValue value = details.UpdateValues[0];
        Assert.That((DateTime)value.SourceTimestamp, Is.EqualTo(s_time));
        Assert.That((DateTime)value.ServerTimestamp, Is.EqualTo(s_time));
        Assert.That(value.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
        Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
        var sentAnnotation = WorkflowAssertions.GetEncodeable<Annotation>(extension);
        Assert.That(sentAnnotation, Is.SameAs(annotation));
        Assert.That(sentAnnotation.Message, Is.EqualTo("校正後\n42°C"));
        Assert.That(sentAnnotation.AnnotationTime, Is.EqualTo((DateTimeUtc)s_time.AddDays(1)));
        Assert.That(outcome.IsGood, Is.True);
    }

    [TestCase("empty")]
    [TestCase("bad")]
    [TestCase("noTargets")]
    [TestCase("nullTarget")]
    [TestCase("fault")]
    [TestCase("canceled")]
    public async Task MissingAnnotationTargetSendsNoUpdateAndReportsNodeUnknown(string result)
    {
        var driver = new HistoryProtocolTestDriver();
        driver.Translate = _ => result switch
        {
            "empty" => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                new TranslateBrowsePathsToNodeIdsResponse()),
            "bad" => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(new TranslateBrowsePathsToNodeIdsResponse
            {
                Results = [new BrowsePathResult { StatusCode = StatusCodes.BadNodeIdUnknown }]
            }),
            "noTargets" => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                new TranslateBrowsePathsToNodeIdsResponse { Results = [new BrowsePathResult()] }),
            "nullTarget" => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                HistoryProtocolTestDriver.Property(NodeId.Null)),
            "canceled" => ValueTask.FromException<TranslateBrowsePathsToNodeIdsResponse>(
                new OperationCanceledException()),
            _ => ValueTask.FromException<TranslateBrowsePathsToNodeIdsResponse>(
                new ServiceResultException(StatusCodes.BadTimeout))
        };

        HistoryUpdateOutcome outcome = await new HistoryUpdater(driver.Session.Object)
            .UpdateAnnotationAsync(s_node, s_time, new Annotation { Message = "calibrated" }, default)
            .ConfigureAwait(false);

        Assert.That(outcome.IsGood, Is.False);
        Assert.That(outcome.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        Assert.That(outcome.Summarise(), Is.EqualTo("BadNodeIdUnknown"));
        Assert.That(driver.Updates, Is.Empty);
        Assert.That(driver.Translations, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task NullAnnotationIsRejectedBeforeAnyServiceCall()
    {
        var driver = new HistoryProtocolTestDriver();

        await Assert.ThatAsync(() => new HistoryUpdater(driver.Session.Object)
            .UpdateAnnotationAsync(s_node, s_time, null!, default),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("annotation")).ConfigureAwait(false);

        Assert.That(driver.Translations, Is.Empty);
        Assert.That(driver.Updates, Is.Empty);
    }

    [TestCase("empty", true, "Good")]
    [TestCase("good", true, "Good (2/2 ok)")]
    [TestCase("uncertain", true, "Uncertain (2/2 ok)")]
    [TestCase("overallBad", false, "BadHistoryOperationUnsupported (2/2 ok)")]
    [TestCase("mixed", false, "Good (1/3 ok, 2 bad — first: BadNoData)")]
    [TestCase("allBad", false, "Good (0/2 ok, 2 bad — first: BadOutOfRange)")]
    [TestCase("hex", true, "0x01230000")]
    public async Task OutcomesSeparateOverallAndPerOperationFailures(string kind, bool good, string summary)
    {
        var driver = new HistoryProtocolTestDriver();
        driver.Update = _ => new ValueTask<HistoryUpdateResponse>(kind switch
        {
            "empty" => new HistoryUpdateResponse(),
            "uncertain" => HistoryProtocolTestDriver.Outcome(
                StatusCodes.Uncertain, StatusCodes.Good, StatusCodes.Uncertain),
            "overallBad" => HistoryProtocolTestDriver.Outcome(
                StatusCodes.BadHistoryOperationUnsupported, StatusCodes.Good, StatusCodes.Good),
            "mixed" => HistoryProtocolTestDriver.Outcome(
                StatusCodes.Good, StatusCodes.Good, StatusCodes.BadNoData, StatusCodes.BadOutOfRange),
            "allBad" => HistoryProtocolTestDriver.Outcome(
                StatusCodes.Good, StatusCodes.BadOutOfRange, StatusCodes.BadNoData),
            "hex" => HistoryProtocolTestDriver.Outcome(0x01230000),
            _ => HistoryProtocolTestDriver.Outcome(StatusCodes.Good, StatusCodes.Good, StatusCodes.Good)
        });

        HistoryUpdateOutcome outcome = await new HistoryUpdater(driver.Session.Object)
            .InsertAsync(s_node, new DataValue(new Variant(6)), default).ConfigureAwait(false);

        Assert.That(outcome.IsGood, Is.EqualTo(good));
        Assert.That(outcome.Summarise(), Is.EqualTo(summary));
        Assert.That(driver.Updates, Has.Count.EqualTo(1));
        var details = WorkflowAssertions.GetEncodeable<UpdateDataDetails>(driver.Updates[0].Details.Single());
        Assert.That(details.NodeId, Is.EqualTo(s_node));
    }

    [TestCase(false, "good")]
    [TestCase(true, "good")]
    [TestCase(false, "overall")]
    [TestCase(true, "overall")]
    [TestCase(false, "perValue")]
    [TestCase(true, "perValue")]
    [TestCase(false, "both")]
    [TestCase(true, "both")]
    public async Task LegacyMethodsThrowForFailedOutcomeAndPreserveRequest(bool delete, string result)
    {
        var driver = new HistoryProtocolTestDriver
        {
            Update = _ => new ValueTask<HistoryUpdateResponse>(HistoryProtocolTestDriver.Outcome(
                result is "overall" or "both" ? StatusCodes.BadHistoryOperationUnsupported : StatusCodes.Good,
                StatusCodes.Good, result is "perValue" or "both" ? StatusCodes.BadNoData : StatusCodes.Good))
        };
        using var cancellation = new CancellationTokenSource();
        var updater = new HistoryUpdater(driver.Session.Object);
        Task operation = delete
            ? updater.DeleteAsync(s_node, s_time, cancellation.Token)
            : updater.UpdateAsync(s_node, PerformUpdateType.Replace, s_time, new Variant(-12),
                StatusCodes.Uncertain, cancellation.Token);

        if (result == "good")
        {
            await operation.ConfigureAwait(false);
        }
        else
        {
            StatusCode expectedStatus = result is "overall" or "both"
                ? StatusCodes.BadHistoryOperationUnsupported : StatusCodes.BadNoData;
            await Assert.ThatAsync(() => operation,
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expectedStatus))
                .ConfigureAwait(false);
        }
        ExtensionObject sent = AssertUpdate(driver, cancellation.Token);
        if (delete)
        {
            var details = WorkflowAssertions.GetEncodeable<DeleteAtTimeDetails>(sent);
            Assert.That(details.NodeId, Is.EqualTo(s_node));
            Assert.That(details.ReqTimes.ToArray(), Is.EqualTo(new DateTimeUtc[] { s_time }));
        }
        else
        {
            var details = WorkflowAssertions.GetEncodeable<UpdateDataDetails>(sent);
            Assert.That(details.PerformInsertReplace, Is.EqualTo(PerformUpdateType.Replace));
            Assert.That(details.UpdateValues[0].WrappedValue, Is.EqualTo(new Variant(-12)));
            Assert.That(details.UpdateValues[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That((DateTime)details.UpdateValues[0].SourceTimestamp, Is.EqualTo(s_time));
            Assert.That((DateTime)details.UpdateValues[0].ServerTimestamp, Is.EqualTo(s_time));
        }
    }

    [TestCase("insert", false)]
    [TestCase("replace", true)]
    [TestCase("update", false)]
    [TestCase("remove", true)]
    [TestCase("raw", false)]
    [TestCase("modified", true)]
    [TestCase("atTime", false)]
    [TestCase("annotation", true)]
    public async Task UpdateCancellationAndServiceFaultPropagateWithoutFalseOutcome(string operation, bool canceled)
    {
        using var cancellation = new CancellationTokenSource();
        Exception failure = canceled
            ? new OperationCanceledException("update canceled", cancellation.Token)
            : new ServiceResultException(StatusCodes.BadCommunicationError, "update failed");
        var driver = new HistoryProtocolTestDriver
        {
            Update = _ => ValueTask.FromException<HistoryUpdateResponse>(failure),
            Translate = _ => new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                HistoryProtocolTestDriver.Property(new NodeId("Annotations", 2)))
        };
        var updater = new HistoryUpdater(driver.Session.Object);
        var value = new DataValue(new Variant(22));
        Task<HistoryUpdateOutcome> work = operation switch
        {
            "insert" => updater.InsertAsync(s_node, value, cancellation.Token),
            "replace" => updater.ReplaceAsync(s_node, value, cancellation.Token),
            "update" => updater.InsertReplaceAsync(s_node, value, cancellation.Token),
            "remove" => updater.RemoveAsync(s_node, s_time, cancellation.Token),
            "raw" => updater.DeleteRawAsync(s_node, s_time, s_time.AddMinutes(1), cancellation.Token),
            "modified" => updater.DeleteModifiedAsync(s_node, s_time, s_time.AddMinutes(1), cancellation.Token),
            "annotation" => updater.UpdateAnnotationAsync(s_node, s_time, new Annotation(), cancellation.Token),
            _ => updater.DeleteAtTimesAsync(s_node, [s_time], cancellation.Token)
        };

        await Assert.ThatAsync(() => work, Throws.Exception.SameAs(failure)).ConfigureAwait(false);

        Assert.That(driver.Updates.Single().Token, Is.EqualTo(cancellation.Token));
        Assert.That(driver.Reads, Is.Empty);
        Assert.That(driver.Translations, Has.Count.EqualTo(operation == "annotation" ? 1 : 0));
    }

    private static HistoryProtocolTestDriver GoodDriver()
    {
        return new HistoryProtocolTestDriver
        {
            Update = _ => new ValueTask<HistoryUpdateResponse>(
                HistoryProtocolTestDriver.Outcome(StatusCodes.Good, StatusCodes.Good))
        };
    }

    private static ExtensionObject AssertUpdate(HistoryProtocolTestDriver driver, CancellationToken token)
    {
        Assert.That(driver.Updates, Has.Count.EqualTo(1));
        HistoryProtocolTestDriver.UpdateCall call = driver.Updates[0];
        Assert.That(call.Header, Is.Null);
        Assert.That(call.Token, Is.EqualTo(token));
        Assert.That(call.Details, Has.Length.EqualTo(1));
        return call.Details[0];
    }
}
