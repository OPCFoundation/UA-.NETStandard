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
    public sealed class NodeManagementServiceToolsTests
    {
        [Test]
        public async Task AddNodesAndDeleteNodesReturnCharacterizedJsonAsync()
        {
            string parentNodeId = ObjectIds.ObjectsFolder.ToString(null, CultureInfo.InvariantCulture);
            string referenceTypeId = ReferenceTypeIds.Organizes.ToString(null, CultureInfo.InvariantCulture);
            string typeDefinition = ObjectTypeIds.BaseObjectType.ToString(null, CultureInfo.InvariantCulture);
            string browseName = "1:McpTestNode" + Guid.NewGuid().ToString("N");
            string fallbackNodeId = "ns=1;s=McpMissingDeleteNode" + Guid.NewGuid().ToString("N");
            string? createdNodeId = null;

            string addJson = await NodeManagementServiceTools.AddNodesAsync(
                McpTestEnvironment.SessionManager,
                parentNodeId,
                referenceTypeId,
                browseName,
                "Object",
                typeDefinition,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument addDocument = JsonDocument.Parse(addJson);
            JsonElement addRoot = addDocument.RootElement;

            if (addRoot.TryGetProperty("error", out JsonElement addError))
            {
                Assert.That(addError.GetBoolean(), Is.True);
                AssertCharacterizedErrorPayload(addRoot);
            }
            else
            {
                JsonElement addResults = GetRequiredProperty(addRoot, "results");
                JsonElement firstAddResult = addResults[0];
                string? addStatusCode = GetRequiredProperty(firstAddResult, "statusCode").GetString();

                Assert.That(GetRequiredProperty(addRoot, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
                Assert.That(addResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(addResults.GetArrayLength(), Is.EqualTo(1));
                Assert.That(addStatusCode, Is.Not.Null.And.Not.EqualTo(string.Empty));
                Assert.That(GetRequiredProperty(firstAddResult, "addedNodeId").GetString(), Is.Not.Null);

                if (string.Equals(addStatusCode, "Good", StringComparison.Ordinal))
                {
                    createdNodeId = GetRequiredProperty(firstAddResult, "addedNodeId").GetString();
                    Assert.That(createdNodeId, Is.Not.Null.And.Not.EqualTo(string.Empty));
                }
            }

            string[] nodeIdsToDelete = createdNodeId != null ? [createdNodeId] : [fallbackNodeId];
            string deleteJson = await NodeManagementServiceTools.DeleteNodesAsync(
                McpTestEnvironment.SessionManager,
                nodeIdsToDelete,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument deleteDocument = JsonDocument.Parse(deleteJson);
            JsonElement deleteRoot = deleteDocument.RootElement;

            if (deleteRoot.TryGetProperty("error", out JsonElement deleteError))
            {
                Assert.That(deleteError.GetBoolean(), Is.True);
                AssertCharacterizedErrorPayload(deleteRoot);
                return;
            }

            JsonElement deleteResults = GetRequiredProperty(deleteRoot, "results");
            string? deleteStatusCode = deleteResults[0].GetString();

            Assert.That(GetRequiredProperty(deleteRoot, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(deleteResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(deleteResults.GetArrayLength(), Is.EqualTo(1));
            Assert.That(deleteStatusCode, Is.Not.Null.And.Not.EqualTo(string.Empty));

            if (createdNodeId != null)
            {
                Assert.That(deleteStatusCode, Is.EqualTo("Good"));
            }
            else
            {
                Assert.That(deleteStatusCode, Does.StartWith("Bad"));
            }
        }

        [Test]
        public async Task AddReferencesAndDeleteReferencesReturnCharacterizedJsonAsync()
        {
            string parentNodeId = ObjectIds.ObjectsFolder.ToString(null, CultureInfo.InvariantCulture);
            string sourceNodeId = ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture);
            string referenceTypeId = ReferenceTypeIds.Organizes.ToString(null, CultureInfo.InvariantCulture);
            string typeDefinition = ObjectTypeIds.BaseObjectType.ToString(null, CultureInfo.InvariantCulture);
            string browseName = "1:McpReferenceNode" + Guid.NewGuid().ToString("N");
            string fallbackTargetNodeId = "ns=1;s=McpMissingReferenceNode" + Guid.NewGuid().ToString("N");
            string targetNodeId = fallbackTargetNodeId;
            string? createdNodeId = null;

            try
            {
                string addNodeJson = await NodeManagementServiceTools.AddNodesAsync(
                    McpTestEnvironment.SessionManager,
                    parentNodeId,
                    referenceTypeId,
                    browseName,
                    "Object",
                    typeDefinition,
                    McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument addNodeDocument = JsonDocument.Parse(addNodeJson);
                JsonElement addNodeRoot = addNodeDocument.RootElement;

                if (!addNodeRoot.TryGetProperty("error", out _))
                {
                    JsonElement addNodeResults = GetRequiredProperty(addNodeRoot, "results");
                    JsonElement firstAddNodeResult = addNodeResults[0];
                    string? addNodeStatusCode = GetRequiredProperty(firstAddNodeResult, "statusCode").GetString();

                    if (string.Equals(addNodeStatusCode, "Good", StringComparison.Ordinal))
                    {
                        createdNodeId = GetRequiredProperty(firstAddNodeResult, "addedNodeId").GetString();
                        Assert.That(createdNodeId, Is.Not.Null.And.Not.EqualTo(string.Empty));
                        targetNodeId = createdNodeId!;
                    }
                }

                string addReferencesJson = await NodeManagementServiceTools.AddReferencesAsync(
                    McpTestEnvironment.SessionManager,
                    sourceNodeId,
                    referenceTypeId,
                    targetNodeId,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument addReferencesDocument = JsonDocument.Parse(addReferencesJson);
                JsonElement addReferencesRoot = addReferencesDocument.RootElement;

                string? addReferenceStatusCode = null;
                if (addReferencesRoot.TryGetProperty("error", out JsonElement addReferenceError))
                {
                    Assert.That(addReferenceError.GetBoolean(), Is.True);
                    AssertCharacterizedErrorPayload(addReferencesRoot);
                }
                else
                {
                    JsonElement addReferenceResults = GetRequiredProperty(addReferencesRoot, "results");
                    addReferenceStatusCode = addReferenceResults[0].GetString();

                    Assert.That(GetRequiredProperty(addReferencesRoot, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
                    Assert.That(addReferenceResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                    Assert.That(addReferenceResults.GetArrayLength(), Is.EqualTo(1));
                    Assert.That(addReferenceStatusCode, Is.Not.Null.And.Not.EqualTo(string.Empty));

                    if (createdNodeId == null)
                    {
                        Assert.That(addReferenceStatusCode, Does.StartWith("Bad"));
                    }
                }

                string deleteReferencesJson = await NodeManagementServiceTools.DeleteReferencesAsync(
                    McpTestEnvironment.SessionManager,
                    sourceNodeId,
                    referenceTypeId,
                    targetNodeId,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument deleteReferencesDocument = JsonDocument.Parse(deleteReferencesJson);
                JsonElement deleteReferencesRoot = deleteReferencesDocument.RootElement;

                if (deleteReferencesRoot.TryGetProperty("error", out JsonElement deleteReferenceError))
                {
                    Assert.That(deleteReferenceError.GetBoolean(), Is.True);
                    AssertCharacterizedErrorPayload(deleteReferencesRoot);
                    return;
                }

                JsonElement deleteReferenceResults = GetRequiredProperty(deleteReferencesRoot, "results");
                string? deleteReferenceStatusCode = deleteReferenceResults[0].GetString();

                Assert.That(GetRequiredProperty(deleteReferencesRoot, "responseHeader").ValueKind, Is.EqualTo(JsonValueKind.Object));
                Assert.That(deleteReferenceResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(deleteReferenceResults.GetArrayLength(), Is.EqualTo(1));
                Assert.That(deleteReferenceStatusCode, Is.Not.Null.And.Not.EqualTo(string.Empty));

                if (string.Equals(addReferenceStatusCode, "Good", StringComparison.Ordinal))
                {
                    Assert.That(deleteReferenceStatusCode, Is.EqualTo("Good"));
                }
                else
                {
                    Assert.That(deleteReferenceStatusCode, Does.StartWith("Bad"));
                }
            }
            finally
            {
                if (createdNodeId != null)
                {
                    string[] nodeIds = [createdNodeId];
                    await NodeManagementServiceTools.DeleteNodesAsync(
                        McpTestEnvironment.SessionManager,
                        nodeIds,
                        sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);
                }
            }
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
