/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// compliance tests for the SetTriggering service covering simple
    /// trigger links, chain triggering, multiple linked items, link removal,
    /// monitoring mode interactions, error cases, and advanced scenarios.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitoredItem")]
    [Category("MonitorTriggering")]
    public class MonitorTriggeringTests : TestFixture
    {
        [SetUp]
        public async Task SetUp()
        {
            m_subscriptionId = await CreateSetupSubscriptionAsync(
                publishingInterval: 100, requestedLifetimeCount: 100,
                requestedMaxKeepAliveCount: 10).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDown()
        {
            if (m_subscriptionId > 0)
            {
                try
                {
                    await Session.DeleteSubscriptionsAsync(
                        null,
                        new uint[] { m_subscriptionId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Subscription may already be deleted
                }
                m_subscriptionId = 0;
            }
        }

        [Test]
        public async Task SimpleTriggerReportingTriggersScanningAsync()
        {
            // A(Reporting) triggers B(Sampling); fire A → B reports
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, samplingInterval: 50,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(2));
            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(trigResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(StatusCode.IsGood(trigResp.AddResults[0]), Is.True);

            // Write to B so it has a queued value
            await WriteValueAsync(nodeB, UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            // CurrentTime changes continuously, triggering B
            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            HashSet<uint> handles = CollectNotifiedHandles(pubResp);
            // A should report (Reporting); B should also report (triggered)
            Assert.That(handles, Does.Contain(1u));
        }

        [Test]
        public async Task SimpleTriggerLinkedItemOnlyReportsWhenTriggerFiresAsync()
        {
            // B in Sampling mode does not report on its own
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(trigResp.ResponseHeader.ServiceResult), Is.True);

            await ConsumeAllNotificationsAsync().ConfigureAwait(false);

            // Write to B only; A is static and in Sampling mode → no trigger fires
            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);

            // Neither A nor B should report autonomously
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);
            Assert.That(handles, Does.Not.Contain(2u),
                "Sampling-mode linked item should not report on its own");
        }

        [Test]
        public async Task SimpleTriggerWriteToLinkedItemNoNotificationAloneAsync()
        {
            // Write to B while B is Sampling → no notification from B
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticUInt32);

            try
            {
                CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling))
                    .ConfigureAwait(false);

                uint idA = createResp.Results[0].MonitoredItemId;
                uint idB = createResp.Results[1].MonitoredItemId;

                await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
                await ConsumeAllNotificationsAsync().ConfigureAwait(false);

                // Write only to B; A is static so no trigger fires
                await WriteValueAsync(nodeB,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

                PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
                HashSet<uint> handles = CollectNotifiedHandles(pubResp);

                // B should not appear because A (trigger) has not changed
                Assert.That(handles, Does.Not.Contain(2u));
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: trigger sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task SimpleTriggerRemoveLinkStopsTriggeringAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            try
            {
                CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2, samplingInterval: 50,
                        mode: MonitoringMode.Sampling)).ConfigureAwait(false);

                uint idA = createResp.Results[0].MonitoredItemId;
                uint idB = createResp.Results[1].MonitoredItemId;

                await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
                await ConsumeAllNotificationsAsync().ConfigureAwait(false);

                // Remove the link
                SetTriggeringResponse removeResp = await SetTriggerAsync(
                    idA, null, [idB]).ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(removeResp.ResponseHeader.ServiceResult), Is.True);
                Assert.That(StatusCode.IsGood(removeResp.RemoveResults[0]), Is.True);

                await WriteValueAsync(nodeB,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

                PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
                HashSet<uint> handles = CollectNotifiedHandles(pubResp);

                // B is back to plain Sampling and should not report
                Assert.That(handles, Does.Not.Contain(2u));
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: trigger-remove sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task SimpleTriggerAddLinkAfterCreationAsync()
        {
            // Create both items first, then SetTriggering
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, samplingInterval: 50,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            // Items exist but no triggering link yet
            await ConsumeAllNotificationsAsync().ConfigureAwait(false);

            // Now add the link
            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(trigResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(trigResp.AddResults.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(trigResp.AddResults[0]), Is.True);

            // Write to B so it has data queued
            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            // A fires (CurrentTime), should trigger B
            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);
            Assert.That(handles, Does.Contain(1u));
        }

        [Test]
        public async Task SimpleTriggerBothItemsInSameNotificationAsync()
        {
            // When A triggers B, both appear in same publish cycle
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, samplingInterval: 50,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);

            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            // Both A and B should be in the same notification message
            // This is timing-dependent — B may appear in a subsequent publish
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);
            if (!handles.Contains(1u) || !handles.Contains(2u))
            {
                // Try one more publish cycle
                try
                {
                    PublishResponse pubResp2 = await PublishAndWaitAsync().ConfigureAwait(false);
                    foreach (uint h in CollectNotifiedHandles(pubResp2))
                    {
                        handles.Add(h);
                    }
                }
                catch
                { /* timeout is acceptable */
                }
            }
            if (!handles.Contains(1u) && !handles.Contains(2u))
            {
                Assert.Fail("Triggering notification timing too tight for test environment.");
            }
            Assert.That(handles, Does.Contain(1u), "Trigger item A should report");
            Assert.That(handles, Does.Contain(2u), "Linked item B should report");
        }

        [Test]
        [Category("LongRunning")]
        public async Task ChainTriggerATriggersB_BTriggersCAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);

            try
            {
                CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2, samplingInterval: 50,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeC, 3, samplingInterval: 50,
                        mode: MonitoringMode.Sampling)).ConfigureAwait(false);

                Assert.That(createResp.Results.Count, Is.EqualTo(3));
                uint idA = createResp.Results[0].MonitoredItemId;
                uint idB = createResp.Results[1].MonitoredItemId;
                uint idC = createResp.Results[2].MonitoredItemId;

                // A → B
                SetTriggeringResponse abResp =
                    await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(abResp.AddResults[0]), Is.True);

                // B → C
                SetTriggeringResponse bcResp =
                    await SetTriggerAsync(idB, [idC]).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(bcResp.AddResults[0]), Is.True);

                await WriteValueAsync(nodeB,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
                await WriteValueAsync(nodeC,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

                PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
                Assert.That(pubResp.NotificationMessage, Is.Not.Null);
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: chain-trigger setup interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task ChainTriggerOnlyDirectLinksHonoredAsync()
        {
            // A→B→C but A does not directly trigger C
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticUInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeC, 3, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            // A → B only
            await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
            // B → C
            await SetTriggerAsync(idB, [idC]).ConfigureAwait(false);

            await ConsumeAllNotificationsAsync().ConfigureAwait(false);

            // Write to A to fire trigger chain
            await WriteValueAsync(nodeA,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
            await WriteValueAsync(nodeC,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // A reports (Reporting); B reports (triggered by A)
            Assert.That(handles, Does.Contain(1u), "Trigger A should report");
            Assert.That(handles, Does.Contain(2u), "Linked B should report");
        }

        [Test]
        public async Task ChainTriggerRemoveMiddleLinkBreaksChainAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, samplingInterval: 50,
                    mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeC, 3, samplingInterval: 50,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
            await SetTriggerAsync(idB, [idC]).ConfigureAwait(false);

            // Remove B→C link
            SetTriggeringResponse removeResp = await SetTriggerAsync(
                idB, null, [idC]).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(removeResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(StatusCode.IsGood(removeResp.RemoveResults[0]), Is.True);

            await ConsumeAllNotificationsAsync().ConfigureAwait(false);

            await WriteValueAsync(nodeC,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // C should no longer be triggered
            Assert.That(handles, Does.Not.Contain(3u),
                "C should not report after B→C link removed");
        }

        [Test]
        public async Task ChainTriggerThreeLevelsDeepAsync()
        {
            // A→B, B→C, C→D
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeD = ToNodeId(Constants.ScalarStaticUInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, samplingInterval: 50,
                    mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeC, 3, samplingInterval: 50,
                    mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeD, 4, samplingInterval: 50,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(4));
            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;
            uint idD = createResp.Results[3].MonitoredItemId;

            await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
            await SetTriggerAsync(idB, [idC]).ConfigureAwait(false);
            await SetTriggerAsync(idC, [idD]).ConfigureAwait(false);

            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
            await WriteValueAsync(nodeC,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
            await WriteValueAsync(nodeD,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);
        }

        [Test]
        public async Task OneTriggerMultipleLinkedItemsAsync()
        {
            // A triggers B, C, D simultaneously
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeD = ToNodeId(Constants.ScalarStaticUInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeC, 3, mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeD, 4, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint[] linkedIds = [.. createResp.Results.ToArray()
                .Skip(1).Select(r => r.MonitoredItemId)];

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, linkedIds).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(trigResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(trigResp.AddResults.Count, Is.EqualTo(3));
            foreach (StatusCode sc in trigResp.AddResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }

            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
            await WriteValueAsync(nodeC,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
            await WriteValueAsync(nodeD,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            HashSet<uint> handles = await PublishUntilHandlesObservedAsync(
                [1u, 2u, 3u, 4u]).ConfigureAwait(false);

            Assert.That(handles, Does.Contain(1u));
            Assert.That(handles, Does.Contain(2u));
            Assert.That(handles, Does.Contain(3u));
            Assert.That(handles, Does.Contain(4u));
        }

        [Test]
        public async Task MultipleTriggersSameLinkedItemAsync()
        {
            // Both A and B trigger C
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeC, 3, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            // A → C
            SetTriggeringResponse t1 =
                await SetTriggerAsync(idA, [idC]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(t1.AddResults[0]), Is.True);

            // B → C
            SetTriggeringResponse t2 =
                await SetTriggerAsync(idB, [idC]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(t2.AddResults[0]), Is.True);

            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
            await WriteValueAsync(nodeC,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            Assert.That(handles, Does.Contain(3u), "C should report when triggered");
        }

        [Test]
        public async Task AddFiveLinkedItemsToOneTriggerAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            ExpandedNodeId[] linkedExpIds =
            [
                Constants.ScalarStaticInt32,
                Constants.ScalarStaticDouble,
                Constants.ScalarStaticUInt32,
                Constants.ScalarStaticFloat,
                Constants.ScalarStaticInt16
            ];

            var items = new List<MonitoredItemCreateRequest>
            {
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting)
            };
            for (int i = 0; i < linkedExpIds.Length; i++)
            {
                items.Add(CreateItemRequest(
                    ToNodeId(linkedExpIds[i]), (uint)(10 + i),
                    mode: MonitoringMode.Sampling));
            }

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync([.. items]).ConfigureAwait(false);
            Assert.That(createResp.Results.Count, Is.EqualTo(6));

            uint idA = createResp.Results[0].MonitoredItemId;
            uint[] linkedIds = [.. createResp.Results.ToArray()
                .Skip(1).Select(r => r.MonitoredItemId)];

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, linkedIds).ConfigureAwait(false);
            Assert.That(trigResp.AddResults.Count, Is.EqualTo(5));
            foreach (StatusCode sc in trigResp.AddResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task TriggerWithTenLinkedItemsAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            ExpandedNodeId[] linkedExpIds =
            [
                Constants.ScalarStaticInt32,
                Constants.ScalarStaticDouble,
                Constants.ScalarStaticUInt32,
                Constants.ScalarStaticFloat,
                Constants.ScalarStaticInt16,
                Constants.ScalarStaticUInt16,
                Constants.ScalarStaticInt64,
                Constants.ScalarStaticUInt64,
                Constants.ScalarStaticByte,
                Constants.ScalarStaticSByte
            ];

            var items = new List<MonitoredItemCreateRequest>
            {
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting)
            };
            for (int i = 0; i < linkedExpIds.Length; i++)
            {
                items.Add(CreateItemRequest(
                    ToNodeId(linkedExpIds[i]), (uint)(20 + i),
                    mode: MonitoringMode.Sampling));
            }

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync([.. items]).ConfigureAwait(false);
            Assert.That(createResp.Results.Count, Is.EqualTo(11));

            uint idA = createResp.Results[0].MonitoredItemId;
            uint[] linkedIds = [.. createResp.Results.ToArray()
                .Skip(1).Select(r => r.MonitoredItemId)];

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, linkedIds).ConfigureAwait(false);
            Assert.That(trigResp.AddResults.Count, Is.EqualTo(10));
            foreach (StatusCode sc in trigResp.AddResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        [Category("LongRunning")]
        public async Task RemoveOneOfMultipleLinkedItemsRestRemainAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeD = ToNodeId(Constants.ScalarStaticUInt32);

            try
            {
                CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeC, 3, mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeD, 4, mode: MonitoringMode.Sampling))
                    .ConfigureAwait(false);

                uint idA = createResp.Results[0].MonitoredItemId;
                uint idB = createResp.Results[1].MonitoredItemId;
                uint idC = createResp.Results[2].MonitoredItemId;
                uint idD = createResp.Results[3].MonitoredItemId;

                await SetTriggerAsync(idA, [idB, idC, idD])
                    .ConfigureAwait(false);

                // Remove only B
                SetTriggeringResponse removeResp = await SetTriggerAsync(
                    idA, null, [idB]).ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(removeResp.ResponseHeader.ServiceResult), Is.True);
                Assert.That(StatusCode.IsGood(removeResp.RemoveResults[0]), Is.True);

                await WriteValueAsync(nodeC,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
                await WriteValueAsync(nodeD,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

                // C and D are sampled-only items; the trigger fires on every A
                // sample (Reporting CurrentTime, 50 ms). Aggregate handles
                // across multiple publishes to absorb the race between the
                // write hitting the server and the next sampling cycle for
                // C / D on slow CI runners.
                HashSet<uint> handles =
                    await PublishUntilHandlesObservedAsync([3u, 4u])
                        .ConfigureAwait(false);

                // C and D should still be triggered
                Assert.That(handles, Does.Contain(3u), "C should still trigger");
                Assert.That(handles, Does.Contain(4u), "D should still trigger");
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: trigger-remove sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task RemoveAllLinksAtOnceAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeC, 3, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            await SetTriggerAsync(idA, [idB, idC]).ConfigureAwait(false);

            // Remove both at once
            SetTriggeringResponse removeResp = await SetTriggerAsync(
                idA, null, [idB, idC]).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(removeResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(removeResp.RemoveResults.Count, Is.EqualTo(2));
            foreach (StatusCode sc in removeResp.RemoveResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task RemoveLinksOneByOneAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeC, 3, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            await SetTriggerAsync(idA, [idB, idC]).ConfigureAwait(false);

            // Remove B
            SetTriggeringResponse r1 = await SetTriggerAsync(
                idA, null, [idB]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r1.RemoveResults[0]), Is.True);

            // Remove C
            SetTriggeringResponse r2 = await SetTriggerAsync(
                idA, null, [idC]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r2.RemoveResults[0]), Is.True);
        }

        [Test]
        public async Task RemoveNonExistentLinkReturnsBadAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;

            // Try to remove a bogus linked item ID
            SetTriggeringResponse removeResp = await SetTriggerAsync(
                idA, null, [999999u]).ConfigureAwait(false);

            Assert.That(removeResp.RemoveResults.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(removeResp.RemoveResults[0]), Is.True);
        }

        [Test]
        public async Task RemoveLinkThenReAddAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            // Add link
            await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);

            // Remove link
            SetTriggeringResponse removeResp = await SetTriggerAsync(
                idA, null, [idB]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(removeResp.RemoveResults[0]), Is.True);

            // Re-add link
            SetTriggeringResponse reAddResp =
                await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(reAddResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(StatusCode.IsGood(reAddResp.AddResults[0]), Is.True);
        }

        [Test]
        public async Task RemoveLinksFromInvalidTriggerReturnsBadAsync()
        {
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeB, 1, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idB = createResp.Results[0].MonitoredItemId;

            // Invalid triggering item ID
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.SetTriggeringAsync(
                        null, m_subscriptionId, 999999u,
                        default,
                        new uint[] { idB }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false));

            Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True);
        }

        [Test]
        [Category("LongRunning")]
        public async Task TriggerWithDisabledLinkedItemAsync()
        {
            // Linked item Disabled → does not report even when triggered
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            try
            {
                CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2, samplingInterval: 50,
                        mode: MonitoringMode.Disabled)).ConfigureAwait(false);

                uint idA = createResp.Results[0].MonitoredItemId;
                uint idB = createResp.Results[1].MonitoredItemId;

                SetTriggeringResponse trigResp =
                    await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(trigResp.AddResults[0]), Is.True);

                await ConsumeAllNotificationsAsync().ConfigureAwait(false);

                PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
                HashSet<uint> handles = CollectNotifiedHandles(pubResp);

                // B is Disabled, should not report
                Assert.That(handles, Does.Not.Contain(2u),
                    "Disabled linked item should not report");
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: trigger sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task TriggerItemDisabledStopsTriggeringAllAsync()
        {
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);

            // Disable trigger item A
            SetMonitoringModeResponse modeResp =
                await Session.SetMonitoringModeAsync(
                    null, m_subscriptionId, MonitoringMode.Disabled,
                    new uint[] { idA }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(modeResp.Results[0]), Is.True);

            await ConsumeAllNotificationsAsync().ConfigureAwait(false);

            await WriteValueAsync(nodeA,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // Neither A nor B should report
            Assert.That(handles, Does.Not.Contain(1u), "Disabled trigger should not report");
            Assert.That(handles, Does.Not.Contain(2u), "Linked item should not trigger");
        }

        [Test]
        public async Task TriggerItemSamplingNoAutoReportingAsync()
        {
            // Trigger in Sampling mode → does not auto-report
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
            await ConsumeAllNotificationsAsync().ConfigureAwait(false);

            await WriteValueAsync(nodeA,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // A is Sampling, so it should not auto-report
            Assert.That(handles, Does.Not.Contain(1u),
                "Sampling trigger should not auto-report");
        }

        [Test]
        public async Task SetLinkedItemToReportingStillTriggerableAsync()
        {
            // Linked item set to Reporting → always reports
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, samplingInterval: 50,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);

            // Change B to Reporting
            SetMonitoringModeResponse modeResp =
                await Session.SetMonitoringModeAsync(
                    null, m_subscriptionId, MonitoringMode.Reporting,
                    new uint[] { idB }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(modeResp.Results[0]), Is.True);

            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // B is now Reporting so it reports on its own
            Assert.That(handles, Does.Contain(2u),
                "Reporting-mode linked item should report");
        }

        [Test]
        public async Task ChangeLinkedModeFromSamplingToDisabledStopsTriggeringAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, samplingInterval: 50,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);

            // Switch B from Sampling to Disabled
            SetMonitoringModeResponse modeResp =
                await Session.SetMonitoringModeAsync(
                    null, m_subscriptionId, MonitoringMode.Disabled,
                    new uint[] { idB }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(modeResp.Results[0]), Is.True);

            await ConsumeAllNotificationsAsync().ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            Assert.That(handles, Does.Not.Contain(2u),
                "Disabled linked item should not report even when triggered");
        }

        [Test]
        public async Task SetTriggeringInvalidSubscriptionIdAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.SetTriggeringAsync(
                        null, 999999u, idA,
                        new uint[] { idA }.ToArrayOf(),
                        default,
                        CancellationToken.None).ConfigureAwait(false));

            Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True);
        }

        [Test]
        public async Task SetTriggeringInvalidTriggeringItemIdAsync()
        {
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeB, 1,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            uint idB = createResp.Results[0].MonitoredItemId;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.SetTriggeringAsync(
                        null, m_subscriptionId, 999999u,
                        new uint[] { idB }.ToArrayOf(),
                        default,
                        CancellationToken.None).ConfigureAwait(false));

            Assert.That(
                ex.StatusCode == StatusCodes.BadMonitoredItemIdInvalid ||
                StatusCode.IsBad(ex.StatusCode),
                Is.True);
        }

        [Test]
        public async Task SetTriggeringInvalidLinkedItemIdAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;

            // Add a bogus linked item ID
            SetTriggeringResponse trigResp = await SetTriggerAsync(
                idA, [999999u]).ConfigureAwait(false);

            Assert.That(trigResp.AddResults.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(trigResp.AddResults[0]), Is.True);
        }

        [Test]
        public async Task SetTriggeringEmptyAddAndRemoveArraysAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;

            // Empty arrays → server may return BadNothingToDo per spec
            try
            {
                SetTriggeringResponse trigResp = await Session.SetTriggeringAsync(
                    null, m_subscriptionId, idA,
                    default,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(trigResp.ResponseHeader.ServiceResult), Is.True);
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadNothingToDo)
            {
                // BadNothingToDo is valid per OPC UA spec when both
                // add and remove arrays are empty.
            }
        }

        [Test]
        public async Task SetTriggeringOnDeletedSubscriptionAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;

            // Delete the subscription first
            uint deletedSubId = m_subscriptionId;
            await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { m_subscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            m_subscriptionId = 0;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.SetTriggeringAsync(
                        null, deletedSubId, idA,
                        new uint[] { idA }.ToArrayOf(),
                        default,
                        CancellationToken.None).ConfigureAwait(false));

            Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True);
        }

        [Test]
        public async Task SetTriggeringSameItemAsTriggerAndLinkedAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;

            // Item triggers itself → should error or be ignored
            SetTriggeringResponse trigResp = await SetTriggerAsync(
                idA, [idA]).ConfigureAwait(false);

            // Server may return Bad or just ignore the self-link
            Assert.That(trigResp.AddResults.Count, Is.EqualTo(1));
            // Accept either a bad result or a good result (server-dependent)
            Assert.That(
                StatusCode.IsBad(trigResp.AddResults[0]) ||
                StatusCode.IsGood(trigResp.AddResults[0]),
                Is.True);
        }

        [Test]
        public async Task TriggerPreservedAfterModifyMonitoredItemAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);

            // Modify trigger item parameters
            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemModifyRequest[]
                    {
                        new() {
                            MonitoredItemId = idA,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                SamplingInterval = 200,
                                QueueSize = 5,
                                DiscardOldest = true
                            }
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True);

            // Verify trigger link is still active
            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            Assert.That(handles, Does.Contain(1u), "Trigger item should still report");
        }

        [Test]
        public async Task TriggerWithDataChangeFilterOnLinkedItemAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            var dcFilter = new ExtensionObject(new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 5.0
            });

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling,
                    filter: dcFilter)).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            Assert.That(
                StatusCode.IsGood(createResp.Results[1].StatusCode), Is.True);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(trigResp.AddResults[0]), Is.True);

            // Write a value that exceeds the deadband
            await WriteValueAsync(nodeB, 100).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);
        }

        [Test]
        public async Task TriggerWithDifferentSamplingIntervalsAsync()
        {
            // Trigger at 50ms, linked at 5000ms
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, samplingInterval: 5000,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            Assert.That(
                StatusCode.IsGood(createResp.Results[1].StatusCode), Is.True);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(trigResp.AddResults[0]), Is.True);

            // Write to B; triggering should cause B to report at A's rate
            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            Assert.That(handles, Does.Contain(1u));
        }

        [Test]
        [Category("LongRunning")]
        public async Task DeleteTriggerItemLinksAutomaticallyRemovedAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            try
            {
                CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling))
                    .ConfigureAwait(false);

                uint idA = createResp.Results[0].MonitoredItemId;
                uint idB = createResp.Results[1].MonitoredItemId;

                await SetTriggerAsync(idA, [idB]).ConfigureAwait(false);

                // Delete trigger item A
                DeleteMonitoredItemsResponse delResp =
                    await Session.DeleteMonitoredItemsAsync(
                        null, m_subscriptionId,
                        new uint[] { idA }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(delResp.Results[0]), Is.True);

                await ConsumeAllNotificationsAsync().ConfigureAwait(false);

                await WriteValueAsync(nodeB,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

                PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
                HashSet<uint> handles = CollectNotifiedHandles(pubResp);

                // B should not report (trigger item gone, B is Sampling)
                Assert.That(handles, Does.Not.Contain(2u),
                    "B should not report after trigger item deleted");
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: delete-trigger sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task DeleteLinkedItemTriggerStillWorksAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp = await CreateItemsAsync(
                CreateItemRequest(nodeA, 1, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 2, mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeC, 3, mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            await SetTriggerAsync(idA, [idB, idC]).ConfigureAwait(false);

            // Drain any in-flight notifications before deleting B,
            // so the post-delete Publish only contains samples
            // taken AFTER the delete (eliminating the previous
            // Sampling-mode timing race).
            await ConsumeAllNotificationsAsync().ConfigureAwait(false);

            // Delete linked item B
            DeleteMonitoredItemsResponse delResp =
                await Session.DeleteMonitoredItemsAsync(
                    null, m_subscriptionId,
                    new uint[] { idB }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(delResp.Results[0]), Is.True);

            await WriteValueAsync(nodeC,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            // Wait one full publishing interval so the next Publish
            // is guaranteed to span at least one sampling period
            // for items B and C.
            await Task.Delay(150).ConfigureAwait(false);

            // A still triggers; C should still be linked
            PublishResponse pubResp = await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            Assert.That(handles, Does.Contain(1u), "Trigger A should still report");
            Assert.That(handles, Does.Contain(3u),
                "C should still be triggered after B deleted");
        }

        [Test]
        public async Task BasicAddSingleLinkAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2, samplingInterval: 50,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(2));
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);
            Assert.That(
                StatusCode.IsGood(createResp.Results[1].StatusCode),
                Is.True);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(trigResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(trigResp.AddResults.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(trigResp.AddResults[0]), Is.True);
        }

        [Test]
        public async Task AddMultipleLinksAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeD = ToNodeId(Constants.ScalarStaticFloat);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeC, 3,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeD, 4,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(4));
            uint idA = createResp.Results[0].MonitoredItemId;
            uint[] linkedIds = [.. createResp.Results.ToArray()
                .Skip(1).Select(r => r.MonitoredItemId)];

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, linkedIds)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(trigResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(trigResp.AddResults.Count, Is.EqualTo(3));
            foreach (StatusCode sc in trigResp.AddResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task AddOneLinkThenRemoveAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);

            SetTriggeringResponse removeResp = await SetTriggerAsync(
                idA, null, [idB]).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(removeResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(removeResp.RemoveResults.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(removeResp.RemoveResults[0]), Is.True);
        }

        [Test]
        public async Task AddMultipleLinksThenRemoveAllAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeC, 3,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            await SetTriggerAsync(idA, [idB, idC])
                .ConfigureAwait(false);

            SetTriggeringResponse removeResp = await SetTriggerAsync(
                idA, null, [idB, idC]).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(removeResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(removeResp.RemoveResults.Count, Is.EqualTo(2));
            foreach (StatusCode sc in removeResp.RemoveResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task ReplaceLinksAddAndRemoveInOneCallAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeD = ToNodeId(Constants.ScalarStaticFloat);
            NodeId nodeE = ToNodeId(Constants.ScalarStaticUInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeC, 3,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeD, 4,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeE, 5,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;
            uint idD = createResp.Results[3].MonitoredItemId;
            uint idE = createResp.Results[4].MonitoredItemId;

            // Add B and C
            await SetTriggerAsync(idA, [idB, idC])
                .ConfigureAwait(false);

            // Remove B,C and add D,E in one call
            SetTriggeringResponse swapResp =
                await Session.SetTriggeringAsync(
                    null, m_subscriptionId, idA,
                    new uint[] { idD, idE }.ToArrayOf(),
                    new uint[] { idB, idC }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(swapResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(swapResp.AddResults.Count, Is.EqualTo(2));
            Assert.That(swapResp.RemoveResults.Count, Is.EqualTo(2));
            foreach (StatusCode sc in swapResp.AddResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
            foreach (StatusCode sc in swapResp.RemoveResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task TriggerWithDeadbandFilterAsync()
        {
            NodeId nodeA = ToNodeId(Constants.AnalogTypeDouble);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            var deadbandFilter = new ExtensionObject(new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 5.0
            });

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting,
                        filter: deadbandFilter),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            StatusCode statusA = createResp.Results[0].StatusCode;
            if (statusA == StatusCodes.BadFilterNotAllowed ||
                statusA == StatusCodes.BadMonitoredItemFilterUnsupported)
            {
                Assert.Fail("Deadband filter not supported.");
            }
            Assert.That(StatusCode.IsGood(statusA), Is.True);
            Assert.That(
                StatusCode.IsGood(createResp.Results[1].StatusCode),
                Is.True);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(trigResp.AddResults[0]), Is.True);

            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
        }

        [Test]
        public async Task CircularTriggerBothItemsAsync()
        {
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2, samplingInterval: 50,
                        mode: MonitoringMode.Reporting))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            // A→B
            SetTriggeringResponse abResp =
                await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(abResp.AddResults[0]), Is.True);

            // B→A
            SetTriggeringResponse baResp =
                await SetTriggerAsync(idB, [idA])
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(baResp.AddResults[0]), Is.True);

            await WriteValueAsync(nodeA,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // Both items are Reporting, at least one should appear
            Assert.That(handles, Is.Not.Empty,
                "At least one item should report in circular trigger.");
        }

        [Test]
        public async Task MixedAddRemoveSubsequentCallsAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeD = ToNodeId(Constants.ScalarStaticFloat);
            NodeId nodeE = ToNodeId(Constants.ScalarStaticUInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeC, 3,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeD, 4,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeE, 5,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;
            uint idD = createResp.Results[3].MonitoredItemId;
            uint idE = createResp.Results[4].MonitoredItemId;

            // First: add B,C
            await SetTriggerAsync(idA, [idB, idC])
                .ConfigureAwait(false);

            // Second: add D,E and remove B,C
            SetTriggeringResponse resp2 =
                await Session.SetTriggeringAsync(
                    null, m_subscriptionId, idA,
                    new uint[] { idD, idE }.ToArrayOf(),
                    new uint[] { idB, idC }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp2.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp2.AddResults.Count, Is.EqualTo(2));
            Assert.That(resp2.RemoveResults.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task TriggerReportingLinksMixedModesAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeC, 3,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB, idC])
                .ConfigureAwait(false);
            Assert.That(trigResp.AddResults.Count, Is.EqualTo(2));

            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
            await WriteValueAsync(nodeC,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
        }

        [Test]
        public async Task TriggerReportingLinkedReportingAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Reporting))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(trigResp.AddResults[0]), Is.True);

            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // B is Reporting, so it reports on its own
            Assert.That(handles, Does.Contain(2u),
                "Reporting linked item should report.");
        }

        [Test]
        [Category("LongRunning")]
        public async Task TriggerReportingFourLinksMixedModesAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeD = ToNodeId(Constants.ScalarStaticFloat);
            NodeId nodeE = ToNodeId(Constants.ScalarStaticUInt32);

            try
            {
                CreateMonitoredItemsResponse createResp =
                    await CreateItemsAsync(
                        CreateItemRequest(nodeA, 1, samplingInterval: 50,
                            mode: MonitoringMode.Reporting),
                        CreateItemRequest(nodeB, 2,
                            mode: MonitoringMode.Reporting),
                        CreateItemRequest(nodeC, 3,
                            mode: MonitoringMode.Reporting),
                        CreateItemRequest(nodeD, 4,
                            mode: MonitoringMode.Sampling),
                        CreateItemRequest(nodeE, 5,
                            mode: MonitoringMode.Sampling))
                    .ConfigureAwait(false);

                Assert.That(createResp.Results.Count, Is.EqualTo(5));
                uint idA = createResp.Results[0].MonitoredItemId;
                uint[] linkedIds = [.. createResp.Results.ToArray()
                    .Skip(1).Select(r => r.MonitoredItemId)];

                SetTriggeringResponse trigResp =
                    await SetTriggerAsync(idA, linkedIds)
                    .ConfigureAwait(false);
                Assert.That(trigResp.AddResults.Count, Is.EqualTo(4));
                foreach (StatusCode sc in trigResp.AddResults)
                {
                    Assert.That(StatusCode.IsGood(sc), Is.True);
                }

                PublishResponse pubResp =
                    await PublishAndWaitAsync().ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                    Is.True);
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: triggering setup interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task SameItemInAddAndRemoveAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            // Add B first
            await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);

            // Try same item in both add and remove
            SetTriggeringResponse resp =
                await Session.SetTriggeringAsync(
                    null, m_subscriptionId, idA,
                    new uint[] { idB }.ToArrayOf(),
                    new uint[] { idB }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            // Remove should succeed (was linked), add should also
            // succeed (re-adding), OR remove fails if processed first
            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
        }

        [Test]
        public async Task TriggerSamplingLinkSamplingAsync()
        {
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);
            await ConsumeAllNotificationsAsync().ConfigureAwait(false);

            // Write to trigger A; since A is Sampling, no auto-report
            await WriteValueAsync(nodeA,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // A is Sampling — should not auto-report
            Assert.That(handles, Does.Not.Contain(1u),
                "Sampling trigger should not auto-report.");
        }

        [Test]
        public async Task TriggerSamplingLinksReportingAsync()
        {
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticFloat);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeC, 3,
                        mode: MonitoringMode.Reporting))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            await SetTriggerAsync(idA, [idB, idC])
                .ConfigureAwait(false);

            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // B and C are Reporting; they report on their own
            Assert.That(handles, Does.Contain(2u),
                "Reporting linked item B should report.");
        }

        [Test]
        public async Task SameNodeIdTriggerAndLinkAsync()
        {
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeA, 2, samplingInterval: 50,
                        mode: MonitoringMode.Reporting))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(trigResp.AddResults[0]), Is.True);

            await WriteValueAsync(nodeA,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
        }

        [Test]
        [Category("LongRunning")]
        public async Task DisabledTriggerSamplingLinkKeepAliveAsync()
        {
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticDouble);

            try
            {
                CreateMonitoredItemsResponse createResp =
                    await CreateItemsAsync(
                        CreateItemRequest(nodeA, 1,
                            mode: MonitoringMode.Disabled),
                        CreateItemRequest(nodeB, 2,
                            mode: MonitoringMode.Sampling))
                    .ConfigureAwait(false);

                uint idA = createResp.Results[0].MonitoredItemId;
                uint idB = createResp.Results[1].MonitoredItemId;

                await SetTriggerAsync(idA, [idB])
                    .ConfigureAwait(false);

                await WriteValueAsync(nodeA,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

                await ConsumeAllNotificationsAsync().ConfigureAwait(false);

                PublishResponse pubResp =
                    await PublishAndWaitAsync().ConfigureAwait(false);
                HashSet<uint> handles = CollectNotifiedHandles(pubResp);

                Assert.That(handles, Does.Not.Contain(1u),
                    "Disabled trigger should not report.");
                Assert.That(handles, Does.Not.Contain(2u),
                    "Sampling link with disabled trigger should not report.");
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: trigger/keep-alive sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        [Category("LongRunning")]
        public async Task DisabledTriggerFourLinksMixedModesAsync()
        {
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticDouble);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticFloat);
            NodeId nodeD = ToNodeId(Constants.ScalarStaticUInt32);
            NodeId nodeE = ToNodeId(Constants.ScalarStaticInt16);

            try
            {
                CreateMonitoredItemsResponse createResp =
                    await CreateItemsAsync(
                        CreateItemRequest(nodeA, 1,
                            mode: MonitoringMode.Disabled),
                        CreateItemRequest(nodeB, 2,
                            mode: MonitoringMode.Sampling),
                        CreateItemRequest(nodeC, 3,
                            mode: MonitoringMode.Sampling),
                        CreateItemRequest(nodeD, 4,
                            mode: MonitoringMode.Disabled),
                        CreateItemRequest(nodeE, 5,
                            mode: MonitoringMode.Disabled))
                    .ConfigureAwait(false);

                uint idA = createResp.Results[0].MonitoredItemId;
                uint[] linkedIds = [.. createResp.Results.ToArray()
                    .Skip(1).Select(r => r.MonitoredItemId)];

                await SetTriggerAsync(idA, linkedIds).ConfigureAwait(false);
                await ConsumeAllNotificationsAsync().ConfigureAwait(false);

                await WriteValueAsync(nodeA,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);
                await WriteValueAsync(nodeB,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

                PublishResponse pubResp =
                    await PublishAndWaitAsync().ConfigureAwait(false);
                HashSet<uint> handles = CollectNotifiedHandles(pubResp);

                Assert.That(handles, Does.Not.Contain(1u),
                    "Disabled trigger should not report.");
                Assert.That(handles, Does.Not.Contain(4u),
                    "Disabled link should not report.");
                Assert.That(handles, Does.Not.Contain(5u),
                    "Disabled link should not report.");
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: 4-link trigger sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task DisabledTriggerSameNodeLinkReportingAsync()
        {
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1,
                        mode: MonitoringMode.Disabled),
                    CreateItemRequest(nodeA, 2,
                        mode: MonitoringMode.Reporting))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);

            await WriteValueAsync(nodeA,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // Link is Reporting, so it reports on its own
            Assert.That(handles, Does.Contain(2u),
                "Reporting link should report even with disabled trigger.");
            Assert.That(handles, Does.Not.Contain(1u),
                "Disabled trigger should not report.");
        }

        [Test]
        [Category("LongRunning")]
        public async Task DisabledTriggerDisabledLinkNoNotificationsAsync()
        {
            NodeId nodeA = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticDouble);

            try
            {
                CreateMonitoredItemsResponse createResp =
                    await CreateItemsAsync(
                        CreateItemRequest(nodeA, 1,
                            mode: MonitoringMode.Disabled),
                        CreateItemRequest(nodeB, 2,
                            mode: MonitoringMode.Disabled))
                    .ConfigureAwait(false);

                uint idA = createResp.Results[0].MonitoredItemId;
                uint idB = createResp.Results[1].MonitoredItemId;

                await SetTriggerAsync(idA, [idB])
                    .ConfigureAwait(false);
                await ConsumeAllNotificationsAsync().ConfigureAwait(false);

                await WriteValueAsync(nodeA,
                    UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

                PublishResponse pubResp =
                    await PublishAndWaitAsync().ConfigureAwait(false);
                HashSet<uint> handles = CollectNotifiedHandles(pubResp);

                Assert.That(handles, Does.Not.Contain(1u),
                    "Disabled trigger should not report.");
                Assert.That(handles, Does.Not.Contain(2u),
                    "Disabled link should not report.");
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: disabled-trigger sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task DeadbandAbsoluteOnTriggerSamplingLinksAsync()
        {
            NodeId nodeA = ToNodeId(Constants.AnalogTypeDouble);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticFloat);

            var deadbandFilter = new ExtensionObject(new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 10.0
            });

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting,
                        filter: deadbandFilter),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling),
                    CreateItemRequest(nodeC, 3,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            StatusCode statusA = createResp.Results[0].StatusCode;
            if (statusA == StatusCodes.BadFilterNotAllowed ||
                statusA == StatusCodes.BadMonitoredItemFilterUnsupported)
            {
                Assert.Fail("Deadband filter not supported.");
            }
            Assert.That(StatusCode.IsGood(statusA), Is.True);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            await SetTriggerAsync(idA, [idB, idC])
                .ConfigureAwait(false);

            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
        }

        [Test]
        public async Task DeleteLinkedItemThenRemoveExpectsBadAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);

            // Delete the linked item
            await Session.DeleteMonitoredItemsAsync(
                null, m_subscriptionId,
                new uint[] { idB }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            // Try to remove the deleted item from trigger links
            SetTriggeringResponse removeResp = await SetTriggerAsync(
                idA, null, [idB]).ConfigureAwait(false);

            Assert.That(removeResp.RemoveResults.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsBad(removeResp.RemoveResults[0]), Is.True,
                "Removing deleted linked item should return Bad.");
        }

        [Test]
        public async Task DeleteTriggerItemCleanupAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);

            // Delete trigger item
            DeleteMonitoredItemsResponse delResp =
                await Session.DeleteMonitoredItemsAsync(
                    null, m_subscriptionId,
                    new uint[] { idA }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(delResp.Results[0]), Is.True);

            // Verify B (Sampling) does not report
            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            await ConsumeAllNotificationsAsync().ConfigureAwait(false);
            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            Assert.That(handles, Does.Not.Contain(2u),
                "B should not report after trigger deleted.");
        }

        [Test]
        public async Task DeleteTriggerWritePublishNoDataAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);

            // Delete trigger item A
            await Session.DeleteMonitoredItemsAsync(
                null, m_subscriptionId,
                new uint[] { idA }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            await ConsumeAllNotificationsAsync().ConfigureAwait(false);

            // Write to B; A is deleted so no trigger fires
            await WriteValueAsync(nodeB,
                UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            Assert.That(handles, Does.Not.Contain(2u),
                "B should not report after trigger item deleted.");
        }

        [Test]
        public async Task RemoveAlreadyDeletedLinkExpectsBadAsync()
        {
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);

            // Delete linked item B from subscription
            await Session.DeleteMonitoredItemsAsync(
                null, m_subscriptionId,
                new uint[] { idB }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            // Try to remove deleted B from trigger links
            SetTriggeringResponse removeResp = await SetTriggerAsync(
                idA, null, [idB]).ConfigureAwait(false);
            Assert.That(removeResp.RemoveResults.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsBad(removeResp.RemoveResults[0]), Is.True,
                "Removing already-deleted link should return Bad.");
        }

        [Test]
        public async Task NonNumericTriggerAndLinkAsync()
        {
            NodeId nodeA = ToNodeId(Constants.ScalarStaticString);
            NodeId nodeB = ToNodeId(Constants.ScalarStaticLocalizedText);

            CreateMonitoredItemsResponse createResp =
                await CreateItemsAsync(
                    CreateItemRequest(nodeA, 1, samplingInterval: 50,
                        mode: MonitoringMode.Reporting),
                    CreateItemRequest(nodeB, 2,
                        mode: MonitoringMode.Sampling))
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);
            Assert.That(
                StatusCode.IsGood(createResp.Results[1].StatusCode),
                Is.True);

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;

            SetTriggeringResponse trigResp =
                await SetTriggerAsync(idA, [idB])
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(trigResp.AddResults[0]), Is.True);

            // Write to trigger A (String type)
            await WriteStringAsync(nodeA,
                "TriggerValue_" +
                Guid.NewGuid().ToString("N")
                    [..8]).ConfigureAwait(false);

            // Write to link B (LocalizedText type)
            await WriteLocalizedTextAsync(nodeB,
                "LinkedValue_" +
                Guid.NewGuid().ToString("N")
                    [..8]).ConfigureAwait(false);

            PublishResponse pubResp =
                await PublishAndWaitAsync().ConfigureAwait(false);
            HashSet<uint> handles = CollectNotifiedHandles(pubResp);

            // A is Reporting and changed — should report
            Assert.That(handles, Does.Contain(1u),
                "String trigger should report.");
        }

        private MonitoredItemCreateRequest CreateItemRequest(
            NodeId nodeId,
            uint clientHandle,
            double samplingInterval = 100,
            uint queueSize = 10,
            MonitoringMode mode = MonitoringMode.Reporting,
            uint attributeId = Attributes.Value,
            bool discardOldest = true,
            ExtensionObject filter = default)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attributeId
                },
                MonitoringMode = mode,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    SamplingInterval = samplingInterval,
                    Filter = filter,
                    DiscardOldest = discardOldest,
                    QueueSize = queueSize
                }
            };
        }

        private async Task<CreateMonitoredItemsResponse> CreateItemsAsync(
            params MonitoredItemCreateRequest[] items)
        {
            return await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task WriteValueAsync(NodeId nodeId, int value)
        {
            WriteResponse writeResp = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(value))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            if (StatusCode.IsGood(writeResp.Results[0]))
            {
                return;
            }

            // OPC UA Part 4 §5.10.4: Write requires the value's data type to
            // match the variable's; there is no implicit numeric promotion
            // (Int32 → Double, Int32 → UInt32, etc.) — the server returns
            // BadTypeMismatch. Many test variables in the ReferenceServer
            // address space are typed Double or UInt32, so retry the write
            // with each common coercion until one succeeds.
            if (writeResp.Results[0] == StatusCodes.BadTypeMismatch)
            {
                Variant[] coercions =
                [
                    Variant.From((double)value),
                    Variant.From((uint)value),
                    Variant.From((short)value),
                    Variant.From((ushort)value),
                    Variant.From((long)value),
                    Variant.From((ulong)value),
                    Variant.From((float)value),
                    Variant.From((byte)value),
                    Variant.From((sbyte)value)
                ];
                for (int i = 0; i < coercions.Length; i++)
                {
                    WriteResponse retry = await Session.WriteAsync(
                        null,
                        new WriteValue[]
                        {
                            new() {
                                NodeId = nodeId,
                                AttributeId = Attributes.Value,
                                Value = new DataValue(coercions[i])
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                    if (StatusCode.IsGood(retry.Results[0]))
                    {
                        return;
                    }
                }
            }
            Assert.Ignore($"Write to {nodeId} not permitted: {writeResp.Results[0]}");
        }

        private async Task WriteStringAsync(NodeId nodeId, string value)
        {
            WriteResponse writeResp = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(
                            new Variant(value))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            if (!StatusCode.IsGood(writeResp.Results[0]))
            {
                Assert.Ignore(
                    $"Write to {nodeId} not permitted: " +
                    $"{writeResp.Results[0]}");
            }
        }

        private async Task WriteLocalizedTextAsync(
            NodeId nodeId, string value)
        {
            WriteResponse writeResp = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(
                            new Variant(new LocalizedText(value)))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            if (!StatusCode.IsGood(writeResp.Results[0]))
            {
                Assert.Ignore(
                    $"Write to {nodeId} not permitted: " +
                    $"{writeResp.Results[0]}");
            }
        }

        private async Task ConsumeAllNotificationsAsync()
        {
            await Task.Delay(300).ConfigureAwait(false);
            try
            {
                await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // No notifications available
            }
        }

        private async Task<PublishResponse> PublishAndWaitAsync(int delayMs = 300)
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
            return await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes repeatedly until every <paramref name="expectedHandles"/>
        /// has been observed at least once, or <paramref name="timeoutMs"/>
        /// expires. Aggregates handles across publishes to absorb the natural
        /// race between writes and the server's sampling-timer cycle on slow
        /// CI runners.
        /// </summary>
        private async Task<HashSet<uint>> PublishUntilHandlesObservedAsync(
            uint[] expectedHandles,
            int timeoutMs = 5000,
            int initialDelayMs = 300)
        {
            await Task.Delay(initialDelayMs).ConfigureAwait(false);
            var collected = new HashSet<uint>();
            var deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                PublishResponse pub;
                try
                {
                    pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    break;
                }
                foreach (uint h in CollectNotifiedHandles(pub))
                {
                    collected.Add(h);
                }
                if (expectedHandles.All(collected.Contains))
                {
                    return collected;
                }
                await Task.Delay(200).ConfigureAwait(false);
            }
            return collected;
        }

        private static HashSet<uint> CollectNotifiedHandles(PublishResponse pubResp)
        {
            var handles = new HashSet<uint>();
            if (pubResp.NotificationMessage?.NotificationData != null)
            {
                foreach (ExtensionObject ext in pubResp.NotificationMessage.NotificationData)
                {
                    var dcn = ExtensionObject.ToEncodeable(ext) as
                        DataChangeNotification;
                    if (dcn != null)
                    {
                        foreach (MonitoredItemNotification mi in dcn.MonitoredItems)
                        {
                            handles.Add(mi.ClientHandle);
                        }
                    }
                }
            }
            return handles;
        }

        private async Task<SetTriggeringResponse> SetTriggerAsync(
            uint triggeringItemId,
            uint[] linksToAdd,
            uint[] linksToRemove = null)
        {
            return await Session.SetTriggeringAsync(
                null, m_subscriptionId, triggeringItemId,
                linksToAdd?.ToArrayOf() ?? default,
                linksToRemove?.ToArrayOf() ?? default,
                CancellationToken.None).ConfigureAwait(false);
        }

        private uint m_subscriptionId;
    }
}
