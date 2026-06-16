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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.Diagnostics.Bindings;
using Opc.Ua.Core.Diagnostics.Capture.Sources;
using Opc.Ua.Core.Diagnostics.Frame;
using Opc.Ua.Core.Diagnostics.KeyLog;
using Opc.Ua.Core.Diagnostics.Models;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Tests.Capture
{
    [TestFixture]
    public sealed class InProcessClientCaptureSourceTests : TempDirectoryFixture
    {
        [Test]
        public async Task StartStopTransitionsStateCorrectly()
        {
            var registry = new ChannelCaptureRegistry();
            var source = new InProcessClientCaptureSource(registry);
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                Assert.That(source.GetRawPcapFilePath(), Is.Not.Null);
                Assert.That(source.GetKeyLogFilePath(), Is.Not.Null);
                Assert.That(registry.CurrentObserver, Is.SameAs(source),
                    "Starting a session must publish the source as the registry's observer.");

                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(source.SupportedFormats, Does.Contain(FormatKind.Pcap));
                Assert.That(registry.CurrentObserver, Is.Null,
                    "Stopping a session must clear the registry observer.");
            }
            finally
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task FrameSinkWritesSentAndReceivedFramesToDisk()
        {
            var registry = new ChannelCaptureRegistry();
            var source = new InProcessClientCaptureSource(registry);
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                IFrameCaptureSink sink = source;

                sink.OnFrameSent(0x1234, [1, 2, 3, 4]);
                sink.OnFrameReceived(0x1234, [5, 6, 7, 8]);
                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

                string path = source.GetRawPcapFilePath() ?? throw new AssertionException("Missing pcap path.");
                List<PcapRecord> records = await PcapTestHelpers.ToListAsync(
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
            var registry = new ChannelCaptureRegistry();
            var source = new InProcessClientCaptureSource(registry);
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                    SecurityPolicies.Basic256Sha256,
                    MessageSecurityMode.SignAndEncrypt);
                using ChannelToken token = MaterializeToken(material);

                IFrameCaptureSink sink = source;
                sink.OnTokenActivated(material.ChannelId, token, previousToken: null);
                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

                string path = source.GetKeyLogFilePath() ?? throw new AssertionException("Missing keylog path.");
                List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
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
            var registry = new ChannelCaptureRegistry();
            var source = new InProcessClientCaptureSource(registry);
            try
            {
                await source.StartAsync(CreateRequest(maxBytes: 100), CancellationToken.None).ConfigureAwait(false);
                IFrameCaptureSink sink = source;

                sink.OnFrameSent(0x1234, new byte[60]);
                sink.OnFrameSent(0x1234, new byte[60]);
                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
                await ForceDisposeWritersAsync(source).ConfigureAwait(false);

                string path = source.GetRawPcapFilePath() ?? throw new AssertionException("Missing pcap path.");
                List<PcapRecord> records = await PcapTestHelpers.ToListAsync(
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

        private static ChannelToken MaterializeToken(ChannelKeyMaterial material)
        {
            // ChannelToken's key fields are internal; InternalsVisibleTo
            // on Opc.Ua.Core gives this test project access so we can
            // build a fully-populated token without going through the
            // live ComputeKeys path.
            return new ChannelToken
            {
                ChannelId = material.ChannelId,
                TokenId = material.TokenId,
                CreatedAt = material.CreatedAt,
                Lifetime = material.Lifetime,
                SecurityPolicy = SecurityPolicies.GetInfo(material.SecurityPolicyUri),
                ClientNonce = material.ClientNonce,
                ServerNonce = material.ServerNonce,
                ClientSigningKey = material.ClientSigningKey,
                ClientEncryptingKey = material.ClientEncryptingKey,
                ClientInitializationVector = material.ClientInitializationVector,
                ServerSigningKey = material.ServerSigningKey,
                ServerEncryptingKey = material.ServerEncryptingKey,
                ServerInitializationVector = material.ServerInitializationVector
            };
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
