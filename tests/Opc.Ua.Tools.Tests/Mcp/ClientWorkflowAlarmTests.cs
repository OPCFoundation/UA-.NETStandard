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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Tools.Tests.McpCompanion.ClientWorkflows
{
    /// <summary>
    /// Proves explicit alarm methods, occurrence identity and borrowed-stream cleanup.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class ClientWorkflowAlarmTests
    {
        /// <summary>
        /// Every exposed condition command dispatches exactly the published standard method.
        /// </summary>
        [TestCaseSource(nameof(Commands))]
        public async Task EveryAlarmCommandPreservesTypedMethodTargetArgumentsAndStatusAsync(
            string name, ExpandedNodeId method, JsonObject extra, ArrayOf<Variant> expected, bool shelving)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId condition = fixture.Remote.Id("condition");
            NodeId target = condition;
            if (shelving)
            {
                target = fixture.Remote.Id("shelving");
                fixture.Remote.Child(condition, new QualifiedName(BrowseNames.ShelvingState), target);
                fixture.Remote.Type(target, ObjectTypeIds.ShelvedStateMachineType);
            }
            fixture.Remote.Reply(StatusCodes.Uncertain);
            var arguments = (JsonObject)extra.DeepClone();
            arguments["conditionId"] = condition.ToString();
            CallToolResult result = await fixture.InvokeAsync(name, arguments).ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.Uncertain.Code));
            fixture.Remote.AssertCall(target, method, expected.ToArray()!);
        }

        /// <summary>
        /// Occurrence commands preserve exact binary EventId and localized comment on the requested ConditionId.
        /// </summary>
        [TestCase("alarms_acknowledge", false)]
        [TestCase("alarms_confirm", false)]
        [TestCase("alarms_comment", false)]
        [TestCase("alarms_acknowledge", true)]
        public async Task OccurrenceMutationsPreserveEventIdentityAndServerRefusalAsync(string tool, bool denied)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId condition = fixture.Remote.Id("condition");
            fixture.Remote.Reply(denied ? StatusCodes.BadEventIdUnknown : StatusCodes.Good);
            CallToolResult result = await fixture.InvokeAsync(tool, new JsonObject
            {
                ["conditionId"] = condition.ToString(),
                ["eventId"] = "AQIDBA==",
                ["comment"] = "Checked",
                ["locale"] = "de",
                ["branchId"] = "i=0"
            }).ConfigureAwait(false);
            ExpandedNodeId method = tool switch
            {
                "alarms_acknowledge" => MethodIds.AcknowledgeableConditionType_Acknowledge,
                "alarms_confirm" => MethodIds.AcknowledgeableConditionType_Confirm,
                _ => MethodIds.ConditionType_AddComment
            };
            fixture.Remote.AssertCall(condition, method,
                Variant.From(ByteString.From(new byte[] { 1, 2, 3, 4 })),
                Variant.From(new LocalizedText("de", "Checked")));
            Assert.That(result.IsError, Is.EqualTo(denied));
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo((denied ? StatusCodes.BadEventIdUnknown : StatusCodes.Good).Code));
        }

        /// <summary>
        /// Post-ack refresh only advertises a new occurrence id on the verified selected branch.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AcknowledgementRefreshesEventIdOnlyForTheSameBranchAsync(bool wrongBranch)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId condition = fixture.Remote.Id("condition");
            NodeId branch = fixture.Remote.Id("branch");
            fixture.Remote.Property(condition, new QualifiedName(BrowseNames.BranchId),
                Variant.From(wrongBranch ? fixture.Remote.Id("other-branch") : branch));
            fixture.Remote.Property(condition, new QualifiedName(BrowseNames.EventId),
                Variant.From(ByteString.From(new byte[] { 5, 6 })));
            fixture.Remote.Reply(StatusCodes.Good);
            CallToolResult result = await fixture.Alarms.AcknowledgeAsync(
                condition.ToString(), "AQIDBA==", "Checked", branch.ToString(), sessionName: "selected")
                .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("observeBeforeConfirm").GetBoolean(), Is.EqualTo(wrongBranch));
            Assert.That(json.TryGetProperty("refreshedEventId", out JsonElement next), Is.EqualTo(!wrongBranch));
            if (!wrongBranch)
            {
                Assert.That(next.GetString(), Is.EqualTo("BQY="));
            }
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Successful acknowledgement survives cancellation that occurs after the server has accepted it.
        /// </summary>
        [Test]
        public async Task AcknowledgementCommitIsNotMaskedByLateCallerCancellationAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            fixture.Remote.OnCall = _ =>
            {
                cancellation.Cancel();
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
            CallToolResult result = await fixture.Alarms.AcknowledgeAsync(
                fixture.Remote.Id("condition").ToString(), "AQID", "Checked", sessionName: "selected",
                ct: cancellation.Token).ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Count-limited observation retains occurrence/source identities and removes its own item only.
        /// </summary>
        [Test]
        public async Task ObservationPreservesOccurrenceSourceAndReleasesOwnedEnumeratorAtCountLimitAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            EventNotification notification = Condition(fixture);
            fixture.Streams.Notifications.Enqueue(notification);
            fixture.Streams.Notifications.Enqueue(Condition(fixture));
            CallToolResult result = await fixture.Alarms.ObserveAsync(
                maxResults: 1, sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("stoppedBy").GetString(), Is.EqualTo("itemLimit"));
            Assert.That(json.GetProperty("complete").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            JsonElement fields = json.GetProperty("items")[0].GetProperty("fields");
            Assert.That(Decode(fields.GetProperty("ConditionId"), fixture), Is.EqualTo(notification.Fields[0]));
            Assert.That(Decode(fields.GetProperty("BranchId"), fixture), Is.EqualTo(notification.Fields[1]));
            Assert.That(Decode(fields.GetProperty("EventId"), fixture), Is.EqualTo(notification.Fields[2]));
            Assert.That(Decode(fields.GetProperty("SourceNode"), fixture), Is.EqualTo(notification.Fields[4]));
            Assert.That(fixture.Streams.Options!.QueueSize, Is.EqualTo(1));
            Assert.That(fixture.Streams.Disposals, Is.EqualTo(1));
            fixture.Streams.Stream.Verify(stream => stream.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// Complete refresh snapshots require both actual markers and explicitly distinguish overflow.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RefreshSnapshotUsesRealMarkersAndNeverClaimsOverflowedCompletenessAsync(bool overflow)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Streams.Notifications.Enqueue(Marker(ObjectTypeIds.RefreshStartEventType));
            fixture.Streams.Notifications.Enqueue(Condition(fixture));
            if (overflow)
            {
                fixture.Streams.Notifications.Enqueue(Marker(ObjectTypeIds.EventQueueOverflowEventType));
            }
            fixture.Streams.Notifications.Enqueue(Marker(ObjectTypeIds.RefreshEndEventType));
            CallToolResult result = await fixture.Alarms.RefreshAsync(
                maxResults: 5, sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(fixture.Streams.Refresh, Is.True);
            Assert.That(json.GetProperty("refreshStarted").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("refreshEnded").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("stoppedBy").GetString(), Is.EqualTo("refreshEnd"));
            Assert.That(json.GetProperty("complete").GetBoolean(), Is.EqualTo(!overflow));
            Assert.That(json.GetProperty("serverQueueOverflow").GetBoolean(), Is.EqualTo(overflow));
            Assert.That(fixture.Streams.Disposals, Is.EqualTo(1));
            fixture.Streams.Stream.Verify(stream => stream.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// Exhausting a stream without RefreshEnd is not a successful snapshot.
        /// </summary>
        [Test]
        public async Task RefreshWithoutEndMarkerIsExplicitlyIncompleteAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Streams.Notifications.Enqueue(Marker(ObjectTypeIds.RefreshStartEventType));
            fixture.Streams.Notifications.Enqueue(Condition(fixture));
            CallToolResult result = await fixture.Alarms.RefreshAsync(sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("refreshEnded").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("complete").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("stoppedBy").GetString(), Is.EqualTo("sourceCompleted"));
            Assert.That(fixture.Streams.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// Caller cancellation removes the owned observation without disposing the session's shared subscription.
        /// </summary>
        [Test]
        public async Task CancelledObservationDisposesOnlyOwnedEnumeratorAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Streams.WaitAfterSequence = true;
            using var cancellation = new CancellationTokenSource();
            Task<CallToolResult> observation = fixture.Alarms.ObserveAsync(
                durationMilliseconds: 30000, sessionName: "selected", ct: cancellation.Token);
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(() => observation,
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(fixture.Streams.Disposals, Is.EqualTo(1));
            fixture.Streams.Stream.Verify(stream => stream.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// Finite timeout is an observation result rather than a false successful complete snapshot.
        /// </summary>
        [Test]
        public async Task ObservationTimeoutIsExplicitAndCleansUpAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Streams.WaitAfterSequence = true;
            CallToolResult result = await fixture.Alarms.ObserveAsync(
                durationMilliseconds: 10, sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("stoppedBy").GetString(), Is.EqualTo("durationLimit"));
            Assert.That(json.GetProperty("complete").GetBoolean(), Is.False);
            Assert.That(fixture.Streams.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// Observation duration checks include exact hard boundaries and neighbors.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(30000, true)]
        [TestCase(30001, false)]
        public async Task ObservationDurationHasExactBoundariesAsync(int duration, bool accepted)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.Alarms.ObserveAsync(
                durationMilliseconds: duration, sessionName: "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.EqualTo(!accepted));
            Assert.That(fixture.Streams.Disposals, Is.EqualTo(accepted ? 1 : 0));
        }

        /// <summary>
        /// Optional shelving absence is not replaced by a call on the wrong parent target.
        /// </summary>
        [Test]
        public async Task MissingShelvingStateIsRefusedWithoutWrongTargetFallbackAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.Alarms.UnshelveAsync(
                fixture.Remote.Id("condition").ToString(), "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadNotSupported);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Defines each fixed command and every supported comment overload.
        /// </summary>
        private static IEnumerable<TestCaseData> Commands()
        {
            yield return Command("alarms_enable", MethodIds.ConditionType_Enable);
            yield return Command("alarms_disable", MethodIds.ConditionType_Disable);
            yield return Command("alarms_silence", MethodIds.AlarmConditionType_Silence);
            yield return Command("alarms_suppress", MethodIds.AlarmConditionType_Suppress);
            yield return CommentCommand("alarms_suppress", MethodIds.AlarmConditionType_Suppress2);
            yield return Command("alarms_unsuppress", MethodIds.AlarmConditionType_Unsuppress);
            yield return CommentCommand("alarms_unsuppress", MethodIds.AlarmConditionType_Unsuppress2);
            yield return Command("alarms_out_of_service", MethodIds.AlarmConditionType_RemoveFromService);
            yield return CommentCommand("alarms_out_of_service", MethodIds.AlarmConditionType_RemoveFromService2);
            yield return Command("alarms_in_service", MethodIds.AlarmConditionType_PlaceInService);
            yield return CommentCommand("alarms_in_service", MethodIds.AlarmConditionType_PlaceInService2);
            yield return Command("alarms_reset", MethodIds.AlarmConditionType_Reset);
            yield return CommentCommand("alarms_reset", MethodIds.AlarmConditionType_Reset2);
            yield return new TestCaseData(
                "alarms_timed_shelve", (ExpandedNodeId)MethodIds.ShelvedStateMachineType_TimedShelve,
                new JsonObject { ["shelvingTimeMilliseconds"] = 1500 },
                (ArrayOf<Variant>)[Variant.From(1500.0)], true);
            yield return Command("alarms_one_shot_shelve", MethodIds.ShelvedStateMachineType_OneShotShelve, true);
            yield return Command("alarms_unshelve", MethodIds.ShelvedStateMachineType_Unshelve, true);
            yield return new TestCaseData(
                "alarms_respond_dialog", (ExpandedNodeId)MethodIds.DialogConditionType_Respond,
                new JsonObject { ["selectedResponse"] = 2 }, (ArrayOf<Variant>)[Variant.From(2)], false);
            yield return new TestCaseData(
                "alarms_respond_dialog", (ExpandedNodeId)MethodIds.DialogConditionType_Respond2,
                new JsonObject { ["selectedResponse"] = 2, ["comment"] = string.Empty, ["locale"] = "en" },
                (ArrayOf<Variant>)[Variant.From(2), Variant.From(new LocalizedText("en", string.Empty))], false);
        }

        /// <summary>
        /// Builds a zero-argument case.
        /// </summary>
        private static TestCaseData Command(string name, ExpandedNodeId method, bool shelving = false)
        {
            return new TestCaseData(name, method, new JsonObject(), ArrayOf<Variant>.Empty, shelving);
        }

        /// <summary>
        /// Builds a localized-comment case with explicit empty text.
        /// </summary>
        private static TestCaseData CommentCommand(string name, ExpandedNodeId method)
        {
            return new TestCaseData(name, method, new JsonObject { ["comment"] = string.Empty, ["locale"] = "en" },
                (ArrayOf<Variant>)[Variant.From(new LocalizedText("en", string.Empty))], false);
        }

        /// <summary>
        /// Supplies all fixed observation fields with intentionally distinct condition/branch/source identities.
        /// </summary>
        private static EventNotification Condition(ClientWorkflowTestContext fixture)
        {
            return new EventNotification(null,
                [
                    Variant.From(fixture.Remote.Id("condition")), Variant.From(fixture.Remote.Id("branch")),
                    Variant.From(ByteString.From(new byte[] { 1, 4, 7 })),
                    Variant.From(ObjectTypeIds.AlarmConditionType),
                    Variant.From(fixture.Remote.Id("source")), Variant.From("source-name"),
                    Variant.From((DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                    Variant.From(new LocalizedText("en", "High temperature")), Variant.From((ushort)800),
                    Variant.From(true), Variant.From("condition-name"), Variant.From(new LocalizedText("Enabled")),
                    Variant.From(new LocalizedText("Unacknowledged")),
                    Variant.From(StatusCodes.BadNoData),
                    Variant.From(new LocalizedText("Active")), Variant.From(new LocalizedText("Not suppressed")),
                    Variant.From(new LocalizedText("In service"))
                ])
            { PartitionServerId = 23 };
        }

        /// <summary>
        /// Emits a real standard refresh or overflow event type without pretending it is a condition.
        /// </summary>
        private static EventNotification Marker(NodeId type)
        {
            return new EventNotification(null, [Variant.Null, Variant.Null, Variant.Null, Variant.From(type)]);
        }

        /// <summary>
        /// Uses the stack decoder to assert typed identity instead of relying on encoder formatting.
        /// </summary>
        private static Variant Decode(JsonElement value, ClientWorkflowTestContext fixture)
        {
            using var decoder = new JsonDecoder("{\"value\":" + value.GetRawText() + "}", fixture.Remote.Context);
            return decoder.ReadVariant("value");
        }
    }
}
#endif
