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
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class AttributeServiceToolsTests
    {
        [Test]
        public async Task ReadAsyncWithCurrentTimeNodeReturnsDataValueJsonAsync()
        {
            string nodeId = VariableIds.Server_ServerStatus_CurrentTime.ToString(null, CultureInfo.InvariantCulture);
            string[] nodeIds = [nodeId];

            string json = await AttributeServiceTools.ReadAsync(
                McpTestEnvironment.SessionManager,
                nodeIds,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement results = GetRequiredProperty(root, "results");
            JsonElement firstResult = results[0];
            JsonElement result = GetRequiredProperty(firstResult, "result");

            Assert.That(root.TryGetProperty("error", out _), Is.False);
            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(results.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(results.GetArrayLength(), Is.EqualTo(1));
            Assert.That(GetRequiredProperty(firstResult, "nodeId").GetString(), Is.EqualTo(nodeId));
            Assert.That(GetRequiredProperty(result, "statusCode").GetString(), Is.EqualTo("Good"));
            Assert.That(GetRequiredProperty(result, "value").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(
                GetRequiredProperty(result, "value").GetString(),
                Is.Not.Null.And.Not.EqualTo(string.Empty));
        }

        [Test]
        public async Task WriteAsyncWithReadOnlyNodeReturnsPerNodeStatusAsync()
        {
            string nodeId = VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints
                .ToString(null, CultureInfo.InvariantCulture);
            string[] nodeIds = [nodeId];
            string[] values = ["1"];

            string json = await AttributeServiceTools.WriteAsync(
                McpTestEnvironment.SessionManager,
                nodeIds,
                values,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement results = GetRequiredProperty(root, "results");
            JsonElement firstResult = results[0];

            Assert.That(root.TryGetProperty("error", out _), Is.False);
            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(results.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(results.GetArrayLength(), Is.EqualTo(1));
            Assert.That(firstResult.ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(firstResult.GetString(), Does.StartWith("Bad"));
        }

        [Test]
        public async Task HistoryReadAsyncWithNonHistorizedNodeReturnsCharacterizedJsonAsync()
        {
            string nodeId = VariableIds.Server_ServerStatus_CurrentTime.ToString(null, CultureInfo.InvariantCulture);
            string[] nodeIds = [nodeId];
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddMinutes(-5);

            string json = await AttributeServiceTools.HistoryReadAsync(
                McpTestEnvironment.SessionManager,
                nodeIds,
                startTime.ToString("o", CultureInfo.InvariantCulture),
                endTime.ToString("o", CultureInfo.InvariantCulture),
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.GetBoolean(), Is.True);
                AssertErrorPayload(root);
                return;
            }

            JsonElement results = GetRequiredProperty(root, "results");
            JsonElement firstResult = results[0];

            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(results.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(results.GetArrayLength(), Is.EqualTo(1));
            Assert.That(GetRequiredProperty(firstResult, "nodeId").GetString(), Is.EqualTo(nodeId));
            Assert.That(
                GetRequiredProperty(firstResult, "statusCode").GetString(),
                Does.StartWith("Bad"));
        }

        [TestCase("Insert")]
        [TestCase("Update")]
        public async Task HistoryUpdateAsyncWithNonHistorizedNodeReturnsCharacterizedJsonAsync(string updateType)
        {
            string nodeId = VariableIds.Server_ServerStatus_CurrentTime.ToString(null, CultureInfo.InvariantCulture);
            DateTime timestamp = DateTime.UtcNow;
            string[] timestamps = [timestamp.ToString("o", CultureInfo.InvariantCulture)];
            string[] values = ["123"];

            string json = await AttributeServiceTools.HistoryUpdateAsync(
                McpTestEnvironment.SessionManager,
                nodeId,
                timestamps,
                values,
                updateType,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.GetBoolean(), Is.True);
                AssertErrorPayload(root);
                return;
            }

            JsonElement results = GetRequiredProperty(root, "results");
            JsonElement firstResult = results[0];

            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(results.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(results.GetArrayLength(), Is.EqualTo(1));
            Assert.That(
                GetRequiredProperty(firstResult, "statusCode").GetString(),
                Does.StartWith("Bad"));
            Assert.That(GetRequiredProperty(firstResult, "operationResults").ValueKind, Is.EqualTo(JsonValueKind.Array));
        }

        private static void AssertErrorPayload(JsonElement root)
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
