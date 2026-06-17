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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Connections
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class PubSubConnectionPrivateMethodTests
    {
        [Test]
        [TestSpec("7.3.4.8", Summary = "Static metadata routing rejects null registry")]
        public void TryRouteInboundMetaData_WithNullRegistry_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PubSubConnection.TryRouteInboundMetaData(
                null!,
                new JsonMetaDataMessage(),
                NullLogger.Instance));
        }

        [Test]
        [TestSpec("7.3.4.8", Summary = "Static metadata routing rejects null message")]
        public void TryRouteInboundMetaData_WithNullMessage_Throws()
        {
            var registry = new DataSetMetaDataRegistry();
            Assert.Throws<ArgumentNullException>(() => PubSubConnection.TryRouteInboundMetaData(
                registry,
                null!,
                NullLogger.Instance));
        }

        [Test]
        [TestSpec("7.3.4.8", Summary = "Null inbound metadata is treated as handled")]
        public void TryRouteInboundMetaData_WithNullMetadata_ReturnsTrue()
        {
            var registry = new DataSetMetaDataRegistry();
            var message = new JsonMetaDataMessage
            {
                PublisherId = PublisherId.FromUInt16(1),
                DataSetWriterId = 2,
                MetaDataPayload = null,
                MetaData = null
            };

            bool routed = PubSubConnection.TryRouteInboundMetaData(
                registry,
                message,
                NullLogger.Instance);

            Assert.That(routed, Is.True);
            Assert.That(registry.Keys, Is.Empty);
        }

        [Test]
        [TestSpec("7.3.4.8", Summary = "Inbound metadata registration failures are swallowed")]
        public void TryRouteInboundMetaData_WhenRegistryThrows_ReturnsTrue()
        {
            var registry = new ThrowingRegistry();
            var message = new JsonMetaDataMessage
            {
                PublisherId = PublisherId.FromUInt16(1),
                DataSetWriterId = 2,
                MetaDataPayload = new DataSetMetaDataType
                {
                    Name = "Throwing",
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                }
            };

            bool routed = PubSubConnection.TryRouteInboundMetaData(
                registry,
                message,
                NullLogger.Instance);

            Assert.That(routed, Is.True);
        }

        [Test]
        public async Task ResolveEncoder_FallsBackToSameFamilyAsync()
        {
            var fallback = new StubEncoder(Profiles.PubSubUdpUadpTransport, new byte[] { 1, 2, 3 });
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubMqttUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>
                {
                    [Profiles.PubSubUdpUadpTransport] = fallback
                },
                new Dictionary<string, INetworkMessageDecoder>());

            INetworkMessageEncoder? resolved = InvokePrivate<INetworkMessageEncoder?>(
                connection,
                "ResolveEncoder");

            Assert.That(resolved, Is.SameAs(fallback));
        }

        [Test]
        public async Task ResolveDecoder_FallsBackToSameFamilyAsync()
        {
            var fallback = new StubDecoder(Profiles.PubSubUdpUadpTransport, (_, _, _) => null);
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubMqttUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>
                {
                    [Profiles.PubSubUdpUadpTransport] = fallback
                });

            INetworkMessageDecoder? resolved = InvokePrivate<INetworkMessageDecoder?>(
                connection,
                "ResolveDecoder");

            Assert.That(resolved, Is.SameAs(fallback));
        }

        [Test]
        [TestSpec("7.3.2", Summary = "Send path skips publish when no encoder is registered")]
        public async Task SendNetworkMessageAsync_WithoutEncoder_DoesNotSendAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>());
            var transport = new SpyTransport();
            SetPrivateField(connection, "m_transport", transport);

            await InvokePrivateAsync(
                connection,
                "SendNetworkMessageAsync",
                new DummyNetworkMessage { PublisherId = PublisherId.FromUInt16(1) },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(transport.SentPayloads, Is.Empty);
        }

        [Test]
        [TestSpec("7.3.2", Summary = "Send path forwards encoded payload to transport")]
        public async Task SendNetworkMessageAsync_WithEncoder_SendsPayloadAsync()
        {
            byte[] payload = [9, 8, 7, 6];
            var encoder = new StubEncoder(Profiles.PubSubUdpUadpTransport, payload);
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>
                {
                    [Profiles.PubSubUdpUadpTransport] = encoder
                },
                new Dictionary<string, INetworkMessageDecoder>());
            var transport = new SpyTransport();
            SetPrivateField(connection, "m_transport", transport);

            await InvokePrivateAsync(
                connection,
                "SendNetworkMessageAsync",
                new DummyNetworkMessage
                {
                    PublisherId = PublisherId.FromUInt16(1),
                    WriterGroupId = 4
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(encoder.EncodeCallCount, Is.EqualTo(1));
            Assert.That(transport.SentPayloads, Has.Count.EqualTo(1));
            Assert.That(transport.SentPayloads[0].ToArray(), Is.EqualTo(payload));
        }

        [Test]
        [TestSpec("7.2.4.4.4", Summary = "Large UADP frames are chunked before transport send")]
        public async Task SendNetworkMessageAsync_WithLargeUadpPayload_UsesChunkingAsync()
        {
            byte[] payload = new byte[48];
            Array.Fill(payload, (byte)0x5A);
            var encoder = new StubEncoder(Profiles.PubSubUdpUadpTransport, payload);
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>
                {
                    [Profiles.PubSubUdpUadpTransport] = encoder
                },
                new Dictionary<string, INetworkMessageDecoder>(),
                maxNetworkMessageSize: 16);
            var transport = new SpyTransport();
            SetPrivateField(connection, "m_transport", transport);

            var message = new Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage
            {
                PublisherId = PublisherId.FromUInt16(11),
                WriterGroupId = 7
            };

            await InvokePrivateAsync(
                connection,
                "SendNetworkMessageAsync",
                message,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(transport.SentPayloads, Has.Count.GreaterThan(1));
        }

        [Test]
        [TestSpec("7.2.4.4.4", Summary = "Chunk splitting failures are surfaced and recorded")]
        public async Task SendChunkedAsync_WithInvalidFrameSize_ThrowsAndRecordsDiagnosticAsync()
        {
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                maxNetworkMessageSize: UadpChunker.ChunkHeaderSize,
                diagnostics: diagnostics);
            var transport = new SpyTransport();

            var exception = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await InvokePrivateAsync(
                    connection,
                    "SendChunkedAsync",
                    transport,
                    new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3, 4 }),
                    new Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage
                    {
                        PublisherId = PublisherId.FromUInt16(1),
                        WriterGroupId = 2
                    },
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(exception, Is.Not.Null);
            Assert.That(
                diagnostics.Read(PubSubDiagnosticsCounterKind.ChunksDiscarded),
                Is.EqualTo(1));
        }

        [Test]
        [TestSpec("7.3.2", Summary = "Receive loop returns when no decoder is registered")]
        public async Task ReceiveLoopAsync_WithoutDecoder_ReturnsAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>());
            SetPrivateField(
                connection,
                "m_transport",
                new SpyTransport(
                    [
                        new PubSubTransportFrame(
                            new byte[] { 1, 2, 3 },
                            null,
                            DateTimeUtc.From(DateTime.UtcNow))
                    ]));

            await InvokePrivateAsync(
                connection,
                "ReceiveLoopAsync",
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        [TestSpec("7.3.4.8", Summary = "Receive loop routes inbound metadata from decoder output")]
        public async Task ReceiveLoopAsync_WithMetadataMessage_UpdatesRegistryAsync()
        {
            var registry = new DataSetMetaDataRegistry();
            var meta = new DataSetMetaDataType
            {
                Name = "Inbound",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 4,
                    MinorVersion = 0
                }
            };
            var decoder = new StubDecoder(
                Profiles.PubSubUdpUadpTransport,
                (_, _, _) => new JsonMetaDataMessage
                {
                    PublisherId = PublisherId.FromUInt16(33),
                    DataSetWriterId = 12,
                    MetaDataPayload = meta
                });
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>
                {
                    [Profiles.PubSubUdpUadpTransport] = decoder
                },
                registry: registry);
            SetPrivateField(
                connection,
                "m_transport",
                new SpyTransport(
                    [
                        new PubSubTransportFrame(
                            new byte[] { 1, 2, 3 },
                            null,
                            DateTimeUtc.From(DateTime.UtcNow))
                    ]));

            await InvokePrivateAsync(
                connection,
                "ReceiveLoopAsync",
                CancellationToken.None).ConfigureAwait(false);

            var key = new DataSetMetaDataKey(
                PublisherId.FromUInt16(33),
                0,
                12,
                Uuid.Empty,
                4);
            MetaDataMatchResult result = registry.TryGet(in key, out DataSetMetaDataType? stored);
            Assert.That(result, Is.EqualTo(MetaDataMatchResult.Match));
            Assert.That(stored, Is.SameAs(meta));
        }

        [Test]
        [TestSpec("7.3.2", Summary = "Receive loop swallows decoder failures and continues")]
        public async Task ReceiveLoopAsync_WhenDecoderThrows_ContinuesToLaterFramesAsync()
        {
            var registry = new DataSetMetaDataRegistry();
            int decodeCount = 0;
            var decoder = new StubDecoder(
                Profiles.PubSubUdpUadpTransport,
                (_, _, _) =>
                {
                    decodeCount++;
                    if (decodeCount == 1)
                    {
                        throw new InvalidOperationException("boom");
                    }
                    return new JsonMetaDataMessage
                    {
                        PublisherId = PublisherId.FromUInt16(4),
                        DataSetWriterId = 9,
                        MetaDataPayload = new DataSetMetaDataType
                        {
                            Name = "Recovered",
                            ConfigurationVersion = new ConfigurationVersionDataType
                            {
                                MajorVersion = 2,
                                MinorVersion = 0
                            }
                        }
                    };
                });
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>
                {
                    [Profiles.PubSubUdpUadpTransport] = decoder
                },
                registry: registry);
            SetPrivateField(
                connection,
                "m_transport",
                new SpyTransport(
                    [
                        new PubSubTransportFrame(new byte[] { 1 }, null, DateTimeUtc.From(DateTime.UtcNow)),
                        new PubSubTransportFrame(new byte[] { 2 }, null, DateTimeUtc.From(DateTime.UtcNow))
                    ]));

            await InvokePrivateAsync(
                connection,
                "ReceiveLoopAsync",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(decodeCount, Is.EqualTo(2));
            var key = new DataSetMetaDataKey(PublisherId.FromUInt16(4), 0, 9, Uuid.Empty, 2);
            Assert.That(registry.TryGet(in key, out _), Is.EqualTo(MetaDataMatchResult.Match));
        }

        [Test]
        [TestSpec("7.2.4.4.4", Summary = "Malformed chunk headers are discarded")]
        public async Task TryReassembleChunk_WithMalformedHeader_ReturnsNullAsync()
        {
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                diagnostics: diagnostics);

            ReadOnlyMemory<byte>? result = InvokePrivate<ReadOnlyMemory<byte>?>(
                connection,
                "TryReassembleChunk",
                new ReadOnlyMemory<byte>(new byte[] { 0xAA, 0xBB, 0xCC }),
                1,
                PublisherId.FromUInt16(1),
                (ushort)2);

            Assert.That(result, Is.Null);
            Assert.That(
                diagnostics.Read(PubSubDiagnosticsCounterKind.ChunksDiscarded),
                Is.EqualTo(1));
        }

        [Test]
        [TestSpec("7.2.4.4.4", Summary = "Valid chunk sequences are reassembled")]
        public async Task TryReassembleChunk_WithValidChunks_ReassemblesPayloadAsync()
        {
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>());
            byte[] encoded = new byte[24];
            for (int ii = 0; ii < encoded.Length; ii++)
            {
                encoded[ii] = (byte)(ii + 1);
            }

            IReadOnlyList<byte[]> chunks = new UadpChunker().Split(encoded, 5, 18);
            byte[] prefix = [0x11, 0x22];

            ReadOnlyMemory<byte>? first = InvokePrivate<ReadOnlyMemory<byte>?>(
                connection,
                "TryReassembleChunk",
                new ReadOnlyMemory<byte>(Combine(prefix, chunks[0])),
                prefix.Length,
                PublisherId.FromUInt16(7),
                (ushort)8);
            ReadOnlyMemory<byte>? second = InvokePrivate<ReadOnlyMemory<byte>?>(
                connection,
                "TryReassembleChunk",
                new ReadOnlyMemory<byte>(Combine(prefix, chunks[1])),
                prefix.Length,
                PublisherId.FromUInt16(7),
                (ushort)8);
            ReadOnlyMemory<byte>? third = InvokePrivate<ReadOnlyMemory<byte>?>(
                connection,
                "TryReassembleChunk",
                new ReadOnlyMemory<byte>(Combine(prefix, chunks[2])),
                prefix.Length,
                PublisherId.FromUInt16(7),
                (ushort)8);

            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(third.HasValue, Is.True);
            Assert.That(third!.Value.ToArray(), Is.EqualTo(encoded));
        }

        [Test]
        [TestSpec("7.2.4.4.3", Summary = "Inbound unwrap failures are recorded and dropped")]
        public async Task TryUnwrapInboundAsync_WhenSecurityWrapperRejects_ReturnsNullAsync()
        {
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            UadpSecurityWrapper wrapHelper = CreateSecurityWrapper(acceptInbound: true);
            UadpSecurityWrapper failingWrapper = CreateSecurityWrapper(acceptInbound: false);
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                diagnostics: diagnostics,
                securityWrapper: failingWrapper);

            byte[] prefix = [0x40, 0x41];
            byte[] inner = [0x50, 0x51, 0x52];
            ReadOnlyMemory<byte> wrapped = await wrapHelper.WrapAsync(prefix, inner).ConfigureAwait(false);

            ReadOnlyMemory<byte>? result = await InvokePrivateAsync<ReadOnlyMemory<byte>?>(
                connection,
                "TryUnwrapInboundAsync",
                wrapped,
                prefix.Length,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(
                diagnostics.Read(PubSubDiagnosticsCounterKind.SignatureErrors),
                Is.EqualTo(1));
        }

        [Test]
        [TestSpec("7.2.4.4.3", Summary = "Security wrapper failures on encode are surfaced and recorded")]
        public async Task EncodeAndWrapUadpAsync_WhenWrapperThrows_RecordsDiagnosticAsync()
        {
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            var throwingWrapper = CreateSecurityWrapper(throwOnCurrentKey: true);
            await using PubSubConnection connection = CreateConnection(
                Profiles.PubSubUdpUadpTransport,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                diagnostics: diagnostics,
                securityWrapper: throwingWrapper);
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()),
                new DataSetMetaDataRegistry(),
                diagnostics,
                TimeProvider.System);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await InvokePrivateAsync(
                    connection,
                    "EncodeAndWrapUadpAsync",
                    new Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage(),
                    context,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(
                diagnostics.Read(PubSubDiagnosticsCounterKind.EncryptionErrors),
                Is.EqualTo(1));
        }

        private static PubSubConnection CreateConnection(
            string transportProfileUri,
            IReadOnlyDictionary<string, INetworkMessageEncoder> encoders,
            IReadOnlyDictionary<string, INetworkMessageDecoder> decoders,
            int maxNetworkMessageSize = 0,
            PubSubDiagnostics? diagnostics = null,
            IDataSetMetaDataRegistry? registry = null,
            UadpSecurityWrapper? securityWrapper = null)
        {
            return new PubSubConnection(
                new PubSubConnectionDataType
                {
                    Name = "private-tests",
                    TransportProfileUri = transportProfileUri
                },
                new StubTransportFactory(),
                encoders,
                decoders,
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                registry ?? new DataSetMetaDataRegistry(),
                diagnostics ?? new PubSubDiagnostics(PubSubDiagnosticsLevel.High),
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                securityWrapper,
                UadpSecurityWrapOptions.SignAndEncrypt,
                maxNetworkMessageSize);
        }

        private static UadpSecurityWrapper CreateSecurityWrapper(
            bool acceptInbound = true,
            bool throwOnCurrentKey = false)
        {
            return new UadpSecurityWrapper(
                new FakeSecurityPolicy(),
                new FakeKeyProvider(acceptInbound, throwOnCurrentKey),
                new FakeNonceProvider(),
                new FakeTokenWindow(acceptInbound),
                NUnitTelemetryContext.Create());
        }

        private static byte[] Combine(byte[] prefix, byte[] payload)
        {
            var combined = new byte[prefix.Length + payload.Length];
            Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
            Buffer.BlockCopy(payload, 0, combined, prefix.Length, payload.Length);
            return combined;
        }

        private static T InvokePrivate<T>(object instance, string methodName, params object?[] arguments)
        {
            MethodInfo method = GetMethod(instance.GetType(), methodName);
            object? result = method.Invoke(instance, arguments);
            return (T)result!;
        }

        private static async Task InvokePrivateAsync(object instance, string methodName, params object?[] arguments)
        {
            MethodInfo method = GetMethod(instance.GetType(), methodName);
            object? result = method.Invoke(instance, arguments);
            await AwaitResultAsync(result).ConfigureAwait(false);
        }

        private static async Task<T> InvokePrivateAsync<T>(object instance, string methodName, params object?[] arguments)
        {
            MethodInfo method = GetMethod(instance.GetType(), methodName);
            object? result = method.Invoke(instance, arguments);
            object? awaited = await AwaitResultAsync(result).ConfigureAwait(false);
            return awaited is null ? default! : (T)awaited;
        }

        private static async Task<object?> AwaitResultAsync(object? result)
        {
            if (result is null)
            {
                return null;
            }

            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                PropertyInfo? property = task.GetType().GetProperty("Result");
                return property?.GetValue(task);
            }

            Type resultType = result.GetType();
            if (resultType == typeof(ValueTask))
            {
                await (ValueTask)result;
                return null;
            }

            if (resultType.IsGenericType &&
                resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                dynamic dynamicValueTask = result;
                return await dynamicValueTask.AsTask().ConfigureAwait(false);
            }

            return result;
        }

        private static MethodInfo GetMethod(Type type, string methodName)
        {
            return type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)!
                ?? throw new MissingMethodException(type.FullName, methodName);
        }

        private static void SetPrivateField(object instance, string fieldName, object? value)
        {
            instance.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(instance, value);
        }

        private sealed class StubTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                return new SpyTransport();
            }
        }

        private sealed class SpyTransport : IPubSubTransport
        {
            private readonly IReadOnlyList<PubSubTransportFrame> m_frames;

            public SpyTransport(IReadOnlyList<PubSubTransportFrame>? frames = null)
            {
                m_frames = frames ?? Array.Empty<PubSubTransportFrame>();
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected => true;

            public List<ReadOnlyMemory<byte>> SentPayloads { get; } = [];

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default) => default;

            public ValueTask CloseAsync(CancellationToken cancellationToken = default) => default;

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                SentPayloads.Add(payload.ToArray());
                return default;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (PubSubTransportFrame frame in m_frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return frame;
                    await Task.Yield();
                }
            }

            public ValueTask DisposeAsync() => default;
        }

        private sealed class StubEncoder : INetworkMessageEncoder
        {
            private readonly ReadOnlyMemory<byte> m_payload;

            public StubEncoder(string transportProfileUri, ReadOnlyMemory<byte> payload)
            {
                TransportProfileUri = transportProfileUri;
                m_payload = payload;
            }

            public string TransportProfileUri { get; }

            public int EstimatedHeaderOverhead => 0;

            public int EncodeCallCount { get; private set; }

            public ValueTask<ReadOnlyMemory<byte>> EncodeAsync(
                PubSubNetworkMessage networkMessage,
                PubSubNetworkMessageContext context,
                CancellationToken cancellationToken = default)
            {
                EncodeCallCount++;
                return ValueTask.FromResult(m_payload);
            }
        }

        private sealed class StubDecoder : INetworkMessageDecoder
        {
            private readonly Func<ReadOnlyMemory<byte>, PubSubNetworkMessageContext, CancellationToken, PubSubNetworkMessage?> m_decode;

            public StubDecoder(
                string transportProfileUri,
                Func<ReadOnlyMemory<byte>, PubSubNetworkMessageContext, CancellationToken, PubSubNetworkMessage?> decode)
            {
                TransportProfileUri = transportProfileUri;
                m_decode = decode;
            }

            public string TransportProfileUri { get; }

            public ValueTask<PubSubNetworkMessage?> TryDecodeAsync(
                ReadOnlyMemory<byte> frame,
                PubSubNetworkMessageContext context,
                CancellationToken cancellationToken = default)
            {
                return ValueTask.FromResult(m_decode(frame, context, cancellationToken));
            }
        }

        private sealed class ThrowingRegistry : IDataSetMetaDataRegistry
        {
            public IReadOnlyCollection<DataSetMetaDataKey> Keys => Array.Empty<DataSetMetaDataKey>();

            public event EventHandler<DataSetMetaDataChangedEventArgs>? MetaDataChanged
            {
                add { }
                remove { }
            }

            public void Register(in DataSetMetaDataKey key, DataSetMetaDataType metaData)
            {
                throw new InvalidOperationException("expected");
            }

            public void Remove(in DataSetMetaDataKey key)
            {
            }

            public MetaDataMatchResult TryGet(in DataSetMetaDataKey key, out DataSetMetaDataType? metaData)
            {
                metaData = null;
                return MetaDataMatchResult.NotFound;
            }
        }

        private sealed record DummyNetworkMessage : PubSubNetworkMessage
        {
            public override string TransportProfileUri => Profiles.PubSubUdpUadpTransport;
        }

        private sealed class FakeSecurityPolicy : IPubSubSecurityPolicy
        {
            public string PolicyUri => "urn:test:policy";

            public int SigningKeyLength => 0;

            public int EncryptingKeyLength => 0;

            public int NonceLength => 0;

            public int SignatureLength => 0;

            public void Sign(
                ReadOnlySpan<byte> data,
                ReadOnlySpan<byte> signingKey,
                Span<byte> signature)
            {
            }

            public bool Verify(
                ReadOnlySpan<byte> data,
                ReadOnlySpan<byte> signature,
                ReadOnlySpan<byte> signingKey)
            {
                return true;
            }

            public void Encrypt(
                ReadOnlySpan<byte> plaintext,
                ReadOnlySpan<byte> encryptingKey,
                ReadOnlySpan<byte> nonce,
                Span<byte> ciphertext)
            {
                plaintext.CopyTo(ciphertext);
            }

            public void Decrypt(
                ReadOnlySpan<byte> ciphertext,
                ReadOnlySpan<byte> encryptingKey,
                ReadOnlySpan<byte> nonce,
                Span<byte> plaintext)
            {
                ciphertext.CopyTo(plaintext);
            }
        }

        private sealed class FakeKeyProvider : IPubSubSecurityKeyProvider
        {
            private readonly bool m_acceptInbound;
            private readonly bool m_throwOnCurrentKey;

            public FakeKeyProvider(bool acceptInbound, bool throwOnCurrentKey)
            {
                m_acceptInbound = acceptInbound;
                m_throwOnCurrentKey = throwOnCurrentKey;
            }

            public string SecurityGroupId => "group";

            public event EventHandler<PubSubKeyRotatedEventArgs>? KeyRotated
            {
                add { }
                remove { }
            }

            public ValueTask<PubSubSecurityKey> GetCurrentKeyAsync(
                CancellationToken cancellationToken = default)
            {
                if (m_throwOnCurrentKey)
                {
                    throw new InvalidOperationException("current key unavailable");
                }

                return ValueTask.FromResult(CreateKey());
            }

            public ValueTask<PubSubSecurityKey?> TryGetKeyAsync(
                uint tokenId,
                CancellationToken cancellationToken = default)
            {
                return ValueTask.FromResult(
                    m_acceptInbound ? CreateKey() : null);
            }

            private static PubSubSecurityKey CreateKey()
            {
                return new PubSubSecurityKey(
                    1,
                    ByteString.Empty,
                    ByteString.Empty,
                    ByteString.Empty,
                    DateTimeUtc.From(DateTime.UtcNow),
                    TimeSpan.FromMinutes(1));
            }
        }

        private sealed class FakeNonceProvider : INonceProvider
        {
            public void GetNext(Span<byte> buffer)
            {
                buffer.Clear();
            }
        }

        private sealed class FakeTokenWindow : ISecurityTokenWindow
        {
            private readonly bool m_accept;

            public FakeTokenWindow(bool accept)
            {
                m_accept = accept;
            }

            public bool TryAccept(uint tokenId, ulong sequenceNumber, ReadOnlySpan<byte> nonce)
            {
                return m_accept;
            }

            public void Reset()
            {
            }
        }
    }
}
