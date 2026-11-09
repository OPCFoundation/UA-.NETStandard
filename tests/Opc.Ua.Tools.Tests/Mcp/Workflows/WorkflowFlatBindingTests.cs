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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests.McpCompanion.Workflows
{
    /// <summary>
    /// Crosses the actual SDK binder with the shallow wire contract, never JSON strings hiding a nested document.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WorkflowFlatBindingTests
    {
        /// <summary>
        /// Flat values and indexed elements preserve scalar/array distinctions, quality, timestamps and result order.
        /// </summary>
        [TestCase("history_insert_values", PerformUpdateType.Insert)]
        [TestCase("history_replace_values", PerformUpdateType.Replace)]
        [TestCase("history_update_values", PerformUpdateType.Update)]
        public async Task FlatValueArraysBindToExactDataValuesAndPerItemStatusesAsync(
            string name, PerformUpdateType operation)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryOperationResults = [StatusCodes.GoodClamped, StatusCodes.BadOutOfRange, StatusCodes.Good];
            NodeId node = fixture.Remote.Id("signal");
            var arguments = new JsonObject
            {
                ["nodeId"] = node.ToString(),
                ["values"] = JsonNode.Parse("""
                    [
                      {"type":"Int32","arrayLength":2,"sourceTimestamp":"2026-01-01T00:00:00Z"},
                      {"type":"String","value":"sample","sourceTimestamp":"2026-01-01T02:00:00+02:00",
                       "serverTimestamp":"2026-01-01T00:00:01Z","statusCode":1073741824},
                      {"type":"Boolean","arrayLength":0,"sourceTimestamp":"2026-01-02T00:00:00Z"}
                    ]
                    """),
                ["arrayElements"] = JsonNode.Parse("""
                    [{"valueIndex":0,"elementIndex":1,"value":9},{"valueIndex":0,"elementIndex":0,"value":4}]
                    """)
            };
            CallToolResult result = await fixture.InvokeAsync(name, arguments).ConfigureAwait(false);
            Assert.That(fixture.HistoryUpdates, Has.Count.EqualTo(1));
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateDataDetails? update), Is.True);
            Assert.That(update!.NodeId, Is.EqualTo(node));
            Assert.That(update.PerformInsertReplace, Is.EqualTo(operation));
            Assert.That(update.UpdateValues.Count, Is.EqualTo(3));
            Assert.That(update.UpdateValues[0].WrappedValue.TryGetValue(out ArrayOf<int> numbers), Is.True);
            Assert.That(numbers.ToArray(), Is.EqualTo(s_expectedHistoryNumbers));
            Assert.That(update.UpdateValues[1].WrappedValue, Is.EqualTo(Variant.From("sample")));
            Assert.That(update.UpdateValues[1].StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(update.UpdateValues[1].SourceTimestamp,
                Is.EqualTo((DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(update.UpdateValues[1].ServerTimestamp,
                Is.EqualTo((DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc)));
            Assert.That(update.UpdateValues[2].WrappedValue.TryGetValue(out ArrayOf<bool> empty), Is.True);
            Assert.That(empty.IsEmpty, Is.True);
            Assert.That(empty.IsNull, Is.False);
            Assert.That(result.IsError, Is.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("results").EnumerateArray()
                .Select(item => item.GetProperty("statusCodeValue").GetUInt32()),
                Is.EqualTo(
                [
                    StatusCodes.GoodClamped.Code, StatusCodes.BadOutOfRange.Code, StatusCodes.Good.Code
                ]));
        }

        /// <summary>
        /// The actual binder rejects obsolete wrappers, hidden nested values and malformed array coordinates.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [TestCase("wrapper")]
        [TestCase("nestedArray")]
        [TestCase("object")]
        [TestCase("missingElement")]
        [TestCase("duplicateElement")]
        [TestCase("wrongValueIndex")]
        [TestCase("unknownField")]
        public async Task FlatValueContractRejectsUndeclaredOrAmbiguousShapesBeforeDispatchAsync(string invalid)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            JsonObject arguments = JsonNode.Parse("""
                {"values":[{"type":"Int32","arrayLength":1,"sourceTimestamp":"2026-01-01T00:00:00Z"}],
                 "arrayElements":[{"valueIndex":0,"elementIndex":0,"value":7}]}
                """)!.AsObject();
            arguments["nodeId"] = fixture.Remote.Id("signal").ToString();
            switch (invalid)
            {
                case "wrapper":
                    arguments["input"] = new JsonObject { ["values"] = arguments["values"]!.DeepClone() };
                    arguments.Remove("values");
                    break;
                case "nestedArray":
                    arguments["values"]![0]!.AsObject().Remove("arrayLength");
                    arguments["values"]![0]!["value"] = new JsonArray(7);
                    arguments["arrayElements"] = new JsonArray();
                    break;
                case "object":
                    arguments["arrayElements"]![0]!["value"] = new JsonObject { ["nested"] = 7 };
                    break;
                case "missingElement":
                    arguments["arrayElements"] = new JsonArray();
                    break;
                case "duplicateElement":
                    arguments["values"]![0]!["arrayLength"] = 2;
                    arguments["arrayElements"]!.AsArray().Add(arguments["arrayElements"]![0]!.DeepClone());
                    break;
                case "wrongValueIndex":
                    arguments["arrayElements"]![0]!["valueIndex"] = 1;
                    break;
                case "unknownField":
                    arguments["values"]![0]!["unadvertised"] = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(invalid));
            }
            if (invalid == "unknownField")
            {
                // Nested DTO rejection happens in SDK binding, before the tool's result envelope.
                await Assert.ThatAsync(() => fixture.InvokeAsync(
                    "history_insert_values", arguments),
                    Throws.TypeOf<JsonException>().With.Property(nameof(JsonException.Path)).EqualTo("$.unadvertised"))
                    .ConfigureAwait(false);
                Assert.That(fixture.Remote.Calls, Is.Empty);
                Assert.That(fixture.Remote.Browses, Is.Empty);
                Assert.That(fixture.Remote.Paths, Is.Empty);
                Assert.That(fixture.Remote.Reads, Is.Empty);
                Assert.That(fixture.Writes, Is.Empty);
                Assert.That(fixture.HistoryReads, Is.Empty);
            }
            else
            {
                CallToolResult result = await fixture.InvokeAsync("history_insert_values", arguments)
                    .ConfigureAwait(false);
                Assert.That(result.IsError, Is.True);
            }
            Assert.That(fixture.HistoryUpdates, Is.Empty);
        }

        /// <summary>
        /// Separate flat path segments retain exact per-segment namespaces through the real event-read binder.
        /// </summary>
        [Test]
        public async Task FlatEventReadPathsPreserveSelectedNamespacesAndOrderAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryPages.Enqueue(new HistoryReadResult
            {
                StatusCode = StatusCodes.Good,
                HistoryData = new ExtensionObject(new HistoryEvent
                {
                    Events = [new HistoryEventFieldList
                    {
                        EventFields = [Variant.From(ByteString.From(new byte[] { 1 })), Variant.From(false)]
                    }]
                })
            });
            var arguments = new JsonObject
            {
                ["nodeId"] = fixture.Remote.Id("notifier").ToString(),
                ["startTime"] = "2026-01-01T00:00:00Z",
                ["endTime"] = "2026-01-02T00:00:00Z",
                ["select"] = JsonNode.Parse("""[{"browseName":"EventId"},{"typeDefinitionId":"i=2782"}]"""),
                ["paths"] = JsonNode.Parse("""
                    [{"selectIndex":1,"segmentIndex":1,"name":"Id"},
                     {"selectIndex":1,"segmentIndex":0,"namespaceUri":"urn:test:remote-model","name":"State"}]
                    """)
            };
            CallToolResult result = await fixture.InvokeAsync("history_read_events", arguments).ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryReads, Has.Count.EqualTo(1));
            Assert.That(fixture.HistoryReads[0].Details.TryGetValue(out ReadEventDetails? details), Is.True);
            Assert.That(details!.Filter.SelectClauses[0].BrowsePath.ToArray(),
                Is.EqualTo([new QualifiedName(BrowseNames.EventId)]));
            Assert.That(details.Filter.SelectClauses[1].BrowsePath.ToArray(),
                Is.EqualTo(
                [
                    new QualifiedName("State", fixture.Remote.InstanceNamespace), new QualifiedName("Id")
                ]));
        }

        /// <summary>
        /// Flat event cells become complete event rows in explicit event/field-index order.
        /// </summary>
        [TestCase("history_insert_events", PerformUpdateType.Insert)]
        [TestCase("history_replace_events", PerformUpdateType.Replace)]
        [TestCase("history_update_events", PerformUpdateType.Update)]
        public async Task FlatEventCellsBindExactRowsAndStandardFieldTypesAsync(
            string name, PerformUpdateType operation)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.HistoryOperationResults = [StatusCodes.Good, StatusCodes.BadEntryExists];
            var arguments = new JsonObject
            {
                ["nodeId"] = fixture.Remote.Id("notifier").ToString(),
                ["select"] = JsonNode.Parse("""
                    [{"browseName":"EventId"},{"browseName":"EventType"},{"browseName":"Time"},
                     {"browseName":"Samples","namespaceUri":"urn:test:remote-model"}]
                    """),
                ["events"] = JsonNode.Parse("""
                    [
                      {"eventIndex":1,"fieldIndex":2,"type":"DateTime","value":"2026-01-02T00:00:00Z"},
                      {"eventIndex":0,"fieldIndex":0,"type":"ByteString","value":"AQ=="},
                      {"eventIndex":1,"fieldIndex":0,"type":"ByteString","value":"Ag=="},
                      {"eventIndex":0,"fieldIndex":1,"type":"NodeId","value":"i=2041"},
                      {"eventIndex":1,"fieldIndex":1,"type":"NodeId","value":"i=2041"},
                      {"eventIndex":0,"fieldIndex":2,"type":"DateTime","value":"2026-01-01T00:00:00Z"},
                      {"eventIndex":0,"fieldIndex":3,"type":"Int32","arrayLength":0},
                      {"eventIndex":1,"fieldIndex":3,"type":"Int32","arrayLength":2}
                    ]
                    """),
                ["arrayElements"] = JsonNode.Parse("""
                    [{"valueIndex":7,"elementIndex":1,"value":9},{"valueIndex":7,"elementIndex":0,"value":4}]
                    """)
            };
            CallToolResult result = await fixture.InvokeAsync(name, arguments).ConfigureAwait(false);
            Assert.That(fixture.HistoryUpdates, Has.Count.EqualTo(1));
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateEventDetails? update), Is.True);
            Assert.That(update!.PerformInsertReplace, Is.EqualTo(operation));
            Assert.That(update.EventData.Count, Is.EqualTo(2));
            Assert.That(update.EventData[0].EventFields[0],
                Is.EqualTo(Variant.From(ByteString.From(new byte[] { 1 }))));
            Assert.That(update.EventData[1].EventFields[0],
                Is.EqualTo(Variant.From(ByteString.From(new byte[] { 2 }))));
            Assert.That(update.EventData[0].EventFields[1], Is.EqualTo(Variant.From(ObjectTypeIds.BaseEventType)));
            Assert.That(update.EventData[1].EventFields[2],
                Is.EqualTo(Variant.From((DateTimeUtc)new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc))));
            Assert.That(update.EventData[0].EventFields[3].TryGetValue(out ArrayOf<int> empty), Is.True);
            Assert.That(empty.IsEmpty, Is.True);
            Assert.That(empty.IsNull, Is.False);
            Assert.That(update.EventData[1].EventFields[3].TryGetValue(out ArrayOf<int> array), Is.True);
            Assert.That(array.ToArray(), Is.EqualTo(s_expectedHistoryNumbers));
            Assert.That(result.IsError, Is.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("results")[1]
                .GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(StatusCodes.BadEntryExists.Code));
        }

        /// <summary>
        /// Root annotation arrays bind as typed standard structures with explicit UTC timestamps.
        /// </summary>
        [TestCase("history_insert_annotations", PerformUpdateType.Insert)]
        [TestCase("history_replace_annotations", PerformUpdateType.Replace)]
        [TestCase("history_update_annotations", PerformUpdateType.Update)]
        public async Task RootAnnotationArrayBindsExactStructureAndTimestampAsync(
            string name, PerformUpdateType operation)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("signal");
            NodeId annotations = fixture.Remote.Id("annotations");
            fixture.Remote.Child(node, new QualifiedName(BrowseNames.Annotations), annotations);
            CallToolResult result = await fixture.InvokeAsync(name, new JsonObject
            {
                ["nodeId"] = node.ToString(),
                ["annotations"] = JsonNode.Parse("""
                    [{"annotationTime":"2026-01-01T02:00:00+02:00","message":"Calibrated","userName":"operator"}]
                    """)
            }).ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.HistoryUpdates[0].TryGetValue(out UpdateStructureDataDetails? update), Is.True);
            Assert.That(update!.NodeId, Is.EqualTo(annotations));
            Assert.That(update.PerformInsertReplace, Is.EqualTo(operation));
            Assert.That(update.UpdateValues[0].WrappedValue.TryGetStructure<Annotation>(out Annotation? annotation),
                Is.True);
            Assert.That(annotation!.Message, Is.EqualTo("Calibrated"));
            Assert.That(annotation.UserName, Is.EqualTo("operator"));
            Assert.That(annotation.AnnotationTime,
                Is.EqualTo((DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        /// <summary>
        /// Malformed flattened selector paths are refused rather than reinterpreted as another namespace or field.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [TestCase("duplicate")]
        [TestCase("gap")]
        [TestCase("wrongSelector")]
        [TestCase("bothForms")]
        [TestCase("unknownNamespace")]
        public async Task FlatEventPathErrorsRefuseBeforeHistoryReadAsync(string invalid)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            JsonObject arguments = JsonNode.Parse("""
                {"startTime":"2026-01-01T00:00:00Z","endTime":"2026-01-02T00:00:00Z",
                 "select":[{"typeDefinitionId":"i=2782"}],
                 "paths":[{"selectIndex":0,"segmentIndex":0,"name":"EnabledState"}]}
                """)!.AsObject();
            arguments["nodeId"] = fixture.Remote.Id("notifier").ToString();
            switch (invalid)
            {
                case "duplicate":
                    arguments["paths"]!.AsArray().Add(arguments["paths"]![0]!.DeepClone());
                    break;
                case "gap":
                    arguments["paths"]![0]!["segmentIndex"] = 1;
                    break;
                case "wrongSelector":
                    arguments["paths"]![0]!["selectIndex"] = 1;
                    break;
                case "bothForms":
                    arguments["select"]![0]!["browseName"] = "EnabledState";
                    break;
                case "unknownNamespace":
                    arguments["paths"]![0]!["namespaceUri"] = "urn:not-advertised";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(invalid));
            }
            CallToolResult result = await fixture.InvokeAsync("history_read_events", arguments).ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            Assert.That(fixture.HistoryReads, Is.Empty);
        }

        /// <summary>
        /// Standalone generation options bind as typed arrays or omitted/null defaults without a nested wrapper.
        /// </summary>
        [TestCase("array")]
        [TestCase("omitted")]
        [TestCase("null")]
        public async Task TemporaryGenerationBindsFlatOptionDiscriminatorAndArrayAsync(string form)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync(files: true).ConfigureAwait(false);
            NodeId transfer = fixture.Remote.Id("transfer");
            NodeId file = fixture.Remote.Id("generated");
            fixture.Remote.Type(transfer, ObjectTypeIds.TemporaryFileTransferType);
            fixture.Remote.Type(file, ObjectTypeIds.FileType);
            fixture.Remote.OnCall = request =>
            {
                if (request.MethodId == MethodIds.TemporaryFileTransferType_GenerateFileForRead)
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(file), Variant.From(71u), Variant.From(NodeId.Null)]
                    };
                }
                if (request.MethodId == MethodIds.FileType_Read)
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(ByteString.Empty)]
                    };
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
            var arguments = new JsonObject
            {
                ["transferId"] = transfer.ToString(),
                ["filePath"] = "generated.bin"
            };
            if (form == "array")
            {
                arguments["optionType"] = "Int32";
                arguments["optionValue"] = new JsonArray(5, 7);
            }
            else if (form == "null")
            {
                arguments["optionValue"] = null;
            }
            CallToolResult result = await fixture.InvokeAsync("files_read_temporary", arguments).ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            CallMethodRequest generate = fixture.Remote.Calls[0];
            Assert.That(generate.ObjectId, Is.EqualTo(transfer));
            if (form == "array")
            {
                Assert.That(generate.InputArguments[0].TryGetValue(out ArrayOf<int> options), Is.True);
                Assert.That(options.ToArray(), Is.EqualTo(s_expectedGenerationOptions));
            }
            else
            {
                Assert.That(generate.InputArguments[0].IsNull, Is.True);
            }
            Assert.That(fixture.Remote.Calls.Count(call => call.MethodId == MethodIds.FileType_Close), Is.EqualTo(1));
        }

        /// <summary>
        /// Expected integer elements after historical array coordinates are applied.
        /// </summary>
        private static readonly int[] s_expectedHistoryNumbers = [4, 9];

        /// <summary>
        /// Expected explicitly typed temporary-file generation options.
        /// </summary>
        private static readonly int[] s_expectedGenerationOptions = [5, 7];
    }
}
#endif
