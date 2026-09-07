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
    public sealed class ViewServiceToolsTests
    {
        [Test]
        public async Task BrowseAsyncWithObjectsFolderReturnsServerReferenceAsync()
        {
            string nodeId = ObjectIds.ObjectsFolder.ToString(null, CultureInfo.InvariantCulture);
            string serverNodeId = ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture);

            string json = await ViewServiceTools.BrowseAsync(
                McpTestEnvironment.SessionManager,
                nodeId,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement references = GetRequiredProperty(root, "references");

            Assert.That(root.TryGetProperty("error", out _), Is.False);
            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(GetRequiredProperty(root, "statusCode").GetString(), Is.EqualTo("Good"));
            Assert.That(references.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(references.GetArrayLength(), Is.GreaterThan(0));
            Assert.That(
                references.EnumerateArray().Any(reference =>
                    GetRequiredProperty(reference, "nodeId").GetString() == serverNodeId),
                Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BrowseNextAsyncWithEmptyContinuationPointReturnsCharacterizedJsonAsync(
            bool releaseContinuationPoint)
        {
            string json = await ViewServiceTools.BrowseNextAsync(
                McpTestEnvironment.SessionManager,
                string.Empty,
                releaseContinuationPoint,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.GetBoolean(), Is.True);
                AssertErrorPayload(root);
                return;
            }

            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(GetRequiredProperty(root, "statusCode").GetString(), Is.Not.Null.And.Not.EqualTo(string.Empty));
            Assert.That(GetRequiredProperty(root, "references").ValueKind, Is.EqualTo(JsonValueKind.Array));
        }

        [Test]
        public async Task TranslateBrowsePathsAsyncWithServerPathReturnsServerTargetAsync()
        {
            string startingNodeId = ObjectIds.ObjectsFolder.ToString(null, CultureInfo.InvariantCulture);
            string expectedTargetId = ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture);
            string[] browsePath = ["0:Server"];

            string json = await ViewServiceTools.TranslateBrowsePathsAsync(
                McpTestEnvironment.SessionManager,
                startingNodeId,
                browsePath,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement targets = GetRequiredProperty(root, "targets");

            Assert.That(root.TryGetProperty("error", out _), Is.False);
            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(GetRequiredProperty(root, "statusCode").GetString(), Is.EqualTo("Good"));
            Assert.That(targets.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(targets.GetArrayLength(), Is.GreaterThan(0));
            Assert.That(
                targets.EnumerateArray().Any(target =>
                    GetRequiredProperty(target, "targetId").GetString() == expectedTargetId),
                Is.True);
        }

        [Test]
        public async Task RegisterNodesAndUnregisterNodesAsyncWithServerNodeRoundTripsAsync()
        {
            string nodeId = ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture);
            string[] nodeIds = [nodeId];

            string registerJson = await ViewServiceTools.RegisterNodesAsync(
                McpTestEnvironment.SessionManager,
                nodeIds,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument registerDocument = JsonDocument.Parse(registerJson);
            JsonElement registerRoot = registerDocument.RootElement;
            JsonElement registeredNodeIdsElement = GetRequiredProperty(registerRoot, "registeredNodeIds");
            string[] registeredNodeIds = [.. registeredNodeIdsElement.EnumerateArray().Select(element => element.GetString()!)];

            Assert.That(registerRoot.TryGetProperty("error", out _), Is.False);
            Assert.That(GetRequiredProperty(registerRoot, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(registeredNodeIdsElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(registeredNodeIds, Is.Not.Empty);

            string unregisterJson = await ViewServiceTools.UnregisterNodesAsync(
                McpTestEnvironment.SessionManager,
                registeredNodeIds,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument unregisterDocument = JsonDocument.Parse(unregisterJson);
            JsonElement unregisterRoot = unregisterDocument.RootElement;

            Assert.That(unregisterRoot.TryGetProperty("error", out _), Is.False);
            Assert.That(GetRequiredProperty(unregisterRoot, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
        }

        [Test]
        public async Task QueryFirstAsyncWithBaseObjectTypeReturnsCharacterizedJsonAsync()
        {
            string typeDefinitionId = ObjectTypeIds.BaseObjectType.ToString(null, CultureInfo.InvariantCulture);

            string json = await ViewServiceTools.QueryFirstAsync(
                McpTestEnvironment.SessionManager,
                typeDefinitionId,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.GetBoolean(), Is.True);
                AssertErrorPayload(root);
                return;
            }

            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(GetRequiredProperty(root, "queryDataSets").ValueKind, Is.EqualTo(JsonValueKind.Array));
        }

        [Test]
        public async Task QueryNextAsyncWithEmptyContinuationPointReturnsCharacterizedJsonAsync()
        {
            string json = await ViewServiceTools.QueryNextAsync(
                McpTestEnvironment.SessionManager,
                string.Empty,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                Assert.That(error.GetBoolean(), Is.True);
                AssertErrorPayload(root);
                return;
            }

            Assert.That(GetRequiredProperty(root, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(GetRequiredProperty(root, "queryDataSets").ValueKind, Is.EqualTo(JsonValueKind.Array));
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
