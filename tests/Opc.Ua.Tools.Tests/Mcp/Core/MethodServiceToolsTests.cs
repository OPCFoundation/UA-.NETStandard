// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tools.Tests.McpAuthorization;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class MethodServiceToolsTests
    {
        private const string kUnknownSessionName = "definitely-not-connected";

        /// <summary>
        /// Both public call tools reject malformed variant envelopes before any method reaches the transport.
        /// </summary>
        [TestCase(false, "[{\"Value\":42}]")]
        [TestCase(true, "[{\"Value\":42}]")]
        [TestCase(false, "[{\"UaTyp\":6,\"Value\":42}]")]
        [TestCase(true, "[{\"UaTyp\":6,\"Value\":42}]")]
        [TestCase(false, "[{\"uaType\":6,\"Value\":42}]")]
        [TestCase(true, "[{\"uaType\":6,\"Value\":42}]")]
        [TestCase(false, "[{\"UaType\":null,\"Value\":42}]")]
        [TestCase(true, "[{\"UaType\":null,\"Value\":42}]")]
        [TestCase(false, "[{\"UaType\":24,\"Value\":[{\"Value\":42}]}]")]
        [TestCase(true, "[{\"UaType\":24,\"Value\":[{\"Value\":42}]}]")]
        [TestCase(false, "[{\"UaType\":23,\"Value\":{\"Value\":42}}]")]
        [TestCase(true, "[{\"UaType\":23,\"Value\":{\"Value\":42}}]")]
        public async Task CallToolsRejectMissingTypesBeforeDispatchAsync(bool convenience, string arguments)
        {
            await using McpGenericGenerationTestContext fixture =
                await McpGenericGenerationTestContext.CreateAsync().ConfigureAwait(false);

            string json = convenience
                ? await ConvenienceTools.CallMethodAsync(
                    fixture.Manager, "ns=1;i=100", "ns=1;i=101",
                    inputArgumentsJson: arguments).ConfigureAwait(false)
                : await MethodServiceTools.CallAsync(
                    fixture.Manager, "ns=1;i=100", "ns=1;i=101",
                    inputArgumentsJson: arguments).ConfigureAwait(false);

            Assert.That(fixture.Mutations, Is.Empty);
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo("BadDecodingError"));
        }

        [Test]
        public async Task CallAsyncWithValidSessionReturnsStatusJsonAsync()
        {
            string objectId = ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture);
            string methodId = MethodIds.Server_GetMonitoredItems.ToString(null, CultureInfo.InvariantCulture);
            string[] inputArguments = ["0"];

            string json = await MethodServiceTools.CallAsync(
                McpTestEnvironment.SessionManager,
                objectId,
                methodId,
                inputArguments,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);

            Assert.That(json, Is.Not.EqualTo(string.Empty));
            Assert.That(
                document.RootElement.TryGetProperty("statusCode", out JsonElement statusCode),
                Is.True);
            Assert.That(statusCode.GetString(), Is.Not.Null.And.Not.EqualTo(string.Empty));

            if (document.RootElement.TryGetProperty("responseHeader", out JsonElement responseHeader))
            {
                Assert.That(responseHeader.ValueKind, Is.EqualTo(JsonValueKind.Object));
            }
            else
            {
                Assert.That(
                    document.RootElement.TryGetProperty("error", out JsonElement error),
                    Is.True);
                Assert.That(error.GetBoolean(), Is.True);
            }
        }

        [Test]
        public void CallAsyncWithUnknownSessionThrowsInvalidOperationException()
        {
            string objectId = ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture);
            string methodId = MethodIds.Server_GetMonitoredItems.ToString(null, CultureInfo.InvariantCulture);
            string[] inputArguments = ["0"];

            Assert.That(
                () => MethodServiceTools.CallAsync(
                    McpTestEnvironment.SessionManager,
                    objectId,
                    methodId,
                    inputArguments,
                    kUnknownSessionName),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contain("not found or not connected"));
        }
    }
}
#endif
