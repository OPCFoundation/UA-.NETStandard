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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transcoding;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;
using JsonDecoderV2 = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;
using JsonEncoderV2 = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpDecoderV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDecoder;
using UadpEncoderV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder;
using UadpNetworkMessageContentMask = Opc.Ua.UadpNetworkMessageContentMask;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Shared helpers for the transcoding fixtures.
    /// </summary>
    internal static class TranscodingTestUtilities
    {
        public static TranscodeContext NewContext(IDataSetMetaDataRegistry? registry = null)
        {
            var messageContext = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                registry ?? new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)));
            return new TranscodeContext(messageContext, NUnitTelemetryContext.Create());
        }

        public static Dictionary<string, INetworkMessageEncoder> Encoders()
        {
            var uadp = new UadpEncoderV2();
            var json = new JsonEncoderV2();
            return new Dictionary<string, INetworkMessageEncoder>(StringComparer.Ordinal)
            {
                [uadp.TransportProfileUri] = uadp,
                [json.TransportProfileUri] = json
            };
        }

        public static UadpNetworkMessageV2 NewUadpMessage(
            PublisherId publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId,
            params DataSetField[] fields)
        {
            return new UadpNetworkMessageV2
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = publisherId,
                WriterGroupId = writerGroupId,
                DataSetMessages =
                [
                    new UadpDataSetMessageV2
                    {
                        DataSetWriterId = dataSetWriterId,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = fields
                    }
                ]
            };
        }

        public static DataSetField Field(string name, Variant value)
        {
            return new DataSetField { Name = name, Value = value };
        }

        public static async Task<UadpNetworkMessageV2> DecodeUadpAsync(
            ReadOnlyMemory<byte> frame,
            TranscodeContext context)
        {
            var decoder = new UadpDecoderV2();
            PubSubNetworkMessage? decoded = await decoder
                .TryDecodeAsync(frame, context.EncodingContext)
                .ConfigureAwait(false);
            return (UadpNetworkMessageV2)decoded!;
        }

        public static async Task<JsonNetworkMessageV2> DecodeJsonAsync(
            ReadOnlyMemory<byte> frame,
            TranscodeContext context)
        {
            var decoder = new JsonDecoderV2();
            PubSubNetworkMessage? decoded = await decoder
                .TryDecodeAsync(frame, context.EncodingContext)
                .ConfigureAwait(false);
            return (JsonNetworkMessageV2)decoded!;
        }

        public static PubSubConnection NewConnection(string name)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var readerGroup = new ReaderGroup(
                new ReaderGroupDataType { Name = "rg" },
                Array.Empty<DataSetReader>(),
                telemetry);
            return new PubSubConnection(
                new PubSubConnectionDataType
                {
                    Name = name,
                    TransportProfileUri = Profiles.PubSubUdpUadpTransport
                },
                new NullTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                new[] { readerGroup },
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                telemetry,
                TimeProvider.System,
                securityWrapper: null,
                UadpSecurityWrapOptions.SignAndEncrypt,
                maxNetworkMessageSize: 0,
                MessageSecurityMode.None);
        }

        public static IServiceProvider BuildApplicationProvider(
            params PubSubConnection[] connections)
        {
            var app = new Mock<IPubSubApplication>();
            app.SetupGet(a => a.Connections).Returns(connections);
            app.SetupGet(a => a.MetaDataRegistry).Returns(new DataSetMetaDataRegistry());
            app.SetupGet(a => a.Diagnostics)
                .Returns(new PubSubDiagnostics(PubSubDiagnosticsLevel.Low));

            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<INetworkMessageEncoder>(new UadpEncoderV2());
            services.AddSingleton<INetworkMessageEncoder>(new JsonEncoderV2());
            services.AddSingleton(app.Object);
            return services.BuildServiceProvider();
        }

        private sealed class NullTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider) => new NullTransport();
        }

        private sealed class NullTransport : IPubSubTransport
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected => true;

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
                CancellationToken cancellationToken = default) => default;

#pragma warning disable CS1998 // async method lacks awaits — empty async sequence by design
            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default)
            {
                yield break;
            }
#pragma warning restore CS1998

            public ValueTask DisposeAsync() => default;
        }
    }
}
