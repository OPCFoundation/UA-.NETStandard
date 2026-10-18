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
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tests;
using V2 = Opc.Ua.ISA95.JobControl.V2;
using static Opc.Ua.Tools.Tests.MachineToolServiceMcpHarness;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Pins validation before session I/O, native payload semantics, and every hard service boundary.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class MachineToolServiceMcpParameterTests
    {
        /// <summary>
        /// All five tools reject both adjacent out-of-range response budgets before accessing a session.
        /// </summary>
        [TestCase("query", 1023)]
        [TestCase("query", 1048577)]
        [TestCase("control", 1023)]
        [TestCase("control", 1048577)]
        [TestCase("list", 1023)]
        [TestCase("list", 1048577)]
        [TestCase("read", 1023)]
        [TestCase("read", 1048577)]
        [TestCase("change", 1023)]
        [TestCase("change", 1048577)]
        public async Task ResponseBoundsAreCheckedBeforeSessionAccess(string tool, int bytes)
        {
            CallToolResult result = await (tool switch
            {
                "query" => MachineToolServiceTools.QueryJobsAsync(null!, "i=1", maximumResponseBytes: bytes),
                "control" => MachineToolServiceTools.ControlJobAsync(
                    null!, "i=1", "Start", "job", confirm: true, maximumResponseBytes: bytes),
                "list" => MachineToolServiceTools.ListFilesAsync(null!, "i=1", maximumResponseBytes: bytes),
                "read" => MachineToolServiceTools.ReadFileAsync(null!, "i=1", "file", maximumResponseBytes: bytes),
                _ => MachineToolServiceTools.ChangeFileAsync(null!, "i=1", "Delete", "file",
                    confirm: true, maximumResponseBytes: bytes)
            }).ConfigureAwait(false);
            Error(result, "BadOutOfRange");
        }

        /// <summary>
        /// Record limits apply independently to jobs and directories.
        /// </summary>
        [TestCase(0)]
        [TestCase(129)]
        public async Task RecordBoundsAreCheckedBeforeSessionAccess(int maximum)
        {
            Error(await MachineToolServiceTools.QueryJobsAsync(null!, "i=1", maximumResults: maximum)
                .ConfigureAwait(false), "BadOutOfRange");
            Error(await MachineToolServiceTools.ListFilesAsync(null!, "i=1", maximumEntries: maximum)
                .ConfigureAwait(false), "BadOutOfRange");
        }

        /// <summary>
        /// Download and upload ceilings share inclusive one-byte through 512-KiB bounds.
        /// </summary>
        [TestCase(0)]
        [TestCase(524289)]
        public async Task FileByteBoundsAreCheckedBeforeSessionAccess(int maximum)
        {
            Error(await MachineToolServiceTools.ReadFileAsync(null!, "i=1", "file", maximumBytes: maximum)
                .ConfigureAwait(false), "BadOutOfRange");
            Error(await MachineToolServiceTools.ChangeFileAsync(
                null!, "i=1", "Write", "file", options: Parse(new JsonObject
                {
                    ["contentBase64"] = string.Empty,
                    ["maximumBytes"] = maximum
                }.ToJsonString()), confirm: true)
                .ConfigureAwait(false), "BadOutOfRange");
        }

        /// <summary>
        /// Read-only and mutation tools never reinterpret unknown root names as FileSystem.
        /// </summary>
        [TestCase("")]
        [TestCase("fileSystem")]
        [TestCase("0")]
        [TestCase("C:\\content")]
        public async Task UnknownRootsAreRejected(string root)
        {
            Error(await MachineToolServiceTools.ListFilesAsync(null!, "i=1", root).ConfigureAwait(false),
                "BadInvalidArgument");
            Error(await MachineToolServiceTools.ReadFileAsync(null!, "i=1", "file", root).ConfigureAwait(false),
                "BadInvalidArgument");
            Error(await MachineToolServiceTools.ChangeFileAsync(null!, "i=1", "Delete", "file", root, confirm: true)
                .ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Selector spellings are deliberate and never become a different destructive operation.
        /// </summary>
        [TestCase("")]
        [TestCase("start")]
        [TestCase("1")]
        [TestCase("Start,Abort")]
        public async Task UnknownJobOperationsAreRejected(string operation)
        {
            Error(await MachineToolServiceTools.ControlJobAsync(null!, "i=1", operation, "job", confirm: true)
                .ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Query selector and its ID/state discriminants must agree.
        /// </summary>
        [TestCase("unknown", null, "null")]
        [TestCase("Orders", "job", "null")]
        [TestCase("Orders", null, "[]")]
        [TestCase("ResponseById", null, "null")]
        [TestCase("ResponseById", "job", "[]")]
        [TestCase("ResponsesByState", null, "null")]
        [TestCase("ResponsesByState", null, "[]")]
        [TestCase("ResponsesByState", "job", "[{\"StateNumber\":1}]")]
        [TestCase("ResponsesByState", null, "[null]")]
        [TestCase("ResponsesByState", null, "[{}]")]
        [TestCase("ResponsesByState", null, "[{\"StateNumber\":-1}]")]
        [TestCase("ResponsesByState", null, "[{\"StateNumber\":\"1\"}]")]
        public async Task InvalidJobQueryDiscriminantsFailBeforeSessionAccess(string query, string? id, string states)
        {
            Error(await MachineToolServiceTools.QueryJobsAsync(null!, "i=1", query, id, Parse(states))
                .ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Complete native orders and ID-only verbs are not interchangeable inputs.
        /// </summary>
        [TestCase("Store", null, "null")]
        [TestCase("StoreAndStart", null, "{}")]
        [TestCase("Update", null, "{\"JobOrderID\":4}")]
        [TestCase("Store", "job", "{\"JobOrderID\":\"job\"}")]
        [TestCase("Start", null, "null")]
        [TestCase("Start", "job", "{\"JobOrderID\":\"job\"}")]
        [TestCase("Store", null, "{\"JobOrderID\":\"job\",\"EncodingMask\":1024}")]
        [TestCase("Store", null, "{\"JobOrderID\":\"job\",\"EncodingMask\":\"1\"}")]
        public async Task InvalidJobCommandDiscriminantsFailBeforeSessionAccess(
            string operation, string? id, string order)
        {
            Error(await MachineToolServiceTools.ControlJobAsync(
                null!, "i=1", operation, id, Parse(order), confirm: true).ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Both mutation tools require affirmative consent even for otherwise valid requests.
        /// </summary>
        [Test]
        public async Task MutationConsentDefaultsToDenied()
        {
            Error(await MachineToolServiceTools.ControlJobAsync(null!, "i=1", "Start", "job").ConfigureAwait(false),
                "BadUserAccessDenied");
            Error(await MachineToolServiceTools.ChangeFileAsync(null!, "i=1", "Delete", "file").ConfigureAwait(false),
                "BadUserAccessDenied");
        }

        /// <summary>
        /// Unsafe names are rejected after namespace parsing and UaPath unescaping, not merely by a raw prefix check.
        /// </summary>
        [TestCase("../file")]
        [TestCase("folder/../file")]
        [TestCase("folder/2:../file")]
        [TestCase("folder/./file")]
        [TestCase("folder&/../file")]
        [TestCase("folder/child&/file")]
        [TestCase("folder/child&:stream")]
        [TestCase("C:\\secret")]
        [TestCase("C:/secret")]
        [TestCase("\\\\host\\secret")]
        [TestCase("//host/secret")]
        [TestCase("folder//file")]
        [TestCase("folder/file.")]
        [TestCase("folder/file ")]
        [TestCase("folder/\tfile")]
        [TestCase("/")]
        public async Task UnsafePathsFailBeforeSessionAccess(string path)
        {
            Error(await MachineToolServiceTools.ReadFileAsync(null!, "i=1", path).ConfigureAwait(false),
                "BadInvalidArgument");
            Error(await MachineToolServiceTools.ChangeFileAsync(null!, "i=1", "Delete", path, confirm: true)
                .ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Existing namespace-qualified paths retain their namespace and root-relative identity.
        /// </summary>
        [Test]
        public void SafePathsAndNewLeafRulesAreExplicit()
        {
            Assert.That(MachineToolServiceParameters.Path("/4:folder/6:part.nc/", false),
                Is.EqualTo("/4:folder/6:part.nc"));
            Assert.That(MachineToolServiceParameters.Path("/", true), Is.EqualTo("/"));
            Assert.That(MachineToolServiceParameters.Path("4:folder/new.nc", false, true),
                Is.EqualTo("/4:folder/new.nc"));
            Assert.Throws<ArgumentException>(() => MachineToolServiceParameters.Path("4:folder/6:new.nc", false, true));
            Assert.Throws<ArgumentException>(() => MachineToolServiceParameters.Path(new string('x', 256), false));
            Assert.Throws<ArgumentException>(() =>
                MachineToolServiceParameters.Path(string.Join("/", Enumerable.Repeat("x", 33)), false));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MachineToolServiceParameters.Path(new string('x', 1025), false));
        }

        /// <summary>
        /// File action-only arguments cannot be silently ignored or cause implicit creation.
        /// </summary>
        [TestCase("unknown", null, null, false)]
        [TestCase("Copy", null, null, false)]
        [TestCase("Move", "file", null, false)]
        [TestCase("Copy", "../file", null, false)]
        [TestCase("Copy", "2:new", null, false)]
        [TestCase("Delete", "other", null, false)]
        [TestCase("Delete", null, "", false)]
        [TestCase("CreateFile", null, null, true)]
        [TestCase("Write", null, null, false)]
        public async Task FileOperationArgumentsAreDiscriminated(
            string operation, string? destination, string? content, bool recursive)
        {
            Error(await MachineToolServiceTools.ChangeFileAsync(null!, "i=1", operation, "file",
                options: Parse(new JsonObject
                {
                    ["destination"] = destination,
                    ["contentBase64"] = content,
                    ["recursive"] = recursive
                }.ToJsonString()), confirm: true)
                .ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Grouped options reject ambiguous or coerced inputs before touching the session.
        /// </summary>
        [TestCase("[]")]
        [TestCase("\"{}\"")]
        [TestCase("false")]
        [TestCase("{\"other\":1}")]
        [TestCase("{\"destination\":1}")]
        [TestCase("{\"contentBase64\":[]}")]
        [TestCase("{\"maximumBytes\":\"1\"}")]
        [TestCase("{\"maximumBytes\":null}")]
        [TestCase("{\"maximumBytes\":1.5}")]
        [TestCase("{\"maximumBytes\":2147483648}")]
        [TestCase("{\"recursive\":1}")]
        [TestCase("{\"recursive\":\"true\"}")]
        [TestCase("{\"recursive\":null}")]
        [TestCase("{\"recursive\":false,\"recursive\":true}")]
        public async Task GroupedFileOptionsRejectInvalidNativeShapes(string options)
        {
            Error(await MachineToolServiceTools.ChangeFileAsync(
                null!, "i=1", "Delete", "file", options: Parse(options), confirm: true).ConfigureAwait(false),
                "BadInvalidArgument");
        }

        /// <summary>
        /// Omitted, null and empty native options retain the established nonrecursive 64-KiB defaults.
        /// </summary>
        [Test]
        public void GroupedFileOptionsPreserveDefaultsAndExplicitValues()
        {
            foreach (JsonElement input in new[] { default, Parse("null"), Parse("{}") })
            {
                var options = MachineToolServiceParameters.FileOptions(input);
                Assert.That(options.Destination, Is.Null);
                Assert.That(options.ContentBase64, Is.Null);
                Assert.That(options.MaximumBytes, Is.EqualTo(65536));
                Assert.That(options.Recursive, Is.False);
            }
            var explicitOptions = MachineToolServiceParameters.FileOptions(Parse("""
                {"destination":"next","contentBase64":"","maximumBytes":524288,"recursive":true}
                """));
            Assert.That(explicitOptions.Destination, Is.EqualTo("next"));
            Assert.That(explicitOptions.ContentBase64, Is.Empty);
            Assert.That(explicitOptions.MaximumBytes, Is.EqualTo(524288));
            Assert.That(explicitOptions.Recursive, Is.True);
        }

        /// <summary>
        /// Base64 validation checks encoded allocation and decoded byte boundaries separately.
        /// </summary>
        [Test]
        public void UploadBoundariesRejectWhitespaceMalformedAndAdjacentOversize()
        {
            Assert.That(MachineToolServiceParameters.Upload(string.Empty, 1).Length, Is.Zero);
            Assert.That(MachineToolServiceParameters.Upload("AQ==", 1).ToArray(), Is.EqualTo(new byte[] { 1 }));
            Assert.Throws<ArgumentException>(() => MachineToolServiceParameters.Upload("AQ= ", 1));
            Assert.Throws<FormatException>(() => MachineToolServiceParameters.Upload("????", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineToolServiceParameters.Upload("AQI=", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineToolServiceParameters.Upload("AQIDBA==", 1));
            byte[] maximum = new byte[MachineToolServiceParameters.MaximumFileBytes];
            Assert.That(MachineToolServiceParameters.Upload(Convert.ToBase64String(maximum), maximum.Length).Length,
                Is.EqualTo(maximum.Length));
        }

        /// <summary>
        /// Oversized native byte budgets, arrays, nesting, tokens and non-finite numbers all fail before I/O.
        /// </summary>
        [Test]
        public async Task NativeJsonHasIndependentAmplificationBounds()
        {
            string[] oversized =
            [
                "{\"JobOrderID\":\"job\",\"Extra\":\"" + new string('x', 65536) + "\"}",
                "{\"JobOrderID\":\"job\",\"Extra\":\"" + new string('\u6F22', 24000) + "\"}",
                "{\"JobOrderID\":\"job\",\"Extra\":[" + string.Join(",", Enumerable.Repeat("0", 1025)) + "]}",
                "{\"JobOrderID\":\"job\",\"Extra\":" + new string('[', 33) + "0" + new string(']', 33) + "}",
                "{\"JobOrderID\":\"job\",\"Extra\":[" +
                    string.Join(",", Enumerable.Repeat("[" + string.Join(",", Enumerable.Repeat("0", 900)) + "]", 5)) +
                    "]}",
                "{\"JobOrderID\":\"job\",\"Extra\":1e999}"
            ];
            foreach (string order in oversized)
            {
                Error(await MachineToolServiceTools.ControlJobAsync(
                    null!, "i=1", "Store", jobOrder: Parse(order), confirm: true).ConfigureAwait(false),
                    "BadOutOfRange");
            }
            Error(await MachineToolServiceTools.ControlJobAsync(null!, "i=1", "Start", "job",
                comments: Parse("[0,0,0,0,0,0,0,0,0]"), confirm: true).ConfigureAwait(false), "BadInvalidArgument");
            Error(await MachineToolServiceTools.QueryJobsAsync(null!, "i=1", "ResponsesByState",
                states: Parse("[" + string.Join(",", Enumerable.Repeat("{\"StateNumber\":1}", 17)) + "]"))
                .ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Native decoding preserves an explicit zero-valued optional field and the exact optional mask.
        /// </summary>
        [Test]
        public void NativeJobCodecPreservesMasksVariantsAndLocalizedComments()
        {
            IServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var original = new V2.ISA95JobOrderDataType
            {
                EncodingMask = (uint)(V2.ISA95JobOrderDataTypeFields.Priority |
                    V2.ISA95JobOrderDataTypeFields.WorkMasterID | V2.ISA95JobOrderDataTypeFields.JobOrderParameters),
                JobOrderID = "job",
                Priority = 0,
                WorkMasterID = [new V2.ISA95WorkMasterDataType { ID = "work" }],
                JobOrderParameters =
                [
                    new V2.ISA95ParameterDataType { ID = "native", Value = Variant.From(17u) }
                ]
            };
            JsonNode encoded = MachineToolServiceJson.Encode(original, context)!;
            Assert.That(encoded["EncodingMask"]!.GetValue<uint>(), Is.EqualTo(original.EncodingMask));
            V2.ISA95JobOrderDataType decoded = MachineToolServiceParameters.DecodeOrder(
                Parse(encoded.ToJsonString()), context);
            Assert.That(decoded.EncodingMask, Is.EqualTo(original.EncodingMask));
            Assert.That(decoded.Priority, Is.Zero);
            Assert.That(decoded.WorkMasterID[0].ID, Is.EqualTo("work"));
            Assert.That(decoded.JobOrderParameters[0].Value.TryGetValue(out uint value), Is.True);
            Assert.That(value, Is.EqualTo(17u));
            ArrayOf<LocalizedText> comments = MachineToolServiceParameters.DecodeComments(
                Parse("[{\"Text\":\"approved\",\"Locale\":\"en\"},{\"Text\":\"freigegeben\",\"Locale\":\"de\"}]"),
                context);
            Assert.That(comments.Count, Is.EqualTo(2));
            Assert.That(comments[1].Text, Is.EqualTo("freigegeben"));
            Assert.That(comments[1].Locale, Is.EqualTo("de"));
            Assert.That(MachineToolServiceParameters.DecodeComments(default, context).Count, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineToolServiceParameters.DecodeComments(
                Parse("[{\"Text\":\"" + new string('x', 1025) + "\"}]"), context));
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineToolServiceParameters.DecodeComments(
                Parse("[{\"Text\":\"ok\",\"Locale\":\"" + new string('x', 65) + "\"}]"), context));
        }

        /// <summary>
        /// Zero is not success, combined bitmaps remain intact, and receipts never claim physical execution.
        /// </summary>
        [TestCase(0UL, false)]
        [TestCase(1UL, true)]
        [TestCase(2UL, false)]
        [TestCase(3UL, true)]
        [TestCase(4294967296UL, false)]
        [TestCase(ulong.MaxValue, true)]
        public void Isa95ReturnStatusUsesTheSuccessBit(ulong value, bool accepted)
        {
            JsonObject result = MachineToolServiceJson.JobStatus("Start", value);
            Assert.That(result["accepted"]!.GetValue<bool>(), Is.EqualTo(accepted));
            Assert.That(ulong.Parse(result["returnStatus"]!.GetValue<string>(),
                System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(value));
            Assert.That(result["meaning"]!.GetValue<string>(), Does.Contain("does not confirm physical execution"));
            Assert.That(MachineToolJson.Result(result, 1024).IsError, Is.Not.True);
        }

        /// <summary>
        /// Cancellation remains cancellation and cannot be rendered as an accepted operation.
        /// </summary>
        [Test]
        public void PreCancellationNeverTouchesTheSession()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await MachineToolServiceTools.QueryJobsAsync(null!, "i=1", ct: cancelled.Token).ConfigureAwait(false));
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await MachineToolServiceTools.ControlJobAsync(null!, "i=1", "Start", "job", confirm: true,
                    ct: cancelled.Token).ConfigureAwait(false));
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await MachineToolServiceTools.ListFilesAsync(null!, "i=1", ct: cancelled.Token).ConfigureAwait(false));
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await MachineToolServiceTools.ReadFileAsync(null!, "i=1", "file", ct: cancelled.Token)
                    .ConfigureAwait(false));
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await MachineToolServiceTools.ChangeFileAsync(null!, "i=1", "Delete", "file", confirm: true,
                    ct: cancelled.Token).ConfigureAwait(false));
        }

        /// <summary>
        /// Requires the precise mapped error rather than a false-success fallback.
        /// </summary>
        internal static JsonElement Error(CallToolResult result, string code)
        {
            Assert.That(result.IsError, Is.True);
            JsonElement json = Json(result);
            Assert.That(json.GetProperty("statusCode").GetString(), Does.Contain(code));
            return json;
        }
    }
}
#endif
