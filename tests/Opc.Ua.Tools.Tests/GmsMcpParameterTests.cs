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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tests;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Tests semantic bounds and errors independently of the MCP schema and of any live server.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GmsMcpParameterTests
    {
        /// <summary>
        /// Every tool validates both adjacent response-budget violations before acquiring a session.
        /// </summary>
        [TestCase("list")]
        [TestCase("system")]
        [TestCase("results")]
        [TestCase("download")]
        [TestCase("observe")]
        [TestCase("ack")]
        public async Task ResponseBoundsFailBeforeSessionAccess(string tool)
        {
            foreach (int bytes in new[] { 1023, 1048577 })
            {
                Error(await Invoke(tool, null!, bytes: bytes).ConfigureAwait(false), "BadOutOfRange");
            }
        }

        /// <summary>
        /// Discovery, snapshot, result, time and file caps are actual enforced limits, not schema-only hints.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(101)]
        public async Task InventoryAndResultCountsRejectAdjacentViolations(int maximum)
        {
            Error(await GmsTools.ListSystemsAsync(null!, maximum).ConfigureAwait(false), "BadOutOfRange");
            Error(await GmsTools.ReadSystemAsync(null!, "i=1", maximumElements: maximum).ConfigureAwait(false),
                "BadOutOfRange");
            Error(await GmsTools.ReadResultsAsync(null!, "i=1", maximumResults: maximum).ConfigureAwait(false),
                "BadOutOfRange");
        }

        /// <summary>
        /// Time and notification limits are checked before allocating any subscription.
        /// </summary>
        [TestCase(-1, 1)]
        [TestCase(0, 1)]
        [TestCase(61, 1)]
        [TestCase(1, -1)]
        [TestCase(1, 0)]
        [TestCase(1, 101)]
        public async Task ObservationRejectsUnboundedRequests(int seconds, int count)
        {
            Error(await GmsTools.ObserveAsync(null!, "i=1", durationSeconds: seconds, maximumNotifications: count)
                .ConfigureAwait(false), "BadOutOfRange");
        }

        /// <summary>
        /// Raw download bounds are independent of the encoded response bound.
        /// </summary>
        [TestCase(-1, 30)]
        [TestCase(0, 30)]
        [TestCase(524289, 30)]
        [TestCase(1, -1)]
        [TestCase(1, 0)]
        [TestCase(1, 61)]
        public async Task DownloadRejectsInvalidByteOrTimeLimits(int bytes, int seconds)
        {
            Error(await GmsTools.DownloadResultAsync(null!, "i=1", "R", bytes, seconds).ConfigureAwait(false),
                "BadOutOfRange");
        }

        /// <summary>
        /// The exact inclusive limits remain accepted by the shared parameter helpers.
        /// </summary>
        [Test]
        public void ExactBoundsAreAccepted()
        {
            GmsParameters.ResponseLimit(1024);
            GmsParameters.ResponseLimit(1048576);
            Assert.That(GmsParameters.Observation(1, 1, 1024).Window, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(GmsParameters.Observation(60, 100, 1048576).MaximumNotifications, Is.EqualTo(100));
            Assert.That(GmsParameters.Results(101, 1048576, 524288).MaximumResults, Is.EqualTo(101));
            Assert.That(GmsParameters.Results(1, 1024, 1).MaximumFileBytes, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => GmsParameters.Results(102, 1024));
        }

        /// <summary>
        /// Selectors accept only documented case-sensitive values, not enum numbers or invented control verbs.
        /// </summary>
        [TestCase("")]
        [TestCase("0")]
        [TestCase("system")]
        [TestCase("StartJob")]
        public async Task InvalidSelectorsNeverReachTheSession(string selector)
        {
            Error(await GmsTools.ReadSystemAsync(null!, "i=1", selector).ConfigureAwait(false), "BadInvalidArgument");
            Error(await GmsTools.ReadResultsAsync(null!, "i=1", selector).ConfigureAwait(false), "BadInvalidArgument");
            Error(await GmsTools.ObserveAsync(null!, "i=1", selector).ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Optional arguments cannot be silently ignored on an unrelated grouped operation.
        /// </summary>
        [Test]
        public async Task InapplicableArgumentsAreRejected()
        {
            Error(await GmsTools.ReadResultsAsync(null!, "i=1", "Latest", resultId: "R").ConfigureAwait(false),
                "BadInvalidArgument");
            Error(await GmsTools.ReadResultsAsync(null!, "i=1", "ById").ConfigureAwait(false), "BadInvalidArgument");
            var filter = new JsonObject { ["filterField"] = "JobId", ["filterValue"] = "J" };
            Error(await GmsTools.ReadResultsAsync(null!, "i=1", "Published",
                query: Json(filter.ToJsonString())).ConfigureAwait(false),
                "BadInvalidArgument");
            Error(await GmsTools.ReadResultsAsync(null!, "i=1", "Latest",
                query: Json(/*lang=json,strict*/ "{\"orderBy\":\"None\"}")).ConfigureAwait(false),
                "BadInvalidArgument");
            Error(await GmsTools.ObserveAsync(null!, "i=1", characteristics: Json("[]")).ConfigureAwait(false),
                "BadInvalidArgument");
        }

        /// <summary>
        /// Null, empty and arbitrarily long identities never expand unbounded remote requests.
        /// </summary>
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("i=0")]
        public async Task NullNodeSelectorsAreNotImplicitRoots(string node)
        {
            Error(await GmsTools.ReadSystemAsync(null!, node).ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Node and result identifier lengths are checked outside schema validation.
        /// </summary>
        [Test]
        public async Task OversizedIdentifiersAreRejected()
        {
            Error(await GmsTools.ReadSystemAsync(null!, "s=" + new string('x', 1024)).ConfigureAwait(false),
                "BadOutOfRange");
            Error(await GmsTools.DownloadResultAsync(null!, "i=1", new string('x', 1025)).ConfigureAwait(false),
                "BadOutOfRange");
            Error(await GmsTools.DownloadResultAsync(null!, "i=1", " ").ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Acknowledgement needs explicit consent before any validation or session work.
        /// </summary>
        [Test]
        public async Task AcknowledgementDefaultsToDenied()
        {
            Error(await GmsTools.AcknowledgeResultsAsync(null!, string.Empty, default).ConfigureAwait(false),
                "BadUserAccessDenied");
        }

        /// <summary>
        /// Acknowledgement refuses an unrepresentable reply before it can change even one result's state.
        /// </summary>
        [Test]
        public async Task AcknowledgementPreflightsReplyBudgetBeforeSessionAccess()
        {
            var identifiers = new JsonArray(new string('測', 300));
            Error(await GmsTools.AcknowledgeResultsAsync(null!, "i=1", Json(identifiers.ToJsonString()), true,
                maximumResponseBytes: 1024).ConfigureAwait(false), "BadEncodingLimitsExceeded");
        }

        /// <summary>
        /// Native result arrays reject null, wrong types and duplicate identities before calling a method.
        /// </summary>
        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("[1]")]
        [TestCase("[null]")]
        [TestCase("[\"\"]")]
        [TestCase("[\"R\",\"R\"]")]
        public async Task InvalidAcknowledgementArraysAreRejected(string array)
        {
            Error(await GmsTools.AcknowledgeResultsAsync(null!, "i=1", Json(array), true).ConfigureAwait(false),
                "BadInvalidArgument");
        }

        /// <summary>
        /// Native array maximums accept exact boundaries and refuse the immediately adjacent element count.
        /// </summary>
        [Test]
        public void NativeArrayMaximumsAreEnforced()
        {
            var ids = new JsonArray();
            for (int i = 0; i < 100; i++)
            {
                ids.Add((JsonNode?)("R" + i));
            }
            Assert.That(GmsParameters.Identifiers(Json(ids.ToJsonString())).Count, Is.EqualTo(100));
            ids.Add((JsonNode?)"extra");
            Assert.Throws<ArgumentException>(() => GmsParameters.Identifiers(Json(ids.ToJsonString())));
            var names = new JsonArray();
            for (int i = 0; i < 32; i++)
            {
                names.Add((JsonNode)new JsonObject { ["name"] = "C" + i, ["namespaceIndex"] = 65535 });
            }
            ArrayOf<QualifiedName> selected = GmsParameters.Characteristics(Json(names.ToJsonString()));
            Assert.That(selected.Count, Is.EqualTo(32));
            Assert.That(selected[0].NamespaceIndex, Is.EqualTo(65535));
            names.Add((JsonNode)new JsonObject { ["name"] = "extra", ["namespaceIndex"] = 0 });
            Assert.Throws<ArgumentException>(() => GmsParameters.Characteristics(Json(names.ToJsonString())));
            Assert.That(GmsParameters.Characteristics(default), Is.Empty);
            Assert.That(GmsParameters.Characteristics(Json("[]")), Is.Empty);
        }

        /// <summary>
        /// Placeholder names retain explicit namespace indices and reject ambiguous or malformed objects.
        /// </summary>
        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("[null]")]
        [TestCase("[\"Bore\"]")]
        [TestCase(/*lang=json,strict*/ "[{\"name\":\"Bore\"}]")]
        [TestCase(/*lang=json,strict*/ "[{\"name\":\"Bore\",\"namespaceIndex\":65536}]")]
        [TestCase(/*lang=json,strict*/ "[{\"name\":\"Bore\",\"namespaceIndex\":-1}]")]
        [TestCase(/*lang=json,strict*/ "[{\"name\":\"Bore\",\"namespaceIndex\":\"12\"}]")]
        [TestCase(/*lang=json,strict*/ "[{\"name\":\"Bore\",\"namespaceIndex\":12,\"extra\":true}]")]
        [TestCase(/*lang=json,strict*/ "[{\"name\":\"Bore\",\"name\":\"Other\",\"namespaceIndex\":12}]")]
        [TestCase(/*lang=json,strict*/ "[{\"name\":\"\",\"namespaceIndex\":12}]")]
        [TestCase(/*lang=json,strict*/ """[{"name":"Bore","namespaceIndex":12},{"name":"Bore","namespaceIndex":12}]""")]
        public void InvalidCharacteristicSelectionsAreRejected(string value)
        {
            Assert.Throws<ArgumentException>(() => GmsParameters.Characteristics(Json(value)));
        }

        /// <summary>
        /// Same text in distinct namespaces is not a duplicate, and each name's length remains bounded.
        /// </summary>
        [Test]
        public void CharacteristicQualificationAndNameLengthRemainExact()
        {
            var selection = new JsonArray(
                new JsonObject { ["name"] = "Bore", ["namespaceIndex"] = 0 },
                new JsonObject { ["name"] = "Bore", ["namespaceIndex"] = 12 });
            ArrayOf<QualifiedName> names = GmsParameters.Characteristics(Json(selection.ToJsonString()));
            Assert.That(names[0], Is.EqualTo(new QualifiedName("Bore", 0)));
            Assert.That(names[1], Is.EqualTo(new QualifiedName("Bore", 12)));
            var entry = new JsonArray(new JsonObject { ["name"] = new string('x', 256), ["namespaceIndex"] = 0 });
            Assert.That(GmsParameters.Characteristics(Json(entry.ToJsonString()))[0].Name, Has.Length.EqualTo(256));
            entry[0]!["name"] = new string('x', 257);
            Assert.Throws<ArgumentOutOfRangeException>(() => GmsParameters.Characteristics(Json(entry.ToJsonString())));
        }

        /// <summary>
        /// Filter literals are not coerced between strings, numbers, booleans and null.
        /// </summary>
        [TestCase("ResultId", "42")]
        [TestCase("JobId", "null")]
        [TestCase("IsPartial", "\"true\"")]
        [TestCase("IsPartial", "1")]
        [TestCase("None", "null")]
        [TestCase("Unknown", "\"R\"")]
        public void FilterTypesAreStrict(string field, string value)
        {
            var selection = new JsonObject { ["filterField"] = field, ["filterValue"] = JsonNode.Parse(value) };
            Assert.Throws<ArgumentException>(() => GmsParameters.Query(Json(selection.ToJsonString())));
        }

        /// <summary>
        /// Grouping filters never permits unknown, duplicate or incorrectly typed selection properties.
        /// </summary>
        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("\"JobId\"")]
        [TestCase("true")]
        [TestCase(/*lang=json,strict*/ "{\"filterField\":1}")]
        [TestCase(/*lang=json,strict*/ "{\"filterField\":null}")]
        [TestCase(/*lang=json,strict*/ "{\"orderBy\":true}")]
        [TestCase(/*lang=json,strict*/ "{\"orderBy\":\"Descending\"}")]
        [TestCase(/*lang=json,strict*/ "{\"maximumResults\":10000}")]
        [TestCase(/*lang=json,strict*/ "{\"filterField\":\"JobId\"}")]
        [TestCase(/*lang=json,strict*/ "{\"filterField\":\"None\",\"filterField\":\"JobId\",\"filterValue\":\"J\"}")]
        [TestCase(/*lang=json,strict*/ "{\"orderBy\":\"None\",\"orderBy\":\"ResultId\"}")]
        [TestCase(/*lang=json,strict*/ "{\"filterField\":\"JobId\",\"filterValue\":\"J\",\"filterValue\":\"K\"}")]
        public async Task InvalidQueryObjectsFailBeforeSessionAccess(string selection)
        {
            Error(await GmsTools.ReadResultsAsync(null!, "i=1", "Query", query: Json(selection))
                .ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Native selection defaults, exact identifier bounds and Boolean literals retain their original semantics.
        /// </summary>
        [Test]
        public void GroupedQueriesPreserveDefaultsAndNativeTypes()
        {
            foreach (JsonElement input in new[] { default, Json("{}") })
            {
                (string field, JsonElement value, string order) = GmsParameters.Query(input);
                Assert.That(field, Is.EqualTo("None"));
                Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
                Assert.That(order, Is.EqualTo("ResultId"));
            }
            var selection = new JsonObject { ["filterField"] = "ResultId", ["filterValue"] = new string('x', 1024) };
            (string selected, JsonElement identifier, string ordering) =
                GmsParameters.Query(Json(selection.ToJsonString()));
            Assert.That(selected, Is.EqualTo("ResultId"));
            Assert.That(identifier.ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(identifier.GetString(), Has.Length.EqualTo(1024));
            Assert.That(ordering, Is.EqualTo("ResultId"));
            selection["filterValue"] = new string('x', 1025);
            Assert.Throws<ArgumentOutOfRangeException>(() => GmsParameters.Query(Json(selection.ToJsonString())));
            selection["filterField"] = "IsPartial";
            selection["filterValue"] = false;
            selection["orderBy"] = "None";
            (string booleanField, JsonElement boolean, string noOrder) =
                GmsParameters.Query(Json(selection.ToJsonString()));
            Assert.That(booleanField, Is.EqualTo("IsPartial"));
            Assert.That(boolean.ValueKind, Is.EqualTo(JsonValueKind.False));
            Assert.That(noOrder, Is.EqualTo("None"));
        }

        /// <summary>
        /// Standard filter operands use the server's namespace mapping and preserve native literal types.
        /// </summary>
        [Test]
        public void FiltersUseNativeMachineryOperands()
        {
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend("urn:vendor:first");
            ushort index = namespaces.GetIndexOrAppend(Machinery.Result.Namespaces.MachineryResult);
            (ContentFilter filter, ArrayOf<RelativePath> order) = GmsParameters.Filter(
                namespaces, "JobId", Json("\"J-1\""), "ResultId");
            Assert.That(filter.Elements.Count, Is.EqualTo(1));
            Assert.That(filter.Elements[0].FilterOperator, Is.EqualTo(FilterOperator.Equals));
            Assert.That(filter.Elements[0].FilterOperands[0].TryGetValue(out SimpleAttributeOperand? operand), Is.True);
            Assert.That(operand!.BrowsePath[0], Is.EqualTo(new QualifiedName("ResultMetaData", index)));
            Assert.That(operand.BrowsePath[1], Is.EqualTo(new QualifiedName("JobId", index)));
            Assert.That(order[0].Elements[1].TargetName, Is.EqualTo(new QualifiedName("ResultId", index)));
            (ContentFilter boolean, _) = GmsParameters.Filter(namespaces, "IsPartial", Json("false"), "None");
            Assert.That(boolean.Elements[0].FilterOperands[1].TryGetValue(out LiteralOperand? literal), Is.True);
            Assert.That(literal!.Value.TryGetValue(out bool selected), Is.True);
            Assert.That(selected, Is.False);
            (ContentFilter empty, ArrayOf<RelativePath> none) =
                GmsParameters.Filter(namespaces, "None", default, "None");
            Assert.That(empty.Elements, Is.Empty);
            Assert.That(none, Is.Empty);
            ServiceResultException missing = Assert.Throws<ServiceResultException>(() =>
                GmsParameters.Filter(new NamespaceTable(), "None", default, "None"))!;
            Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Each facade owns no connection and missing model discovery performs no native service calls.
        /// </summary>
        [Test]
        public async Task MissingModelAndNamedSessionsUseOnlyTheSharedManager()
        {
            Mock<ISession> session = MockSession();
            await using (GmsMcpHarness harness = await GmsMcpHarness.CreateAsync(session.Object).ConfigureAwait(false))
            {
                CallToolResult empty = await harness.CallAsync("gms_list_systems").ConfigureAwait(false);
                Assert.That(empty.IsError, Is.False);
                Assert.That(GmsMcpHarness.Json(empty).GetProperty("modelPresent").GetBoolean(), Is.False);
                Assert.That(GmsMcpHarness.Json(empty).GetProperty("systems").GetArrayLength(), Is.Zero);
                Error(await harness.CallAsync("gms_list_systems", new JsonObject { ["sessionName"] = "unknown" })
                    .ConfigureAwait(false), "BadInvalidState");
                await harness.Manager.RegisterExistingSessionAsync("second", session.Object, "Anonymous")
                    .ConfigureAwait(false);
                Error(await harness.CallAsync("gms_list_systems").ConfigureAwait(false), "BadInvalidState");
                Assert.That((await harness.CallAsync("gms_list_systems", new JsonObject { ["sessionName"] = "gms" })
                    .ConfigureAwait(false)).IsError, Is.False);
                await using GmsClient client = harness.Accessor.CreateClient("gms");
                Assert.That(client.Session, Is.SameAs(session.Object));
                Assert.Throws<ArgumentOutOfRangeException>(() => new GmsClientAccessor(
                    harness.Manager, new GmsClientOptions { MaximumElements = 0 }));
                Assert.Throws<ArgumentOutOfRangeException>(() => harness.Accessor.CreateClient("gms", 101));
                Assert.Throws<ArgumentException>(() => harness.Accessor.CreateClient(" "));
                Assert.Throws<ArgumentOutOfRangeException>(() => harness.Accessor.CreateClient(new string('x', 129)));
            }
            session.Verify(value => value.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.WriteAsync(It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.CallAsync(It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.Dispose(), Times.Never);
            session.Verify(value => value.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// All valid workflows return a specific error if the shared host has no connected session.
        /// </summary>
        [TestCase("list")]
        [TestCase("system")]
        [TestCase("results")]
        [TestCase("download")]
        [TestCase("observe")]
        [TestCase("ack")]
        public async Task MissingSessionIsNotAnEmptySuccess(string tool)
        {
            await using GmsMcpHarness harness = await GmsMcpHarness.CreateAsync().ConfigureAwait(false);
            Error(await Invoke(tool, harness.Accessor).ConfigureAwait(false), "BadInvalidState");
        }

        /// <summary>
        /// Pre-cancelled requests never acquire a facade or turn cancellation into a tool success.
        /// </summary>
        [TestCase("list")]
        [TestCase("system")]
        [TestCase("results")]
        [TestCase("download")]
        [TestCase("observe")]
        [TestCase("ack")]
        public void PreCancellationRetainsTheCallersToken(string tool)
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            OperationCanceledException failure = Assert.CatchAsync<OperationCanceledException>(
                async () => await Invoke(tool, null!, cancellationToken: cancel.Token).ConfigureAwait(false))!;
            Assert.That(failure.CancellationToken, Is.EqualTo(cancel.Token));
        }

        /// <summary>
        /// The encoded byte boundary counts UTF-8 and complete JSON, not characters or just a raw payload.
        /// </summary>
        [Test]
        public void ClosedSerializerEnforcesTheExactEncodedByteBoundary()
        {
            var body = new JsonObject { ["value"] = "温度", ["reading"] = new JsonArray(1, 2, 3) };
            CallToolResult result = GmsJson.Result(body, 1024);
            string text = result.Content.OfType<TextContentBlock>().Single().Text;
            int bytes = Encoding.UTF8.GetByteCount(text);
            Assert.That(GmsMcpHarness.Json(GmsJson.Result(body, bytes)).GetProperty("value").GetString(),
                Is.EqualTo("温度"));
            ServiceResultException error =
                Assert.Throws<ServiceResultException>(() => GmsJson.Result(body, bytes - 1))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(GmsJsonContext.Default.JsonObject.Type, Is.EqualTo(typeof(JsonObject)));
        }

        /// <summary>
        /// Native absent folders and selected empty inventories are distinct even when neither contains a sample.
        /// </summary>
        [Test]
        public void ResultFolderAbsenceDiffersFromAnEmptySelectedFolder()
        {
            var json = new GmsJson(ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            JsonElement absent = GmsMcpHarness.Json(GmsJson.Result(json.Published(default), 1024));
            JsonElement empty = GmsMcpHarness.Json(GmsJson.Result(json.Published([]), 1024));
            Assert.That(absent.GetProperty("present").GetBoolean(), Is.False);
            Assert.That(absent.GetProperty("results").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(empty.GetProperty("present").GetBoolean(), Is.True);
            Assert.That(empty.GetProperty("results").GetArrayLength(), Is.Zero);
        }

        /// <summary>
        /// Expected transport/input failures preserve their error category, including nonzero result-domain codes.
        /// </summary>
        [Test]
        public async Task ErrorMappingKeepsServiceDomainAndTimeoutFailuresDistinct()
        {
            (Exception Failure, string Code)[] cases =
            [
                (new ServiceResultException(StatusCodes.BadUserAccessDenied), "BadUserAccessDenied"),
                (new ArgumentOutOfRangeException(), "BadOutOfRange"),
                (new ArgumentException("invalid"), "BadInvalidArgument"),
                (new FormatException("format"), "BadDecodingError"),
                (new JsonException("json"), "BadDecodingError"),
                (new UnauthorizedAccessException("denied"), "BadUserAccessDenied"),
                (new IOException("io"), "BadUnexpectedError"),
                (new IOException("io", new ServiceResultException(StatusCodes.BadNotFound)), "BadNotFound"),
                (new InvalidOperationException("state"), "BadInvalidState"),
                (new TimeoutException("deadline"), "BadTimeout"),
                (new AggregateException("cleanup", new IOException()), "BadUnexpectedError")
            ];
            foreach ((Exception failure, string code) in cases)
            {
                Error(await GmsJson.ExecuteAsync(() => Task.FromException<CallToolResult>(failure), default)
                    .ConfigureAwait(false), code);
            }
            CallToolResult domain = await GmsJson.ExecuteAsync(() => Task.FromException<CallToolResult>(
                new GmsResultDomainException("GetLatestResult", -42)), default).ConfigureAwait(false);
            Assert.That(domain.IsError, Is.True);
            JsonElement error = GmsMcpHarness.Json(domain);
            Assert.That(error.GetProperty("domainError").GetInt32(), Is.EqualTo(-42));
            Assert.That(error.GetProperty("errorKind").GetString(), Is.EqualTo("ResultDomain"));
            Assert.That(error.TryGetProperty("statusCode", out _), Is.False);
            CallToolResult longError = await GmsJson.ExecuteAsync(() => Task.FromException<CallToolResult>(
                new ArgumentException(new string('x', 3000))), default).ConfigureAwait(false);
            Assert.That(GmsMcpHarness.Json(longError).GetProperty("message").GetString(), Has.Length.EqualTo(128));
        }

        /// <summary>
        /// Creates detached JSON inputs without retaining a disposed document.
        /// </summary>
        internal static JsonElement Json(string value)
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Requires both MCP failure and the exact original OPC UA status category.
        /// </summary>
        internal static JsonElement Error(CallToolResult result, string status)
        {
            Assert.That(result.IsError, Is.True);
            JsonElement json = GmsMcpHarness.Json(result);
            Assert.That(json.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("statusCode").GetString(), Does.StartWith(status));
            return json;
        }

        /// <summary>
        /// Calls every public workflow with otherwise valid semantic arguments.
        /// </summary>
        private static Task<CallToolResult> Invoke(
            string tool, GmsClientAccessor accessor, int bytes = 131072, CancellationToken cancellationToken = default)
        {
            return tool switch
            {
                "list" => GmsTools.ListSystemsAsync(accessor, maximumResponseBytes: bytes, ct: cancellationToken),
                "system" => GmsTools.ReadSystemAsync(
                    accessor, "i=1", maximumResponseBytes: bytes, ct: cancellationToken),
                "results" => GmsTools.ReadResultsAsync(
                    accessor, "i=1", maximumResponseBytes: bytes, ct: cancellationToken),
                "download" => GmsTools.DownloadResultAsync(accessor, "i=1", "R",
                    maximumResponseBytes: bytes, ct: cancellationToken),
                "observe" => GmsTools.ObserveAsync(accessor, "i=1", maximumResponseBytes: bytes, ct: cancellationToken),
                _ => GmsTools.AcknowledgeResultsAsync(accessor, "i=1", Json("[\"R\"]"), true,
                    maximumResponseBytes: bytes, ct: cancellationToken)
            };
        }

        /// <summary>
        /// Provides the caller-owned connected-session boundary needed for native model-absence discovery.
        /// </summary>
        private static Mock<ISession> MockSession()
        {
            var session = new Mock<ISession>();
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            session.SetupGet(value => value.Connected).Returns(true);
            session.SetupGet(value => value.MessageContext).Returns(context);
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(value => value.ConfiguredEndpoint).Returns(
                new ConfiguredEndpoint(null, new EndpointDescription(), EndpointConfiguration.Create()));
            return session;
        }
    }
}
#endif
