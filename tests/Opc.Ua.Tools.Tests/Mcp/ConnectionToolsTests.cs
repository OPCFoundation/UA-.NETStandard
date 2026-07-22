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
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class ConnectionToolsTests
    {
        private const string kConnectSessionName = "connection-tools-connect";
        private const string kDisconnectSessionName = "connection-tools-disconnect";
        private const string kUnknownSessionName = "connection-tools-unknown";

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
