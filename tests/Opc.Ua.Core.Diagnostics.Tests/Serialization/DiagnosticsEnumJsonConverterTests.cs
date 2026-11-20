/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Serialization
{
    [TestFixture]
    public sealed class DiagnosticsEnumJsonConverterTests
    {
        [TestCase("inproc-client", CaptureSourceKind.InProcessClient)]
        [TestCase("InProcessClient", CaptureSourceKind.InProcessClient)]
        [TestCase("inprocclient", CaptureSourceKind.InProcessClient)]
        [TestCase("nic", CaptureSourceKind.Nic)]
        [TestCase("replay", CaptureSourceKind.Replay)]
        public void StartCaptureRequestAcceptsCaptureSourceAliases(
            string source,
            CaptureSourceKind expected)
        {
            JsonSerializerOptions options = CreateMcpSerializerOptions();

            StartCaptureRequest? request = JsonSerializer.Deserialize<StartCaptureRequest>(
                $$"""{"source":{{JsonSerializer.Serialize(source)}}}""",
                options);

            Assert.That(request, Is.Not.Null);
            Assert.That(request!.Source, Is.EqualTo(expected));
        }

        [TestCase("service-timeline", FormatKind.ServiceTimeline)]
        [TestCase("ServiceTimeline", FormatKind.ServiceTimeline)]
        [TestCase("timeline", FormatKind.ServiceTimeline)]
        [TestCase("pcapng", FormatKind.PcapNg)]
        [TestCase("json", FormatKind.Json)]
        public void CaptureNowRequestAcceptsFormatAliases(string format, FormatKind expected)
        {
            JsonSerializerOptions options = CreateMcpSerializerOptions();

            CaptureNowRequest? request = JsonSerializer.Deserialize<CaptureNowRequest>(
                $$"""{"format":{{JsonSerializer.Serialize(format)}}}""",
                options);

            Assert.That(request, Is.Not.Null);
            Assert.That(request!.Format, Is.EqualTo(expected));
        }

        [Test]
        public void EnumPropertiesAcceptDefinedIntegerValues()
        {
            JsonSerializerOptions options = CreateMcpSerializerOptions();

            StartCaptureRequest? capture = JsonSerializer.Deserialize<StartCaptureRequest>(
                """{"source":1}""",
                options);
            CaptureNowRequest? format = JsonSerializer.Deserialize<CaptureNowRequest>(
                """{"format":5}""",
                options);

            Assert.That(capture!.Source, Is.EqualTo(CaptureSourceKind.InProcessClient));
            Assert.That(format!.Format, Is.EqualTo(FormatKind.ServiceTimeline));
        }

        [TestCase("""{"source":"not-a-source"}""", typeof(StartCaptureRequest))]
        [TestCase("""{"source":99}""", typeof(StartCaptureRequest))]
        [TestCase("""{"format":"not-a-format"}""", typeof(CaptureNowRequest))]
        [TestCase("""{"format":99}""", typeof(CaptureNowRequest))]
        public void EnumPropertiesRejectUnknownValues(string json, System.Type targetType)
        {
            JsonSerializerOptions options = CreateMcpSerializerOptions();

            Assert.That(
                () => JsonSerializer.Deserialize(json, targetType, options),
                Throws.TypeOf<JsonException>());
        }

        [Test]
        public void EnumPropertiesWriteCanonicalWireNames()
        {
            JsonSerializerOptions options = CreateMcpSerializerOptions();

            string startJson = JsonSerializer.Serialize(
                new StartCaptureRequest { Source = CaptureSourceKind.InProcessClient },
                options);
            string nowJson = JsonSerializer.Serialize(
                new CaptureNowRequest { Format = FormatKind.ServiceTimeline },
                options);
            string infoJson = JsonSerializer.Serialize(
                new CaptureSessionInfo { Source = CaptureSourceKind.InProcessServer },
                options);

            using JsonDocument start = JsonDocument.Parse(startJson);
            using JsonDocument now = JsonDocument.Parse(nowJson);
            using JsonDocument info = JsonDocument.Parse(infoJson);
            Assert.That(start.RootElement.GetProperty("source").GetString(), Is.EqualTo("inproc-client"));
            Assert.That(now.RootElement.GetProperty("format").GetString(), Is.EqualTo("service-timeline"));
            Assert.That(info.RootElement.GetProperty("source").GetString(), Is.EqualTo("inproc-server"));
        }

        private static JsonSerializerOptions CreateMcpSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
