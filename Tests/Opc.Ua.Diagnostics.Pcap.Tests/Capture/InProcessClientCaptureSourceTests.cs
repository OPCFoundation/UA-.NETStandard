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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Diagnostics.Pcap.Capture.Sources;
using Opc.Ua.Diagnostics.Pcap.Frame;
using Opc.Ua.Diagnostics.Pcap.KeyLog;
using Opc.Ua.Diagnostics.Pcap.Models;

namespace Opc.Ua.Diagnostics.Pcap.Tests.Capture
{
    [TestFixture]
    public sealed class InProcessClientCaptureSourceTests : TempDirectoryFixture
    {
        [Test]
        public async Task StartStopTransitionsStateCorrectly()
        {
            var source = new InProcessClientCaptureSource();
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                Assert.That(source.GetRawPcapFilePath(), Is.Not.Null);
                Assert.That(source.GetKeyLogFilePath(), Is.Not.Null);

                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(source.SupportedFormats, Does.Contain(FormatKind.Pcap));
            }
            finally
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task FrameSinkWritesSentAndReceivedFramesToDisk()
        {
            var source = new InProcessClientCaptureSource();
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                IFrameCaptureSink sink = GetSink(source);

                sink.OnFrameSent(0x1234, new byte[] { 1, 2, 3, 4 });
                sink.OnFrameReceived(0x1234, new byte[] { 5, 6, 7, 8 });
                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

                string path = source.GetRawPcapFilePath() ?? throw new AssertionException("Missing pcap path.");
                var records = await PcapTestHelpers.ToListAsync(
                    PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                    maxCount: 2).ConfigureAwait(false);

                Assert.That(new System.IO.FileInfo(path).Length, Is.GreaterThan(24));
                Assert.That(records, Has.Count.EqualTo(2));
            }
            finally
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TokenObserverWritesKeyLog()
        {
            var source = new InProcessClientCaptureSource();
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                    SecurityPolicies.Basic256Sha256,
                    MessageSecurityMode.SignAndEncrypt);

                InvokeOnTokenObserved(source, material);
                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

                string path = source.GetKeyLogFilePath() ?? throw new AssertionException("Missing keylog path.");
                var records = await PcapTestHelpers.ToListAsync(
                    new UaKeyLogJsonReader().ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

                Assert.That(new System.IO.FileInfo(path).Length, Is.GreaterThan(0));
                Assert.That(records, Has.Count.EqualTo(1));
                PcapTestHelpers.AssertMaterialEqual(records[0], material, includeJsonOnlyFields: true);
            }
            finally
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task MaxBytesCapStopsAcceptingFramesAfterLimit()
        {
            var source = new InProcessClientCaptureSource();
            try
            {
                await source.StartAsync(CreateRequest(maxBytes: 100), CancellationToken.None).ConfigureAwait(false);
                IFrameCaptureSink sink = GetSink(source);

                sink.OnFrameSent(0x1234, new byte[60]);
                sink.OnFrameSent(0x1234, new byte[60]);
                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
                await ForceDisposeWritersAsync(source).ConfigureAwait(false);

                string path = source.GetRawPcapFilePath() ?? throw new AssertionException("Missing pcap path.");
                var records = await PcapTestHelpers.ToListAsync(
                    PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                    maxCount: 1).ConfigureAwait(false);

                Assert.That(source.ByteCount, Is.EqualTo(120));
                Assert.That(records, Has.Count.EqualTo(1));
            }
            finally
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }

        private StartCaptureRequest CreateRequest(long? maxBytes = null)
        {
            return new StartCaptureRequest
            {
                SessionFolder = TempDirectory,
                MaxBytes = maxBytes
            };
        }

        private static IFrameCaptureSink GetSink(InProcessClientCaptureSource source)
        {
            FieldInfo field = typeof(InProcessClientCaptureSource).BaseType!.GetField(
                "m_sink",
                BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new AssertionException("Missing m_sink field.");
            return (IFrameCaptureSink)(field.GetValue(source) ?? throw new AssertionException("Missing sink."));
        }

        private static void InvokeOnTokenObserved(InProcessClientCaptureSource source, ChannelKeyMaterial material)
        {
            MethodInfo method = typeof(InProcessClientCaptureSource).BaseType!.GetMethod(
                "OnTokenObserved",
                BindingFlags.NonPublic | BindingFlags.Instance) ??
                throw new AssertionException("Missing OnTokenObserved method.");
            method.Invoke(source, new object[] { material });
        }

        private static async Task ForceDisposeWritersAsync(InProcessClientCaptureSource source)
        {
            await ForceDisposeWriterAsync(source, "m_pcapWriter").ConfigureAwait(false);
            await ForceDisposeWriterAsync(source, "m_jsonKeyWriter").ConfigureAwait(false);
            await ForceDisposeWriterAsync(source, "m_textKeyWriter").ConfigureAwait(false);
        }

        private static async Task ForceDisposeWriterAsync(InProcessClientCaptureSource source, string fieldName)
        {
            FieldInfo field = typeof(InProcessClientCaptureSource).BaseType!.GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance) ??
                throw new AssertionException($"Missing {fieldName} field.");
            if (field.GetValue(source) is IAsyncDisposable writer)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
                field.SetValue(source, null);
            }
        }
    }
}
