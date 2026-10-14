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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion.Workflows
{
    /// <summary>
    /// Tests the historian's actual query/update payloads and per-item outcomes independently of cursor ownership.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WorkflowHistoryTests
    {
        /// <summary>
        /// Raw and modified reads remain distinct and preserve their real audit/quality metadata.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RawAndModifiedReadsPreserveUtcQueryAndQualityWithoutConflatingAuditInfoAsync(bool modified)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            DataValue data = Data(4.5, StatusCodes.Uncertain);
            fixture.HistoryPages.Enqueue(new HistoryReadResult
            {
                StatusCode = StatusCodes.Good,
                HistoryData = modified
                    ? new ExtensionObject(new HistoryModifiedData
                    {
                        DataValues = [data],
                        ModificationInfos =
                        [
                            new ModificationInfo
                            {
                                ModificationTime = kTime, UpdateType = HistoryUpdateType.Replace, UserName = "archivist"
                            }
                        ]
                    })
                    : new ExtensionObject(new HistoryData { DataValues = [data] })
            });
            string node = fixture.Remote.Id("signal").ToString();
            CallToolResult result = modified
                ? await fixture.History.ReadModifiedAsync(
                    node, "2026-01-01T02:00:00+02:00", "2026-01-02T00:00:00Z",
                    maxResults: 2, sessionName: "selected").ConfigureAwait(false)
                : await fixture.History.ReadRawAsync(
                    node, "2026-01-01T02:00:00+02:00", "2026-01-02T00:00:00Z",
                    returnBounds: true, maxResults: 2, sessionName: "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryReads, Has.Count.EqualTo(1));
            Assert.That(fixture.HistoryReads[0].Details.TryGetValue(out ReadRawModifiedDetails? details), Is.True);
            Assert.That(details!.StartTime, Is.EqualTo((DateTimeUtc)kTime));
            Assert.That(details.EndTime, Is.EqualTo((DateTimeUtc)kTime.AddDays(1)));
            Assert.That(details.IsReadModified, Is.EqualTo(modified));
            Assert.That(details.ReturnBounds, Is.EqualTo(!modified));
            Assert.That(details.NumValuesPerNode, Is.EqualTo(2));
            Assert.That(fixture.HistoryReads[0].Timestamps, Is.EqualTo(TimestampsToReturn.Both));
            JsonElement item = RemoteModelTestSession.Payload(result).GetProperty("items")[0];
            DataValue decoded = DecodeData(modified ? item.GetProperty("dataValue") : item, fixture);
            Assert.That(decoded.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(decoded.WrappedValue, Is.EqualTo(Variant.From(4.5)));
            Assert.That(decoded.SourceTimestamp, Is.EqualTo((DateTimeUtc)kTime));
            if (modified)
            {
                Assert.That(item.GetProperty("modificationInfo").GetProperty("UserName").GetString(),
                    Is.EqualTo("archivist"));
            }
        }

        /// <summary>
        /// The MCP host's decoded-page ceiling reaches the SDK before projection and still releases its own point.
        /// </summary>
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        public async Task HistoricalReadEnforcesHostDecodedPageLimitAndReleasesContinuationAsync(
            int count, bool modified)
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
                options: new WorkflowOptions { MaxItems = 3 }).ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("bounded-history");
            var point = ByteString.From(new byte[] { 9, 7 });
            var values = Enumerable.Range(1, count).Select(index => Data(index)).ToArrayOf();
            fixture.HistoryPages.Enqueue(new HistoryReadResult
            {
                StatusCode = StatusCodes.Good,
                ContinuationPoint = point,
                HistoryData = modified
                    ? new ExtensionObject(new HistoryModifiedData
                    {
                        DataValues = values,
                        ModificationInfos = Enumerable.Range(1, count).Select(_ => new ModificationInfo
                        {
                            ModificationTime = kTime,
                            UpdateType = HistoryUpdateType.Replace,
                            UserName = "editor"
                        }).ToArrayOf()
                    })
                    : new ExtensionObject(new HistoryData { DataValues = values })
            });
            CallToolResult result = modified
                ? await fixture.History.ReadModifiedAsync(
                    node.ToString(), kStart, kEnd, maxResults: 1, sessionName: "selected").ConfigureAwait(false)
                : await fixture.History.ReadRawAsync(
                    node.ToString(), kStart, kEnd, maxResults: 1, sessionName: "selected").ConfigureAwait(false);
            if (count > 3)
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadEncodingLimitsExceeded);
            }
            else
            {
                Assert.That(result.IsError, Is.False);
                Assert.That(RemoteModelTestSession.Payload(result).GetProperty("items").GetArrayLength(),
                    Is.EqualTo(1));
            }
            Assert.That(fixture.HistoryReads.Count(read => !read.Release), Is.EqualTo(1));
            WorkflowHistoryRead release = fixture.HistoryReads.Single(read => read.Release);
            Assert.That(release.Nodes[0].NodeId, Is.EqualTo(node));
            Assert.That(release.Nodes[0].ContinuationPoint, Is.EqualTo(point));
            Assert.That(release.Token.CanBeCanceled, Is.False);
        }

        /// <summary>
        /// At-time reads preserve the selected timestamp order and actual details discriminator.
        /// </summary>
        [Test]
        public async Task AtTimeReadPreservesOrderedUtcTimesAndSimpleBoundsAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryPages.Enqueue(Page([Data(1), Data(2)]));
            CallToolResult result = await fixture.History.ReadAtTimeAsync(
                fixture.Remote.Id("signal").ToString(),
                ["2026-01-01T01:00:00+01:00", "2026-01-01T01:00:00Z"],
                useSimpleBounds: true, sessionName: "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryReads[0].Details.TryGetValue(out ReadAtTimeDetails? details), Is.True);
            Assert.That(details!.UseSimpleBounds, Is.True);
            Assert.That(details.ReqTimes.ToArray(), Is.EqualTo(new DateTimeUtc[] { kTime, kTime.AddHours(1) }));
        }

        /// <summary>
        /// Processed reads require the exact server-advertised aggregate, not a fabricated standard list.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ProcessedReadRequiresRealAggregateAndPreservesItsExactIdentifierAsync(bool advertised)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId folder = fixture.Remote.Id("aggregates");
            NodeId aggregate = fixture.Remote.Id("custom-aggregate");
            fixture.Remote.Child(ObjectIds.HistoryServerCapabilities, new QualifiedName(BrowseNames.AggregateFunctions),
                folder);
            if (advertised)
            {
                fixture.Remote.Browse(folder, ReferenceTypeIds.HierarchicalReferences,
                    [fixture.Remote.Reference(aggregate, ObjectTypeIds.BaseObjectType)]);
            }
            fixture.HistoryPages.Enqueue(Page([Data(7)]));
            CallToolResult result = await fixture.History.ReadProcessedAsync(
                fixture.Remote.Id("signal").ToString(), aggregate.ToString(),
                "2026-01-01T00:00:00Z", "2026-01-01T00:00:01Z", 1000, sessionName: "selected")
                .ConfigureAwait(false);
            if (advertised)
            {
                Assert.That(result.IsError, Is.False);
                Assert.That(fixture.HistoryReads[0].Details.TryGetValue(out ReadProcessedDetails? details), Is.True);
                Assert.That(details!.AggregateType.ToArray(), Is.EqualTo([aggregate]));
                Assert.That(details.ProcessingInterval, Is.EqualTo(1000));
                Assert.That(details.AggregateConfiguration.UseServerCapabilitiesDefaults, Is.True);
            }
            else
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadAggregateNotSupported);
                Assert.That(fixture.HistoryReads, Is.Empty);
            }
        }

        /// <summary>
        /// Typed event selectors bind qualified paths and values without metadata reflection.
        /// </summary>
        [Test]
        public async Task EventReadPreservesExactNamespaceSelectOrderAndTypedFieldsAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            var input = new WorkflowHistoryEventsInput
            {
                Select =
                [
                    Field(BrowseNames.EventId),
                    new WorkflowEventFieldInput
                    {
                        TypeDefinitionId = fixture.Remote.Id("vendor-event").ToString(),
                        NamespaceUri = "urn:test:remote-model", BrowseName = "VendorField"
                    }
                ]
            };
            fixture.HistoryPages.Enqueue(new HistoryReadResult
            {
                StatusCode = StatusCodes.Good,
                HistoryData = new ExtensionObject(new HistoryEvent
                {
                    Events =
                    [
                        new HistoryEventFieldList
                        {
                            EventFields = [Variant.From(ByteString.From("\t"u8.ToArray())), Variant.From(false)]
                        }
                    ]
                })
            });
            CallToolResult result = await fixture.History.ReadEventsAsync(
                fixture.Remote.Id("notifier").ToString(), kStart, kEnd, input.Select, input.Paths,
                sessionName: "selected")
                .ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryReads[0].Details.TryGetValue(out ReadEventDetails? details), Is.True);
            Assert.That(details!.Filter.SelectClauses[0].BrowsePath[0],
                Is.EqualTo(new QualifiedName(BrowseNames.EventId)));
            Assert.That(details.Filter.SelectClauses[1].BrowsePath[0],
                Is.EqualTo(new QualifiedName("VendorField", fixture.Remote.InstanceNamespace)));
            Assert.That(details.NumValuesPerNode, Is.EqualTo(100));
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("items").GetArrayLength(), Is.EqualTo(1));
        }

        /// <summary>
        /// Annotation reads preserve DataValue quality rather than returning only decoded bodies.
        /// </summary>
        [Test]
        public async Task AnnotationReadUsesActualPropertyAndRetainsUncertainQualityAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId variable = fixture.Remote.Id("signal");
            NodeId annotationNode = fixture.Remote.Id("annotations");
            fixture.Remote.Child(variable, new QualifiedName(BrowseNames.Annotations), annotationNode);
            var annotation = new Annotation { AnnotationTime = kTime, Message = "Checked", UserName = "archivist" };
            fixture.HistoryPages.Enqueue(Page(
                [new DataValue(Variant.FromStructure(annotation), StatusCodes.Uncertain, kTime, kTime.AddSeconds(1))]));
            CallToolResult result = await fixture.History.ReadAnnotationsAsync(
                variable.ToString(), kStart, kEnd, sessionName: "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryReads[0].Nodes[0].NodeId, Is.EqualTo(annotationNode));
            DataValue value = DecodeData(RemoteModelTestSession.Payload(result).GetProperty("items")[0], fixture);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(value.SourceTimestamp, Is.EqualTo((DateTimeUtc)kTime));
            Assert.That(value.WrappedValue.TryGetStructure<Annotation>(
                fixture.Remote.Context, out Annotation? decoded), Is.True);
            Assert.That(decoded!.Message, Is.EqualTo("Checked"));
        }

        /// <summary>
        /// Each data-update verb sends its own enum and preserves mixed per-item results.
        /// </summary>
        [TestCase(PerformUpdateType.Insert)]
        [TestCase(PerformUpdateType.Replace)]
        [TestCase(PerformUpdateType.Update)]
        public async Task EveryDataUpdatePreservesExactTypedPayloadAndMixedStatusesAsync(PerformUpdateType operation)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryOperationResults = [StatusCodes.GoodClamped, StatusCodes.BadOutOfRange];
            var input = new WorkflowDataValuesInput
            {
                Values =
                [
                    new WorkflowDataValueInput
                    {
                        Type = BuiltInType.Int32, Value = Value(BuiltInType.Int32, "0").Value, SourceTimestamp = kStart
                    },
                    new WorkflowDataValueInput
                    {
                        Type = BuiltInType.Boolean, Value = Value(BuiltInType.Boolean, "false").Value,
                        SourceTimestamp = "2026-01-01T01:00:00Z",
                        ServerTimestamp = "2026-01-01T01:00:01Z", StatusCode = StatusCodes.Uncertain.Code
                    }
                ]
            };
            string node = fixture.Remote.Id("signal").ToString();
            CallToolResult result = operation switch
            {
                PerformUpdateType.Insert => await fixture.History.InsertValuesAsync(
                    node, input.Values, input.ArrayElements, "selected")
                    .ConfigureAwait(false),
                PerformUpdateType.Replace => await fixture.History.ReplaceValuesAsync(
                    node, input.Values, input.ArrayElements, "selected")
                    .ConfigureAwait(false),
                _ => await fixture.History.UpdateValuesAsync(node, input.Values, input.ArrayElements, "selected")
                    .ConfigureAwait(false)
            };
            Assert.That(fixture.HistoryUpdates, Has.Count.EqualTo(1));
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateDataDetails? details), Is.True);
            Assert.That(details!.NodeId.ToString(), Is.EqualTo(node));
            Assert.That(details.PerformInsertReplace, Is.EqualTo(operation));
            Assert.That(details.UpdateValues[0].WrappedValue, Is.EqualTo(Variant.From(0)));
            Assert.That(details.UpdateValues[1].WrappedValue, Is.EqualTo(Variant.From(false)));
            Assert.That(details.UpdateValues[1].StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(details.UpdateValues[1].ServerTimestamp,
                Is.EqualTo((DateTimeUtc)kTime.AddHours(1).AddSeconds(1)));
            AssertResults(result, StatusCodes.GoodClamped, StatusCodes.BadOutOfRange);
        }

        /// <summary>
        /// Every event-update verb carries exact select clauses, typed occurrence ids and ordered statuses.
        /// </summary>
        [TestCase(PerformUpdateType.Insert)]
        [TestCase(PerformUpdateType.Replace)]
        [TestCase(PerformUpdateType.Update)]
        public async Task EveryEventUpdatePreservesTypedFieldsAndPerEventStatusAsync(PerformUpdateType operation)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryOperationResults = [StatusCodes.Good, StatusCodes.BadEntryExists];
            WorkflowHistoryEventsInput input = EventInput();
            string node = fixture.Remote.Id("notifier").ToString();
            CallToolResult result = operation switch
            {
                PerformUpdateType.Insert => await fixture.History.InsertEventsAsync(
                    node, input.Select, input.Events, input.Paths, input.ArrayElements, "selected")
                    .ConfigureAwait(false),
                PerformUpdateType.Replace => await fixture.History.ReplaceEventsAsync(
                    node, input.Select, input.Events, input.Paths, input.ArrayElements, "selected")
                    .ConfigureAwait(false),
                _ => await fixture.History.UpdateEventsAsync(
                    node, input.Select, input.Events, input.Paths, input.ArrayElements, "selected")
                    .ConfigureAwait(false)
            };
            Assert.That(fixture.HistoryUpdates, Has.Count.EqualTo(1));
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateEventDetails? details), Is.True);
            Assert.That(details!.NodeId.ToString(), Is.EqualTo(node));
            Assert.That(details.PerformInsertReplace, Is.EqualTo(operation));
            Assert.That(details.Filter.SelectClauses.ToArray()!.Select(field => field.BrowsePath[0].Name),
                Is.EqualTo([BrowseNames.EventId, BrowseNames.EventType, BrowseNames.Time]));
            Assert.That(details.EventData[0].EventFields[0],
                Is.EqualTo(Variant.From(ByteString.From(new byte[] { 1 }))));
            Assert.That(details.EventData[1].EventFields[0],
                Is.EqualTo(Variant.From(ByteString.From(new byte[] { 2 }))));
            AssertResults(result, StatusCodes.Good, StatusCodes.BadEntryExists);
        }

        /// <summary>
        /// Annotation verbs resolve the actual property and use UpdateStructureData with exact timestamps.
        /// </summary>
        [TestCase(PerformUpdateType.Insert)]
        [TestCase(PerformUpdateType.Replace)]
        [TestCase(PerformUpdateType.Update)]
        public async Task EveryAnnotationUpdateUsesExactPropertyStructureAndStatusesAsync(PerformUpdateType operation)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId signal = fixture.Remote.Id("signal");
            NodeId annotationNode = fixture.Remote.Id("annotations");
            fixture.Remote.Child(signal, new QualifiedName(BrowseNames.Annotations), annotationNode);
            fixture.HistoryOperationResults = [StatusCodes.GoodClamped, StatusCodes.BadNoEntryExists];
            var input = new WorkflowAnnotationsInput
            {
                Annotations =
                [
                    new WorkflowAnnotationInput { AnnotationTime = kStart, Message = "first", UserName = "auditor" },
                    new WorkflowAnnotationInput { AnnotationTime = kEnd, Message = string.Empty }
                ]
            };
            CallToolResult result = operation switch
            {
                PerformUpdateType.Insert => await fixture.History.InsertAnnotationsAsync(
                    signal.ToString(), input.Annotations, "selected").ConfigureAwait(false),
                PerformUpdateType.Replace => await fixture.History.ReplaceAnnotationsAsync(
                    signal.ToString(), input.Annotations, "selected").ConfigureAwait(false),
                _ => await fixture.History.UpdateAnnotationsAsync(signal.ToString(), input.Annotations, "selected")
                    .ConfigureAwait(false)
            };
            Assert.That(fixture.HistoryUpdates, Has.Count.EqualTo(1));
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateStructureDataDetails? details), Is.True);
            Assert.That(details!.NodeId, Is.EqualTo(annotationNode));
            Assert.That(details.PerformInsertReplace, Is.EqualTo(operation));
            Assert.That(details.UpdateValues[0].SourceTimestamp, Is.EqualTo((DateTimeUtc)kTime));
            Assert.That(details.UpdateValues[0].WrappedValue.TryGetStructure<Annotation>(
                out Annotation? annotation), Is.True);
            Assert.That(annotation!.Message, Is.EqualTo("first"));
            Assert.That(annotation.UserName, Is.EqualTo("auditor"));
            AssertResults(result, StatusCodes.GoodClamped, StatusCodes.BadNoEntryExists);
        }

        /// <summary>
        /// Range deletion preserves raw/modified selection and half-open range timestamps.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RangeDeletePreservesExactTargetTimesAndModifiedFlagAsync(bool modified)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryUpdateStatus = StatusCodes.BadUserAccessDenied;
            fixture.HistoryOperationResults = [];
            NodeId node = fixture.Remote.Id("signal");
            CallToolResult result = await fixture.History.DeleteRangeAsync(
                node.ToString(), kStart, kEnd, modified, "selected").ConfigureAwait(false);
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out DeleteRawModifiedDetails? details), Is.True);
            Assert.That(details!.NodeId, Is.EqualTo(node));
            Assert.That(details.IsDeleteModified, Is.EqualTo(modified));
            Assert.That(details.StartTime, Is.EqualTo((DateTimeUtc)kTime));
            Assert.That(details.EndTime, Is.EqualTo((DateTimeUtc)kTime.AddDays(1)));
            AssertResults(result, StatusCodes.BadUserAccessDenied);
        }

        /// <summary>
        /// At-time deletion is distinct from range deletion and returns per-timestamp outcomes.
        /// </summary>
        [Test]
        public async Task AtTimeDeletePreservesExactTimestampArrayAndMixedResultsAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryOperationResults = [StatusCodes.Good, StatusCodes.BadNoEntryExists];
            NodeId node = fixture.Remote.Id("signal");
            CallToolResult result = await fixture.History.DeleteAtTimeAsync(
                node.ToString(), [kStart, kEnd], "selected")
                .ConfigureAwait(false);
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out DeleteAtTimeDetails? details), Is.True);
            Assert.That(details!.NodeId, Is.EqualTo(node));
            Assert.That(details.ReqTimes.ToArray(), Is.EqualTo(new DateTimeUtc[] { kTime, kTime.AddDays(1) }));
            AssertResults(result, StatusCodes.Good, StatusCodes.BadNoEntryExists);
        }

        /// <summary>
        /// Event deletion does not reinterpret EventIds as names or timestamps.
        /// </summary>
        [Test]
        public async Task EventDeletePreservesBinaryIdentifiersAndOrderedServerResultsAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryOperationResults = [StatusCodes.BadNoEntryExists, StatusCodes.Good];
            NodeId node = fixture.Remote.Id("notifier");
            CallToolResult result = await fixture.History.DeleteEventsAsync(
                node.ToString(), ["AQ==", "AgM="], "selected")
                .ConfigureAwait(false);
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out DeleteEventDetails? details), Is.True);
            Assert.That(details!.NodeId, Is.EqualTo(node));
            Assert.That(details.EventIds.ToArray(),
                Is.EqualTo([ByteString.From(new byte[] { 1 }), ByteString.From(new byte[] { 2, 3 })]));
            AssertResults(result, StatusCodes.BadNoEntryExists, StatusCodes.Good);
        }

        /// <summary>
        /// Annotation deletion uses structure Remove rather than deleting a raw data sample.
        /// </summary>
        [Test]
        public async Task AnnotationDeleteUsesRemoveOnActualAnnotationPropertyAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("signal");
            NodeId annotations = fixture.Remote.Id("annotations");
            fixture.Remote.Child(node, new QualifiedName(BrowseNames.Annotations), annotations);
            CallToolResult result = await fixture.History.DeleteAnnotationsAsync(
                node.ToString(), [kStart], "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateStructureDataDetails? details), Is.True);
            Assert.That(details!.NodeId, Is.EqualTo(annotations));
            Assert.That(details.PerformInsertReplace, Is.EqualTo(PerformUpdateType.Remove));
            Assert.That(details.UpdateValues[0].SourceTimestamp, Is.EqualTo((DateTimeUtc)kTime));
            Assert.That(details.UpdateValues[0].WrappedValue.TryGetStructure<Annotation>(
                out Annotation? annotation), Is.True);
            Assert.That(annotation!.Message, Is.Empty);
        }

        /// <summary>
        /// Time zone omission, malformed offsets and impossible calendar dates fail before any historian request.
        /// </summary>
        [TestCase("2026-01-01T00:00:00")]
        [TestCase("2026-02-30T00:00:00Z")]
        [TestCase("2026-01-01")]
        [TestCase("2026-01-01T00:00:00+15:00")]
        [TestCase("2026-01-01T00:00:00+2")]
        public async Task HistoryRefusesAmbiguousOrInvalidTimesBeforeRemoteWorkAsync(string time)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.History.ReadRawAsync(
                fixture.Remote.Id("signal").ToString(), time, kEnd, sessionName: "selected").ConfigureAwait(false);
            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.HistoryReads, Is.Empty);
        }

        /// <summary>
        /// Batch size boundaries are enforced before dispatch without changing insert/replace semantics.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(500, true)]
        [TestCase(501, false)]
        public async Task HistoryBatchSizeHasExactBoundariesAsync(int count, bool accepted)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryOperationResults = Enumerable.Repeat(StatusCodes.Good, count).ToArrayOf();
            var input = new WorkflowDataValuesInput
            {
                Values = Enumerable.Range(0, count).Select(index => new WorkflowDataValueInput
                {
                    Type = BuiltInType.Int32,
                    Value = Value(BuiltInType.Int32, index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Value,
                    SourceTimestamp = kStart
                }).ToArrayOf()
            };
            CallToolResult result = await fixture.History.InsertValuesAsync(
                fixture.Remote.Id("signal").ToString(), input.Values, input.ArrayElements, "selected")
                .ConfigureAwait(false);
            Assert.That(result.IsError, Is.EqualTo(!accepted));
            Assert.That(fixture.HistoryUpdates, Has.Count.EqualTo(accepted ? 1 : 0));
        }

        /// <summary>
        /// Creates an explicit typed value without reflection-based serialization.
        /// </summary>
        internal static WorkflowValueInput Value(BuiltInType type, string json)
        {
            using var document = JsonDocument.Parse(json);
            return new WorkflowValueInput { Type = type, Value = document.RootElement.Clone() };
        }

        /// <summary>
        /// Creates a finite history page for continuation tests.
        /// </summary>
        internal static HistoryReadResult Page(ArrayOf<DataValue> values, ByteString continuation = default)
        {
            return new HistoryReadResult
            {
                StatusCode = StatusCodes.Good,
                ContinuationPoint = continuation,
                HistoryData = new ExtensionObject(new HistoryData { DataValues = values })
            };
        }

        /// <summary>
        /// Creates a deterministic quality-bearing historical sample.
        /// </summary>
        internal static DataValue Data(double value, StatusCode status = default)
        {
            return new DataValue(Variant.From(value), status, kTime, kTime.AddSeconds(1));
        }

        /// <summary>
        /// Decodes the value envelope without depending on incidental JSON formatting.
        /// </summary>
        private static DataValue DecodeData(JsonElement value, WorkflowTestContext fixture)
        {
            using var decoder = new JsonDecoder("{\"value\":" + value.GetRawText() + "}", fixture.Remote.Context);
            return decoder.ReadDataValue("value");
        }

        /// <summary>
        /// Asserts the entire ordered status array and the aggregate tool failure flag.
        /// </summary>
        private static void AssertResults(CallToolResult result, params StatusCode[] expected)
        {
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("results").EnumerateArray()
                .Select(item => item.GetProperty("statusCodeValue").GetUInt32()),
                Is.EqualTo(expected.Select(status => status.Code)));
            Assert.That(result.IsError, Is.EqualTo(expected.Any(StatusCode.IsBad)));
            Assert.That(json.GetProperty("atomic").GetBoolean(), Is.False);
        }

        /// <summary>
        /// Builds one namespace-zero standard event field.
        /// </summary>
        private static WorkflowEventFieldInput Field(string name)
        {
            return new WorkflowEventFieldInput
            {
                BrowseName = name
            };
        }

        /// <summary>
        /// Builds two events with the mandatory SDK-supported event selectors.
        /// </summary>
        private static WorkflowHistoryEventsInput EventInput()
        {
            return new WorkflowHistoryEventsInput
            {
                Select = [Field(BrowseNames.EventId), Field(BrowseNames.EventType), Field(BrowseNames.Time)],
                Events =
                [
                    EventCell(0, 0, Value(BuiltInType.ByteString, "\"AQ==\"")),
                    EventCell(0, 1, Value(BuiltInType.NodeId, "\"i=2041\"")),
                    EventCell(0, 2, Value(BuiltInType.DateTime, "\"" + kStart + "\"")),
                    EventCell(1, 0, Value(BuiltInType.ByteString, "\"Ag==\"")),
                    EventCell(1, 1, Value(BuiltInType.NodeId, "\"i=2041\"")),
                    EventCell(1, 2, Value(BuiltInType.DateTime, "\"" + kEnd + "\""))
                ]
            };
        }

        /// <summary>
        /// Creates a flat event cell with explicit event and selected-field indexes.
        /// </summary>
        private static WorkflowHistoryEventInput EventCell(int eventIndex, int fieldIndex, WorkflowValueInput value)
        {
            return new WorkflowHistoryEventInput
            {
                EventIndex = eventIndex,
                FieldIndex = fieldIndex,
                Type = value.Type,
                Value = value.Value
            };
        }

        private const string kStart = "2026-01-01T00:00:00Z";
        private const string kEnd = "2026-01-02T00:00:00Z";
        private static readonly DateTime kTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
#endif
