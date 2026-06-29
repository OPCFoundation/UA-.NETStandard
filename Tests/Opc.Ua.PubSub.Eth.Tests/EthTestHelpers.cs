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
            var payload = new byte[length];
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
