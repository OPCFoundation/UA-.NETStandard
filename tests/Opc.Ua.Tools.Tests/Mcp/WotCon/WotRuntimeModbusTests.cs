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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpRuntime;
using Opc.Ua.WotCon.Bindings.Modbus;
using Opc.Ua.WotCon.Bindings.Tests.Support;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises the existing Modbus engine with exact unit/function/range grants and an injected connector.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    public sealed class WotRuntimeModbusTests
    {
        [Test]
        public async Task LocalServerReadsAndWritesOnlyTheAuthorizedRegisterRangeAsync()
        {
            using var server = new TestModbusServer();
            server.HoldingRegisters[100] = 0x1234;
            server.HoldingRegisters[101] = 0x5678;
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
            [
                WotRuntimeTestContext.Rule(McpCapability.WotRuntime, ["readHoldingRegisters"],
                    ["unit=1;function=readHoldingRegisters;address=100;quantity=2"], "modbus+tcp", port: server.Port),
                WotRuntimeTestContext.Rule(McpCapability.WotWrite, ["writeMultipleHoldingRegisters"],
                    ["unit=1;function=writeMultipleHoldingRegisters;address=100;quantity=2"],
                    "modbus+tcp", port: server.Port)
            ], realExecutors: true);
            using IDisposable caller = fixture.Enter();
            string document = Document(server.Port);
            WotRuntimeSelection selection = WotRuntimeTestContext.Selection(document) with
            {
                BindingId = "w3c.modbus",
                BindingVersion = "1.0-ed"
            };
            JsonObject opened = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("modbus",
                new WotDocumentSource { Text = document }, selection, ["readproperty", "writeproperty"])
                .ConfigureAwait(false));
            string handle = opened["runtime"]!["handle"]!.GetValue<string>();
            Assert.That(server.AcceptedConnectionCount, Is.Zero);

            JsonObject read = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
            Assert.That(read["dataValue"]!["Value"]!.GetValue<int>(), Is.EqualTo(0x12345678));
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.WritePropertyAsync(handle,
                WotRuntimeTestContext.Value(BuiltInType.Int32, "1000042")).ConfigureAwait(false));

            Assert.That(server.HoldingRegisters[100], Is.EqualTo((ushort)(1_000_042 >> 16)));
            Assert.That(server.HoldingRegisters[101], Is.EqualTo((ushort)(1_000_042 & 0xFFFF)));
            Assert.That(server.LastFunctionCode, Is.EqualTo(16));
            Assert.That(server.AcceptedConnectionCount, Is.EqualTo(2));
        }

        [TestCase(2, 100)]
        [TestCase(1, 101)]
        public async Task ProtocolResourceSelectorsAreExactAsync(int unit, int address)
        {
            using var server = new TestModbusServer();
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
                [WotRuntimeTestContext.Rule(McpCapability.WotRuntime, ["readHoldingRegisters"],
                    ["unit=1;function=readHoldingRegisters;address=100;quantity=2"], "modbus+tcp", port: server.Port)],
                realExecutors: true);
            using IDisposable caller = fixture.Enter();
            string document = Document(server.Port, unit, address);
            WotRuntimeSelection selection = WotRuntimeTestContext.Selection(document) with
            {
                BindingId = "w3c.modbus",
                BindingVersion = "1.0-ed"
            };
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("denied",
                new WotDocumentSource { Text = document }, selection, ["readproperty"]).ConfigureAwait(false),
                error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(server.AcceptedConnectionCount, Is.Zero);
            fixture.Dns.VerifyNoOtherCalls();
        }

        [Test]
        public async Task InjectedConnectorIsUsedOnConnectAndReconnectAsync()
        {
            using var server = new TestModbusServer();
            server.HoldingRegisters[5] = 123;
            var calls = new List<(string Host, int Port)>();
            using var client = new ModbusTcpClient("must-not-resolve.invalid", 502, TimeSpan.FromSeconds(3),
                async (host, port, token) =>
                {
                    calls.Add((host, port));
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, server.Port), token)
                            .ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
            ushort[] first = await client.ReadHoldingRegistersAsync(1, 5, 1, deadline.Token).ConfigureAwait(false);
            server.DisconnectClients();
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                client.ReadHoldingRegistersAsync(1, 5, 1, deadline.Token).AsTask()).ConfigureAwait(false);

            Assert.That(failure, Is.InstanceOf<IOException>().Or.InstanceOf<ModbusException>()
                .Or.InstanceOf<SocketException>());
            Assert.That(calls, Has.Count.EqualTo(1), "A failed read must not be replayed automatically.");
            ushort[] second = await client.ReadHoldingRegistersAsync(1, 5, 1, deadline.Token).ConfigureAwait(false);

            Assert.That(first, Is.EqualTo(new ushort[] { 123 }));
            Assert.That(second, Is.EqualTo(new ushort[] { 123 }));
            Assert.That(calls, Is.EqualTo(
            [
                ("must-not-resolve.invalid", 502), ("must-not-resolve.invalid", 502)
            ]));
        }

        /// <summary>
        /// Builds an explicit register form; the existing planner computes its concrete Modbus operations.
        /// </summary>
        private static string Document(int port, int unit = 1, int address = 100)
        {
            return $$"""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "Bounded Modbus runtime",
                  "properties": {
                    "temperature": {
                      "type": "number",
                      "forms": [{
                        "href": "modbus+tcp://device.example:{{port}}/{{unit}}",
                        "modv:entity": "holdingRegister",
                        "modv:address": {{address}},
                        "modv:quantity": 2,
                        "modv:type": "int32"
                      }]
                    }
                  }
                }
                """;
        }
    }
}
#endif
