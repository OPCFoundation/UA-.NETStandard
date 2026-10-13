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
using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Proves authorization precedes document processing, executor activation and actual socket connection.
    /// </summary>
    [TestFixture]
    public sealed class WotRuntimePolicyTests
    {
        [Test]
        public async Task DisabledHostDeniesBeforePreparationAsync()
        {
            await using var fixture = new WotRuntimeTestContext(options => options.EnabledCapabilities = []);
            using IDisposable caller = fixture.Enter();
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("disabled",
                new WotDocumentSource { Text = "not JSON" }, WotRuntimeTestContext.Selection(),
                ["readproperty"]).ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            fixture.Dns.VerifyNoOtherCalls();
        }

        [TestCase("http://unapproved.example/value")]
        [TestCase("http://device.example:81/value")]
        [TestCase("https://device.example/value")]
        [TestCase("http://device.example/private")]
        [TestCase("http://device.example/value?token=not-a-reference")]
        [TestCase("http://user:password@device.example/value")]
        [TestCase("http://device.example/value#fragment")]
        public async Task UnauthorizedTargetsNeverActivateAsync(string address)
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string document = WotRuntimeTestContext.PropertyDocument.Replace(
                "http://device.example/value", address, StringComparison.Ordinal);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("denied",
                new WotDocumentSource { Text = document }, WotRuntimeTestContext.Selection(document),
                ["readproperty"]).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            Assert.That(fixture.Registry.List(), Is.Empty);
            fixture.Dns.VerifyNoOtherCalls();
        }

        [Test]
        public async Task WriteNeedsAnExactCallerCapabilityBeforeHandleLookupAsync()
        {
            await using var fixture = new WotRuntimeTestContext(options =>
                options.CallerGrants[0].Capabilities = [McpCapability.WotRuntime]);
            using IDisposable caller = fixture.Enter();
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.WritePropertyAsync("not-a-handle",
                WotRuntimeTestContext.Value(BuiltInType.Int32, "12")).ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(fixture.Factory.Writes, Is.Empty);
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
        }

        [Test]
        public async Task RequestedWriteCannotReuseAReadOnlyResourceGrantAsync()
        {
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
                [WotRuntimeTestContext.Rule(McpCapability.WotRuntime, ["GET"], ["/value"])]);
            using IDisposable caller = fixture.Enter();
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("write",
                new WotDocumentSource { Text = WotRuntimeTestContext.PropertyDocument },
                WotRuntimeTestContext.Selection(), ["writeproperty"]).ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
        }

        [Test]
        public async Task DnsAddressDenialOccursBeforeTheSocketConnectorAsync()
        {
            var connector = new Mock<IMcpTcpConnector>(MockBehavior.Strict);
            await using var fixture = new WotRuntimeTestContext(realExecutors: true,
                register: services => services.AddSingleton(connector.Object));
            fixture.Dns.Setup(value => value.ResolveAsync("device.example", It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<ArrayOf<IPAddress>>(
                    Task.FromResult<ArrayOf<IPAddress>>([IPAddress.Parse("203.0.113.9")])));
            using IDisposable caller = fixture.Enter();
            string handle = await fixture.OpenAsync().ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false), error: true);

            fixture.Dns.Verify(value => value.ResolveAsync("device.example", It.IsAny<CancellationToken>()),
                Times.Once);
            connector.VerifyNoOtherCalls();
        }

        [Test]
        public async Task HttpReadCannotAuthorizePostThenSilentlySendGetAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string document = WotRuntimeTestContext.PropertyDocument.Replace(
                "\"href\": \"http://device.example/value\"",
                "\"htv:methodName\": \"POST\", \"href\": \"http://device.example/value\"", StringComparison.Ordinal);
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("method",
                new WotDocumentSource { Text = document }, WotRuntimeTestContext.Selection(document),
                ["readproperty"]).ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadNotSupported.Code));
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
        }
    }
}
#endif
