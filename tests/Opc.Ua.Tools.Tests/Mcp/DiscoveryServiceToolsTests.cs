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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class DiscoveryServiceToolsTests
    {
        [Test]
        public async Task FindServersAsyncWithReferenceServerDiscoveryUrlReturnsServerListAsync()
        {
            string discoveryUrl = McpTestEnvironment.ServerUrl;
            int expectedPort = new Uri(discoveryUrl).Port;

            string json = await DiscoveryServiceTools.FindServersAsync(
                McpTestEnvironment.SessionManager,
                discoveryUrl,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement servers = GetRequiredProperty(root, "servers");

            Assert.That(root.TryGetProperty("error", out _), Is.False);
            Assert.That(servers.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(servers.GetArrayLength(), Is.GreaterThan(0));
            Assert.That(
                servers.EnumerateArray().Any(server =>
                    GetRequiredProperty(server, "applicationType").GetString() != null &&
                    GetRequiredProperty(server, "applicationName").GetString() != null &&
                    GetRequiredProperty(server, "discoveryUrls").EnumerateArray().Any(url =>
                        url.GetString() != null &&
                        Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? discoveredUri) &&
                        discoveredUri.Port == expectedPort)),
                Is.True);
        }

        [Test]
        public async Task FindServersOnNetworkAsyncWithReferenceServerReturnsCharacterizedJsonAsync()
        {
            string json = await DiscoveryServiceTools.FindServersOnNetworkAsync(
                McpTestEnvironment.SessionManager,
                McpTestEnvironment.ServerUrl,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.GetBoolean(), Is.True);
                AssertCharacterizedErrorPayload(root);
                return;
            }

            Assert.That(GetRequiredProperty(root, "servers").ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(GetRequiredProperty(root, "lastCounterResetTime").GetString(), Is.Not.Null.And.Not.EqualTo(string.Empty));
        }

        [Test]
        public async Task RegisterServerAsyncWithReferenceServerReturnsCharacterizedJsonAsync()
        {
            string applicationUri = "urn:localhost:opcua:mcp-register-server-tests";
            string serverName = "Mcp Register Server Test";
            string[] discoveryUrls = [McpTestEnvironment.ServerUrl];

            string json = await DiscoveryServiceTools.RegisterServerAsync(
                McpTestEnvironment.SessionManager,
                applicationUri,
                serverName,
                discoveryUrls,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.GetBoolean(), Is.True);
                AssertCharacterizedErrorPayload(root);
                return;
            }

            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
        }

        [Test]
        public async Task RegisterServer2AsyncWithReferenceServerReturnsCharacterizedJsonAsync()
        {
            string applicationUri = "urn:localhost:opcua:mcp-register-server2-tests";
            string serverName = "Mcp Register Server2 Test";
            string[] discoveryUrls = [McpTestEnvironment.ServerUrl];

            string json = await DiscoveryServiceTools.RegisterServer2Async(
                McpTestEnvironment.SessionManager,
                applicationUri,
                serverName,
                discoveryUrls,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.GetBoolean(), Is.True);
                AssertCharacterizedErrorPayload(root);
                return;
            }

            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(GetRequiredProperty(root, "configurationResults").ValueKind, Is.EqualTo(JsonValueKind.Array));
        }

        private static void AssertCharacterizedErrorPayload(JsonElement root)
        {
            Assert.That(GetRequiredProperty(root, "statusCode").GetString(), Does.StartWith("Bad"));
            Assert.That(GetRequiredProperty(root, "message").GetString(), Is.Not.Null.And.Not.EqualTo(string.Empty));
        }

        private static JsonElement GetRequiredProperty(JsonElement element, string propertyName)
        {
            Assert.That(element.TryGetProperty(propertyName, out JsonElement property), Is.True);
            return property;
        }
    }
}
#endif
