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

using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests that verify the source-generated typed
    /// <c>Publish&lt;TEvent&gt;</c> overload on the boiler's
    /// <c>DrumX001</c> notifier wrapper actually wires an
    /// <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/>
    /// event source through the runtime
    /// <c>EventSourceRegistry</c> and dispatches events to
    /// monitored items under NativeAOT constraints (no JIT, no
    /// reflection).
    /// </summary>
    [ClassDataSource<BoilerAotFixture>(Shared = SharedType.PerTestSession)]
    public class PublishedEventsAotTests(BoilerAotFixture fixture)
    {
        private const string kBoilerNamespaceUri =
            "http://opcfoundation.org/UA/Boiler/";

        [Test]
        public async Task DrumHeartbeatEventsArriveAtMonitoredItem()
        {
            NodeId drumNodeId = await ResolveBoilerNodeAsync(
                "DrumX001").ConfigureAwait(false);

            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("EventId"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("EventType"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("SourceName"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("Severity"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("Message"));

            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotDrumHeartbeats",
                PublishingEnabled = true,
                PublishingInterval = 250,
                KeepAliveCount = 10
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            try
            {
                var received = new TaskCompletionSource<EventFieldList>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                var eventItem = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = drumNodeId,
                    AttributeId = Attributes.EventNotifier,
                    DisplayName = "DrumHeartbeats",
                    Filter = eventFilter,
                    QueueSize = 16
                };

                eventItem.Notification += (item, args) =>
                {
                    if (args.NotificationValue is EventFieldList fields)
                    {
                        received.TrySetResult(fields);
                    }
                };

                subscription.AddItem(eventItem);
                await subscription.ApplyChangesAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                using var cts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(15));
                using (cts.Token.Register(
                    () => received.TrySetCanceled(cts.Token)))
                {
                    EventFieldList fields = await received.Task
                        .ConfigureAwait(false);

                    await Assert.That(fields.EventFields.Count)
                        .IsEqualTo(eventFilter.SelectClauses.Count);

                    List<Variant> values = fields.EventFields.ToList();
                    string sourceName = values[2].GetString();
                    ushort severity = values[3].GetUInt16();
                    LocalizedText message = values[4].GetLocalizedText();

                    await Assert.That(sourceName).IsEqualTo("DrumX001");
                    await Assert.That((int)severity)
                        .IsEqualTo((int)EventSeverity.Medium);
                    await Assert.That(message.IsNull).IsFalse();
                    await Assert.That(message.Text)
                        .StartsWith("Drum heartbeat #");
                }
            }
            finally
            {
                await fixture.Session.RemoveSubscriptionAsync(subscription)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Walks the boiler instance tree starting from
        /// <c>Boilers/Boiler #1</c> in the boiler namespace using
        /// TranslateBrowsePathsToNodeIds.
        /// </summary>
        private async Task<NodeId> ResolveBoilerNodeAsync(
            params string[] tail)
        {
            ushort nsIndex = (ushort)fixture.Session.NamespaceUris
                .GetIndex(kBoilerNamespaceUri);
            await Assert.That(nsIndex).IsGreaterThan((ushort)0);

            var elements = new List<RelativePathElement>
            {
                new()
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName("Boilers", nsIndex)
                },
                new()
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName("Boiler #1", nsIndex)
                }
            };
            foreach (string segment in tail)
            {
                elements.Add(new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName(segment, nsIndex)
                });
            }

            var browsePaths = new List<BrowsePath>
            {
                new()
                {
                    StartingNode = ObjectIds.ObjectsFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = elements.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await fixture.Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, browsePaths.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            await Assert.That(response.Results.Count).IsEqualTo(1);
            BrowsePathResult result = response.Results.ToList()[0];
            await Assert.That(StatusCode.IsGood(result.StatusCode)).IsTrue();
            await Assert.That(result.Targets.Count).IsGreaterThan(0);

            return ExpandedNodeId.ToNodeId(
                result.Targets.ToList()[0].TargetId,
                fixture.Session.NamespaceUris);
        }
    }
}
