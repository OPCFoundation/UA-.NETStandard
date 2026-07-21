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
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Http;
using Opc.Ua.WotCon.Binding.Modbus;
using Opc.Ua.WotCon.Binding.Planners;
using Opc.Ua.WotCon.Binding.Tests.Support;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Binding.Tests
{
    /// <summary>Unit tests for executor identity, dispatch and HTTP error mapping.</summary>
    [TestFixture]
    public sealed class ExecutorUnitTests
    {
        private static WotCompiledForm Compiled(string bindingId, string scheme)
            => new WotCompiledForm(
                new WotBindingIdentity(bindingId, "1.0", "urn:x"),
                WotAffordanceKind.Property, "p", "/properties/p/forms/0",
                WoTBindingCapabilityEnum.ReadProperty, "readproperty",
                new WotEndpointDescriptor(scheme, "h", 1, scheme + "://h"),
                new WotAddressingDescriptor("t"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "GET"),
                new WotPayloadDescriptor("application/json", "json"),
                ImmutableArray<WotCredentialReference>.Empty, isExecutable: true);

        [Test]
        public void CanExecute_MatchesOwnBindingOnly()
        {
            var http = new HttpWotBindingExecutor();
            var modbus = new ModbusWotBindingExecutor();

            Assert.That(http.CanExecute(Compiled("w3c.http", "https")), Is.True);
            Assert.That(http.CanExecute(Compiled("w3c.modbus", "modbus+tcp")), Is.False);
            Assert.That(modbus.CanExecute(Compiled("w3c.modbus", "modbus+tcp")), Is.True);
            Assert.That(modbus.CanExecute(Compiled("w3c.http", "https")), Is.False);
        }

        [Test]
        public void Executors_IdentifyTheirPlannerBinding()
        {
            Assert.That(new HttpWotBindingExecutor().Identity.Id, Is.EqualTo(new HttpBindingPlanner().Identity.Id));
            Assert.That(new ModbusWotBindingExecutor().Identity.Id, Is.EqualTo(new ModbusBindingPlanner().Identity.Id));
        }

        [Test]
        public async Task Http_ErrorStatusMapping()
        {
            (int Http, StatusCode Expected)[] cases =
            {
                (400, StatusCodes.BadInvalidArgument),
                (401, StatusCodes.BadUserAccessDenied),
                (404, StatusCodes.BadNodeIdUnknown),
                (500, StatusCodes.BadInternalError)
            };

            foreach ((int http, StatusCode expected) in cases)
            {
                using var server = new TestHttpServer((method, path, body) =>
                    new TestHttpResponse(http, "application/json", Encoding.UTF8.GetBytes("\"x\"")));

                var registry = new WotProtocolBinderRegistry(
                    new IWotProtocolBinder[] { new HttpBindingPlanner() },
                    new IWotBindingExecutor[] { new HttpWotBindingExecutor(
                        new HttpWotBindingOptions { ClientFactory = () => new HttpClient() }) });
                string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                    "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{\"href\":\"" + server.BaseUrl + "/p\"}]}}}";
                WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                    "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
                WotCompiledForm read = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

                IWotBindingChannel channel = await registry.OpenChannelAsync(read);
                await using (channel.ConfigureAwait(false))
                {
                    WotReadResult result = await channel.ReadAsync();
                    Assert.That(result.Status, Is.EqualTo(expected), $"HTTP {http} mapping.");
                }
            }
        }
    }
}
