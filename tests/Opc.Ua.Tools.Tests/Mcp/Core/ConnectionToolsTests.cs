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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class ConnectionToolsTests
    {
        private const string kConnectSessionName = "connection-tools-connect";
        private const string kDisconnectSessionName = "connection-tools-disconnect";
        private const string kUnknownSessionName = "connection-tools-unknown";

        /// <summary>
        /// Connection schemas stay bounded without removing the established anonymous and username arguments.
        /// </summary>
        [Test]
        public void ConnectionCataloguePreservesLegacyArgumentsWithinTheGradingLimit()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddMcpServer().WithOpcUaConnectionTools();
            using ServiceProvider provider = services.BuildServiceProvider();
            McpServerTool[] tools = provider.GetServices<McpServerTool>().ToArray();

            Assert.That(tools.Select(tool => tool.ProtocolTool.Name), Is.Unique);
            foreach (McpServerTool tool in tools)
            {
                JsonElement properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
                Assert.That(properties.EnumerateObject().ToArray(), Has.Length.LessThanOrEqualTo(8),
                    tool.ProtocolTool.Name);
            }
            JsonElement connect = tools.Single(tool => tool.ProtocolTool.Name == "Connect").ProtocolTool.InputSchema;
            string[] legacyArguments =
                ["endpointUrl", "securityMode", "securityPolicy", "authType", "username", "password",
                    "autoAcceptCerts", "name"];
            string[] required = ["endpointUrl"];
            Assert.That(connect.GetProperty("properties").EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(legacyArguments));
            Assert.That(connect.GetProperty("required").EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(required));
        }

        /// <summary>
        /// Certificate login advertises only an approved identity reference and keeps trust bypass out of its schema.
        /// </summary>
        [Test]
        public void CertificateConnectionCatalogueUsesARequiredHostIdentity()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddMcpServer().WithOpcUaConnectionTools();
            using ServiceProvider provider = services.BuildServiceProvider();
            JsonElement schema = provider.GetServices<McpServerTool>()
                .Single(tool => tool.ProtocolTool.Name == "ConnectCertificate").ProtocolTool.InputSchema;
            string[] arguments = ["endpointUrl", "certificateIdentity", "securityMode", "securityPolicy", "name"];
            string[] required = ["endpointUrl", "certificateIdentity"];

            Assert.That(schema.GetProperty("properties").EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(arguments));
            Assert.That(schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(required));
        }

        /// <summary>
        /// The certificate tool resolves only configured identities and never falls back to anonymous access.
        /// </summary>
        [TestCase("")]
        [TestCase("not-configured")]
        public async Task CertificateConnectionRejectsUnconfiguredIdentityAsync(string identity)
        {
            const string sessionName = "connection-tools-certificate";
            string json = await ConnectionTools.ConnectCertificateAsync(
                McpTestEnvironment.SessionManager,
                McpTestEnvironment.ServerUrl,
                identity,
                name: sessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(),
                Is.EqualTo("BadIdentityTokenRejected"));
            Assert.That(ConnectionTools.GetConnectionStatus(McpTestEnvironment.SessionManager, sessionName),
                Is.EqualTo($"Session '{sessionName}' not found."));
        }

        [Test]
        public async Task GetEndpointsAsyncWithValidServerUrlReturnsEndpointsJsonAsync()
        {
            string json = await ConnectionTools.GetEndpointsAsync(
                McpTestEnvironment.SessionManager,
                McpTestEnvironment.ServerUrl).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);

            Assert.That(
                document.RootElement.TryGetProperty("endpoints", out JsonElement endpoints),
                Is.True);
            Assert.That(endpoints.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(endpoints.GetArrayLength(), Is.GreaterThan(0));
        }

        [Test]
        public async Task GetEndpointsAsyncWithInvalidUrlReturnsUnexpectedErrorJsonAsync()
        {
            string json = await ConnectionTools.GetEndpointsAsync(
                McpTestEnvironment.SessionManager,
                "not-a-valid-uri").ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.That(root.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(
                root.GetProperty("statusCode").GetString(),
                Is.EqualTo("BadUnexpectedError"));
            Assert.That(root.GetProperty("message").GetString(), Is.Not.Empty);
        }

        [Test]
        public async Task ConnectAsyncWithValidEndpointReturnsSessionInfoAsync()
        {
            try
            {
                string result = await ConnectionTools.ConnectAsync(
                    McpTestEnvironment.SessionManager,
                    McpTestEnvironment.ServerUrl,
                    authType: "Anonymous",
                    autoAcceptCerts: true,
                    name: kConnectSessionName).ConfigureAwait(false);

                string status = ConnectionTools.GetConnectionStatus(
                    McpTestEnvironment.SessionManager,
                    kConnectSessionName);

                Assert.That(result, Does.Contain("Connected to"));
                Assert.That(result, Does.Contain(kConnectSessionName));
                Assert.That(status, Does.Contain(kConnectSessionName));
                Assert.That(status, Does.Contain("Connected to"));
            }
            finally
            {
                await DisconnectIfPresentAsync(kConnectSessionName).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConnectAsyncWithInvalidUrlReturnsExceptionDetailsAsync()
        {
            string json = await ConnectionTools.ConnectAsync(
                McpTestEnvironment.SessionManager,
                "not-a-valid-uri",
                name: "invalid-uri").ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.That(root.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(
                root.GetProperty("statusCode").GetString(),
                Is.EqualTo("BadUnexpectedError"));
            Assert.That(root.GetProperty("exceptionType").GetString(), Is.Not.Empty);
        }

        [Test]
        public async Task DisconnectAsyncWithConnectedSessionReturnsDisconnectedMessageAsync()
        {
            try
            {
                await ConnectionTools.ConnectAsync(
                    McpTestEnvironment.SessionManager,
                    McpTestEnvironment.ServerUrl,
                    authType: "Anonymous",
                    autoAcceptCerts: true,
                    name: kDisconnectSessionName).ConfigureAwait(false);

                string result = await ConnectionTools.DisconnectAsync(
                    McpTestEnvironment.SessionManager,
                    kDisconnectSessionName).ConfigureAwait(false);
                string status = ConnectionTools.GetConnectionStatus(
                    McpTestEnvironment.SessionManager,
                    kDisconnectSessionName);

                Assert.That(result, Is.EqualTo($"Disconnected session '{kDisconnectSessionName}'."));
                Assert.That(status, Is.EqualTo($"Session '{kDisconnectSessionName}' not found."));
            }
            finally
            {
                await DisconnectIfPresentAsync(kDisconnectSessionName).ConfigureAwait(false);
            }
        }

        [Test]
        public void GetConnectionStatusWithSharedSessionReturnsConnectedStatus()
        {
            string status = ConnectionTools.GetConnectionStatus(
                McpTestEnvironment.SessionManager,
                McpTestEnvironment.SessionName);

            Assert.That(status, Does.Contain(McpTestEnvironment.SessionName));
            Assert.That(status, Does.Contain("Connected to"));
            Assert.That(status, Does.Contain(McpTestEnvironment.ServerUrl));
        }

        [Test]
        public void GetConnectionStatusWithUnknownSessionReturnsNotFoundMessage()
        {
            string status = ConnectionTools.GetConnectionStatus(
                McpTestEnvironment.SessionManager,
                kUnknownSessionName);

            Assert.That(status, Is.EqualTo($"Session '{kUnknownSessionName}' not found."));
        }

        private static async Task DisconnectIfPresentAsync(string sessionName)
        {
            _ = await ConnectionTools.DisconnectAsync(
                McpTestEnvironment.SessionManager,
                sessionName).ConfigureAwait(false);
        }
    }
}
#endif
