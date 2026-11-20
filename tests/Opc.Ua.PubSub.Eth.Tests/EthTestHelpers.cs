// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Eth.Tests
{
    /// <summary>
    /// Shared helpers for the Ethernet transport tests.
    /// </summary>
    internal static class EthTestHelpers
    {
        public const string LoopbackInterface = "ethtest";

        public static PubSubConnectionDataType NewConnection(string url, string name = "Conn")
        {
            return new PubSubConnectionDataType
            {
                Name = name,
                TransportProfileUri = EthProfiles.PubSubEthUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = url
                })
            };
        }

        public static EthChannelParameters LoopbackParameters(
            string interfaceName = LoopbackInterface)
        {
            return new EthChannelParameters
            {
                InterfaceName = interfaceName,
                EtherType = EthernetFrameCodec.OpcUaEtherType,
                ReceiveQueueCapacity = 16,
                MaxFrameSize = 1500
            };
        }

        public static EthTransportOptions LoopbackOptions()
        {
            return new EthTransportOptions
            {
                ReceiveQueueCapacity = 16,
                MaxFrameSize = 1500
            };
        }

        public static byte[] MakePayload(int length)
        {
            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = (byte)(i + 7);
            }
            return payload;
        }

        public static async Task<PubSubTransportFrame?> ReceiveOneAsync(
            IPubSubTransport transport,
            TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await foreach (PubSubTransportFrame frame in transport.ReceiveAsync(cts.Token)
                    .ConfigureAwait(false))
                {
                    return frame;
                }
            }
            catch (OperationCanceledException)
            {
            }
            return null;
        }
    }
}
