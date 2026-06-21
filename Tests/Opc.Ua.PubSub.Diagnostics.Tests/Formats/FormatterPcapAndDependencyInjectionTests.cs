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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.PubSub.Pcap.DependencyInjection;
using Opc.Ua.PubSub.Transports;
using TextEncoding = System.Text.Encoding;

namespace Opc.Ua.PubSub.Pcap.Tests.Formats
{
    [TestFixture]
    [Category("PubSub")]
    public sealed class FormatterPcapAndDependencyInjectionTests
    {
        [Test]
        public async Task TextFormatterFormatsMalformedFramesAsTimelineAsync()
        {
            PubSubCaptureFrame[] frames =
            [
                CreateFrame(new byte[] { 0xFF, 0x00 }, PubSubCaptureDirection.Inbound, "239.0.0.1:4840", null),
                CreateFrame(
                    TextEncoding.UTF8.GetBytes("{\"bad\":true}"),
                    PubSubCaptureDirection.Outbound,
                    null,
                    "topic/a")
            ];
            var formatter = new PubSubTextFormatter();

            string text = await formatter.FormatAsync(ToAsync(frames)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(formatter.MimeType, Is.EqualTo("text/plain"));
                Assert.That(text, Does.Contain("Inbound"));
                Assert.That(text, Does.Contain("Uadp"));
                Assert.That(text, Does.Contain("Json"));
                Assert.That(text, Does.Contain("note="));
            });
        }

        [Test]
        public async Task JsonFormatterFormatsFramesAsJsonArrayAsync()
        {
            PubSubCaptureFrame[] frames =
            [
                CreateFrame(new byte[] { 0xFF, 0x00 }, PubSubCaptureDirection.Inbound, "239.0.0.1:4840", null),
                CreateFrame(TextEncoding.UTF8.GetBytes("{"), PubSubCaptureDirection.Outbound, null, "topic/a")
            ];
            var formatter = new PubSubJsonFormatter();

            byte[] json = await formatter.FormatAsync(ToAsync(frames)).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Multiple(() =>
            {
                Assert.That(formatter.MimeType, Is.EqualTo("application/json"));
                Assert.That(document.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(document.RootElement.GetArrayLength(), Is.EqualTo(2));
                Assert.That(document.RootElement[0].GetProperty("MessageType").GetString(), Is.EqualTo("Uadp"));
                Assert.That(document.RootElement[1].GetProperty("MessageType").GetString(), Is.EqualTo("Json"));
            });
        }

        [Test]
        public async Task PcapWriterWritesLibpcapAndSkipsMqttFramesAsync()
        {
            string filePath = Path.GetTempFileName();
            try
            {
                PubSubCaptureFrame[] frames =
                [
                    CreateFrame(new byte[] { 1, 2, 3, 4 }, PubSubCaptureDirection.Outbound, "239.0.0.1:4840", null),
                    CreateFrame(new byte[] { 5, 6 }, PubSubCaptureDirection.Inbound, null, "mqtt/topic")
                ];
                var writer = new PubSubPcapWriter();

                long count = await writer.WritePcapAsync(ToAsync(frames), filePath).ConfigureAwait(false);
                byte[] header = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(count, Is.EqualTo(1));
                    Assert.That(header, Has.Length.GreaterThan(24));
                    Assert.That(header[0], Is.EqualTo(0xd4));
                    Assert.That(header[1], Is.EqualTo(0xc3));
                    Assert.That(header[2], Is.EqualTo(0xb2));
                    Assert.That(header[3], Is.EqualTo(0xa1));
                });
            }
            finally
            {
                TryDelete(filePath);
            }
        }

        [Test]
        public async Task PcapWriterWritesPcapNgHeaderAsync()
        {
            string filePath = Path.GetTempFileName();
            try
            {
                PubSubCaptureFrame[] frames =
                [
                    CreateFrame(new byte[] { 1, 2, 3 }, PubSubCaptureDirection.Inbound, "10.0.0.1:4840", null)
                ];
                var writer = new PubSubPcapWriter();

                long count = await writer.WritePcapNgAsync(ToAsync(frames), filePath).ConfigureAwait(false);
                byte[] header = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(count, Is.EqualTo(1));
                    Assert.That(header, Has.Length.GreaterThan(12));
                    Assert.That(header[0], Is.EqualTo(0x0a));
                    Assert.That(header[1], Is.EqualTo(0x0d));
                    Assert.That(header[2], Is.EqualTo(0x0d));
                    Assert.That(header[3], Is.EqualTo(0x0a));
                });
            }
            finally
            {
                TryDelete(filePath);
            }
        }

        [Test]
        public void AddPubSubPcapRegistersCaptureServices()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddPubSubPcap();

            Assert.Multiple(() =>
            {
                Assert.That(services, Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IPubSubCaptureRegistry)));
                Assert.That(services, Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(PubSubCaptureSessionManager)));
            });
        }

        [Test]
        public void AddPubSubPcapFromEnvironmentRegistersHostedServiceWhenEnabled()
        {
            string filePath = Path.GetTempFileName();
            try
            {
                Environment.SetEnvironmentVariable(
                    PubSubPcapEnvironmentVariableNames.OpcuaPubSubPcapFile,
                    "  " + filePath + "  ");
                Environment.SetEnvironmentVariable(
                    PubSubPcapEnvironmentVariableNames.OpcuaPubSubKeyLogFile,
                    " keylog.jsonl ");
                IServiceCollection services = new ServiceCollection();

                services.AddPubSubPcapFromEnvironment();

                Assert.Multiple(() =>
                {
                    Assert.That(services, Has.Some.Matches<ServiceDescriptor>(
                        d => d.ServiceType == typeof(IHostedService)));
                    Assert.That(services, Has.Some.Matches<ServiceDescriptor>(
                        d => d.ImplementationInstance is PubSubPcapEnvironmentOptions
                        {
                            KeyLogFilePath: "keylog.jsonl"
                        }));
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable(PubSubPcapEnvironmentVariableNames.OpcuaPubSubPcapFile, null);
                Environment.SetEnvironmentVariable(PubSubPcapEnvironmentVariableNames.OpcuaPubSubKeyLogFile, null);
                TryDelete(filePath);
            }
        }

        [Test]
        public void AddPubSubPcapFromEnvironmentDoesNotRegisterHostedServiceWhenDisabled()
        {
            Environment.SetEnvironmentVariable(PubSubPcapEnvironmentVariableNames.OpcuaPubSubPcapFile, null);
            Environment.SetEnvironmentVariable(PubSubPcapEnvironmentVariableNames.OpcuaPubSubKeyLogFile, null);
            IServiceCollection services = new ServiceCollection();

            services.AddPubSubPcapFromEnvironment();

            Assert.That(services, Has.None.Matches<ServiceDescriptor>(
                d => d.ServiceType == typeof(IHostedService)));
        }

        [Test]
        public async Task EnvironmentHostedServiceStartsAndFlushesCaptureAsync()
        {
            string filePath = Path.GetTempFileName();
            try
            {
                PubSubCaptureRegistry registry = new();
                PubSubPcapEnvironmentOptions options = new(filePath, null);
                await using var service = new PubSubPcapEnvironmentAutoStartHostedService(registry, options);

                await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
                EmitFrame(registry);
                await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

                byte[] header = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(registry.CurrentObserver, Is.Null);
                    Assert.That(header, Has.Length.GreaterThan(24));
                    Assert.That(header[0], Is.EqualTo(0xd4));
                    Assert.That(header[1], Is.EqualTo(0xc3));
                    Assert.That(header[2], Is.EqualTo(0xb2));
                    Assert.That(header[3], Is.EqualTo(0xa1));
                });
            }
            finally
            {
                TryDelete(filePath);
            }
        }

        [Test]
        public async Task EnvironmentHostedServiceIgnoresDisabledOptionsAsync()
        {
            PubSubCaptureRegistry registry = new();
            PubSubPcapEnvironmentOptions options = new(null, "keylog");
            await using var service = new PubSubPcapEnvironmentAutoStartHostedService(registry, options);

            await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public void EnvironmentOptionsAndVariableNamesExposeExpectedValues()
        {
            PubSubPcapEnvironmentOptions enabled = new("capture.pcap", "keys.jsonl");
            PubSubPcapEnvironmentOptions disabled = new(null, null);

            Assert.Multiple(() =>
            {
                Assert.That(enabled.IsEnabled, Is.True);
                Assert.That(enabled.KeyLogFilePath, Is.EqualTo("keys.jsonl"));
                Assert.That(disabled.IsEnabled, Is.False);
                Assert.That(
                    PubSubPcapEnvironmentVariableNames.OpcuaPubSubPcapFile,
                    Is.EqualTo("OPCUA_PUBSUB_PCAP_FILE"));
                Assert.That(
                    PubSubPcapEnvironmentVariableNames.OpcuaPubSubKeyLogFile,
                    Is.EqualTo("OPCUA_PUBSUB_KEYLOGFILE"));
            });
        }

        private static PubSubCaptureFrame CreateFrame(
            ReadOnlyMemory<byte> data,
            PubSubCaptureDirection direction,
            string? endpoint,
            string? topic)
        {
            string profile = topic is null ? "opc.udp.uadp" : "opc.mqtt.json";
            return new PubSubCaptureFrame(
                Timestamp,
                direction,
                profile,
                data,
                endpoint,
                topic);
        }

        private static async IAsyncEnumerable<PubSubCaptureFrame> ToAsync(
            IEnumerable<PubSubCaptureFrame> frames)
        {
            foreach (PubSubCaptureFrame frame in frames)
            {
                await Task.Yield();
                yield return frame;
            }
        }

        private static void EmitFrame(PubSubCaptureRegistry registry)
        {
            var context = new PubSubCaptureContext(
                PubSubCaptureDirection.Outbound,
                "opc.udp.uadp",
                new DateTimeUtc(DateTime.UtcNow),
                "239.0.0.1:4840");
            registry.CurrentObserver!.OnFrameCaptured(in context, [1, 2, 3, 4]);
        }

        private static void TryDelete(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private static readonly DateTimeOffset Timestamp =
            new(2026, 6, 21, 9, 0, 0, TimeSpan.Zero);
    }
}
