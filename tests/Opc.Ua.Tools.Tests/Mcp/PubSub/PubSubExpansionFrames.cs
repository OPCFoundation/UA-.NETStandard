/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Produces real UADP traffic for local adapter and bridge tests without sockets or alternate protocol engines.
    /// </summary>
    internal static class PubSubExpansionFrames
    {
        /// <summary>
        /// Constructs a key frame matching the declared MCP reader masks and identifiers.
        /// </summary>
        internal static UadpNetworkMessage Data(
            PubSubApplicationInput input, ArrayOf<DataSetField> fields, bool dataValues = false)
        {
            return new UadpNetworkMessage
            {
                PublisherId = PubSubInputValidation.Publisher(input.PublisherId, PubSubMessageEncoding.Uadp),
                WriterGroupId = input.WriterGroupId,
                ContentMask = (UadpNetworkMessageContentMask)McpPubSubConfiguration.UadpNetworkMask,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = input.DataSetWriterId,
                        ContentMask = (UadpDataSetMessageContentMask)McpPubSubConfiguration.UadpDataSetMask,
                        FieldEncoding = dataValues ? PubSubFieldEncoding.DataValue : PubSubFieldEncoding.Variant,
                        FieldContentMask = DataSetFieldContentMask.StatusCode | DataSetFieldContentMask.SourceTimestamp,
                        MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 },
                        Fields = fields
                    }
                ]
            };
        }

        /// <summary>
        /// Encodes an ordinary data or action message through the shipped encoder.
        /// </summary>
        internal static async ValueTask<PubSubTransportFrame> EncodeAsync(
            PubSubNetworkMessage message, PubSubExpansionTestContext fixture, CancellationToken ct)
        {
            ReadOnlyMemory<byte> bytes = await new UadpEncoder().EncodeAsync(message, Context(fixture), ct)
                .ConfigureAwait(false);
            return new PubSubTransportFrame(bytes, null, DateTimeUtc.From(fixture.Clock.GetUtcNow()));
        }

        /// <summary>
        /// Decodes a copied output frame using the shipped decoder.
        /// </summary>
        internal static PubSubNetworkMessage? Decode(byte[] bytes, PubSubExpansionTestContext fixture)
        {
            return UadpDecoder.Decode(bytes, Context(fixture));
        }

        /// <summary>
        /// Waits for an observable local state transition under the test's bounded lifetime.
        /// </summary>
        internal static async Task UntilAsync(Func<bool> condition, CancellationToken ct)
        {
            while (!condition())
            {
                await Task.Delay(1, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Uses variant field encoding so no independent shadow metadata implementation is needed.
        /// </summary>
        private static PubSubNetworkMessageContext Context(PubSubExpansionTestContext fixture)
        {
            return new PubSubNetworkMessageContext(fixture.Remote.Context, new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low), fixture.Clock);
        }
    }
}
#endif
