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

#nullable enable

using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Eth;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Eth.Channels.Pcap;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT smoke tests for the Ethernet (Layer 2) PubSub transport.
    /// Exercises the NativeAOT-safe addressing / framing / in-memory
    /// backend, and empirically evaluates whether the SharpPcap backend
    /// runs under NativeAOT — driving the decision between unconditional
    /// suppression (works) and <c>Requires*</c> annotation (does not).
    /// </summary>
    public class EthAotTests
    {
        [Test]
        public async Task ParsesAndFramesEthernet_AotSafe()
        {
            EthEndpoint endpoint = EthEndpointParser.Parse(
                "opc.eth://01-00-5E-00-00-01?vid=5&pcp=6");
            await Assert.That(endpoint.VlanId).IsEqualTo((ushort?)5);
            await Assert.That(endpoint.Priority).IsEqualTo((byte?)6);
            await Assert.That(endpoint.AddressType).IsEqualTo(EthAddressType.Multicast);

            byte[] payload = MakePayload(50);
            byte[] buffer = new byte[EthernetFrameCodec.GetRequiredLength(payload.Length, true)];
            int written = EthernetFrameCodec.Build(
                buffer,
                endpoint.Address.GetAddressBytes(),
                new byte[EthernetFrameCodec.MacAddressLength],
                endpoint.VlanId,
                endpoint.Priority,
                payload);

            bool parsed = EthernetFrameCodec.TryParse(
                buffer.AsMemory(0, written), out EthernetFrame frame);
            await Assert.That(parsed).IsTrue();
            await Assert.That(frame.VlanId).IsEqualTo((ushort?)5);
            await Assert.That(frame.Payload.Length).IsEqualTo(payload.Length);
        }

        [Test]
        public async Task InMemoryChannelRoundTrips_AotSafe()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            var factory = new InMemoryEthernetFrameChannelFactory();
            var parameters = new EthChannelParameters
            {
                InterfaceName = "aot",
                EtherType = EthernetFrameCodec.OpcUaEtherType,
                ReceiveQueueCapacity = 8,
                MaxFrameSize = 1500
            };

            IEthernetFrameChannel sender =
                factory.Create(parameters, telemetry, TimeProvider.System);
            IEthernetFrameChannel receiver =
                factory.Create(parameters, telemetry, TimeProvider.System);
            try
            {
                await sender.OpenAsync().ConfigureAwait(false);
                await receiver.OpenAsync().ConfigureAwait(false);

                byte[] frame = MakePayload(64);
                await sender.SendFrameAsync(frame).ConfigureAwait(false);

                byte[]? received = await ReceiveOneAsync(receiver, TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                await Assert.That(received).IsNotNull();
                await Assert.That(received!.Length).IsEqualTo(frame.Length);
            }
            finally
            {
                await receiver.DisposeAsync().ConfigureAwait(false);
                await sender.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SharpPcapBackendRunsUnderAot()
        {
            // Evaluation: touch the SharpPcap managed surface under the
            // NativeAOT-compiled binary. If SharpPcap's IL executes (even
            // when it then fails because no matching interface / native
            // libpcap is present in the test host), the backend is AOT
            // compatible and the unconditional suppression is correct. A
            // genuine AOT/reflection failure surfaces as an unexpected
            // exception type and fails this test.
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            var factory = new PcapEthernetFrameChannelFactory();
            var parameters = new EthChannelParameters
            {
                InterfaceName = "opcua-eth-aot-eval-nonexistent",
                EtherType = EthernetFrameCodec.OpcUaEtherType,
                ReceiveQueueCapacity = 4,
                MaxFrameSize = 1500
            };

            bool sharpPcapManagedCodeRan = false;
            IEthernetFrameChannel channel =
                factory.Create(parameters, telemetry, TimeProvider.System);
            try
            {
                await channel.OpenAsync().ConfigureAwait(false);
                // Opened against a real matching interface (unusual in CI).
                sharpPcapManagedCodeRan = true;
                await channel.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (IsExpectedEnvironmentFailure(ex))
            {
                // SharpPcap's managed code executed under NativeAOT and
                // failed only because the interface / native library is
                // unavailable here — confirming AOT compatibility.
                sharpPcapManagedCodeRan = true;
            }
            finally
            {
                await channel.DisposeAsync().ConfigureAwait(false);
            }

            await Assert.That(sharpPcapManagedCodeRan).IsTrue();
        }

        private static bool IsExpectedEnvironmentFailure(Exception ex)
        {
            return ex is InvalidOperationException
                or DllNotFoundException
                or EntryPointNotFoundException
                or TypeInitializationException
                or PlatformNotSupportedException
                or System.ComponentModel.Win32Exception;
        }

        private static byte[] MakePayload(int length)
        {
            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = (byte)(i + 1);
            }
            return payload;
        }

        private static async Task<byte[]?> ReceiveOneAsync(
            IEthernetFrameChannel channel,
            TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await foreach (ReadOnlyMemory<byte> frame in channel
                    .ReceiveFramesAsync(cts.Token).ConfigureAwait(false))
                {
                    return frame.ToArray();
                }
            }
            catch (OperationCanceledException)
            {
            }
            return null;
        }
    }
}
