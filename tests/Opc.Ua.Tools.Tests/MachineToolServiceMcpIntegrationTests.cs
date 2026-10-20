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
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tests;
using V2 = Opc.Ua.ISA95.JobControl.V2;
using static Opc.Ua.Tools.Tests.MachineToolServiceMcpHarness;
using static Opc.Ua.Tools.Tests.MachineToolServiceMcpParameterTests;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Verifies MCP transport and schemas through generated clients to actual provider-backed opc.tcp services.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineToolServiceMcpIntegrationTests
    {
        /// <summary>
        /// Starts the real machine runtime and borrows three independently authenticated sessions into MCP.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new MachineToolServiceMcpFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_mcp = await MachineToolServiceMcpHarness.CreateAsync().ConfigureAwait(false);
            await m_mcp.Manager.RegisterExistingSessionAsync("operator", m_fixture.Session, "UserName")
                .ConfigureAwait(false);
            await m_mcp.Manager.RegisterExistingSessionAsync("peer", m_fixture.Peer, "UserName").ConfigureAwait(false);
            await m_mcp.Manager.RegisterExistingSessionAsync("anonymous", m_fixture.Anonymous, "Anonymous")
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes MCP without disposing the borrowed sessions, then releases every fixture resource and PKI file.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            try
            {
                if (m_mcp != null)
                {
                    await m_mcp.DisposeAsync().ConfigureAwait(false);
                    Assert.That(m_fixture.Session.Connected, Is.True);
                    Assert.That(m_fixture.Peer.Connected, Is.True);
                    Assert.That(m_fixture.Anonymous.Connected, Is.True);
                }
            }
            finally
            {
                if (m_fixture != null)
                {
                    await m_fixture.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Missing optional services are explicit failures, unlike an actually present empty catalog.
        /// </summary>
        [Test]
        public async Task MissingAddInsAreNotEmptyGoodCatalogs()
        {
            JsonElement empty = Success(await CallAsync("machinetool_query_jobs", new JsonObject
            {
                ["machineNodeId"] = m_fixture.Independent.NodeId.ToString()
            }).ConfigureAwait(false));
            Assert.That(empty.GetProperty("count").GetInt32(), Is.Zero);
            Assert.That(DecodeOrders(empty).Count, Is.Zero);
            foreach (string name in new[]
            {
                "machinetool_query_jobs", "machinetool_control_job", "machinetool_list_files",
                "machinetool_read_file", "machinetool_change_file"
            })
            {
                var arguments = new JsonObject { ["machineNodeId"] = m_fixture.Minimal.NodeId.ToString() };
                if (name == "machinetool_control_job")
                {
                    arguments["operation"] = "Start";
                    arguments["jobOrderId"] = "job";
                    arguments["confirm"] = true;
                }
                if (name is "machinetool_read_file" or "machinetool_change_file")
                {
                    arguments["path"] = "file.bin";
                }
                if (name == "machinetool_change_file")
                {
                    arguments["operation"] = "Delete";
                    arguments["confirm"] = true;
                }
                Error(await CallAsync(name, arguments).ConfigureAwait(false), "BadNotSupported");
            }
            Assert.That(m_fixture.Management.Parent!.BrowseName.Name, Is.EqualTo("MachineryBuildingBlocks"));
            Assert.That(m_fixture.Management.JobOrderControl, Is.Not.Null);
            Assert.That(m_fixture.Management.JobOrderResults, Is.Not.Null);
        }

        /// <summary>
        /// Eight distinct standard control verbs preserve native download data and never act as physical execution.
        /// </summary>
        [Test]
        public async Task DownloadUpdateStartPauseResumeStopAndClearUseStandardJobControl()
        {
            const string id = "Job-0";
            V2.ISA95JobOrderDataType order = MachineToolServiceMcpFixture.Order(id);
            order.EncodingMask |= (uint)V2.ISA95JobOrderDataTypeFields.JobOrderParameters;
            order.JobOrderParameters =
            [
                new V2.ISA95ParameterDataType { ID = "NativeArray", Value = Variant.From(new uint[] { 7, 11, 13 }) },
                new V2.ISA95ParameterDataType
                {
                    ID = "Payload",
                    Value = Variant.From(ByteString.From(new byte[] { 4, 2 }))
                }
            ];
            Accepted(await CommandAsync("Store", id, order).ConfigureAwait(false));
            Assert.That(m_fixture.ReceivedEncodingMask, Is.EqualTo(order.EncodingMask),
                "The adapter must preserve present-but-zero optional fields across actual MCP and opc.tcp.");
            V2.ISA95JobOrderAndStateDataType stored = (await OrdersAsync().ConfigureAwait(false))
                .ToList().Single(entry => entry.JobOrder.JobOrderID == id);
            V2.ISA95JobOrderAndStateDataType canonical = (await m_fixture.Source.GetJobOrdersV2Async()
                .ConfigureAwait(false)).ToList().Single(entry => entry.JobOrder.JobOrderID == id);
            Assert.That(stored.JobOrder.EncodingMask, Is.EqualTo(canonical.JobOrder.EncodingMask),
                "Read back the provider's actual canonical mask, not a fabricated echo of the download.");
            Assert.That(stored.JobOrder.Priority, Is.Zero);
            Assert.That(stored.JobOrder.JobOrderParameters[0].Value.TryGetValue(out ArrayOf<uint> numbers), Is.True);
            Assert.That(numbers, Is.EqualTo(new uint[] { 7, 11, 13 }));
            Assert.That(stored.JobOrder.JobOrderParameters[1].Value.TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes.ToArray(), Is.EqualTo(new byte[] { 4, 2 }));
            order.Priority = 7;
            Accepted(await CommandAsync("Update", id, order).ConfigureAwait(false));
            V2.ISA95JobOrderAndStateDataType updated = (await OrdersAsync().ConfigureAwait(false))
                .ToList().Single(entry => entry.JobOrder.JobOrderID == id);
            Assert.That(updated.JobOrder.Priority, Is.EqualTo(7));
            Assert.That(updated.State, Is.EqualTo(stored.State), "Update must not start execution.");
            Accepted(await CommandAsync("Start", id).ConfigureAwait(false));
            Accepted(await CommandAsync("RevokeStart", id).ConfigureAwait(false));
            Accepted(await CommandAsync("Start", id).ConfigureAwait(false));
            Assert.That((await m_fixture.Source.ReadAsync().ConfigureAwait(false)).Production.Jobs
                .ToList().Single(job => job.Identifier == id).Phase,
                Is.EqualTo(MachineToolProductionPhase.Initializing));
            Isa95JobOrderReceiptV2 executing = await m_fixture.Source
                .TransitionAsync(id, Isa95JobExecutionTransition.BeginExecution).ConfigureAwait(false);
            Assert.That(executing.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
            Accepted(await CommandAsync("Pause", id).ConfigureAwait(false));
            Accepted(await CommandAsync("Resume", id).ConfigureAwait(false));
            Accepted(await CommandAsync("Stop", id).ConfigureAwait(false));
            MachineToolSnapshot observation = await m_fixture.Source.ReadAsync().ConfigureAwait(false);
            Assert.That(observation.Operation.Mode, Is.EqualTo(MachineOperationMode.Manual),
                "Job-control acceptance cannot invent acquired equipment operation mode.");
            Assert.That(observation.Production.Jobs.ToList().Single(job => job.Identifier == id).RunsCompleted,
                Is.Zero);
            Accepted(await CommandAsync("Clear", id).ConfigureAwait(false));
            Assert.That((await OrdersAsync().ConfigureAwait(false)).ToList()
                .Any(entry => entry.JobOrder.JobOrderID == id),
                Is.False);
        }

        /// <summary>
        /// The remaining standard verbs use the same real generated receiver rather than adapter-invented methods.
        /// </summary>
        [TestCase("Abort", "Job-1")]
        [TestCase("Cancel", "Job-2")]
        public async Task StoreAndStartAbortAndCancelUseTheExistingEngine(string verb, string id)
        {
            Accepted(await CommandAsync(verb == "Abort" ? "StoreAndStart" : "Store", id,
                MachineToolServiceMcpFixture.Order(id)).ConfigureAwait(false));
            if (verb == "Abort")
            {
                Assert.That((await m_fixture.Source.TransitionAsync(id, Isa95JobExecutionTransition.BeginExecution)
                    .ConfigureAwait(false)).ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
            }
            Accepted(await CommandAsync(verb, id).ConfigureAwait(false));
            if (verb == "Abort")
            {
                Accepted(await CommandAsync("Clear", id).ConfigureAwait(false));
            }
            Assert.That((await OrdersAsync().ConfigureAwait(false)).ToList()
                .Any(entry => entry.JobOrder.JobOrderID == id),
                Is.False);
        }

        /// <summary>
        /// Unknown identities and invalid-state refusals retain the exact bitmap and do not mutate catalog state.
        /// </summary>
        [Test]
        public async Task UnknownAndRefusedJobCommandsPreserveState()
        {
            const string id = "Job-3";
            Accepted(await CommandAsync("Store", id, MachineToolServiceMcpFixture.Order(id)).ConfigureAwait(false));
            V2.ISA95JobOrderAndStateDataType before = (await OrdersAsync().ConfigureAwait(false))
                .ToList().Single(entry => entry.JobOrder.JobOrderID == id);
            CallToolResult refused = await CommandAsync("Pause", id).ConfigureAwait(false);
            Assert.That(refused.IsError, Is.True);
            Assert.That(Json(refused).GetProperty("returnStatus").GetString(),
                Is.EqualTo(Isa95JobReturnStatus.InvalidStatus.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
            CallToolResult unknown = await CommandAsync("Start", "unknown").ConfigureAwait(false);
            Assert.That(unknown.IsError, Is.True);
            Assert.That(Json(unknown).GetProperty("returnStatus").GetString(), Is.EqualTo("2"));
            Assert.That(Json(unknown).GetProperty("accepted").GetBoolean(), Is.False);
            V2.ISA95JobOrderAndStateDataType after = (await OrdersAsync().ConfigureAwait(false))
                .ToList().Single(entry => entry.JobOrder.JobOrderID == id);
            Assert.That(after.State, Is.EqualTo(before.State));
            Assert.That(after.JobOrder, Is.EqualTo(before.JobOrder));
            Accepted(await CommandAsync("Cancel", id).ConfigureAwait(false));
        }

        /// <summary>
        /// Response-by-ID and full-state-path queries preserve explicit native results and reject oversized sets.
        /// </summary>
        [Test]
        public async Task ExplicitResultsByIdAndStatePreserveNativeResponseData()
        {
            foreach (string id in new[] { "Job-4", "Job-5" })
            {
                Accepted(await CommandAsync("Store", id, MachineToolServiceMcpFixture.Order(id)).ConfigureAwait(false));
                V2.ISA95JobOrderAndStateDataType order = (await m_fixture.Source.GetJobOrdersV2Async()
                    .ConfigureAwait(false)).ToList().Single(value => value.JobOrder.JobOrderID == id);
                Isa95JobResponseReceiptV2 receipt = await m_fixture.Source.ReceiveJobResponseAsync(
                    new V2.ISA95JobResponseDataType
                    {
                        EncodingMask = (uint)V2.ISA95JobResponseDataTypeFields.JobResponseData,
                        JobResponseID = "Response-" + id,
                        JobOrderID = id,
                        JobState = order.State,
                        JobResponseData =
                        [
                            new V2.ISA95ParameterDataType { ID = "Count", Value = Variant.From(17u) }
                        ]
                    }).ConfigureAwait(false);
                Assert.That(receipt.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
            }
            JsonElement single = Accepted(await CallAsync("machinetool_query_jobs",
                new JsonObject { ["query"] = "ResponseById", ["jobOrderId"] = "Job-4" }).ConfigureAwait(false));
            using var decoder = new JsonDecoder(single.GetRawText(), m_fixture.Session.MessageContext);
            V2.ISA95JobResponseDataType received = decoder.ReadEncodeable<V2.ISA95JobResponseDataType>("response");
            Assert.That(received.JobResponseID, Is.EqualTo("Response-Job-4"));
            Assert.That(received.EncodingMask, Is.EqualTo((uint)V2.ISA95JobResponseDataTypeFields.JobResponseData));
            Assert.That(received.JobResponseData[0].Value.TryGetValue(out uint count), Is.True);
            Assert.That(count, Is.EqualTo(17u));
            JsonObject query = StateQuery(received.JobState);
            JsonElement byState = Accepted(await CallAsync("machinetool_query_jobs", query).ConfigureAwait(false));
            Assert.That(byState.GetProperty("responses").GetArrayLength(), Is.EqualTo(2));
            using var stateDecoder = new JsonDecoder(byState.GetRawText(), m_fixture.Session.MessageContext);
            ArrayOf<V2.ISA95JobResponseDataType> responses =
                stateDecoder.ReadEncodeableArray<V2.ISA95JobResponseDataType>("responses");
            Assert.That(responses.ToList().Select(response => response.JobOrderID), Is.EquivalentTo(s_responseJobs));
            Assert.That(responses[0].JobState, Is.EqualTo(received.JobState));
            query = StateQuery(received.JobState);
            query["maximumResults"] = 1;
            Error(await CallAsync("machinetool_query_jobs", query).ConfigureAwait(false), "BadEncodingLimitsExceeded");
            CallToolResult missing = await CallAsync("machinetool_query_jobs",
                new JsonObject { ["query"] = "ResponseById", ["jobOrderId"] = "unknown" }).ConfigureAwait(false);
            Assert.That(missing.IsError, Is.True);
            Assert.That(Json(missing).GetProperty("returnStatus").GetString(), Is.EqualTo("2"));
            Accepted(await CommandAsync("Cancel", "Job-4").ConfigureAwait(false));
            Accepted(await CommandAsync("Cancel", "Job-5").ConfigureAwait(false));
        }

        /// <summary>
        /// Native job inventory overflow is an error, not a truncated or empty successful result.
        /// </summary>
        [Test]
        public async Task JobCatalogRecordAndWholeResponseBoundsDoNotTruncate()
        {
            V2.ISA95JobOrderDataType large = MachineToolServiceMcpFixture.Order("Job-6");
            large.EncodingMask |= (uint)V2.ISA95JobOrderDataTypeFields.Description;
            large.Description = [new LocalizedText(new string('x', 1000)), new LocalizedText(new string('y', 1000))];
            Accepted(await CommandAsync("Store", "Job-6", large).ConfigureAwait(false));
            Accepted(await CommandAsync("Store", "Job-7", MachineToolServiceMcpFixture.Order("Job-7"))
                .ConfigureAwait(false));
            Error(await CallAsync("machinetool_query_jobs", new JsonObject { ["maximumResults"] = 1 })
                .ConfigureAwait(false), "BadEncodingLimitsExceeded");
            Error(await CallAsync("machinetool_query_jobs", new JsonObject { ["maximumResponseBytes"] = 1024 })
                .ConfigureAwait(false), "BadEncodingLimitsExceeded");
            ArrayOf<V2.ISA95JobOrderAndStateDataType> orders = await OrdersAsync().ConfigureAwait(false);
            Assert.That(orders.ToList().Single(entry => entry.JobOrder.JobOrderID == "Job-6")
                .JobOrder.Description[0].Text,
                Is.EqualTo(new string('x', 1000)));
            Accepted(await CommandAsync("Cancel", "Job-6").ConfigureAwait(false));
            Accepted(await CommandAsync("Cancel", "Job-7").ConfigureAwait(false));
        }

        /// <summary>
        /// All six explicit file actions round-trip real provider bytes without implicit create or overwrite.
        /// </summary>
        [Test]
        public async Task RemoteFileCreateWriteCopyMoveAndDeleteUseStandardHelpers()
        {
            await ChangeAsync("CreateDirectory", "batch").ConfigureAwait(false);
            await ChangeAsync("CreateFile", "batch/input.bin").ConfigureAwait(false);
            Assert.That(await DownloadAsync("batch/input.bin").ConfigureAwait(false), Is.Empty);
            await ChangeAsync("Write", "batch/input.bin", new JsonObject
            {
                ["options"] = new JsonObject { ["contentBase64"] = "AAECAwQ=" }
            })
                .ConfigureAwait(false);
            Assert.That(await DownloadAsync("batch/input.bin").ConfigureAwait(false),
                Is.EqualTo(new byte[] { 0, 1, 2, 3, 4 }));
            await ChangeAsync("Copy", "batch/input.bin", new JsonObject
            {
                ["options"] = new JsonObject { ["destination"] = "batch/copy.bin" }
            })
                .ConfigureAwait(false);
            await ChangeAsync("Move", "batch/copy.bin", new JsonObject
            {
                ["options"] = new JsonObject { ["destination"] = "batch/moved.bin" }
            })
                .ConfigureAwait(false);
            Assert.That(await DownloadAsync("batch/moved.bin").ConfigureAwait(false),
                Is.EqualTo(new byte[] { 0, 1, 2, 3, 4 }));
            Error(await CallAsync("machinetool_read_file", new JsonObject { ["path"] = "batch/copy.bin" })
                .ConfigureAwait(false), "BadNotFound");
            JsonElement listed = Success(await CallAsync("machinetool_list_files",
                new JsonObject { ["path"] = "batch", ["maximumEntries"] = 2 }).ConfigureAwait(false));
            Assert.That(listed.GetProperty("entries").GetArrayLength(), Is.EqualTo(2));
            Assert.That(listed.GetProperty("entries").EnumerateArray()
                .All(entry => entry.GetProperty("openCount").GetUInt16() == 0), Is.True);
            Error(await CallAsync("machinetool_change_file",
                new JsonObject { ["operation"] = "Delete", ["path"] = "batch", ["confirm"] = true })
                .ConfigureAwait(false), "BadUnexpectedError");
            await ChangeAsync("Write", "batch/input.bin", new JsonObject
            {
                ["options"] = new JsonObject { ["contentBase64"] = string.Empty }
            })
                .ConfigureAwait(false);
            Assert.That(await DownloadAsync("batch/input.bin").ConfigureAwait(false), Is.Empty);
            await ChangeAsync("Delete", "batch/moved.bin").ConfigureAwait(false);
            await ChangeAsync("Delete", "batch", new JsonObject
            {
                ["options"] = new JsonObject { ["recursive"] = true }
            }).ConfigureAwait(false);
            Assert.That(await m_fixture.Physical.GetEntryAsync("batch", default).ConfigureAwait(false), Is.Null);
            Assert.That(m_fixture.ActiveStreams, Is.Zero);
        }

        /// <summary>
        /// WorkMasters uses the model-qualified directory and both paths remain under the selected provider root.
        /// </summary>
        [Test]
        public async Task WorkMastersUsesItsActualQualifiedDirectoryAndIndependentRoot()
        {
            JsonElement root = Success(await CallAsync("machinetool_list_files").ConfigureAwait(false));
            JsonElement masters = root.GetProperty("entries").EnumerateArray()
                .Single(entry => entry.GetProperty("isDirectory").GetBoolean());
            ushort ns = m_fixture.Session.NamespaceUris.GetIndexOrAppend(Opc.Ua.MachineTool.Namespaces.MachineTool);
            Assert.That(masters.GetProperty("browseName").GetString(), Is.EqualTo($"{ns}:WorkMasters"));
            Assert.That(await DownloadAsync("program.nc", "WorkMasters").ConfigureAwait(false),
                Is.EqualTo(new byte[] { 5, 6, 7 }));
            await ChangeAsync("CreateFile", "new.nc", new JsonObject { ["root"] = "WorkMasters" })
                .ConfigureAwait(false);
            await ChangeAsync("Write", "new.nc",
                new JsonObject
                {
                    ["root"] = "WorkMasters",
                    ["options"] = new JsonObject { ["contentBase64"] = "Kis=" }
                }).ConfigureAwait(false);
            Assert.That(await DownloadAsync("new.nc", "WorkMasters").ConfigureAwait(false),
                Is.EqualTo(new byte[] { 42, 43 }));
            Assert.That(await m_fixture.Physical.GetEntryAsync("new.nc", default).ConfigureAwait(false), Is.Null);
            await ChangeAsync("Copy", "new.nc",
                new JsonObject
                {
                    ["root"] = "WorkMasters",
                    ["options"] = new JsonObject { ["destination"] = "copy.nc" }
                }).ConfigureAwait(false);
            await ChangeAsync("Move", "copy.nc",
                new JsonObject
                {
                    ["root"] = "WorkMasters",
                    ["options"] = new JsonObject { ["destination"] = "renamed.nc" }
                }).ConfigureAwait(false);
            await ChangeAsync("Delete", "new.nc", new JsonObject { ["root"] = "WorkMasters" }).ConfigureAwait(false);
            await ChangeAsync("Delete", "renamed.nc", new JsonObject { ["root"] = "WorkMasters" })
                .ConfigureAwait(false);
            Error(await CallAsync("machinetool_list_files", new JsonObject
            {
                ["machineNodeId"] = m_fixture.Independent.NodeId.ToString(),
                ["root"] = "WorkMasters"
            }).ConfigureAwait(false), "BadNotSupported");
            Error(await CallAsync("machinetool_change_file", new JsonObject
            {
                ["operation"] = "Move",
                ["root"] = "WorkMasters",
                ["path"] = "program.nc",
                ["options"] = new JsonObject { ["destination"] = "../escaped.nc" },
                ["confirm"] = true
            }).ConfigureAwait(false), "BadInvalidArgument");
            Assert.That(await DownloadAsync("program.nc", "WorkMasters").ConfigureAwait(false),
                Is.EqualTo(new byte[] { 5, 6, 7 }));
        }

        /// <summary>
        /// Consent does not bypass actual server session authorization or the standard provider's refusal.
        /// </summary>
        [Test]
        public async Task CallerAndProviderAuthorizationRemainAuthoritative()
        {
            ulong revision = (await m_fixture.Source.ReadAsync().ConfigureAwait(false)).Revision;
            Error(await CallAsync("machinetool_control_job", new JsonObject
            {
                ["operation"] = "Store",
                ["jobOrder"] = Encode(MachineToolServiceMcpFixture.Order("Job-8")),
                ["confirm"] = true,
                ["sessionName"] = "anonymous"
            }).ConfigureAwait(false), "BadUserAccessDenied");
            Assert.That((await m_fixture.Source.ReadAsync().ConfigureAwait(false)).Revision, Is.EqualTo(revision));
            foreach (string operation in new[] { "Write", "CreateFile", "Delete" })
            {
                var arguments = new JsonObject
                {
                    ["operation"] = operation,
                    ["path"] = "file.bin",
                    ["confirm"] = true,
                    ["sessionName"] = "anonymous"
                };
                if (operation == "Write")
                {
                    arguments["options"] = new JsonObject { ["contentBase64"] = "AQ==" };
                }
                Error(await CallAsync("machinetool_change_file", arguments).ConfigureAwait(false),
                    "BadUserAccessDenied");
            }
            m_fixture.DenyWrites = true;
            try
            {
                Assert.That(m_fixture.Provider.Object.IsWritable, Is.False);
                int mutations = m_fixture.Mutations.Count;
                CallToolResult denied = await CallAsync("machinetool_change_file", new JsonObject
                {
                    ["operation"] = "Write",
                    ["path"] = "file.bin",
                    ["options"] = new JsonObject { ["contentBase64"] = "AQ==" },
                    ["confirm"] = true
                }).ConfigureAwait(false);
                Error(denied, "BadUserAccessDenied");
                Assert.That(m_fixture.Mutations, Has.Count.EqualTo(mutations));
            }
            finally
            {
                m_fixture.DenyWrites = false;
            }
            Assert.That(await DownloadAsync("file.bin").ConfigureAwait(false),
                Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(m_fixture.ActiveStreams, Is.Zero);
        }

        /// <summary>
        /// Raw and base64 response bounds close handles on failure; invalid writes never reach the provider.
        /// </summary>
        [Test]
        public async Task FileByteReplyAndRecordBoundsFailWithoutTruncationOrMutation()
        {
            JsonElement exact = Success(await CallAsync("machinetool_read_file",
                new JsonObject { ["path"] = "file.bin", ["maximumBytes"] = 4, ["maximumResponseBytes"] = 1024 })
                .ConfigureAwait(false));
            Assert.That(exact.GetProperty("byteCount").GetInt32(), Is.EqualTo(4));
            Error(await CallAsync("machinetool_read_file",
                new JsonObject { ["path"] = "file.bin", ["maximumBytes"] = 3 }).ConfigureAwait(false),
                "BadEncodingLimitsExceeded");
            Error(await CallAsync("machinetool_read_file", new JsonObject
            {
                ["path"] = "large.bin",
                ["maximumBytes"] = 131072,
                ["maximumResponseBytes"] = 1024
            }).ConfigureAwait(false), "BadEncodingLimitsExceeded");
            Error(await CallAsync("machinetool_list_files", new JsonObject { ["maximumEntries"] = 1 })
                .ConfigureAwait(false), "BadEncodingLimitsExceeded");
            int mutations = m_fixture.Mutations.Count;
            Error(await CallAsync("machinetool_change_file", new JsonObject
            {
                ["operation"] = "Write",
                ["path"] = "file.bin",
                ["options"] = new JsonObject { ["contentBase64"] = "AAECAwQ=", ["maximumBytes"] = 4 },
                ["confirm"] = true
            }).ConfigureAwait(false), "BadOutOfRange");
            Error(await CallAsync("machinetool_change_file", new JsonObject
            {
                ["operation"] = "Write",
                ["path"] = "file.bin",
                ["options"] = new JsonObject { ["contentBase64"] = "AQ==" },
                ["maximumResponseBytes"] = 1023,
                ["confirm"] = true
            }).ConfigureAwait(false), "BadOutOfRange");
            Assert.That(m_fixture.Mutations, Has.Count.EqualTo(mutations));
            Assert.That(await DownloadAsync("file.bin").ConfigureAwait(false),
                Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(m_fixture.ActiveStreams, Is.Zero);
        }

        /// <summary>
        /// The largest upload still produces a prevalidated small receipt, never a post-commit upload echo failure.
        /// </summary>
        [Test]
        public async Task MaximumUploadHasSmallReceiptAndCompleteBoundedDownload()
        {
            byte[] bytes = new byte[MachineToolServiceParameters.MaximumFileBytes];
            Array.Fill(bytes, (byte)255);
            await ChangeAsync("CreateFile", "maximum.bin").ConfigureAwait(false);
            try
            {
                await ChangeAsync("Write", "maximum.bin", new JsonObject
                {
                    ["options"] = new JsonObject
                    {
                        ["contentBase64"] = Convert.ToBase64String(bytes),
                        ["maximumBytes"] = bytes.Length
                    }
                }).ConfigureAwait(false);
                JsonElement complete = Success(await CallAsync("machinetool_read_file", new JsonObject
                {
                    ["path"] = "maximum.bin",
                    ["maximumBytes"] = bytes.Length,
                    ["maximumResponseBytes"] = 1048576
                }).ConfigureAwait(false));
                Assert.That(complete.GetProperty("byteCount").GetInt32(), Is.EqualTo(bytes.Length));
                Assert.That(Convert.FromBase64String(complete.GetProperty("contentBase64").GetString()!),
                    Is.EqualTo(bytes));
                Assert.That(m_fixture.ActiveStreams, Is.Zero);
            }
            finally
            {
                await ChangeAsync("Delete", "maximum.bin").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Replacement requires an existing file, creation requires an existing parent, and copy cannot overwrite.
        /// </summary>
        [Test]
        public async Task FileMutationsHaveNoImplicitCreationOrOverwrite()
        {
            Error(await CallAsync("machinetool_change_file", new JsonObject
            {
                ["operation"] = "Write", ["path"] = "absent.bin",
                ["options"] = new JsonObject { ["contentBase64"] = string.Empty }, ["confirm"] = true
            }).ConfigureAwait(false), "BadNotFound");
            Error(await CallAsync("machinetool_change_file", new JsonObject
            {
                ["operation"] = "CreateFile", ["path"] = "absent/child.bin", ["confirm"] = true
            }).ConfigureAwait(false), "BadNotFound");
            Error(await CallAsync("machinetool_change_file", new JsonObject
            {
                ["operation"] = "CreateDirectory", ["path"] = "absent/child", ["confirm"] = true
            }).ConfigureAwait(false), "BadNotFound");
            CallToolResult collision = await CallAsync("machinetool_change_file", new JsonObject
            {
                ["operation"] = "Copy", ["path"] = "file.bin",
                ["options"] = new JsonObject { ["destination"] = "empty.bin" }, ["confirm"] = true
            }).ConfigureAwait(false);
            Assert.That(collision.IsError, Is.True);
            Assert.That(await DownloadAsync("empty.bin").ConfigureAwait(false), Is.Empty);
            Assert.That(await m_fixture.Physical.GetEntryAsync("absent", default).ConfigureAwait(false), Is.Null);
            Assert.That(await m_fixture.Physical.GetEntryAsync("absent.bin", default).ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// Named sessions and independent machine roots do not share handles, ownership, jobs or provider data.
        /// </summary>
        [Test]
        public async Task MultipleSessionsAndMachineRootsRemainIsolated()
        {
            await using var peer = new MachineToolClient(m_fixture.Peer, NUnitTelemetryContext.Create());
            FileDirectoryTypeClient directory = (await peer.GetFileSystemAsync(m_fixture.Machine.NodeId)
                .ConfigureAwait(false))!;
            var files = new FileSystemClient(m_fixture.Peer, directory.ObjectId);
            UaFileStream held = await files.OpenReadAsync("file.bin").ConfigureAwait(false);
            await using (held.ConfigureAwait(false))
            {
                Assert.That(m_fixture.ActiveStreams, Is.EqualTo(1));
                Assert.That(await DownloadAsync("file.bin").ConfigureAwait(false),
                    Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(m_fixture.ActiveStreams, Is.EqualTo(1), "MCP closes its handle, not the other session's.");
                CallToolResult refused = await CallAsync("machinetool_change_file", new JsonObject
                {
                    ["operation"] = "Write", ["path"] = "file.bin",
                    ["options"] = new JsonObject { ["contentBase64"] = string.Empty }, ["confirm"] = true
                }).ConfigureAwait(false);
                Assert.That(refused.IsError, Is.True);
                Assert.That(held.CanRead, Is.True);
                Assert.That(m_fixture.ActiveStreams, Is.EqualTo(1));
            }
            Assert.That(m_fixture.ActiveStreams, Is.Zero);
            JsonElement other = Success(await CallAsync("machinetool_read_file", new JsonObject
            {
                ["machineNodeId"] = m_fixture.Independent.NodeId.ToString(), ["path"] = "file.bin",
                ["sessionName"] = "peer"
            }).ConfigureAwait(false));
            Assert.That(Convert.FromBase64String(other.GetProperty("contentBase64").GetString()!),
                Is.EqualTo(new byte[] { 99 }));
            Assert.That(await m_fixture.IndependentSource.GetJobOrdersV2Async().ConfigureAwait(false), Is.Empty);
            Assert.That(m_fixture.Session.SessionId, Is.Not.EqualTo(m_fixture.Peer.SessionId));
            Assert.That(m_fixture.Session.Connected, Is.True);
            Assert.That(m_fixture.Peer.Connected, Is.True);
            Assert.That((await m_mcp.CallAsync("machinetool_list_files",
                new JsonObject { ["machineNodeId"] = m_fixture.Machine.NodeId.ToString() })
                .ConfigureAwait(false)).IsError, Is.True, "Multiple sessions require an explicit selector.");
        }

        /// <summary>
        /// Client cancellation traverses MCP and closes actual handles without retries, compensation or false success.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ClientCancellationDrainsOnlyCallOwnedFileHandles(bool write)
        {
            await MachineToolServiceMcpFixture.SeedAsync(m_fixture.Physical, "pause.bin", new byte[] { 8, 9, 10 })
                .ConfigureAwait(false);
            MachineToolServiceMcpFixture.IoGate gate = m_fixture.Pause(write);
            int mutations = m_fixture.Mutations.Count;
            byte[] payload = new byte[65536];
            Task<CallToolResult> pending = CallAsync(write ? "machinetool_change_file" : "machinetool_read_file",
                write ? new JsonObject
                {
                    ["operation"] = "Write", ["path"] = "pause.bin",
                    ["options"] = new JsonObject { ["contentBase64"] = Convert.ToBase64String(payload) },
                    ["confirm"] = true
                } : new JsonObject { ["path"] = "pause.bin" });
            try
            {
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Assert.That(m_fixture.ActiveStreams, Is.EqualTo(1));
                await m_mcp.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await pending.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                await m_mcp.Finished.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                await gate.Closed.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Assert.That(m_mcp.LastCancellationToken.IsCancellationRequested, Is.True);
                Assert.That(m_fixture.ActiveStreams, Is.Zero);
                Assert.That(m_fixture.Mutations.ToArray().Skip(mutations),
                    Is.EqualTo(write ? s_writeOnly : Array.Empty<string>()));
                Assert.That(m_fixture.Session.Connected, Is.True);
                Assert.That(m_fixture.Peer.Connected, Is.True);
            }
            finally
            {
                gate.Release.TrySetResult(true);
            }
            byte[] actual = await DownloadAsync("pause.bin").ConfigureAwait(false);
            if (write)
            {
                Assert.That(actual, Has.Length.InRange(1, payload.Length - 1));
                Assert.That(actual, Is.All.Zero, "A partial replacement is not restored or retried.");
            }
            else
            {
                Assert.That(actual, Is.EqualTo(new byte[] { 8, 9, 10 }));
            }
        }

        /// <summary>
        /// Calls a real MCP tool using the explicitly selected operator and machine unless overridden by the test.
        /// </summary>
        private Task<CallToolResult> CallAsync(string name, JsonObject? arguments = null)
        {
            arguments ??= new JsonObject();
            arguments["machineNodeId"] ??= m_fixture.Machine.NodeId.ToString();
            arguments["sessionName"] ??= "operator";
            return m_mcp.CallAsync(name, arguments);
        }

        /// <summary>
        /// Sends a standard native order or an ID-only verb through the actual MCP binder.
        /// </summary>
        private Task<CallToolResult> CommandAsync(string operation, string id, V2.ISA95JobOrderDataType? order = null)
        {
            var arguments = new JsonObject { ["operation"] = operation, ["confirm"] = true };
            if (order == null)
            {
                arguments["jobOrderId"] = id;
            }
            else
            {
                arguments["jobOrder"] = Encode(order);
            }
            return CallAsync("machinetool_control_job", arguments);
        }

        /// <summary>
        /// Requires a fully completed file mutation and a bounded, non-echoing receipt.
        /// </summary>
        private async Task ChangeAsync(string operation, string path, JsonObject? extra = null)
        {
            extra ??= new JsonObject();
            extra["operation"] = operation;
            extra["path"] = path;
            extra["confirm"] = true;
            extra["maximumResponseBytes"] = 1024;
            JsonElement receipt = Success(await CallAsync("machinetool_change_file", extra).ConfigureAwait(false));
            Assert.That(receipt.GetProperty("completed").GetBoolean(), Is.True);
            Assert.That(receipt.GetProperty("operation").GetString(), Is.EqualTo(operation));
            Assert.That(receipt.TryGetProperty("contentBase64", out _), Is.False);
            Assert.That(m_fixture.ActiveStreams, Is.Zero);
        }

        /// <summary>
        /// Reads the actual complete file result through MCP.
        /// </summary>
        private async Task<byte[]> DownloadAsync(string path, string root = "FileSystem")
        {
            JsonElement result = Success(await CallAsync("machinetool_read_file",
                new JsonObject { ["path"] = path, ["root"] = root }).ConfigureAwait(false));
            byte[] bytes = Convert.FromBase64String(result.GetProperty("contentBase64").GetString()!);
            Assert.That(result.GetProperty("byteCount").GetInt32(), Is.EqualTo(bytes.Length));
            return bytes;
        }

        /// <summary>
        /// Reads a real catalog and decodes the unchanged DataValue envelope.
        /// </summary>
        private async Task<ArrayOf<V2.ISA95JobOrderAndStateDataType>> OrdersAsync()
        {
            return DecodeOrders(Success(await CallAsync("machinetool_query_jobs").ConfigureAwait(false)));
        }

        /// <summary>
        /// Decodes the original native list including its UA acquisition quality.
        /// </summary>
        private ArrayOf<V2.ISA95JobOrderAndStateDataType> DecodeOrders(JsonElement json)
        {
            using var decoder = new JsonDecoder(json.GetProperty("jobOrders").GetRawText(),
                m_fixture.Session.MessageContext);
            DataValue value = decoder.ReadDataValue("DataValue");
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True);
            Assert.That(value.WrappedValue.TryGetStructure(out ArrayOf<V2.ISA95JobOrderAndStateDataType> orders),
                Is.True);
            Assert.That(json.GetProperty("count").GetInt32(), Is.EqualTo(orders.Count));
            return orders;
        }

        /// <summary>
        /// Encodes a complete native state path instead of approximating it with an integer selector.
        /// </summary>
        private JsonObject StateQuery(ArrayOf<V2.ISA95StateDataType> states)
        {
            using var encoder = new JsonEncoder(m_fixture.Session.MessageContext);
            encoder.WriteEncodeableArray("states", states);
            return new JsonObject
            {
                ["query"] = "ResponsesByState",
                ["states"] = JsonNode.Parse(encoder.CloseAndReturnText())!["states"]!.DeepClone()
            };
        }

        /// <summary>
        /// Encodes a generated native model, preserving masks, arrays and variant types in MCP input.
        /// </summary>
        /// <typeparam name="T">The generated UA structure contract.</typeparam>
        private JsonNode Encode<T>(T value) where T : IEncodeable, new()
        {
            return MachineToolServiceJson.Encode(value, m_fixture.Session.MessageContext)!;
        }

        /// <summary>
        /// Requires standard success-bit semantics in every accepted command and result query.
        /// </summary>
        private static JsonElement Accepted(CallToolResult result)
        {
            JsonElement json = Success(result);
            Assert.That(json.GetProperty("returnStatus").GetString(), Is.EqualTo("1"));
            Assert.That(json.GetProperty("accepted").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("meaning").GetString(), Does.Contain("does not confirm physical execution"));
            return json;
        }

        /// <summary>
        /// Requires genuine successful MCP delivery rather than just deserializable JSON.
        /// </summary>
        private static JsonElement Success(CallToolResult result)
        {
            Assert.That(result.IsError, Is.Not.True, result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
            return Json(result);
        }

        /// <summary>The owned real provider/server/session fixture.</summary>
        private MachineToolServiceMcpFixture m_fixture = null!;

        /// <summary>The owned real MCP transport.</summary>
        private MachineToolServiceMcpHarness m_mcp = null!;

        /// <summary>The explicit response jobs used by the state query.</summary>
        private static readonly string[] s_responseJobs = ["Job-4", "Job-5"];

        /// <summary>The sole mutation expected after a cancelled replacement.</summary>
        private static readonly string[] s_writeOnly = ["Write"];
    }
}
#endif
