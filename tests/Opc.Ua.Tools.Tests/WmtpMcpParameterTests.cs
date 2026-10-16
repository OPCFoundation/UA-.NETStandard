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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Pins exact native JSON inputs, local bounds, consent and cancellation without network side effects.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WmtpMcpParameterTests
    {
        /// <summary>
        /// Creates an empty shared session manager so omitted validation would fail with BadInvalidState instead.
        /// </summary>
        [OneTimeSetUp]
        public void SetUp()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore().AddOpcUaMcpMachineTool();
            m_services = services.BuildServiceProvider();
            m_accessor = m_services.GetRequiredService<MachineToolClientAccessor>();
        }

        /// <summary>
        /// Disposes all host services without creating a background session helper.
        /// </summary>
        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_services.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Both adjacent invalid boundaries are checked before session selection.
        /// </summary>
        [TestCase("measurements", 0)]
        [TestCase("measurements", 257)]
        [TestCase("records", 0)]
        [TestCase("records", 4097)]
        [TestCase("readBytes", 1023)]
        [TestCase("readBytes", 1048577)]
        [TestCase("reportBytes", 1023)]
        [TestCase("reportBytes", 1048577)]
        [TestCase("timeout", 0)]
        [TestCase("timeout", 61)]
        [TestCase("transferBytes", 0)]
        [TestCase("transferBytes", 1048577)]
        public async Task BoundsRejectAdjacentInvalidInputsBeforeSessionAccess(string bound, int value)
        {
            CallToolResult result = await (bound switch
            {
                "measurements" => WmtpMcpTools.ReadPeripheralAsync(m_accessor, "i=1", maximumMeasurements: value),
                "records" => WmtpMcpTools.ReportRecordsAsync(m_accessor, "i=1", "work", Json(kAll),
                    maximumRecords: value),
                "readBytes" => WmtpMcpTools.ReadPeripheralAsync(m_accessor, "i=1", maximumResponseBytes: value),
                "reportBytes" => WmtpMcpTools.ReportRecordsAsync(m_accessor, "i=1", "work", Json(kAll),
                    maximumResponseBytes: value),
                "timeout" => WmtpMcpTools.ReadPeripheralAsync(m_accessor, "i=1", timeoutSeconds: value),
                _ => WmtpMcpTools.TransferAsync(m_accessor, "i=1", "license", "AQ==",
                    maximumBytes: value, confirm: true)
            }).ConfigureAwait(false);
            AssertError(result, "BadOutOfRange");
        }

        /// <summary>
        /// Exact valid boundaries reach the empty session manager rather than being rejected as malformed.
        /// </summary>
        [TestCase(1, 1, 1024, 1)]
        [TestCase(256, 4096, 1048576, 60)]
        public async Task ExactResourceBoundariesReachSessionSelection(
            int measurements, int records, int bytes, int seconds)
        {
            AssertError(await WmtpMcpTools.ReadPeripheralAsync(m_accessor, "i=1",
                maximumMeasurements: measurements, maximumResponseBytes: bytes, timeoutSeconds: seconds)
                .ConfigureAwait(false), "BadInvalidState");
            AssertError(await WmtpMcpTools.ReportRecordsAsync(m_accessor, "i=1", "service", Json(kAll),
                maximumRecords: records, maximumResponseBytes: bytes, timeoutSeconds: seconds)
                .ConfigureAwait(false), "BadInvalidState");
        }

        /// <summary>
        /// A report, count and deletion do not reinterpret unsupported selectors or ignore unused endpoints.
        /// </summary>
        [TestCase("count", """{"selection":"index","fromIndex":1,"toIndex":2}""", "BadInvalidArgument")]
        [TestCase("count", """{"selection":"first"}""", "BadInvalidArgument")]
        [TestCase("count", """{"selection":"last"}""", "BadInvalidArgument")]
        [TestCase("delete", """{"selection":"first"}""", "BadInvalidArgument")]
        [TestCase("delete", """{"selection":"last"}""", "BadInvalidArgument")]
        [TestCase("report", "null", "BadInvalidArgument")]
        [TestCase("report", "[]", "BadInvalidArgument")]
        [TestCase("report", "\"all\"", "BadInvalidArgument")]
        [TestCase("report", "{}", "BadInvalidArgument")]
        [TestCase("report", """{"selection":1}""", "BadInvalidArgument")]
        [TestCase("report", """{"selection":"ALL"}""", "BadInvalidArgument")]
        [TestCase("report", """{"selection":"all","fromIndex":1}""", "BadInvalidArgument")]
        [TestCase("report", """{"selection":"all","selection":"all"}""", "BadInvalidArgument")]
        [TestCase("report", """{"selection":"index","fromIndex":1}""", "BadInvalidArgument")]
        [TestCase("report", """{"selection":"index","fromIndex":2,"toIndex":1}""", "BadOutOfRange")]
        [TestCase("report", """{"selection":"index","fromIndex":-1,"toIndex":1}""", "BadOutOfRange")]
        [TestCase("report", """{"selection":"index","fromIndex":1,"toIndex":4294967296}""", "BadOutOfRange")]
        [TestCase("report", """{"selection":"index","fromIndex":1.5,"toIndex":2}""", "BadOutOfRange")]
        [TestCase("report", """{"selection":"index","fromIndex":"1","toIndex":2}""", "BadOutOfRange")]
        [TestCase("report", """{"selection":"time","fromTime":"bad","toTime":"2026-09-01T00:00:02Z"}""",
            "BadInvalidArgument")]
        [TestCase("report", """{"selection":"time","fromTime":"2026-09-01T00:00:01","toTime":"2026-09-01T00:00:02Z"}""",
            "BadInvalidArgument")]
        [TestCase("report",
            """{"selection":"time","fromTime":"2026-09-01T00:00:01+00:00","toTime":"2026-09-01T00:00:02Z"}""",
            "BadInvalidArgument")]
        [TestCase("report",
            """{"selection":"time","fromTime":"2026-09-01T00:00:02Z","toTime":"2026-09-01T00:00:01Z"}""",
            "BadOutOfRange")]
        public async Task InvalidRecordUnionsFailBeforeSessionAccess(string operation, string query, string status)
        {
            CallToolResult result = await (operation switch
            {
                "count" => WmtpMcpTools.CountRecordsAsync(m_accessor, "i=1", "work", Json(query)),
                "delete" => WmtpMcpTools.DeleteRecordsAsync(m_accessor, "i=1", "work", Json(query), confirm: true),
                _ => WmtpMcpTools.ReportRecordsAsync(m_accessor, "i=1", "work", Json(query))
            }).ConfigureAwait(false);
            AssertError(result, status);
        }

        /// <summary>
        /// Native object caps and string caps apply before any provider or session lookup.
        /// </summary>
        [Test]
        public async Task OversizedJsonAndIdentifiersAreRejected()
        {
            AssertError(await WmtpMcpTools.ReportRecordsAsync(m_accessor, "i=1", "work",
                Json("{\"selection\":\"all\",\"extra\":\"" + new string('x', 4096) + "\"}")).ConfigureAwait(false),
                "BadInvalidArgument");
            AssertError(await WmtpMcpTools.ReadPeripheralAsync(m_accessor, "s=" + new string('x', 1024))
                .ConfigureAwait(false), "BadOutOfRange");
            AssertError(await WmtpMcpTools.ReadPeripheralAsync(m_accessor, "i=1", sessionName: new string('s', 129))
                .ConfigureAwait(false), "BadOutOfRange");
            AssertError(await WmtpMcpTools.TransferAsync(m_accessor, "i=1", "license",
                new string('A', 1398108), confirm: true).ConfigureAwait(false), "BadOutOfRange");
            AssertError(await WmtpMcpTools.ConfigureMeasurementAsync(m_accessor, "i=1", "i=2", "trigger",
                Json("1." + new string('0', 4096)), confirm: true).ConfigureAwait(false), "BadOutOfRange");
        }

        /// <summary>
        /// Blank and null-equivalent selectors never become implicit device or session choices.
        /// </summary>
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("i=0")]
        public async Task InvalidPeripheralIdentifiersAreRejected(string node)
        {
            AssertError(await WmtpMcpTools.ReadPeripheralAsync(m_accessor, node).ConfigureAwait(false),
                "BadInvalidArgument");
        }

        /// <summary>
        /// Session selectors must be explicit bounded text, not blank aliases for the current session.
        /// </summary>
        [TestCase("")]
        [TestCase(" ")]
        public async Task BlankSessionNamesAreRejected(string name)
        {
            AssertError(await WmtpMcpTools.CountRecordsAsync(m_accessor, "i=1", "work", Json(kAll),
                sessionName: name).ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Unknown store and action strings cannot choose a different operation through a fallback branch.
        /// </summary>
        [TestCase("WORK")]
        [TestCase("0")]
        [TestCase("")]
        public async Task UnknownDatasetNamesAreRejected(string dataset)
        {
            AssertError(await WmtpMcpTools.CountRecordsAsync(m_accessor, "i=1", dataset, Json(kAll))
                .ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Every device/destructive action defaults to refusal before obtaining a session.
        /// </summary>
        [TestCase("measurement")]
        [TestCase("peripheral")]
        [TestCase("delete")]
        [TestCase("abort")]
        [TestCase("transfer")]
        public async Task DeviceChangesRequireExplicitConfirmation(string operation)
        {
            AssertError(await InvokeAsync(operation, m_accessor).ConfigureAwait(false), "BadUserAccessDenied");
        }

        /// <summary>
        /// Every tool preserves an already-cancelled caller token without resolving a session or sending Abort.
        /// </summary>
        [TestCase("read")]
        [TestCase("count")]
        [TestCase("report")]
        [TestCase("measurement")]
        [TestCase("peripheral")]
        [TestCase("delete")]
        [TestCase("abort")]
        [TestCase("transfer")]
        public void PreCancelledCallsPreserveTheOriginalToken(string operation)
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OperationCanceledException error = Assert.CatchAsync<OperationCanceledException>(async () =>
                await InvokeAsync(operation, null!, cancellation.Token).ConfigureAwait(false))!;
            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
        }

        /// <summary>
        /// TriggerDuration inputs must be finite, nonnegative whole UInt32 milliseconds, never rounded.
        /// </summary>
        [TestCase("-1")]
        [TestCase("0.5")]
        [TestCase("4294967296")]
        [TestCase("1e1000")]
        [TestCase("\"1\"")]
        [TestCase("true")]
        [TestCase("null")]
        public async Task TriggerSettingsRejectNonrepresentableInputs(string value)
        {
            AssertError(await WmtpMcpTools.ConfigureMeasurementAsync(m_accessor, "i=1", "i=2", "trigger",
                Json(value), confirm: true).ConfigureAwait(false), "BadOutOfRange");
        }

        /// <summary>
        /// Both trigger endpoints are transmitted as the released Double Duration, not coerced UInt32 arguments.
        /// </summary>
        [TestCase("0", 0d)]
        [TestCase("4294967295", 4294967295d)]
        public void TriggerSettingsKeepTheReleasedDurationInput(string value, double expected)
        {
            (double trigger, Variant delta) = WmtpMcpParameters.MeasurementSetting("trigger", Json(value));
            Assert.That(trigger, Is.EqualTo(expected));
            Assert.That(delta.IsNull, Is.True);
        }

        /// <summary>
        /// Typed delta values preserve every native numeric wire width.
        /// </summary>
        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        public void NumericUnionsPreserveExactVariantTypes(BuiltInType type)
        {
            (_, Variant value) = WmtpMcpParameters.MeasurementSetting("delta",
                Json($"{{\"type\":\"{type}\",\"value\":8}}"));
            Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(type));
            Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(value, Is.EqualTo(WmtpMcpFixture.Scalar(type, 8)));
        }

        /// <summary>
        /// JSON integers and exact digit strings retain values that cannot survive conversion through Double.
        /// </summary>
        [TestCase("9007199254740993")]
        [TestCase("\"9007199254740993\"")]
        [TestCase("18446744073709551615")]
        [TestCase("\"18446744073709551615\"")]
        public void UInt64InputsNeverRoundThroughDouble(string literal)
        {
            (_, Variant delta) = WmtpMcpParameters.MeasurementSetting("delta",
                Json("{\"type\":\"UInt64\",\"value\":" + literal + "}"));
            Assert.That(delta.TryGetValue(out ulong value), Is.True);
            Assert.That(value, Is.EqualTo(literal.Contains("1844", StringComparison.Ordinal)
                ? ulong.MaxValue : 9007199254740993ul));
        }

        /// <summary>
        /// Signed integral limits and an exactly representable fractional Double remain distinct.
        /// </summary>
        [Test]
        public void SignedMaximumAndFractionalDeltasKeepTheirDeclaredTypes()
        {
            (_, Variant signed) = WmtpMcpParameters.MeasurementSetting("delta",
                Json("""{"type":"Int64","value":"9223372036854775807"}"""));
            Assert.That(signed.TryGetValue(out long integer), Is.True);
            Assert.That(integer, Is.EqualTo(long.MaxValue));
            (_, Variant fractional) = WmtpMcpParameters.MeasurementSetting("delta",
                Json("""{"type":"Double","value":0.125}"""));
            Assert.That(fractional.TryGetValue(out double number), Is.True);
            Assert.That(number, Is.EqualTo(0.125));
        }

        /// <summary>
        /// Numeric declarations never accept overflows, fractional integers, unsupported Decimal or string coercion.
        /// </summary>
        [TestCase("""{"type":"Byte","value":256}""", "BadOutOfRange")]
        [TestCase("""{"type":"SByte","value":-1}""", "BadOutOfRange")]
        [TestCase("""{"type":"UInt16","value":65536}""", "BadOutOfRange")]
        [TestCase("""{"type":"Int16","value":32768}""", "BadOutOfRange")]
        [TestCase("""{"type":"Int32","value":2147483648}""", "BadOutOfRange")]
        [TestCase("""{"type":"UInt32","value":4294967296}""", "BadOutOfRange")]
        [TestCase("""{"type":"Int64","value":9223372036854775808}""", "BadOutOfRange")]
        [TestCase("""{"type":"UInt64","value":18446744073709551616}""", "BadOutOfRange")]
        [TestCase("""{"type":"UInt64","value":1.5}""", "BadOutOfRange")]
        [TestCase("""{"type":"UInt64","value":"18446744073709551616"}""", "BadOutOfRange")]
        [TestCase("""{"type":"Int64","value":"-1"}""", "BadOutOfRange")]
        [TestCase("""{"type":"Float","value":1e100}""", "BadOutOfRange")]
        [TestCase("""{"type":"Double","value":1e1000}""", "BadOutOfRange")]
        [TestCase("""{"type":"Decimal","value":0.1234567890123456789012345678}""", "BadOutOfRange")]
        [TestCase("""{"type":"Double","value":"0.1"}""", "BadInvalidArgument")]
        [TestCase("""{"type":"Double","value":true}""", "BadInvalidArgument")]
        [TestCase("""{"type":"Double","value":[1]}""", "BadInvalidArgument")]
        [TestCase("""{"type":"Double","value":1,"extra":0}""", "BadInvalidArgument")]
        [TestCase("""{"type":"Double"}""", "BadInvalidArgument")]
        [TestCase("1", "BadInvalidArgument")]
        public async Task DeltaUnionsRejectCoercionAndUnsupportedPrecision(string value, string status)
        {
            AssertError(await WmtpMcpTools.ConfigureMeasurementAsync(m_accessor, "i=1", "i=2", "delta",
                Json(value), confirm: true).ConfigureAwait(false), status);
        }

        /// <summary>
        /// Neither configuration group interprets an unknown setting as its other action.
        /// </summary>
        [TestCase("")]
        [TestCase("0")]
        [TestCase("Trigger")]
        public async Task UnknownSettingsFailBeforeSessionAccess(string setting)
        {
            AssertError(await WmtpMcpTools.ConfigureMeasurementAsync(m_accessor, "i=1", "i=2", setting,
                Json("0"), confirm: true).ConfigureAwait(false), "BadInvalidArgument");
            AssertError(await WmtpMcpTools.ConfigurePeripheralAsync(m_accessor, "i=1", setting,
                Json("false"), confirm: true).ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Calibration requires a native Boolean, never numeric or textual truthiness.
        /// </summary>
        [TestCase("0")]
        [TestCase("1")]
        [TestCase("\"true\"")]
        [TestCase("null")]
        [TestCase("{}")]
        public async Task CalibrationRejectsNonBooleanInputs(string value)
        {
            AssertError(await WmtpMcpTools.ConfigurePeripheralAsync(m_accessor, "i=1", "calibration",
                Json(value), confirm: true).ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Time changes require every actual TimeZoneDataType field and reject ambiguous timestamps.
        /// </summary>
        [TestCase("""{"utcTime":"2026-09-01T00:00:00Z"}""", "BadInvalidArgument")]
        [TestCase("""{"utcTime":"2026-09-01T00:00:00Z","timeZone":0}""", "BadInvalidArgument")]
        [TestCase("""{"utcTime":"2026-09-01T00:00:00Z","timeZone":{"offset":0}}""", "BadInvalidArgument")]
        [TestCase("""{"utcTime":"2026-09-01T00:00:00","timeZone":{"offset":0,"daylightSavingInOffset":false}}""",
            "BadInvalidArgument")]
        [TestCase("""{"utcTime":"2026-09-01T00:00:00Z","timeZone":{"offset":841,"daylightSavingInOffset":false}}""",
            "BadOutOfRange")]
        [TestCase("""{"utcTime":"2026-09-01T00:00:00Z","timeZone":{"offset":-841,"daylightSavingInOffset":false}}""",
            "BadOutOfRange")]
        [TestCase("""{"utcTime":"2026-09-01T00:00:00Z","timeZone":{"offset":0.5,"daylightSavingInOffset":false}}""",
            "BadOutOfRange")]
        [TestCase("""{"utcTime":"2026-09-01T00:00:00Z","timeZone":{"offset":"0","daylightSavingInOffset":false}}""",
            "BadOutOfRange")]
        [TestCase("""{"utcTime":"2026-09-01T00:00:00Z","timeZone":{"offset":0,"daylightSavingInOffset":1}}""",
            "BadInvalidArgument")]
        public async Task TimeZoneUnionsRejectMissingAndAmbiguousFields(string value, string status)
        {
            AssertError(await WmtpMcpTools.ConfigurePeripheralAsync(m_accessor, "i=1", "time",
                Json(value), confirm: true).ConfigureAwait(false), status);
        }

        /// <summary>
        /// Base64 is bounded before session lookup, and strings are never interpreted as host paths or URLs.
        /// </summary>
        [TestCase("AQI=", 1, "BadOutOfRange")]
        [TestCase("AQ", 8, "BadDecodingError")]
        [TestCase("AQ ==", 8, "BadDecodingError")]
        [TestCase("AQ==\r\n  ", 8, "BadDecodingError")]
        [TestCase("!!!!", 8, "BadDecodingError")]
        [TestCase("C:\\device.bin", 64, "BadDecodingError")]
        [TestCase("https://device/firmware", 64, "BadDecodingError")]
        [TestCase("", 64, "BadInvalidArgument")]
        public async Task TransfersRejectMalformedOrUnboundedPayloads(string payload, int maximum, string status)
        {
            AssertError(await WmtpMcpTools.TransferAsync(m_accessor, "i=1", "firmware", payload,
                maximumBytes: maximum, confirm: true).ConfigureAwait(false), status);
        }

        /// <summary>
        /// Exact one-byte payloads reach session selection and unknown transfer types do not.
        /// </summary>
        [Test]
        public async Task TransfersAcceptExactBoundsButRejectUnknownKinds()
        {
            AssertError(await WmtpMcpTools.TransferAsync(m_accessor, "i=1", "license", "AQ==",
                maximumBytes: 1, confirm: true).ConfigureAwait(false), "BadInvalidState");
            AssertError(await WmtpMcpTools.TransferAsync(m_accessor, "i=1", "Firmware", "AQ==",
                maximumBytes: 1, confirm: true).ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Expected errors preserve their UA status while never exposing remote diagnostics or secret markers.
        /// </summary>
        [TestCase("ua", "BadUserAccessDenied")]
        [TestCase("range", "BadOutOfRange")]
        [TestCase("argument", "BadInvalidArgument")]
        [TestCase("format", "BadDecodingError")]
        [TestCase("state", "BadInvalidState")]
        [TestCase("timeout", "BadTimeout")]
        public async Task ErrorsRetainStatusWithoutLeakingRemoteDiagnostics(string kind, string status)
        {
            const string secret = "never-return-this-private-marker";
            Exception error = kind switch
            {
                "ua" => new ServiceResultException(StatusCodes.BadUserAccessDenied, secret),
                "range" => new ArgumentOutOfRangeException(nameof(kind), secret),
                "argument" => new ArgumentException(secret),
                "format" => new FormatException(secret),
                "state" => new InvalidOperationException(secret),
                _ => new TimeoutException(secret)
            };
            CallToolResult result = await WmtpMcpJson.ExecuteAsync(
                _ => Task.FromException<CallToolResult>(error), 30, CancellationToken.None).ConfigureAwait(false);
            AssertError(result, status);
            Assert.That(result.Content.OfType<TextContentBlock>().Single().Text, Does.Not.Contain(secret));
        }

        /// <summary>
        /// Parses a supplied native JSON value for direct boundary tests.
        /// </summary>
        internal static JsonElement Json(string value)
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Asserts both MCP error semantics and the retained UA status, not merely presence of JSON.
        /// </summary>
        internal static void AssertError(CallToolResult result, string status)
        {
            Assert.That(result.IsError, Is.True);
            JsonElement error = WmtpMcpHarness.Json(result);
            Assert.That(error.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(error.GetProperty("statusCode").GetString(), Does.StartWith(status));
        }

        /// <summary>
        /// Invokes every entry point with otherwise valid parameters.
        /// </summary>
        private static Task<CallToolResult> InvokeAsync(
            string operation, MachineToolClientAccessor accessor, CancellationToken ct = default)
        {
            return operation switch
            {
                "read" => WmtpMcpTools.ReadPeripheralAsync(accessor, "i=1", ct: ct),
                "count" => WmtpMcpTools.CountRecordsAsync(accessor, "i=1", "work", Json(kAll), ct: ct),
                "report" => WmtpMcpTools.ReportRecordsAsync(accessor, "i=1", "service", Json(kAll), ct: ct),
                "measurement" => WmtpMcpTools.ConfigureMeasurementAsync(accessor, "i=1", "i=2",
                    "trigger", Json("1"), ct: ct),
                "peripheral" => WmtpMcpTools.ConfigurePeripheralAsync(accessor, "i=1", "calibration", Json("true"),
                    ct: ct),
                "delete" => WmtpMcpTools.DeleteRecordsAsync(accessor, "i=1", "work", Json(kAll), ct: ct),
                "abort" => WmtpMcpTools.AbortOperationAsync(accessor, "i=1", "work", ct: ct),
                _ => WmtpMcpTools.TransferAsync(accessor, "i=1", "firmware", "AQ==", ct: ct)
            };
        }

        /// <summary>The exact complete-selection query.</summary>
        private const string kAll = """{"selection":"all"}""";

        /// <summary>The isolated services with no connected sessions.</summary>
        private ServiceProvider m_services = null!;

        /// <summary>The real shared accessor whose manager remains empty.</summary>
        private MachineToolClientAccessor m_accessor = null!;
    }
}
#endif
