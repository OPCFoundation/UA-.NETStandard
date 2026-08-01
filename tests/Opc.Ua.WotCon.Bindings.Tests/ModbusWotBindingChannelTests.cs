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
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Modbus;
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Bindings.Tests.Support;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Tests for <c>ModbusWotBindingChannel</c> (entity dispatch, error mapping,
    /// polling subscriptions, disposal) and additional <c>ModbusTcpClient</c>
    /// protocol paths (FC 02, FC 0x0F, exception responses, transaction-id mismatch,
    /// zero-length responses, unexpected function codes, and not-connected faults).
    /// </summary>
    [TestFixture]
    public sealed class ModbusWotBindingChannelTests
    {
        private static WotProtocolBinderRegistry Registry()
        {
            return new WotProtocolBinderRegistry(
                [new ModbusBindingPlanner()],
                [new ModbusWotBindingExecutor(new ModbusWotBindingOptions
                {
                    ObserveInterval = TimeSpan.FromMilliseconds(100)
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
        }

        private static WotBindingPlan Plan(WotProtocolBinderRegistry registry, string td)
        {
            return registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
        }

        private static string RegisterTd(int port, string entity, int address, int quantity, string type)
        {
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                port.ToString(CultureInfo.InvariantCulture) + "/1\",\"modv:entity\":\"" + entity +
                "\",\"modv:address\":" + address.ToString(CultureInfo.InvariantCulture) +
                ",\"modv:quantity\":" + quantity.ToString(CultureInfo.InvariantCulture) +
                ",\"modv:type\":\"" + type + "\"}]}}}";
        }

        private static string BooleanTd(int port, string entity, int address)
        {
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"p\":{\"type\":\"boolean\",\"forms\":[{\"href\":\"modbus+tcp://127.0.0.1:" +
                port.ToString(CultureInfo.InvariantCulture) + "/1\",\"modv:entity\":\"" + entity +
                "\",\"modv:address\":" + address.ToString(CultureInfo.InvariantCulture) +
                ",\"modv:quantity\":1}]}}}";
        }

        private static WotCompiledForm BuildRawForm(
            int port, string entity, WoTBindingCapabilityEnum capability, string opName)
        {
            var addressing = new WotAddressingDescriptor(
                entity + ":0:1@1",
                ImmutableDictionary<string, string>.Empty
                    .Add("entity", entity)
                    .Add("address", "0")
                    .Add("quantity", "1")
                    .Add("unitId", "1"));
            var payload = new WotPayloadDescriptor(
                "application/octet-stream", "octet-stream",
                ImmutableDictionary<string, string>.Empty
                    .Add("type", "uint16")
                    .Add("mostSignificantByte", "true")
                    .Add("mostSignificantWord", "true"));
            return new WotCompiledForm(
                new WotBindingIdentity("w3c.modbus", "1.0-ed", ModbusBindingPlanner.BindingUri),
                WotAffordanceKind.Property, "p", "/properties/p/forms/0",
                capability, opName,
                new WotEndpointDescriptor(
                    "modbus+tcp", "127.0.0.1", port,
                    "modbus+tcp://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)),
                addressing,
                new WotOperationDescriptor(capability, opName, opName),
                payload,
                [], isExecutable: true);
        }

        private static async Task<bool> WaitForNotificationAsync(
            ConcurrentQueue<WotNotification> queue, int maxAttempts = 100)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                if (!queue.IsEmpty)
                {
                    return true;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            return false;
        }

        [Test]
        public async Task ModbusChannelReadsInputRegisterEndToEnd()
        {
            using var server = new TestModbusServer();
            server.InputRegisters[5] = 0xABCD;

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, RegisterTd(server.Port, "inputRegister", 5, 1, "uint16"));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo((ushort)0xABCD));
            }
        }

        [Test]
        public async Task ModbusChannelReadsDiscreteInputEndToEnd()
        {
            using var server = new EphemeralModbusServer((_, pdu) =>
            {
                if (pdu.Length >= 1 && pdu[0] == 0x02)
                {
                    return [0x02, 0x01, 0x01];
                }
                return [];
            });

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, BooleanTd(server.Port, "discreteInput", 0));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.True);
            }
        }

        [Test]
        public void ModbusChannelWriteToDiscreteInputIsRejectedDuringActivation()
        {
            using var server = new TestModbusServer();
            WotCompiledForm form = BuildRawForm(
                server.Port, "discreteInput", WoTBindingCapabilityEnum.WriteProperty, "writeproperty");
            var executor = new ModbusWotBindingExecutor();
            Assert.ThrowsAsync<ArgumentException>(
                async () => await executor
                    .ActivateAsync(form, new WotExecutorContext())
                    .ConfigureAwait(false));
        }

        [Test]
        public void ModbusChannelWriteToInputRegisterIsRejectedDuringActivation()
        {
            using var server = new TestModbusServer();
            WotCompiledForm form = BuildRawForm(
                server.Port, "inputRegister", WoTBindingCapabilityEnum.WriteProperty, "writeproperty");
            var executor = new ModbusWotBindingExecutor();
            Assert.ThrowsAsync<ArgumentException>(
                async () => await executor
                    .ActivateAsync(form, new WotExecutorContext())
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task ModbusChannelInvokeAsyncReturnsBadNotSupported()
        {
            using var server = new TestModbusServer();

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, RegisterTd(server.Port, "holdingRegister", 0, 1, "uint16"));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNotSupported));
            }
        }

        [Test]
        public async Task ModbusChannelObserveAsyncReceivesNotification()
        {
            using var server = new TestModbusServer();
            server.HoldingRegisters[0] = 0x0042;

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, RegisterTd(server.Port, "holdingRegister", 0, 1, "uint16"));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                var received = new ConcurrentQueue<WotNotification>();
                IWotSubscription sub = await channel.ObserveAsync(n => received.Enqueue(n))
                    .ConfigureAwait(false);
                await using (sub.ConfigureAwait(false))
                {
                    bool got = await WaitForNotificationAsync(received).ConfigureAwait(false);
                    Assert.That(got, Is.True, "The observe subscription should deliver at least one notification.");
                }
            }
        }

        [Test]
        public async Task ModbusChannelSubscribeEventAsyncReceivesNotification()
        {
            using var server = new TestModbusServer();
            server.HoldingRegisters[0] = 0x0007;

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, RegisterTd(server.Port, "holdingRegister", 0, 1, "uint16"));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                var received = new ConcurrentQueue<WotNotification>();
                IWotSubscription sub = await channel.SubscribeEventAsync(n => received.Enqueue(n))
                    .ConfigureAwait(false);
                await using (sub.ConfigureAwait(false))
                {
                    bool got = await WaitForNotificationAsync(received).ConfigureAwait(false);
                    Assert.That(got, Is.True,
                        "SubscribeEventAsync should delegate to ObserveAsync and deliver notifications.");
                }
            }
        }

        [Test]
        public async Task ModbusChannelDisposeAsyncIsIdempotent()
        {
            using var server = new TestModbusServer();

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, RegisterTd(server.Port, "holdingRegister", 0, 1, "uint16"));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);

            // First dispose should succeed.
            await channel.DisposeAsync().ConfigureAwait(false);

            // Second dispose must not throw.
            Assert.DoesNotThrowAsync(
                async () => await channel.DisposeAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task ModbusChannelReadMapsModbusExceptionToStatusCode()
        {
            // Server returns Modbus exception 0x02 (illegal data address) for FC 03.
            using var server = new EphemeralModbusServer((_, pdu) =>
            {
                if (pdu.Length >= 1 && pdu[0] == 0x03)
                {
                    return [0x83, 0x02];
                }
                return [];
            });

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, RegisterTd(server.Port, "holdingRegister", 0, 1, "uint16"));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                // Exception code 0x02 maps to BadNodeIdUnknown via ModbusStatusMapper.
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
        }

        [Test]
        public async Task ModbusChannelReadMapsTimeoutToBadTimeout()
        {
            // Server holds connection without responding on first request (timeout).
            int connectionCount = 0;
            using var server = new EphemeralModbusServer((conn, _) =>
            {
                int c = Interlocked.Increment(ref connectionCount);
                // Null = hold the connection open forever (provoke client timeout).
                return c <= 0 ? null : null;
            });

            // Use a very short timeout so the test completes quickly.
            var options = new ModbusWotBindingOptions { ObserveInterval = TimeSpan.FromSeconds(1) };
            var smallBounds = new WotBindingBounds { DefaultTimeout = TimeSpan.FromMilliseconds(300) };
            var registry = new WotProtocolBinderRegistry(
                [new ModbusBindingPlanner()],
                [new ModbusWotBindingExecutor(options)],
                bounds: smallBounds,
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });

            WotBindingPlan plan = Plan(registry, RegisterTd(server.Port, "holdingRegister", 0, 1, "uint16"));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadTimeout));
            }
        }

        [Test]
        public async Task ModbusChannelWriteMapsModbusExceptionToStatusCode()
        {
            // Server returns Modbus exception 0x03 (illegal data value) for FC 06.
            using var server = new EphemeralModbusServer((_, pdu) =>
            {
                if (pdu.Length >= 1 && pdu[0] == 0x06)
                {
                    return [0x86, 0x03];
                }
                return [pdu[0], pdu[1], pdu[2], pdu[3], pdu[4]];
            });

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, RegisterTd(server.Port, "holdingRegister", 0, 1, "uint16"));
            WotCompiledForm write = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotWriteResult result = await channel.WriteAsync(
                    new DataValue(new Variant((ushort)42))).ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                // Exception code 0x03 maps to BadInvalidArgument via ModbusStatusMapper.
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadInvalidArgument));
            }
        }

        [Test]
        public async Task ModbusChannelWriteMapsTimeoutToBadTimeout()
        {
            using var server = new EphemeralModbusServer((_, _2) => null);

            var options = new ModbusWotBindingOptions { ObserveInterval = TimeSpan.FromSeconds(1) };
            var smallBounds = new WotBindingBounds { DefaultTimeout = TimeSpan.FromMilliseconds(300) };
            var registry = new WotProtocolBinderRegistry(
                [new ModbusBindingPlanner()],
                [new ModbusWotBindingExecutor(options)],
                bounds: smallBounds,
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });

            WotBindingPlan plan = Plan(registry, RegisterTd(server.Port, "holdingRegister", 0, 1, "uint16"));
            WotCompiledForm write = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotWriteResult result = await channel.WriteAsync(
                    new DataValue(new Variant((ushort)1))).ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadTimeout));
            }
        }

        [Test]
        public async Task ModbusChannelWriteMultipleRegistersEndToEnd()
        {
            using var server = new TestModbusServer();

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, RegisterTd(server.Port, "holdingRegister", 10, 2, "int32"));
            WotCompiledForm write = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotWriteResult result = await channel.WriteAsync(
                    new DataValue(new Variant(0x12345678))).ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(server.HoldingRegisters[10], Is.EqualTo((ushort)0x1234));
                Assert.That(server.HoldingRegisters[11], Is.EqualTo((ushort)0x5678));
            }
        }

        [Test]
        public async Task ModbusTcpClientReadsDiscreteInputsSuccessfully()
        {
            using var server = new EphemeralModbusServer((_, pdu) =>
            {
                if (pdu.Length >= 1 && pdu[0] == 0x02)
                {
                    // 3 discrete inputs, bits 1 and 3 set (0b00001010 = 0x0A).
                    return [0x02, 0x01, 0x0A];
                }
                return [];
            });

            using var client = new ModbusTcpClient("127.0.0.1", server.Port, TimeSpan.FromSeconds(2));
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            bool[] bits = await client.ReadDiscreteInputsAsync(
                1, 0, 3, CancellationToken.None).ConfigureAwait(false);

            Assert.That(bits, Has.Length.EqualTo(3));
            Assert.That(bits[0], Is.False); // bit 0 of 0x0A
            Assert.That(bits[1], Is.True);  // bit 1 of 0x0A
            Assert.That(bits[2], Is.False); // bit 2 of 0x0A
        }

        [Test]
        public async Task ModbusTcpClientWritesMultipleCoilsSuccessfully()
        {
            byte[]? capturedPdu = null;
            using var server = new EphemeralModbusServer((_, pdu) =>
            {
                if (pdu.Length >= 1 && pdu[0] == 0x0F)
                {
                    capturedPdu = pdu;
                    // Echo back: FC, addrHi, addrLo, qtyHi, qtyLo.
                    return [0x0F, pdu[1], pdu[2], pdu[3], pdu[4]];
                }
                return [];
            });

            using var client = new ModbusTcpClient("127.0.0.1", server.Port, TimeSpan.FromSeconds(2));
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            await client.WriteMultipleCoilsAsync(
                1, 0, [true, false, true], CancellationToken.None).ConfigureAwait(false);

            Assert.That(capturedPdu, Is.Not.Null);
            Assert.That(capturedPdu![0], Is.EqualTo((byte)0x0F), "Function code must be 0x0F.");
            // 3 coils packed into 1 byte: true=1, false=0, true=1 → 0b00000101 = 0x05.
            Assert.That(capturedPdu[6], Is.EqualTo((byte)0x05), "Packed coil byte must encode correct bit pattern.");
        }

        [Test]
        public async Task ModbusTcpClientExceptionResponseThrowsWithCorrectCode()
        {
            // Server returns Modbus exception 0x04 (server device failure) for FC 03.
            using var server = new EphemeralModbusServer((_, pdu) =>
            {
                if (pdu.Length >= 1 && pdu[0] == 0x03)
                {
                    return [0x83, 0x04];
                }
                return [];
            });

            using var client = new ModbusTcpClient("127.0.0.1", server.Port, TimeSpan.FromSeconds(2));
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            ModbusException? ex = Assert.ThrowsAsync<ModbusException>(async () =>
                await client.ReadHoldingRegistersAsync(
                    1, 0, 1, CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex!.ExceptionCode, Is.EqualTo((byte)0x04));
        }

        [Test]
        public void ModbusTcpClientNotConnectedThrowsModbusException()
        {
            // Never call ConnectAsync — the client is not connected.
            using var client = new ModbusTcpClient("127.0.0.1", 1, TimeSpan.FromSeconds(1));

            ModbusException? ex = Assert.ThrowsAsync<ModbusException>(async () =>
                await client.ReadHoldingRegistersAsync(
                    1, 0, 1, CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex!.Message, Does.Contain("not connected").IgnoreCase);
        }

        [Test]
        public async Task ModbusTcpClientTransactionIdMismatchThrowsModbusException()
        {
            // Server always replies with transaction ID 0x00 0x00, never echoing
            // the client's actual transaction ID.
            using var server = new BadTxnModbusServer();

            using var client = new ModbusTcpClient(
                "127.0.0.1", server.Port, TimeSpan.FromSeconds(2));
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            ModbusException? ex = Assert.ThrowsAsync<ModbusException>(async () =>
                await client.ReadHoldingRegistersAsync(
                    1, 0, 1, CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex!.Message, Does.Contain("transaction").IgnoreCase);
        }

        [Test]
        public async Task ModbusTcpClientZeroLengthResponseThrowsModbusException()
        {
            // Server returns an empty PDU [], which builds a frame with length=1
            // (unit byte only, no PDU bytes). The client reads responseLength=0 < 1
            // and must throw.
            using var server = new EphemeralModbusServer((_, _2) => []);

            using var client = new ModbusTcpClient(
                "127.0.0.1", server.Port, TimeSpan.FromSeconds(2));
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            ModbusException? ex = Assert.ThrowsAsync<ModbusException>(async () =>
                await client.ReadHoldingRegistersAsync(
                    1, 0, 1, CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex!.Message, Does.Contain("invalid").IgnoreCase.Or.Contain("length").IgnoreCase);
        }

        [Test]
        public async Task ModbusTcpClientUnexpectedFunctionCodeThrowsModbusException()
        {
            // Server replies to FC 0x03 with FC 0x04 (not an exception, just the wrong function).
            using var server = new EphemeralModbusServer((_, pdu) =>
            {
                if (pdu.Length >= 1 && pdu[0] == 0x03)
                {
                    // Valid input-register response format but wrong FC.
                    return [0x04, 0x02, 0xAB, 0xCD];
                }
                return [];
            });

            using var client = new ModbusTcpClient(
                "127.0.0.1", server.Port, TimeSpan.FromSeconds(2));
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            ModbusException? ex = Assert.ThrowsAsync<ModbusException>(async () =>
                await client.ReadHoldingRegistersAsync(
                    1, 0, 1, CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex!.Message, Does.Contain("Unexpected").IgnoreCase);
        }

        /// <summary>
        /// A minimal scripted Modbus TCP server. The responder receives the
        /// connection index and request PDU and returns the response PDU (or
        /// <c>null</c> to hold the connection without responding).
        /// </summary>
        private sealed class EphemeralModbusServer : IDisposable
        {
            public EphemeralModbusServer(Func<int, byte[], byte[]?> responder)
            {
                m_responder = responder;
                m_listener = new TcpListener(IPAddress.Loopback, 0);
                m_listener.Start();
                Port = ((IPEndPoint)m_listener.LocalEndpoint).Port;
                m_loop = Task.Run(AcceptLoopAsync);
            }

            public int Port { get; }

            public void Dispose()
            {
                m_cts.Cancel();
                m_listener.Stop();
                m_listener.Dispose();
                try
                {
                    m_loop.Wait(2000);
                }
                catch (AggregateException)
                {
                    // Ignore teardown faults.
                }
                m_cts.Dispose();
            }

            private async Task AcceptLoopAsync()
            {
                while (!m_cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await m_listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        return;
                    }
                    int conn = m_connections++;
                    _ = Task.Run(() => ServeAsync(client, conn));
                }
            }

            private async Task ServeAsync(TcpClient client, int connection)
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    try
                    {
                        while (!m_cts.IsCancellationRequested)
                        {
                            byte[]? header = await ReadExactAsync(stream, 6).ConfigureAwait(false);
                            if (header is null)
                            {
                                return;
                            }
                            int len = (header[4] << 8) | header[5];
                            byte[]? rest = await ReadExactAsync(stream, len).ConfigureAwait(false);
                            if (rest is null)
                            {
                                return;
                            }
                            byte unit = rest[0];
                            byte[] pdu = new byte[rest.Length - 1];
                            Array.Copy(rest, 1, pdu, 0, pdu.Length);

                            byte[]? responsePdu = m_responder(connection, pdu);
                            if (responsePdu is null)
                            {
                                await Task.Delay(Timeout.Infinite, m_cts.Token).ConfigureAwait(false);
                                return;
                            }

                            byte[] frame = BuildFrame(header[0], header[1], unit, responsePdu);
                            await stream.WriteAsync(frame).ConfigureAwait(false);
                            await stream.FlushAsync().ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            private static byte[] BuildFrame(byte txnHi, byte txnLo, byte unit, byte[] pdu)
            {
                int length = pdu.Length + 1;
                byte[] frame = new byte[7 + pdu.Length];
                frame[0] = txnHi;
                frame[1] = txnLo;
                frame[2] = 0x00;
                frame[3] = 0x00;
                frame[4] = (byte)(length >> 8);
                frame[5] = (byte)(length & 0xFF);
                frame[6] = unit;
                Array.Copy(pdu, 0, frame, 7, pdu.Length);
                return frame;
            }

            private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count)
            {
                byte[] buffer = new byte[count];
                int offset = 0;
                while (offset < count)
                {
                    int read = await stream
                        .ReadAsync(buffer.AsMemory(offset, count - offset)).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return null;
                    }
                    offset += read;
                }
                return buffer;
            }

            private readonly Func<int, byte[], byte[]?> m_responder;
            private readonly TcpListener m_listener;
            private readonly Task m_loop;
            private readonly CancellationTokenSource m_cts = new();
            private int m_connections;
        }

        /// <summary>
        /// A Modbus TCP server that always replies with transaction ID 0x0000,
        /// deliberately mismatching any client request to trigger the
        /// transaction-id-mismatch fault path.
        /// </summary>
        private sealed class BadTxnModbusServer : IDisposable
        {
            public BadTxnModbusServer()
            {
                m_listener = new TcpListener(IPAddress.Loopback, 0);
                m_listener.Start();
                Port = ((IPEndPoint)m_listener.LocalEndpoint).Port;
                m_loop = Task.Run(AcceptLoopAsync);
            }

            public int Port { get; }

            public void Dispose()
            {
                m_cts.Cancel();
                m_listener.Stop();
                m_listener.Dispose();
                try
                {
                    m_loop.Wait(2000);
                }
                catch (AggregateException)
                {
                }
                m_cts.Dispose();
            }

            private async Task AcceptLoopAsync()
            {
                while (!m_cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await m_listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        return;
                    }
                    _ = Task.Run(() => ServeAsync(client));
                }
            }

            private async Task ServeAsync(TcpClient client)
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    try
                    {
                        // Read the request but always reply with txn ID 0x0000.
                        byte[]? header = await ReadExactAsync(stream, 6).ConfigureAwait(false);
                        if (header is null)
                        {
                            return;
                        }
                        int len = (header[4] << 8) | header[5];
                        byte[]? rest = await ReadExactAsync(stream, len).ConfigureAwait(false);
                        if (rest is null)
                        {
                            return;
                        }
                        byte unit = rest[0];
                        // Send a valid holding-register read response but with txn ID 0x00 0x00.
                        byte[] response = [0x00, 0x00, 0x00, 0x00, 0x00, 0x05, unit,
                                           0x03, 0x02, 0x00, 0x00];
                        await stream.WriteAsync(response).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count)
            {
                byte[] buffer = new byte[count];
                int offset = 0;
                while (offset < count)
                {
                    int read = await stream
                        .ReadAsync(buffer.AsMemory(offset, count - offset)).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return null;
                    }
                    offset += read;
                }
                return buffer;
            }

            private readonly TcpListener m_listener;
            private readonly Task m_loop;
            private readonly CancellationTokenSource m_cts = new();
        }
    }
}
