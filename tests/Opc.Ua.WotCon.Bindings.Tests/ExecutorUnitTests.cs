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

using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Modbus;
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Bindings.Tests.Support;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for executor identity, dispatch and HTTP error mapping.
    /// </summary>
    [TestFixture]
    public sealed class ExecutorUnitTests
    {
        private static WotCompiledForm Compiled(string bindingId, string scheme)
        {
            return new WotCompiledForm(
                        new WotBindingIdentity(bindingId, "1.0", "urn:x"),
                        WotAffordanceKind.Property, "p", "/properties/p/forms/0",
                        WoTBindingCapabilityEnum.ReadProperty, "readproperty",
                        new WotEndpointDescriptor(scheme, "h", 1, scheme + "://h"),
                        new WotAddressingDescriptor("t"),
                        new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "GET"),
                        new WotPayloadDescriptor("application/json", "json"),
                        [], isExecutable: true);
        }

        private static WotEndpointPolicy AllowLoopbackPolicy()
        {
            return new WotEndpointPolicy { AllowLoopback = true };
        }

        [Test]
        public void CanExecuteMatchesOwnBindingOnly()
        {
            var http = new HttpWotBindingExecutor();
            var modbus = new ModbusWotBindingExecutor();

            Assert.That(http.CanExecute(Compiled("w3c.http", "https")), Is.True);
            Assert.That(http.CanExecute(Compiled("w3c.modbus", "modbus+tcp")), Is.False);
            Assert.That(modbus.CanExecute(Compiled("w3c.modbus", "modbus+tcp")), Is.True);
            Assert.That(modbus.CanExecute(Compiled("w3c.http", "https")), Is.False);
        }

        [Test]
        public void ExecutorsIdentifyTheirPlannerBinding()
        {
            Assert.That(new HttpWotBindingExecutor().Identity.Id, Is.EqualTo(new HttpBindingPlanner().Identity.Id));
            Assert.That(new ModbusWotBindingExecutor().Identity.Id, Is.EqualTo(new ModbusBindingPlanner().Identity.Id));
        }

        [Test]
        public async Task HttpErrorStatusMapping()
        {
            (int Http, StatusCode Expected)[] cases =
            [
                (400, StatusCodes.BadInvalidArgument),
                (401, StatusCodes.BadUserAccessDenied),
                (404, StatusCodes.BadNodeIdUnknown),
                (500, StatusCodes.BadInternalError)
            ];

            foreach ((int http, StatusCode expected) in cases)
            {
                using var server = new TestHttpServer((method, path, body) =>
                    new TestHttpResponse(http, "application/json", Encoding.UTF8.GetBytes("\"x\"")));

                var registry = new WotProtocolBinderRegistry(
                    [new HttpBindingPlanner()],
                    [new HttpWotBindingExecutor()],
                    endpointPolicy: AllowLoopbackPolicy());
                string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                    "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{\"href\":\"" +
                    server.BaseUrl +
                    "/p\"}]}}}";
                WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                    "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
                WotCompiledForm read = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

                IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                    Assert.That(result.Status, Is.EqualTo(expected), $"HTTP {http} mapping.");
                }
            }
        }

        [Test]
        public async Task HttpChannelRejectsInvalidContentTypeAtSink()
        {
            int requests = 0;
            using var server = new TestHttpServer(request =>
            {
                requests++;
                return TestHttpResponse.Json(200, "\"ok\"");
            });
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor()],
                endpointPolicy: AllowLoopbackPolicy());
            var form = new WotCompiledForm(
                new HttpBindingPlanner().Identity,
                WotAffordanceKind.Property,
                "p",
                "/properties/p/forms/0",
                WoTBindingCapabilityEnum.WriteProperty,
                "writeproperty",
                new WotEndpointDescriptor("http", "127.0.0.1", server.Port, server.BaseUrl),
                new WotAddressingDescriptor(server.BaseUrl + "/p"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.WriteProperty, "writeproperty", "PUT"),
                new WotPayloadDescriptor("application/json\r\nX-Injected: pwned", "json"),
                [],
                isExecutable: true);

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);
            WotWriteResult result = await channel.WriteAsync(new DataValue(new Variant(42L))).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(requests, Is.Zero);
        }

        [Test]
        public async Task HttpChannelRejectsInvalidDefaultHeader()
        {
            using var server = new TestHttpServer(_ => TestHttpResponse.Json(200, "1"));
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor(new HttpWotBindingOptions
                {
                    DefaultHeaders = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["X-Test"] = "ok\r\nX-Injected: pwned"
                    }
                })],
                endpointPolicy: AllowLoopbackPolicy());
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{\"href\":\"" +
                server.BaseUrl +
                "/p\"}]}}}";
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task HttpChannelRejectsInvalidCredentialHeader()
        {
            using var server = new TestHttpServer(_ => TestHttpResponse.Json(200, "1"));
            var credential = new WotCredential(
                WotSecurityScheme.Bearer,
                ImmutableDictionary<string, string>.Empty.Add("Authorization", "Bearer token\r\nMetadata: true"));
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor()],
                credentials: new StaticCredentialProvider(credential),
                endpointPolicy: AllowLoopbackPolicy());
            var security = ImmutableArray.Create(new WotCredentialReference(
                "bearer_sc",
                WotSecurityScheme.Bearer,
                HttpBindingPlanner.BindingUri,
                server.BaseUrl));
            var form = new WotCompiledForm(
                new HttpBindingPlanner().Identity,
                WotAffordanceKind.Property,
                "p",
                "/properties/p/forms/0",
                WoTBindingCapabilityEnum.ReadProperty,
                "readproperty",
                new WotEndpointDescriptor("http", "127.0.0.1", server.Port, server.BaseUrl),
                new WotAddressingDescriptor(server.BaseUrl + "/p"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "GET"),
                new WotPayloadDescriptor("application/json", "json"),
                security,
                isExecutable: true);

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);
            WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void ModbusWriteMultipleRegistersRejectsNullValues()
        {
            using var client = new ModbusTcpClient("127.0.0.1", 502, System.TimeSpan.FromSeconds(1));

            Assert.ThrowsAsync<System.ArgumentNullException>(
                async () => await client.WriteMultipleRegistersAsync(
                    1, 0, null!, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public void ModbusWriteMultipleRegistersRejectsOutOfRangeValues()
        {
            using var client = new ModbusTcpClient("127.0.0.1", 502, System.TimeSpan.FromSeconds(1));
            ushort[] values = new ushort[ModbusProtocolLimits.MaxWriteRegisters + 1];

            Assert.ThrowsAsync<System.ArgumentOutOfRangeException>(
                async () => await client.WriteMultipleRegistersAsync(
                    1, 0, values, CancellationToken.None).ConfigureAwait(false));
        }

        private sealed class StaticCredentialProvider : IWotCredentialProvider
        {
            public StaticCredentialProvider(WotCredential credential)
            {
                m_credential = credential;
            }

            public ValueTask<WotCredential?> ResolveAsync(
                WotCredentialReference reference, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotCredential?>(m_credential);
            }

            private readonly WotCredential m_credential;
        }
    }
}
