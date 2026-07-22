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
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class ConvenienceToolsTests
    {
        [Test]
        public async Task ReadValueAsyncWithCurrentTimeNodeReturnsValueJsonAsync()
        {
            string nodeId = VariableIds.Server_ServerStatus_CurrentTime.ToString(null, CultureInfo.InvariantCulture);

            string json = await ConvenienceTools.ReadValueAsync(
                McpTestEnvironment.SessionManager,
                nodeId,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.That(root.TryGetProperty("error", out _), Is.False);
            Assert.That(GetRequiredProperty(root, "nodeId").GetString(), Is.EqualTo(nodeId));
            Assert.That(GetRequiredProperty(root, "statusCode").GetString(), Is.EqualTo("Good"));
            Assert.That(GetRequiredProperty(root, "value").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(GetRequiredProperty(root, "value").GetString(), Is.Not.Null.And.Not.EqualTo(string.Empty));
        }

        [Test]
        public async Task ReadValuesAsyncWithCurrentTimeAndStateNodesReturnsArrayJsonAsync()
        {
            string currentTimeNodeId = VariableIds.Server_ServerStatus_CurrentTime.ToString(null, CultureInfo.InvariantCulture);
            string stateNodeId = VariableIds.Server_ServerStatus_State.ToString(null, CultureInfo.InvariantCulture);
            string[] nodeIds = [currentTimeNodeId, stateNodeId];

            string json = await ConvenienceTools.ReadValuesAsync(
                McpTestEnvironment.SessionManager,
                nodeIds,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(root.GetArrayLength(), Is.EqualTo(2));
            Assert.That(
                root.EnumerateArray().Select(element => GetRequiredProperty(element, "nodeId").GetString()),
                Is.EqualTo(nodeIds));
            Assert.That(
                root.EnumerateArray().All(element =>
                    GetRequiredProperty(element, "statusCode").GetString() == "Good" &&
                    GetRequiredProperty(element, "serviceResult").GetString() == "Good"),
                Is.True);
        }

        [Test]
        public async Task WriteValueAsyncWithReadOnlyNodeReturnsBadStatusAsync()
        {
            string nodeId = VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints
                .ToString(null, CultureInfo.InvariantCulture);

            string json = await ConvenienceTools.WriteValueAsync(
                McpTestEnvironment.SessionManager,
                nodeId,
                "1",
                "UInt16",
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.GetBoolean(), Is.True);
                AssertCharacterizedErrorPayload(root);
                return;
            }

            Assert.That(GetRequiredProperty(root, "nodeId").GetString(), Is.EqualTo(nodeId));
            Assert.That(GetRequiredProperty(root, "statusCode").GetString(), Does.StartWith("Bad"));
        }

        [Test]
        public async Task BrowseAllAsyncWithDefaultNodeReturnsServerReferenceAsync()
        {
            string objectsFolderNodeId = ObjectIds.ObjectsFolder.ToString(null, CultureInfo.InvariantCulture);
            string serverNodeId = ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture);

            string json = await ConvenienceTools.BrowseAllAsync(
                McpTestEnvironment.SessionManager,
                nodeId: null,
                maxDepth: 1,
                maxResults: 20,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement references = GetRequiredProperty(root, "references");

            Assert.That(root.TryGetProperty("error", out _), Is.False);
            Assert.That(GetRequiredProperty(root, "startingNode").GetString(), Is.EqualTo(objectsFolderNodeId));
            Assert.That(GetRequiredProperty(root, "totalReferences").GetInt32(), Is.GreaterThan(0));
            Assert.That(references.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(references.GetArrayLength(), Is.EqualTo(GetRequiredProperty(root, "totalReferences").GetInt32()));
            Assert.That(
                references.EnumerateArray().Any(reference =>
                    GetRequiredProperty(reference, "nodeId").GetString() == serverNodeId),
                Is.True);
        }

        [Test]
        public async Task CallMethodAsyncWithGetMonitoredItemsReturnsCharacterizedJsonAsync()
        {
            string objectId = ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture);
            string methodId = MethodIds.Server_GetMonitoredItems.ToString(null, CultureInfo.InvariantCulture);
            string[] inputArguments = ["0"];

            string json = await ConvenienceTools.CallMethodAsync(
                McpTestEnvironment.SessionManager,
                objectId,
                methodId,
                inputArguments,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.GetBoolean(), Is.True);
                AssertCharacterizedErrorPayload(root);

                if (root.TryGetProperty("inputArgumentResults", out JsonElement inputArgumentResults))
                {
                    Assert.That(inputArgumentResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                }

                return;
            }

            Assert.That(GetRequiredProperty(root, "objectId").GetString(), Is.EqualTo(objectId));
            Assert.That(GetRequiredProperty(root, "methodId").GetString(), Is.EqualTo(methodId));
            Assert.That(GetRequiredProperty(root, "outputArguments").ValueKind, Is.EqualTo(JsonValueKind.Array));
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
