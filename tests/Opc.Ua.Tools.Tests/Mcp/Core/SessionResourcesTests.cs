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
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class SessionResourcesTests
    {
        private const string kUnknownSessionName = "missing-session";

        [Test]
        public void ListSessionsReturnsSharedSessionSummary()
        {
            string json = SessionResources.ListSessions(McpTestEnvironment.SessionManager);

            using JsonDocument document = JsonDocument.Parse(json);

            Assert.That(
                document.RootElement.TryGetProperty("sessionCount", out JsonElement sessionCount),
                Is.True);
            Assert.That(
                document.RootElement.TryGetProperty("sessions", out JsonElement sessions),
                Is.True);
            Assert.That(sessionCount.GetInt32(), Is.GreaterThanOrEqualTo(1));
            Assert.That(sessions.ValueKind, Is.EqualTo(JsonValueKind.Array));

            JsonElement sharedSession = sessions.EnumerateArray()
                .FirstOrDefault(element =>
                    element.TryGetProperty("name", out JsonElement name) &&
                    name.GetString() == McpTestEnvironment.SessionName);

            Assert.That(sharedSession.ValueKind, Is.Not.EqualTo(JsonValueKind.Undefined));
            Assert.That(
                sharedSession.GetProperty("endpointUrl").GetString(),
                Does.StartWith(McpTestEnvironment.ServerUrl));
            Assert.That(sharedSession.GetProperty("authType").GetString(), Is.EqualTo("Anonymous"));
            Assert.That(sharedSession.GetProperty("isConnected").GetBoolean(), Is.True);
        }

        [Test]
        public void GetSessionWithValidNameReturnsDetailedSessionJson()
        {
            string json = SessionResources.GetSession(
                McpTestEnvironment.SessionManager,
                McpTestEnvironment.SessionName);

            using JsonDocument document = JsonDocument.Parse(json);

            Assert.That(document.RootElement.GetProperty("name").GetString(), Is.EqualTo(McpTestEnvironment.SessionName));
            Assert.That(
                document.RootElement.GetProperty("endpointUrl").GetString(),
                Does.StartWith(McpTestEnvironment.ServerUrl));
            Assert.That(document.RootElement.GetProperty("isConnected").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("sessionId").GetString(), Is.Not.Null.And.Not.EqualTo(string.Empty));
            Assert.That(document.RootElement.GetProperty("namespaces").GetArrayLength(), Is.GreaterThan(0));
        }

        [Test]
        public void GetSessionWithUnknownNameReturnsErrorJson()
        {
            string json = SessionResources.GetSession(
                McpTestEnvironment.SessionManager,
                kUnknownSessionName);

            using JsonDocument document = JsonDocument.Parse(json);

            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("message").GetString(), Does.Contain("not found"));
        }

        [Test]
        public void GetNamespacesWithValidNameReturnsNamespaceArray()
        {
            string json = SessionResources.GetNamespaces(
                McpTestEnvironment.SessionManager,
                McpTestEnvironment.SessionName);

            using JsonDocument document = JsonDocument.Parse(json);

            Assert.That(document.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(document.RootElement.GetArrayLength(), Is.GreaterThan(0));
            Assert.That(
                document.RootElement.EnumerateArray().Any(element =>
                    element.GetProperty("index").GetInt32() == 0 &&
                    element.GetProperty("uri").GetString() != null),
                Is.True);
        }

        [Test]
        public void GetNamespacesWithUnknownNameReturnsErrorJson()
        {
            string json = SessionResources.GetNamespaces(
                McpTestEnvironment.SessionManager,
                kUnknownSessionName);

            using JsonDocument document = JsonDocument.Parse(json);

            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("message").GetString(), Does.Contain("not found"));
        }
    }
}
#endif
