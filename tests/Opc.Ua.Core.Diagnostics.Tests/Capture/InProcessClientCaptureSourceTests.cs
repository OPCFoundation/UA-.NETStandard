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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Capture
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
        public async Task MaximumUaChunkWritesSequentialTcpFrames()
        {
            var registry = new ChannelCaptureRegistry();
            var source = new InProcessClientCaptureSource(registry);
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                byte[] chunk = new byte[ushort.MaxValue];
                for (int ii = 0; ii < chunk.Length; ii++)
                {
                    chunk[ii] = (byte)ii;
                }
                ((IFrameCaptureSink)source).OnFrameSent(0x1234, chunk);
                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

                string path = source.GetRawPcapFilePath() ??
                    throw new AssertionException("Missing pcap path.");
                List<PcapRecord> records = await PcapTestHelpers.ToListAsync(
                    PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                    maxCount: 2).ConfigureAwait(false);
                var reassembler = new TcpStreamReassembler();
                byte[] reassembled = [.. records
                    .SelectMany(reassembler.Process)
                    .SelectMany(static segment => segment.Data.ToArray())];

                Assert.That(source.FrameCount, Is.EqualTo(1));
                Assert.That(records, Has.Count.EqualTo(2));
                Assert.That(reassembled, Is.EqualTo(chunk).AsCollection);
            }
            finally
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task FullSyntheticChannelIdsHaveIndependentSequenceSpace()
        {
            var registry = new ChannelCaptureRegistry();
            var source = new InProcessClientCaptureSource(registry);
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                IFrameCaptureSink sink = source;
                sink.OnFrameSent(0x1234, [1, 2, 3]);
                sink.OnFrameSent(0x5234, [4, 5]);
                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

                string path = source.GetRawPcapFilePath() ??
                    throw new AssertionException("Missing pcap path.");
                List<PcapRecord> records = await PcapTestHelpers.ToListAsync(
                    PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                    maxCount: 2).ConfigureAwait(false);

                Assert.That(records, Has.Count.EqualTo(2));
                Assert.That(
                    BinaryPrimitives.ReadUInt32BigEndian(records[0].Data.Span[28..]),
                    Is.Zero);
                Assert.That(
                    BinaryPrimitives.ReadUInt32BigEndian(records[1].Data.Span[28..]),
                    Is.Zero);
                Assert.That(
                    records[0].Data.Span[16..20].ToArray(),
                    Is.Not.EqualTo(records[1].Data.Span[16..20].ToArray()).AsCollection);
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
        public async Task FrameQueueSaturationDoesNotLoseKeyMaterial()
        {
            var registry = new ChannelCaptureRegistry();
            TaskCompletionSource<object?> queueGate = CreateSignal();
            var source = CreateTestSource(registry, frameQueueCapacity: 1, queueGate: queueGate);
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                using ChannelToken token = MaterializeToken(material);

                IFrameCaptureSink sink = source;
                sink.OnFrameSent(0x1234, [1, 2, 3]);
                sink.OnFrameSent(0x1234, [4, 5, 6]);
                sink.OnTokenActivated(material.ChannelId, token, previousToken: null);

                queueGate.TrySetResult(null);
                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

                List<PcapRecord> records = await ReadPcapRecordsAsync(source, maxCount: 1).ConfigureAwait(false);
                List<ChannelKeyMaterial> keys = await ReadKeyRecordsAsync(source).ConfigureAwait(false);

                Assert.That(source.FrameCount, Is.EqualTo(2));
                Assert.That(records, Has.Count.EqualTo(1));
                Assert.That(keys, Has.Count.EqualTo(1));
                PcapTestHelpers.AssertMaterialEqual(keys[0], material, includeJsonOnlyFields: true);
            }
            finally
            {
                material.Dispose();
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RejectedMiddleChunkPreservesSequenceGapForLaterData()
        {
            var registry = new ChannelCaptureRegistry();
            TaskCompletionSource<object?> queueGate = CreateSignal();
            TaskCompletionSource<object?> firstFrameProcessed = CreateSignal();
            var source = CreateTestSource(
                registry,
                frameQueueCapacity: 1,
                queueGate: queueGate,
                frameProcessed: firstFrameProcessed);
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                IFrameCaptureSink sink = source;

                sink.OnFrameSent(0x1234, [1, 2, 3]);
                sink.OnFrameSent(0x1234, [4, 5, 6, 7, 8]);

                queueGate.TrySetResult(null);
                await firstFrameProcessed.Task.ConfigureAwait(false);

                sink.OnFrameSent(0x1234, [9, 10]);
                await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

                List<PcapRecord> records = await ReadPcapRecordsAsync(source, maxCount: 2).ConfigureAwait(false);

                Assert.That(records, Has.Count.EqualTo(2));
                Assert.That(ReadTcpSequenceNumber(records[0]), Is.Zero);
                Assert.That(ReadTcpSequenceNumber(records[1]), Is.EqualTo(8));
                Assert.That(records[0].Data.ToArray()[44..], Is.EqualTo(new byte[] { 1, 2, 3 }).AsCollection);
                Assert.That(records[1].Data.ToArray()[44..], Is.EqualTo(new byte[] { 9, 10 }).AsCollection);
            }
            finally
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task StopAsyncDrainsFrameAndKeyQueues()
        {
            var registry = new ChannelCaptureRegistry();
            TaskCompletionSource<object?> queueGate = CreateSignal();
            var source = CreateTestSource(registry, frameQueueCapacity: 1, queueGate: queueGate);
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            try
            {
                await source.StartAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false);
                using ChannelToken token = MaterializeToken(material);

                IFrameCaptureSink sink = source;
                sink.OnFrameSent(0x1234, [1, 2, 3, 4]);
                sink.OnTokenActivated(material.ChannelId, token, previousToken: null);

                Task stopTask = source.StopAsync(CancellationToken.None).AsTask();
                Assert.That(stopTask.IsCompleted, Is.False);

                queueGate.TrySetResult(null);
                await stopTask.ConfigureAwait(false);

                List<PcapRecord> records = await ReadPcapRecordsAsync(source, maxCount: 1).ConfigureAwait(false);
                List<ChannelKeyMaterial> keys = await ReadKeyRecordsAsync(source).ConfigureAwait(false);

                Assert.That(records, Has.Count.EqualTo(1));
                Assert.That(keys, Has.Count.EqualTo(1));
                PcapTestHelpers.AssertMaterialEqual(keys[0], material, includeJsonOnlyFields: true);
            }
            finally
            {
                material.Dispose();
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

        private static InProcessClientCaptureSource CreateTestSource(
            IChannelCaptureRegistry registry,
            int frameQueueCapacity,
            TaskCompletionSource<object?>? queueGate = null,
            TaskCompletionSource<object?>? frameProcessed = null,
            TaskCompletionSource<object?>? keyProcessed = null)
        {
            return new InProcessClientCaptureSource(
                registry,
                loggerFactory: null,
                new InProcessCaptureSourceQueueOptions
                {
                    FrameQueueCapacity = frameQueueCapacity,
                    BeforeQueueReadAsync = queueGate is null
                        ? null
                        : () => new ValueTask(queueGate.Task),
                    AfterFrameProcessed = frameProcessed is null
                        ? null
                        : () => frameProcessed.TrySetResult(null),
                    AfterKeyProcessed = keyProcessed is null
                        ? null
                        : () => keyProcessed.TrySetResult(null)
                });
        }

        private static TaskCompletionSource<object?> CreateSignal()
        {
            return new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static Task<List<PcapRecord>> ReadPcapRecordsAsync(
            InProcessClientCaptureSource source,
            int maxCount)
        {
            string path = source.GetRawPcapFilePath() ?? throw new AssertionException("Missing pcap path.");
            return PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                maxCount);
        }

        private static Task<List<ChannelKeyMaterial>> ReadKeyRecordsAsync(InProcessClientCaptureSource source)
        {
            string path = source.GetKeyLogFilePath() ?? throw new AssertionException("Missing keylog path.");
            return PcapTestHelpers.ToListAsync(
                new UaKeyLogJsonReader().ReadAllAsync(path, CancellationToken.None));
        }

        private static uint ReadTcpSequenceNumber(PcapRecord record)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(record.Data.Span[28..]);
        }
    }
}
