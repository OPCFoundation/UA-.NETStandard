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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Eth;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Checks full-frame Ethernet scope through the real transport and injected in-memory frame backend.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionEthernetTests
    {
        [Test]
        public async Task WrongVlanAndSourceMacAreRejectedBeforeAValidFrameReachesTheReaderAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            var backend = new InMemoryEthernetFrameChannelFactory();
            var options = new McpPubSubOptions
            {
                EthernetRules =
                [
                    new McpPubSubEthernetRule
                    {
                        Endpoint = "opc.eth://01-1B-19-00-00-01?vid=7&pcp=3",
                        NetworkInterface = "virtual-policy-test",
                        LocalMac = "02-00-00-00-00-02",
                        SourceMacs = ["02-00-00-00-00-01"],
                        Operations = ["subscribe"]
                    }
                ]
            };
            IMcpOperationPolicy authority = fixture.Services.GetRequiredService<IMcpOperationPolicy>();
            var provider = new McpPubSubTransportProvider(authority, new McpNetworkPolicy(authority, []),
                options, fixture.Clock, ethernetFactory: backend);
            PubSubApplicationInput input = PubSubExpansionTestContext.Input("receiver", "Subscriber") with
            {
                Connection = new PubSubConnectionInput
                {
                    Transport = PubSubTransportKind.Ethernet,
                    Endpoint = options.EthernetRules[0].Endpoint,
                    NetworkInterface = "virtual-policy-test"
                }
            };
            var source = new McpPubSubDataSetSource(input, options, fixture.Remote.Context, fixture.Clock);
            await using IPubSubTransport receiver = provider.CreateFactory(input).Create(
                McpPubSubConfiguration.Build(input, source.BuildMetaData()).Connections[0],
                fixture.Sessions.Telemetry, fixture.Clock);
            await using IEthernetFrameChannel sender = backend.Create(new EthChannelParameters
            {
                InterfaceName = "virtual-policy-test",
                InterfaceAddress = PhysicalAddress.Parse("02-00-00-00-00-01")
            }, fixture.Sessions.Telemetry, fixture.Clock);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await receiver.OpenAsync(timeout.Token).ConfigureAwait(false);
            await sender.OpenAsync(timeout.Token).ConfigureAwait(false);
            await using IAsyncEnumerator<PubSubTransportFrame> frames = receiver.ReceiveAsync(timeout.Token)
                .GetAsyncEnumerator(timeout.Token);

            await sender.SendFrameAsync(Frame(8, "02-00-00-00-00-01", 0x41), timeout.Token).ConfigureAwait(false);
            await sender.SendFrameAsync(Frame(7, "02-00-00-00-00-03", 0x42), timeout.Token).ConfigureAwait(false);
            await sender.SendFrameAsync(Frame(7, "02-00-00-00-00-01", 0x77), timeout.Token).ConfigureAwait(false);
            bool received = await frames.MoveNextAsync().ConfigureAwait(false);

            Assert.That(received, Is.True);
            byte[] expectedPayload = new byte[42];
            expectedPayload[0] = 0x77;
            Assert.That(frames.Current.Payload.ToArray(), Is.EqualTo(expectedPayload),
                "A tagged minimum-size Ethernet frame has one data byte and 41 zero padding bytes.");
            Assert.That(receiver.IsConnected, Is.True);
        }

        private static byte[] Frame(ushort vlan, string source, byte value)
        {
            byte[] frame = new byte[EthernetFrameCodec.GetRequiredLength(1, true)];
            int count = EthernetFrameCodec.Build(frame,
                PhysicalAddress.Parse("01-1B-19-00-00-01").GetAddressBytes(),
                PhysicalAddress.Parse(source).GetAddressBytes(), vlan, 3, [value]);
            Assert.That(count, Is.EqualTo(frame.Length));
            return frame;
        }
    }
}
#endif
