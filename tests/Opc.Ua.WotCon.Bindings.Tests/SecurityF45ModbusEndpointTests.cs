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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Modbus;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Exercises Modbus endpoint policy at activation and the injected stream connector without sockets.
    /// </summary>
    [TestFixture]
    public sealed class SecurityF45ModbusEndpointTests
    {
        /// <summary>
        /// Read, write and observe forms cannot inherit an implicit local Modbus server.
        /// </summary>
        [TestCase("modbus+tcp", null, false)]
        [TestCase("modbus+tcp", "", false)]
        [TestCase("modbus+tcp", " \t", false)]
        [TestCase("modbus", null, false)]
        [TestCase("modbus", "", false)]
        [TestCase("modbus", " \t", false)]
        [TestCase("modbus+tcp", null, true)]
        [TestCase("modbus", " \t", true)]
        [Category("SecurityRegression")]
        public async Task SecurityF45HostlessModbusNeverConnects(string scheme, string? host, bool allowLoopback)
        {
            var attempts = new RejectedModbusConnection(new WotEndpointPolicy { AllowLoopback = allowLoopback });
            WotProtocolBinderRegistry registry = attempts.Registry();
            ImmutableArray<WotCompiledForm> forms = Plan(registry, scheme + "://device.example/1");
            Assert.That(forms.Select(form => form.Operation), Is.EquivalentTo(
            [
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty
            ]));
            foreach (WotCompiledForm planned in forms)
            {
                WotCompiledForm form = WithEndpoint(planned, new WotEndpointDescriptor(
                    scheme, host, -1, scheme + "://device.example"));

                await AssertRejectedAsync(
                    () => attempts.Executor.ActivateAsync(form, attempts.Context),
                    StatusCodes.BadInvalidArgument);
                await AssertRejectedAsync(() => registry.OpenChannelAsync(form), StatusCodes.BadInvalidArgument);
            }

            Assert.That(attempts.ConnectionAttempts, Is.Zero);
        }

        /// <summary>
        /// A hostless authority is rejected even when the separately supplied connector host is usable.
        /// </summary>
        [TestCase("modbus+tcp")]
        [TestCase("modbus")]
        [Category("SecurityRegression")]
        public async Task SecurityF45HostlessModbusAuthorityNeverActivates(string scheme)
        {
            var attempts = new RejectedModbusConnection();
            WotProtocolBinderRegistry registry = attempts.Registry();
            WotCompiledForm planned = Plan(registry, scheme + "://device.example/1")[0];
            WotCompiledForm form = WithEndpoint(planned, new WotEndpointDescriptor(
                scheme, "device.example", -1, scheme + "://"));

            await AssertRejectedAsync(() => registry.OpenChannelAsync(form), StatusCodes.BadInvalidArgument);

            Assert.That(attempts.ConnectionAttempts, Is.Zero);
        }

        /// <summary>
        /// A public BaseUri never grants loopback access to the actual Modbus connector.
        /// </summary>
        [TestCase("modbus+tcp", "127.0.0.1")]
        [TestCase("modbus+tcp", "::1")]
        [TestCase("modbus", "127.0.0.1")]
        [TestCase("modbus", "::1")]
        [TestCase("modbus+tcp", "localhost")]
        [Category("SecurityRegression")]
        public async Task SecurityF45FinalModbusDestinationCannotBypassLoopbackPolicy(string scheme, string host)
        {
            var attempts = new RejectedModbusConnection();
            WotProtocolBinderRegistry registry = attempts.Registry();
            WotCompiledForm planned = Plan(registry, scheme + "://device.example/1")[0];
            WotCompiledForm form = WithEndpoint(planned, new WotEndpointDescriptor(
                scheme, host, -1, scheme + "://device.example"));

            await AssertRejectedAsync(
                () => attempts.Executor.ActivateAsync(form, attempts.Context),
                StatusCodes.BadSecurityChecksFailed);
            await AssertRejectedAsync(() => registry.OpenChannelAsync(form), StatusCodes.BadSecurityChecksFailed);

            Assert.That(attempts.ConnectionAttempts, Is.Zero);
        }

        /// <summary>
        /// Host restrictions govern the connector host rather than merely the form's BaseUri.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF45FinalModbusDestinationHonorsHostRestrictions(bool useAllowList)
        {
            var policy = new WotEndpointPolicy();
            if (useAllowList)
            {
                policy.AllowedHosts.Add("device.example");
            }
            else
            {
                policy.BlockedHosts.Add("other.example");
            }
            var attempts = new RejectedModbusConnection(policy);
            WotProtocolBinderRegistry registry = attempts.Registry();
            WotCompiledForm planned = Plan(registry, "modbus+tcp://device.example/1")[0];
            WotCompiledForm form = WithEndpoint(planned, new WotEndpointDescriptor(
                "modbus+tcp", "other.example", 502, "modbus+tcp://device.example"));

            await AssertRejectedAsync(
                () => attempts.Executor.ActivateAsync(form, attempts.Context),
                StatusCodes.BadSecurityChecksFailed);
            await AssertRejectedAsync(() => registry.OpenChannelAsync(form), StatusCodes.BadSecurityChecksFailed);

            Assert.That(attempts.ConnectionAttempts, Is.Zero);
        }

        /// <summary>
        /// Permitted destinations retain their exact host and effective port and return the actual register value.
        /// </summary>
        [TestCase("modbus+tcp://device.example/1", "device.example", 502, false)]
        [TestCase("modbus://device.example:1502/1", "device.example", 1502, false)]
        [TestCase("modbus+tcp://127.0.0.1/1", "127.0.0.1", 502, true)]
        [TestCase("modbus+tcp://[::1]/1", "::1", 502, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF45PermittedModbusReadsFromExactDestination(
            string href, string expectedHost, int expectedPort, bool allowLoopback)
        {
            var policy = new WotEndpointPolicy { AllowLoopback = allowLoopback };
            policy.AllowedHosts.Add(expectedHost);
            using var stream = new ModbusReplyStream();
            string? connectedHost = null;
            int connectedPort = 0;
            int attempts = 0;
            var executor = new ModbusWotBindingExecutor(new ModbusWotBindingOptions
            {
                StreamConnector = (host, port, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    attempts++;
                    connectedHost = host;
                    connectedPort = port;
                    return new ValueTask<Stream>(stream);
                }
            });
            var registry = new WotProtocolBinderRegistry(
                [new ModbusBindingPlanner()], [executor], endpointPolicy: policy);
            WotCompiledForm form = Plan(registry, href)
                .Single(value => value.Operation == WoTBindingCapabilityEnum.ReadProperty);

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);
            WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Value.WrappedValue.TryGetValue(out ushort register), Is.True);
            Assert.That(register, Is.EqualTo((ushort)0x1234));
            Assert.That(stream.RequestHex, Is.EqualTo("000100000006010300640001"));
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(connectedHost, Is.EqualTo(expectedHost));
            Assert.That(connectedPort, Is.EqualTo(expectedPort));
        }

        private static ImmutableArray<WotCompiledForm> Plan(WotProtocolBinderRegistry registry, string href)
        {
            string document = $$"""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "Modbus destination",
                  "properties": {
                    "status": {
                      "type": "number",
                      "observable": true,
                      "forms": [{
                        "href": "{{href}}",
                        "modv:entity": "holdingRegister",
                        "modv:address": 100,
                        "modv:quantity": 1,
                        "modv:type": "uint16",
                        "op": ["readproperty", "writeproperty", "observeproperty"]
                      }]
                    }
                  }
                }
                """;
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "modbus-destination", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document)));
            return plan.CompiledForms;
        }

        private static WotCompiledForm WithEndpoint(WotCompiledForm form, WotEndpointDescriptor endpoint)
        {
            return new WotCompiledForm(
                form.Binding, form.AffordanceKind, form.AffordanceName, form.JsonPointer, form.Operation, form.OpToken,
                endpoint, form.Addressing, form.OperationInfo, form.Payload, form.Security, form.IsExecutable);
        }

        private static async Task AssertRejectedAsync(Func<ValueTask<IWotBindingChannel>> activate, StatusCode expected)
        {
            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await using IWotBindingChannel channel = await activate().ConfigureAwait(false);
            });
            Assert.That(exception!.StatusCode, Is.EqualTo(expected));
        }

        /// <summary>
        /// Counts denied connector calls while preventing any fallback to a real network connection.
        /// </summary>
        private sealed class RejectedModbusConnection
        {
            /// <summary>
            /// Configures a counting connector that cannot open a real socket.
            /// </summary>
            public RejectedModbusConnection(WotEndpointPolicy? policy = null)
            {
                m_policy = policy ?? new WotEndpointPolicy();
                Executor = new ModbusWotBindingExecutor(new ModbusWotBindingOptions
                {
                    StreamConnector = (_, _, _) =>
                    {
                        ConnectionAttempts++;
                        throw new IOException("An unauthorized connector was invoked.");
                    }
                });
                Context = new WotExecutorContext(endpointPolicy: m_policy);
            }

            /// <summary>
            /// Gets the real executor under test.
            /// </summary>
            public ModbusWotBindingExecutor Executor { get; }

            /// <summary>
            /// Gets the direct activation context with the same policy as the registry.
            /// </summary>
            public WotExecutorContext Context { get; }

            /// <summary>
            /// Gets the number of attempted stream connections.
            /// </summary>
            public int ConnectionAttempts { get; private set; }

            /// <summary>
            /// Creates the normal registry activation path.
            /// </summary>
            public WotProtocolBinderRegistry Registry()
            {
                return new WotProtocolBinderRegistry(
                    [new ModbusBindingPlanner()], [Executor], endpointPolicy: m_policy);
            }

            private readonly WotEndpointPolicy m_policy;
        }

        /// <summary>
        /// Separates a fixed register-read reply from the request bytes written by the real Modbus client.
        /// </summary>
        private sealed class ModbusReplyStream : MemoryStream
        {
            /// <summary>
            /// Initializes a reply with a holding-register value of 0x1234.
            /// </summary>
            public ModbusReplyStream()
                : base([0x00, 0x01, 0x00, 0x00, 0x00, 0x05, 0x01, 0x03, 0x02, 0x12, 0x34], false)
            {
            }

            /// <summary>
            /// Gets the actual MBAP header and request PDU emitted by the client.
            /// </summary>
            public string? RequestHex { get; private set; }

            /// <inheritdoc/>
            public override bool CanWrite => true;

            /// <inheritdoc/>
            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequestHex = Convert.ToHexString(buffer.Span);
                return default;
            }
        }
    }
}
