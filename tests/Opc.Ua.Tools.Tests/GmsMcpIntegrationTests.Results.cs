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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.GMS;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Tests;

namespace Opc.Ua.Tools.Tests
{
    public sealed partial class GmsMcpIntegrationTests
    {
        /// <summary>
        /// Queries and corrections cross real method/read services with raw metadata, units and acquisition quality.
        /// </summary>
        [Test]
        public async Task ResultsQueriesAndCorrectionsUseRealServices()
        {
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("R1", "A")).ConfigureAwait(false);
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("R2", "B")).ConfigureAwait(false);
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("R3", "A")).ConfigureAwait(false);
            JsonElement query = Success(await ResultCallAsync("Query", new JsonObject
            {
                ["query"] = new JsonObject
                {
                    ["filterField"] = "JobId",
                    ["filterValue"] = "A",
                    ["orderBy"] = "ResultId"
                },
                ["maximumResults"] = 2
            }).ConfigureAwait(false));
            Assert.That(query.GetProperty("resultIds").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(s_filteredResults));
            JsonElement latest = Success(await ResultCallAsync("Latest").ConfigureAwait(false));
            Assert.That(latest.GetProperty("resultId").GetString(), Is.EqualTo("R3"));
            Assert.That(latest.GetProperty("acquisitionQualityProvided").GetBoolean(), Is.False);
            ResultDataType envelope = ResultData(latest);
            Assert.That(envelope.ResultMetaData!.JobId, Is.EqualTo("A"));
            Assert.That(envelope.ResultContent[0].TryGetValue(out double measurement), Is.True);
            Assert.That(measurement, Is.EqualTo(10.25));
            JsonElement selected = Success(await ResultCallAsync("ById", new JsonObject { ["resultId"] = "R2" })
                .ConfigureAwait(false));
            Assert.That(ResultData(selected).ResultMetaData!.JobId, Is.EqualTo("B"));
            JsonElement published = Success(await ResultCallAsync("Published").ConfigureAwait(false));
            Assert.That(published.GetProperty("present").GetBoolean(), Is.True);
            Assert.That(published.GetProperty("results").GetArrayLength(), Is.EqualTo(2));
            foreach (JsonElement slot in published.GetProperty("results").EnumerateArray())
            {
                DataValue sample = Decode(slot.GetProperty("reading"));
                Assert.That(sample.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
                Assert.That(sample.SourceTimestamp, Is.EqualTo(GmsMcpFixture.Acquired));
                Assert.That(slot.GetProperty("usedToolsPresent").GetBoolean(), Is.True);
                Assert.That(Decode(slot.GetProperty("usedTools")).WrappedValue.TryGetValue(out ArrayOf<string> tools),
                    Is.True);
                Assert.That(tools, Is.EqualTo((ArrayOf<string>)["probe-7"]));
            }
            JsonElement corrections = Success(await ResultCallAsync("Corrections").ConfigureAwait(false));
            Assert.That(corrections.GetProperty("present").GetBoolean(), Is.True);
            Assert.That(corrections.GetProperty("actuationRequested").GetBoolean(), Is.False);
            JsonElement correction = corrections.GetProperty("corrections")[0];
            DataValue relative = Decode(correction.GetProperty("relativeValue").GetProperty("reading"));
            Assert.That(relative.WrappedValue.TryGetValue(out double deviation), Is.True);
            Assert.That(deviation, Is.EqualTo(0.25));
            Assert.That(relative.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(relative.SourceTimestamp, Is.EqualTo(GmsMcpFixture.Acquired));
            Assert.That(Decode(correction.GetProperty("absoluteValue").GetProperty("reading")).WrappedValue
                .TryGetValue(out double absolute), Is.True);
            Assert.That(absolute, Is.EqualTo(10.25));
            Assert.That(Decode(correction.GetProperty("relativeValue").GetProperty("engineeringUnits")).WrappedValue
                .TryGetStructure<EUInformation>(out EUInformation? unit), Is.True);
            Assert.That(unit!.UnitId, Is.EqualTo(5066068));
            Assert.That(Decode(correction.GetProperty("lowerControlLimit").GetProperty("reading")).WrappedValue
                .TryGetValue(out double lower), Is.True);
            Assert.That(lower, Is.EqualTo(-0.2));
            Assert.That(corrections.GetProperty("corrections")[1].GetProperty("relativeValue").ValueKind,
                Is.EqualTo(JsonValueKind.Null));
            Assert.That(corrections.GetProperty("corrections")[1].GetProperty("programName").ValueKind,
                Is.EqualTo(JsonValueKind.Null));
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Empty and absent surfaces, partial-result filtering and count overflows remain distinct native outcomes.
        /// </summary>
        [Test]
        public async Task ResultQueryBoundsAndOptionalSurfacesAreNotSilentTruncation()
        {
            JsonElement empty = Success(await ResultCallAsync("Query").ConfigureAwait(false));
            Assert.That(empty.GetProperty("resultIds").GetArrayLength(), Is.Zero);
            JsonElement noSlots = Success(await ResultCallAsync("Published",
                new JsonObject { ["systemNodeId"] = m_fixture.Minimal.NodeId.ToString() }).ConfigureAwait(false));
            Assert.That(noSlots.GetProperty("present").GetBoolean(), Is.True);
            Assert.That(noSlots.GetProperty("results").GetArrayLength(), Is.Zero);
            JsonElement noCorrections = Success(await ResultCallAsync("Corrections",
                new JsonObject { ["systemNodeId"] = m_fixture.Minimal.NodeId.ToString() }).ConfigureAwait(false));
            Assert.That(noCorrections.GetProperty("present").GetBoolean(), Is.False);
            Assert.That(noCorrections.GetProperty("corrections").ValueKind, Is.EqualTo(JsonValueKind.Null));
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("Final")).ConfigureAwait(false);
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("Partial", intermediate: true))
                .ConfigureAwait(false);
            JsonElement partial = Success(await ResultCallAsync("Query", new JsonObject
            {
                ["query"] = new JsonObject
                {
                    ["filterField"] = "IsPartial",
                    ["filterValue"] = true,
                    ["orderBy"] = "None"
                }
            }).ConfigureAwait(false));
            Assert.That(partial.GetProperty("resultIds").GetArrayLength(), Is.EqualTo(1));
            Assert.That(partial.GetProperty("resultIds")[0].GetString(), Is.EqualTo("Partial"));
            JsonElement exact = Success(await ResultCallAsync("Query", new JsonObject
            {
                ["query"] = new JsonObject { ["filterField"] = "ResultId", ["filterValue"] = "Final" },
                ["maximumResults"] = 1
            }).ConfigureAwait(false));
            Assert.That(exact.GetProperty("resultIds")[0].GetString(), Is.EqualTo("Final"));
            foreach (string operation in new[] { "Query", "Published", "Corrections" })
            {
                JsonElement error = GmsMcpParameterTests.Error(await ResultCallAsync(operation,
                    new JsonObject { ["maximumResults"] = 1 }).ConfigureAwait(false), "BadEncodingLimitsExceeded");
                Assert.That(error.TryGetProperty("resultIds", out _), Is.False);
            }
            Assert.That(Success(await ResultCallAsync("Query", new JsonObject { ["maximumResults"] = 2 })
                .ConfigureAwait(false)).GetProperty("resultIds").GetArrayLength(), Is.EqualTo(2));
            JsonElement defaults = Success(await ResultCallAsync("Query", new JsonObject
            {
                ["query"] = new JsonObject(),
                ["maximumResults"] = 2
            }).ConfigureAwait(false));
            Assert.That(defaults.GetProperty("filterField").GetString(), Is.EqualTo("None"));
            Assert.That(defaults.GetProperty("orderBy").GetString(), Is.EqualTo("ResultId"));
            Assert.That(defaults.GetProperty("resultIds")[0].GetString(), Is.EqualTo("Final"));
            Assert.That(defaults.GetProperty("resultIds")[1].GetString(), Is.EqualTo("Partial"));
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Acknowledgement is opt-in and preserves each standard domain error without deleting acquired results.
        /// </summary>
        [Test]
        public async Task AcknowledgementRequiresExplicitConsentAndPreservesDomainErrors()
        {
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("R1")).ConfigureAwait(false);
            var arguments = new JsonObject
            {
                ["systemNodeId"] = m_fixture.System.NodeId.ToString(),
                ["resultIds"] = new JsonArray("R1", "missing")
            };
            GmsMcpParameterTests.Error(
                await m_mcp.CallAsync("gms_acknowledge_results", arguments).ConfigureAwait(false),
                "BadUserAccessDenied");
            VerifyNoAcknowledgements();
            arguments["confirmAcknowledgement"] = true;
            CallToolResult mixed = await m_mcp.CallAsync("gms_acknowledge_results", arguments).ConfigureAwait(false);
            Assert.That(mixed.IsError, Is.True);
            JsonElement statuses = GmsMcpHarness.Json(mixed).GetProperty("results");
            Assert.That(statuses[0].GetProperty("resultId").GetString(), Is.EqualTo("R1"));
            Assert.That(statuses[0].GetProperty("domainError").GetInt32(), Is.Zero);
            Assert.That(statuses[0].GetProperty("acknowledged").GetBoolean(), Is.True);
            Assert.That(statuses[1].GetProperty("domainError").GetInt32(),
                Is.EqualTo(unchecked((int)StatusCodes.BadNotFound.Code)));
            Assert.That(statuses[1].GetProperty("acknowledged").GetBoolean(), Is.False);
            m_fixture.Store.Verify(value => value.AcknowledgeAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(Success(await ResultCallAsync("ById", new JsonObject { ["resultId"] = "R1" })
                .ConfigureAwait(false)).GetProperty("resultId").GetString(), Is.EqualTo("R1"));
            arguments["resultIds"] = new JsonArray("R1");
            Assert.That(Success(await m_mcp.CallAsync("gms_acknowledge_results", arguments).ConfigureAwait(false))
                .GetProperty("results")[0].GetProperty("acknowledged").GetBoolean(), Is.True);
            m_fixture.Store.Setup(value => value.AcknowledgeAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<int>>(Task.FromException<ArrayOf<int>>(
                    new ServiceResultException(StatusCodes.BadUserAccessDenied))));
            GmsMcpParameterTests.Error(
                await m_mcp.CallAsync("gms_acknowledge_results", arguments).ConfigureAwait(false),
                "BadUserAccessDenied");
        }

        /// <summary>
        /// Result-domain failure remains separate from service failure and does not retain a metadata handle.
        /// </summary>
        [Test]
        public async Task ResultDomainAndServiceFailuresRemainDistinctAndRecoverable()
        {
            CallToolResult empty = await ResultCallAsync("Latest").ConfigureAwait(false);
            Assert.That(empty.IsError, Is.True);
            JsonElement error = GmsMcpHarness.Json(empty);
            Assert.That(error.GetProperty("errorKind").GetString(), Is.EqualTo("ResultDomain"));
            Assert.That(error.GetProperty("domainError").GetInt32(),
                Is.EqualTo(unchecked((int)StatusCodes.BadNoDataAvailable.Code)));
            Assert.That(error.TryGetProperty("statusCode", out _), Is.False);
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("present")).ConfigureAwait(false);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                CallToolResult missing = await ResultCallAsync("ById", new JsonObject { ["resultId"] = "absent" })
                    .ConfigureAwait(false);
                Assert.That(missing.IsError, Is.True);
                Assert.That(GmsMcpHarness.Json(missing).GetProperty("operation").GetString(),
                    Is.EqualTo("GetResultById"));
            }
            Assert.That(Success(await ResultCallAsync("Latest").ConfigureAwait(false))
                .GetProperty("resultId").GetString(), Is.EqualTo("present"));
            m_fixture.Store.Setup(value => value.ReadLatestAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<GMS.Contracts.GmsResult?>(Task.FromException<GMS.Contracts.GmsResult?>(
                    new ServiceResultException(StatusCodes.BadOutOfService))));
            GmsMcpParameterTests.Error(await ResultCallAsync("Latest").ConfigureAwait(false), "BadOutOfService");
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Exact byte limits and overflow free call-owned files/metadata while leaving caller-owned resources alive.
        /// </summary>
        [Test]
        public async Task DownloadBoundsReleaseOnlyOwnedResources()
        {
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("file", fileBytes: 64))
                .ConfigureAwait(false);
            var metadata = new GMSResultManagementTypeClient(
                m_fixture.Session, m_fixture.System.State.ResultManagement!.NodeId, NUnitTelemetryContext.Create());
            (uint ownedHandle, _, _) = await metadata.GetLatestResultAsync(0).ConfigureAwait(false);
            var transfer = new TemporaryFileTransferClient(
                m_fixture.Session, m_fixture.System.State.ResultManagement.ResultTransfer!.NodeId);
            await using UaFileStream ownedFile = await transfer.GenerateFileForReadAsync(
                Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = "file" })).ConfigureAwait(false);
            try
            {
                GmsMcpParameterTests.Error(await DownloadAsync("file", 63).ConfigureAwait(false),
                    "BadEncodingLimitsExceeded");
                JsonElement file = Success(await DownloadAsync("file", 64).ConfigureAwait(false));
                Assert.That(file.GetProperty("byteCount").GetInt32(), Is.EqualTo(64));
                Assert.That(Convert.FromBase64String(file.GetProperty("contentBase64").GetString()!),
                    Is.EqualTo(Enumerable.Range(0, 64).Select(value => (byte)value).ToArray()));
                Assert.That(Success(await DownloadAsync("file", 65).ConfigureAwait(false))
                    .GetProperty("byteCount").GetInt32(), Is.EqualTo(64));
                byte[] prefix = new byte[4];
                Assert.That(await ownedFile.ReadAsync(prefix).ConfigureAwait(false), Is.EqualTo(4));
                Assert.That(prefix, Is.EqualTo(new byte[] { 0, 1, 2, 3 }));
                Assert.That(m_fixture.Session.Connected, Is.True);
                Assert.That(await metadata.ReleaseResultHandleAsync(ownedHandle).ConfigureAwait(false), Is.Zero,
                    "The MCP call must not release the caller's pinned metadata.");
                ownedHandle = 0;
            }
            finally
            {
                if (ownedHandle != 0)
                {
                    await metadata.ReleaseResultHandleAsync(ownedHandle).ConfigureAwait(false);
                }
            }
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Missing optional transfer and base64 envelope overflow fail while keeping later native calls usable.
        /// </summary>
        [Test]
        public async Task DownloadOptionalAbsenceAndEncodedOverflowNeverReturnPartialFiles()
        {
            await m_fixture.Minimal.Results.PublishAsync(
                GmsMcpFixture.Result("inline", fileBytes: 0, includeUsedTools: false)).ConfigureAwait(false);
            GmsMcpParameterTests.Error(await m_mcp.CallAsync("gms_download_result", new JsonObject
            {
                ["systemNodeId"] = m_fixture.Minimal.NodeId.ToString(),
                ["resultId"] = "inline"
            }).ConfigureAwait(false), "BadNotSupported");
            Assert.That(Success(await ResultCallAsync("Latest",
                new JsonObject { ["systemNodeId"] = m_fixture.Minimal.NodeId.ToString() }).ConfigureAwait(false))
                .GetProperty("resultId").GetString(), Is.EqualTo("inline"));
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("larger", fileBytes: 4096))
                .ConfigureAwait(false);
            CallToolResult oversized = await DownloadAsync("larger", 4096, 4096).ConfigureAwait(false);
            JsonElement error = GmsMcpParameterTests.Error(oversized, "BadEncodingLimitsExceeded");
            Assert.That(error.TryGetProperty("contentBase64", out _), Is.False);
            Assert.That(Success(await DownloadAsync("larger", 4096).ConfigureAwait(false))
                .GetProperty("byteCount").GetInt32(), Is.EqualTo(4096));
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Sends a grouped result operation with the full native instance selected by default.
        /// </summary>
        private Task<CallToolResult> ResultCallAsync(string operation, JsonObject? arguments = null)
        {
            arguments ??= [];
            if (!arguments.ContainsKey("systemNodeId"))
            {
                arguments["systemNodeId"] = m_fixture.System.NodeId.ToString();
            }
            arguments["operation"] = operation;
            return m_mcp.CallAsync("gms_read_results", arguments);
        }

        /// <summary>
        /// Sends a bounded inline download through the MCP transport.
        /// </summary>
        private Task<CallToolResult> DownloadAsync(string id, int maximumBytes, int responseBytes = 131072)
        {
            return m_mcp.CallAsync("gms_download_result", new JsonObject
            {
                ["systemNodeId"] = m_fixture.System.NodeId.ToString(),
                ["resultId"] = id,
                ["maximumDownloadBytes"] = maximumBytes,
                ["maximumResponseBytes"] = responseBytes
            });
        }

        /// <summary>
        /// Decodes the actual method-returned Machinery envelope without adding acquisition metadata.
        /// </summary>
        private ResultDataType ResultData(JsonElement value)
        {
            using var decoder = new JsonDecoder(
                value.GetProperty("result").GetRawText(), m_fixture.Session.MessageContext);
            Variant decoded = decoder.ReadVariant("Value");
            Assert.That(decoded.TryGetStructure<ResultDataType>(out ResultDataType? result), Is.True);
            return result!;
        }

        /// <summary>The expected ascending result identity order for the selected native job.</summary>
        private static readonly string[] s_filteredResults = ["R1", "R3"];
    }
}
#endif
