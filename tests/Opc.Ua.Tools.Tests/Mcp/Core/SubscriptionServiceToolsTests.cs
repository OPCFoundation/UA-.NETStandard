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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class SubscriptionServiceToolsTests
    {
        /// <summary>
        /// Missing or unequal acknowledgement arrays must not silently discard a caller's acknowledgements.
        /// </summary>
        [TestCase(-1, 1)]
        [TestCase(1, -1)]
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(1, 2)]
        [TestCase(2, 1)]
        public async Task PublishRejectsMismatchedAcknowledgementsAsync(int subscriptionCount, int sequenceCount)
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                uint[]? subscriptions = subscriptionCount < 0
                    ? null : Enumerable.Repeat(subscriptionId, subscriptionCount).ToArray();
                uint[]? sequences = sequenceCount < 0 ? null : Enumerable.Repeat(1u, sequenceCount).ToArray();

                string json = await SubscriptionServiceTools.PublishAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptions,
                    sequences,
                    McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument document = JsonDocument.Parse(json);
                AssertErrorPayload(document.RootElement, "BadInvalidArgument");
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Omitted and empty acknowledgement collections both represent a request without acknowledgements.
        /// </summary>
        [TestCase(-1, -1)]
        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        [TestCase(0, 0)]
        public async Task PublishAcceptsEmptyAcknowledgementsAsync(int subscriptionCount, int sequenceCount)
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                string json = await SubscriptionServiceTools.PublishAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionCount < 0 ? null : [],
                    sequenceCount < 0 ? null : [],
                    McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument document = JsonDocument.Parse(json);
                AssertSuccessPayload(document.RootElement);
                Assert.That(GetRequiredProperty(document.RootElement, "subscriptionId").GetUInt32(),
                    Is.EqualTo(subscriptionId));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A successful Publish service response still exposes each acknowledgement's failure in request order.
        /// </summary>
        [Test]
        public async Task PublishPreservesAcknowledgementResultsAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                string json = await SubscriptionServiceTools.PublishAsync(
                    McpTestEnvironment.SessionManager,
                    [0, subscriptionId],
                    [1, uint.MaxValue],
                    McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument document = JsonDocument.Parse(json);
                AssertSuccessPayload(document.RootElement);
                JsonElement results = GetRequiredProperty(document.RootElement, "results");
                Assert.That(results.GetArrayLength(), Is.EqualTo(2));
                Assert.That(results[0].GetString(), Is.EqualTo("BadSubscriptionIdInvalid"));
                Assert.That(results[1].GetString(), Is.EqualTo("BadSequenceNumberUnknown"));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Matching acknowledgements reach the server and remove the acknowledged notification from retransmission.
        /// </summary>
        [Test]
        public async Task PublishAcknowledgesTheRequestedNotificationAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                string monitored = await MonitoredItemServiceTools.CreateMonitoredItemsAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    [VariableIds.Server_ServerStatus_CurrentTime.ToString()],
                    samplingInterval: 50,
                    sessionName: McpTestEnvironment.SessionName,
                    ct: timeout.Token).ConfigureAwait(false);
                using JsonDocument monitoredDocument = JsonDocument.Parse(monitored);
                AssertSuccessPayload(monitoredDocument.RootElement);
                Assert.That(GetRequiredProperty(monitoredDocument.RootElement, "results")[0]
                    .GetProperty("statusCode").GetString(), Is.EqualTo("Good"));

                uint sequenceNumber = 0;
                for (int attempt = 0; attempt < 10 && sequenceNumber == 0; attempt++)
                {
                    string published = await SubscriptionServiceTools.PublishAsync(
                        McpTestEnvironment.SessionManager,
                        sessionName: McpTestEnvironment.SessionName,
                        ct: timeout.Token).ConfigureAwait(false);
                    using JsonDocument publishedDocument = JsonDocument.Parse(published);
                    AssertSuccessPayload(publishedDocument.RootElement);
                    JsonElement notification = GetRequiredProperty(
                        publishedDocument.RootElement, "notificationMessage");
                    if (GetRequiredProperty(notification, "notificationDataCount").GetInt32() > 0)
                    {
                        sequenceNumber = GetRequiredProperty(notification, "sequenceNumber").GetUInt32();
                    }
                }
                Assert.That(sequenceNumber, Is.Positive);

                string acknowledged = await SubscriptionServiceTools.PublishAsync(
                    McpTestEnvironment.SessionManager,
                    [subscriptionId],
                    [sequenceNumber],
                    McpTestEnvironment.SessionName,
                    timeout.Token).ConfigureAwait(false);
                using JsonDocument acknowledgedDocument = JsonDocument.Parse(acknowledged);
                AssertSuccessPayload(acknowledgedDocument.RootElement);
                JsonElement results = GetRequiredProperty(acknowledgedDocument.RootElement, "results");
                Assert.That(results.GetArrayLength(), Is.EqualTo(1));
                Assert.That(results[0].GetString(), Is.EqualTo("Good"));

                string republished = await SubscriptionServiceTools.RepublishAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    sequenceNumber,
                    McpTestEnvironment.SessionName,
                    timeout.Token).ConfigureAwait(false);
                using JsonDocument republishedDocument = JsonDocument.Parse(republished);
                AssertErrorPayload(republishedDocument.RootElement, "BadMessageNotAvailable");
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SubscriptionLifecycleAsync()
        {
            uint subscriptionId = 0;
            bool subscriptionDeleted = false;

            try
            {
                string createJson = await SubscriptionServiceTools.CreateSubscriptionAsync(
                    McpTestEnvironment.SessionManager,
                    publishingInterval: 100,
                    lifetimeCount: 30,
                    maxKeepAliveCount: 1,
                    maxNotificationsPerPublish: 5,
                    publishingEnabled: true,
                    priority: 1,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument createDocument = JsonDocument.Parse(createJson);
                JsonElement createRoot = createDocument.RootElement;

                AssertSuccessPayload(createRoot);
                subscriptionId = GetRequiredProperty(createRoot, "subscriptionId").GetUInt32();
                Assert.That(subscriptionId, Is.Positive);
                Assert.That(
                    GetRequiredProperty(createRoot, "revisedPublishingInterval").GetDouble(),
                    Is.Positive);
                Assert.That(GetRequiredProperty(createRoot, "revisedLifetimeCount").GetUInt32(), Is.Positive);
                Assert.That(
                    GetRequiredProperty(createRoot, "revisedMaxKeepAliveCount").GetUInt32(),
                    Is.Positive);

                string modifyJson = await SubscriptionServiceTools.ModifySubscriptionAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    publishingInterval: 150,
                    lifetimeCount: 45,
                    maxKeepAliveCount: 2,
                    maxNotificationsPerPublish: 10,
                    priority: 2,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument modifyDocument = JsonDocument.Parse(modifyJson);
                JsonElement modifyRoot = modifyDocument.RootElement;

                AssertSuccessPayload(modifyRoot);
                Assert.That(
                    GetRequiredProperty(modifyRoot, "revisedPublishingInterval").GetDouble(),
                    Is.Positive);
                Assert.That(GetRequiredProperty(modifyRoot, "revisedLifetimeCount").GetUInt32(), Is.Positive);
                Assert.That(
                    GetRequiredProperty(modifyRoot, "revisedMaxKeepAliveCount").GetUInt32(),
                    Is.Positive);

                uint[] subscriptionIds = [subscriptionId];
                string setPublishingJson = await SubscriptionServiceTools.SetPublishingModeAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionIds,
                    publishingEnabled: true,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument setPublishingDocument = JsonDocument.Parse(setPublishingJson);
                JsonElement setPublishingRoot = setPublishingDocument.RootElement;
                JsonElement setPublishingResults = GetRequiredProperty(setPublishingRoot, "results");

                AssertSuccessPayload(setPublishingRoot);
                Assert.That(setPublishingResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(setPublishingResults.GetArrayLength(), Is.EqualTo(1));
                Assert.That(setPublishingResults[0].GetString(), Is.EqualTo("Good"));

                string publishJson = await SubscriptionServiceTools.PublishAsync(
                    McpTestEnvironment.SessionManager,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument publishDocument = JsonDocument.Parse(publishJson);
                JsonElement publishRoot = publishDocument.RootElement;
                JsonElement notificationMessage = GetRequiredProperty(publishRoot, "notificationMessage");
                string publishTime = GetRequiredProperty(notificationMessage, "publishTime").GetString()!;

                AssertSuccessPayload(publishRoot);
                Assert.That(GetRequiredProperty(publishRoot, "subscriptionId").GetUInt32(), Is.EqualTo(subscriptionId));
                Assert.That(GetRequiredProperty(publishRoot, "moreNotifications").GetBoolean(), Is.False);
                Assert.That(GetRequiredProperty(notificationMessage, "sequenceNumber").GetUInt32(), Is.GreaterThanOrEqualTo(0u));
                Assert.That(GetRequiredProperty(notificationMessage, "notificationDataCount").GetInt32(), Is.Zero);
                Assert.That(publishTime, Is.Not.EqualTo(string.Empty));

                string deleteJson = await SubscriptionServiceTools.DeleteSubscriptionsAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionIds,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument deleteDocument = JsonDocument.Parse(deleteJson);
                JsonElement deleteRoot = deleteDocument.RootElement;
                JsonElement deleteResults = GetRequiredProperty(deleteRoot, "results");

                AssertSuccessPayload(deleteRoot);
                Assert.That(deleteResults.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(deleteResults.GetArrayLength(), Is.EqualTo(1));
                Assert.That(deleteResults[0].GetString(), Is.EqualTo("Good"));
                subscriptionDeleted = true;
            }
            finally
            {
                if (!subscriptionDeleted && subscriptionId > 0)
                {
                    await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task RepublishAsyncWithoutQueuedNotificationReturnsBadMessageNotAvailableAsync()
        {
            uint subscriptionId = 0;

            try
            {
                subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);

                string republishJson = await SubscriptionServiceTools.RepublishAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionId,
                    retransmitSequenceNumber: 1,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument republishDocument = JsonDocument.Parse(republishJson);
                JsonElement republishRoot = republishDocument.RootElement;

                AssertErrorPayload(republishRoot, "BadMessageNotAvailable");
            }
            finally
            {
                if (subscriptionId > 0)
                {
                    await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task TransferSubscriptionsAsyncWithinSameSessionReturnsCharacterizedResultAsync()
        {
            uint subscriptionId = 0;

            try
            {
                subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
                uint[] subscriptionIds = [subscriptionId];

                string transferJson = await SubscriptionServiceTools.TransferSubscriptionsAsync(
                    McpTestEnvironment.SessionManager,
                    subscriptionIds,
                    sendInitialValues: true,
                    sessionName: McpTestEnvironment.SessionName).ConfigureAwait(false);

                using JsonDocument transferDocument = JsonDocument.Parse(transferJson);
                JsonElement transferRoot = transferDocument.RootElement;

                AssertSuccessPayload(transferRoot);

                JsonElement results = GetRequiredProperty(transferRoot, "results");
                Assert.That(results.ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(results.GetArrayLength(), Is.EqualTo(1));

                JsonElement firstResult = results[0];
                Assert.That(GetRequiredProperty(firstResult, "statusCode").GetString(), Is.EqualTo("BadNothingToDo"));

                if (firstResult.TryGetProperty("availableSequenceNumbers", out JsonElement availableSequenceNumbers))
                {
                    Assert.That(availableSequenceNumbers.ValueKind, Is.EqualTo(JsonValueKind.Array));
                }
            }
            finally
            {
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

        private static void AssertErrorPayload(JsonElement root, string expectedStatusCode)
        {
            Assert.That(GetRequiredProperty(root, "error").GetBoolean(), Is.True);
            Assert.That(GetRequiredProperty(root, "statusCode").GetString(), Is.EqualTo(expectedStatusCode));
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
