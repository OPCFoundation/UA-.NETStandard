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
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class MonitoredItemServiceToolsTests
    {
        [Test]
        public async Task MonitoredItemLifecycleAsync()
        {
            uint subscriptionId = 0;
            uint monitoredItemId = 0;
            bool monitoredItemDeleted = false;

            try
            {
                subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
                string currentTimeNodeId = VariableIds.Server_ServerStatus_CurrentTime
                    .ToString(null, CultureInfo.InvariantCulture);
                string[] nodeIds = [currentTimeNodeId];

                string createJson = await MonitoredItemServiceTools.CreateMonitoredItemsAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    nodeIds,
                    samplingInterval: 100,
                    queueSize: 5,
                    discardOldest: true,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument createDocument = JsonDocument.Parse(createJson);
                JsonElement createRoot = createDocument.RootElement;
                JsonElement createResults = GetRequiredProperty(createRoot, "results");
                JsonElement createResult = createResults[0];

                AssertSuccessPayload(createRoot);
                Assert.That(createResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(createResults.GetArrayLength(), Is.EqualTo(1));
                Assert.That(GetRequiredProperty(createResult, "statusCode").GetString(), Is.EqualTo("Good"));

                monitoredItemId = GetRequiredProperty(createResult, "monitoredItemId").GetUInt32();
                Assert.That(monitoredItemId, Is.Positive);
                Assert.That(
                    GetRequiredProperty(createResult, "revisedSamplingInterval").GetDouble(),
                    Is.GreaterThanOrEqualTo(0d));
                Assert.That(GetRequiredProperty(createResult, "revisedQueueSize").GetUInt32(), Is.Positive);

                uint[] monitoredItemIds = [monitoredItemId];
                string modifyJson = await MonitoredItemServiceTools.ModifyMonitoredItemsAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    monitoredItemIds,
                    samplingInterval: 200,
                    queueSize: 3,
                    discardOldest: false,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument modifyDocument = JsonDocument.Parse(modifyJson);
                JsonElement modifyRoot = modifyDocument.RootElement;
                JsonElement modifyResults = GetRequiredProperty(modifyRoot, "results");
                JsonElement modifyResult = modifyResults[0];

                AssertSuccessPayload(modifyRoot);
                Assert.That(modifyResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(modifyResults.GetArrayLength(), Is.EqualTo(1));
                Assert.That(GetRequiredProperty(modifyResult, "statusCode").GetString(), Is.EqualTo("Good"));
                Assert.That(
                    GetRequiredProperty(modifyResult, "revisedSamplingInterval").GetDouble(),
                    Is.GreaterThanOrEqualTo(0d));
                Assert.That(GetRequiredProperty(modifyResult, "revisedQueueSize").GetUInt32(), Is.Positive);

                string setMonitoringModeJson = await MonitoredItemServiceTools.SetMonitoringModeAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    monitoredItemIds,
                    monitoringMode: "Sampling",
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument setMonitoringModeDocument = JsonDocument.Parse(setMonitoringModeJson);
                JsonElement setMonitoringModeRoot = setMonitoringModeDocument.RootElement;
                JsonElement setMonitoringModeResults = GetRequiredProperty(setMonitoringModeRoot, "results");

                AssertSuccessPayload(setMonitoringModeRoot);
                Assert.That(setMonitoringModeResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(setMonitoringModeResults.GetArrayLength(), Is.EqualTo(1));
                Assert.That(setMonitoringModeResults[0].GetString(), Is.EqualTo("Good"));

                string deleteJson = await MonitoredItemServiceTools.DeleteMonitoredItemsAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    monitoredItemIds,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument deleteDocument = JsonDocument.Parse(deleteJson);
                JsonElement deleteRoot = deleteDocument.RootElement;
                JsonElement deleteResults = GetRequiredProperty(deleteRoot, "results");

                AssertSuccessPayload(deleteRoot);
                Assert.That(deleteResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(deleteResults.GetArrayLength(), Is.EqualTo(1));
                Assert.That(deleteResults[0].GetString(), Is.EqualTo("Good"));
                monitoredItemDeleted = true;
            }
            finally
            {
                if (!monitoredItemDeleted && subscriptionId > 0 && monitoredItemId > 0)
                {
                    await DeleteMonitoredItemsAsync(subscriptionId, [monitoredItemId]).ConfigureAwait(false);
                }

                if (subscriptionId > 0)
                {
                    await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task SetTriggeringAsyncWithLinkedItemAddsAndRemovesLinkAsync()
        {
            uint subscriptionId = 0;
            uint[] monitoredItemIds = [];

            try
            {
                subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
                string currentTimeNodeId = VariableIds.Server_ServerStatus_CurrentTime
                    .ToString(null, CultureInfo.InvariantCulture);
                string[] nodeIds = [currentTimeNodeId, currentTimeNodeId];

                string createJson = await MonitoredItemServiceTools.CreateMonitoredItemsAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    nodeIds,
                    samplingInterval: 100,
                    queueSize: 5,
                    discardOldest: true,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument createDocument = JsonDocument.Parse(createJson);
                JsonElement createRoot = createDocument.RootElement;
                JsonElement createResults = GetRequiredProperty(createRoot, "results");

                AssertSuccessPayload(createRoot);
                Assert.That(createResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(createResults.GetArrayLength(), Is.EqualTo(2));
                Assert.That(GetRequiredProperty(createResults[0], "statusCode").GetString(), Is.EqualTo("Good"));
                Assert.That(GetRequiredProperty(createResults[1], "statusCode").GetString(), Is.EqualTo("Good"));

                monitoredItemIds =
                [
                    GetRequiredProperty(createResults[0], "monitoredItemId").GetUInt32(),
                    GetRequiredProperty(createResults[1], "monitoredItemId").GetUInt32()
                ];
                Assert.That(monitoredItemIds[0], Is.Positive);
                Assert.That(monitoredItemIds[1], Is.Positive);

                uint[] linksToAdd = [monitoredItemIds[1]];
                string addTriggeringJson = await MonitoredItemServiceTools.SetTriggeringAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    monitoredItemIds[0],
                    linksToAdd,
                    linksToRemove: null,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument addTriggeringDocument = JsonDocument.Parse(addTriggeringJson);
                JsonElement addTriggeringRoot = addTriggeringDocument.RootElement;
                JsonElement addResults = GetRequiredProperty(addTriggeringRoot, "addResults");
                JsonElement removeResults = GetRequiredProperty(addTriggeringRoot, "removeResults");

                AssertSuccessPayload(addTriggeringRoot);
                Assert.That(addResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(addResults.GetArrayLength(), Is.EqualTo(1));
                Assert.That(addResults[0].GetString(), Is.EqualTo("Good"));
                Assert.That(removeResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(removeResults.GetArrayLength(), Is.Zero);

                uint[] linksToRemove = [monitoredItemIds[1]];
                string removeTriggeringJson = await MonitoredItemServiceTools.SetTriggeringAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    monitoredItemIds[0],
                    linksToAdd: null,
                    linksToRemove,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument removeTriggeringDocument = JsonDocument.Parse(removeTriggeringJson);
                JsonElement removeTriggeringRoot = removeTriggeringDocument.RootElement;
                JsonElement removeAddResults = GetRequiredProperty(removeTriggeringRoot, "addResults");
                JsonElement removeResultsResults = GetRequiredProperty(removeTriggeringRoot, "removeResults");

                AssertSuccessPayload(removeTriggeringRoot);
                Assert.That(removeAddResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(removeAddResults.GetArrayLength(), Is.Zero);
                Assert.That(removeResultsResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(removeResultsResults.GetArrayLength(), Is.EqualTo(1));
                Assert.That(removeResultsResults[0].GetString(), Is.EqualTo("Good"));
            }
            finally
            {
                if (subscriptionId > 0 && monitoredItemIds.Length > 0)
                {
                    await DeleteMonitoredItemsAsync(subscriptionId, monitoredItemIds).ConfigureAwait(false);
                }

                if (subscriptionId > 0)
                {
                    await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
                }
            }
        }

        private static async Task<uint> CreateSubscriptionAsync()
        {
            string json = await SubscriptionServiceTools.CreateSubscriptionAsync(
                McpTestEnvironment.SessionManager,
                publishingInterval: 100,
                lifetimeCount: 30,
                maxKeepAliveCount: 1,
                maxNotificationsPerPublish: 5,
                publishingEnabled: true,
                priority: 1,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            AssertSuccessPayload(root);

            uint subscriptionId = GetRequiredProperty(root, "subscriptionId").GetUInt32();
            Assert.That(subscriptionId, Is.Positive);
            return subscriptionId;
        }

        private static async Task DeleteMonitoredItemsAsync(uint subscriptionId, uint[] monitoredItemIds)
        {
            await MonitoredItemServiceTools.DeleteMonitoredItemsAsync(
                McpTestEnvironment.SessionManager,
                subscriptionId,
                monitoredItemIds,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);
        }

        private static async Task DeleteSubscriptionAsync(uint subscriptionId)
        {
            uint[] subscriptionIds = [subscriptionId];
            await SubscriptionServiceTools.DeleteSubscriptionsAsync(
                McpTestEnvironment.SessionManager,
                subscriptionIds,
                sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);
        }

        private static void AssertSuccessPayload(JsonElement root)
        {
            Assert.That(root.TryGetProperty("error", out _), Is.False);

            JsonElement responseHeader = GetRequiredProperty(root, "responseHeader");
            Assert.That(responseHeader.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(GetRequiredProperty(responseHeader, "serviceResult").GetString(), Is.EqualTo("Good"));
            Assert.That(
                GetRequiredProperty(responseHeader, "timestamp").GetString(),
                Is.Not.Null.And.Not.EqualTo(string.Empty));
        }

        private static JsonElement GetRequiredProperty(JsonElement element, string propertyName)
        {
            Assert.That(element.TryGetProperty(propertyName, out JsonElement property), Is.True);
            return property;
        }
    }
}
#endif
