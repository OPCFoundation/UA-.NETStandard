/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Test Client Services.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    public class SubscriptionTest
    {
        const string SubscriptionTestXml = "SubscriptionTest.xml";
        const int MaxReferences = 100;
        ServerFixture<ReferenceServer> m_serverFixture;
        ClientFixture m_clientFixture;
        ReferenceServer m_server;
        Session m_session;
        Uri m_url;

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public Task OneTimeSetUp()
        {
            return OneTimeSetUpAsync(null);
        }

        /// <summary>
        /// Setup a server and client fixture.
        /// </summary>
        /// <param name="writer">The test output writer.</param>
        public async Task OneTimeSetUpAsync(TextWriter writer = null)
        {
            // start Ref server
            m_serverFixture = new ServerFixture<ReferenceServer>();
            m_clientFixture = new ClientFixture();
            m_serverFixture.AutoAccept = true;
            m_serverFixture.OperationLimits = true;
            if (writer != null)
            {
                m_serverFixture.TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security;
            }
            m_server = await m_serverFixture.StartAsync(writer ?? TestContext.Out).ConfigureAwait(false);
            await m_clientFixture.LoadClientConfiguration();
            m_url = new Uri("opc.tcp://localhost:" + m_serverFixture.Port.ToString());
            m_session = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.Basic256Sha256);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_session.Close();
            m_session.Dispose();
            m_session = null;
            await m_serverFixture.StopAsync();
            await Task.Delay(1000);
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_serverFixture.SetTraceOutput(TestContext.Out);
        }
        #endregion

        #region Test Methods
        [Test, Order(100)]
        public void AddSubscription()
        {
            var subscription = new Subscription();

            // check keepAlive
            int keepAlive = 0;
            m_session.KeepAlive += (Session sender, KeepAliveEventArgs e) => { keepAlive++; };

            // add current time
            var list = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusCurrentTime", StartNodeId = VariableIds.Server_ServerStatus_CurrentTime
                }
            };
            list.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                foreach (var value in item.DequeueValues())
                {
                    TestContext.Out.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                }
            });

            subscription = new Subscription(m_session.DefaultSubscription);
            TestContext.Out.WriteLine("MaxMessageCount: {0}", subscription.MaxMessageCount);
            TestContext.Out.WriteLine("MaxNotificationsPerPublish: {0}", subscription.MaxNotificationsPerPublish);
            TestContext.Out.WriteLine("MinLifetimeInterval: {0}", subscription.MinLifetimeInterval);

            subscription.AddItem(list.First());
            Assert.AreEqual(1, subscription.MonitoredItemCount);
            Assert.True(subscription.ChangesPending);
            m_session.AddSubscription(subscription);
            subscription.Create();

            // add state
            var list2 = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusState", StartNodeId = VariableIds.Server_ServerStatus_State
                }
            };
            list2.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                foreach (var value in item.DequeueValues())
                {
                    TestContext.Out.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                }
            });
            subscription.AddItems(list);
            subscription.ApplyChanges();
            subscription.SetPublishingMode(false);
            Assert.False(subscription.PublishingEnabled);
            subscription.SetPublishingMode(true);
            Assert.True(subscription.PublishingEnabled);
            Assert.False(subscription.PublishingStopped);

            subscription.Priority = 200;
            subscription.Modify();

            // save
            m_session.Save(SubscriptionTestXml);

            Thread.Sleep(5000);
            TestContext.Out.WriteLine("CurrentKeepAliveCount   : {0}", subscription.CurrentKeepAliveCount);
            TestContext.Out.WriteLine("CurrentPublishingEnabled: {0}", subscription.CurrentPublishingEnabled);
            TestContext.Out.WriteLine("CurrentPriority         : {0}", subscription.CurrentPriority);
            TestContext.Out.WriteLine("PublishTime             : {0}", subscription.PublishTime);
            TestContext.Out.WriteLine("LastNotificationTime    : {0}", subscription.LastNotificationTime);
            TestContext.Out.WriteLine("SequenceNumber          : {0}", subscription.SequenceNumber);
            TestContext.Out.WriteLine("NotificationCount       : {0}", subscription.NotificationCount);
            TestContext.Out.WriteLine("LastNotification        : {0}", subscription.LastNotification);
            TestContext.Out.WriteLine("Notifications           : {0}", subscription.Notifications.Count());
            TestContext.Out.WriteLine("OutstandingMessageWorker: {0}", subscription.OutstandingMessageWorkers);

            subscription.ConditionRefresh();
            var sre = Assert.Throws<ServiceResultException>(() => subscription.Republish(subscription.SequenceNumber));
            Assert.AreEqual(StatusCodes.BadMessageNotAvailable, sre.StatusCode);

            subscription.RemoveItems(list);
            subscription.ApplyChanges();

            subscription.RemoveItem(list2.First());

            var result = m_session.RemoveSubscription(subscription);
            Assert.True(result);
        }

        [Test, Order(200)]
        public void LoadSubscription()
        {
            if (!File.Exists(SubscriptionTestXml)) Assert.Ignore("Save file {0} does not exist yet", SubscriptionTestXml);

            // save
            var subscriptions = m_session.Load(SubscriptionTestXml);
            Assert.NotNull(subscriptions);
            Assert.IsNotEmpty(subscriptions);

            foreach (var subscription in subscriptions)
            {
                m_session.AddSubscription(subscription);
                subscription.Create();
            }

            Thread.Sleep(5000);

            foreach (var subscription in subscriptions)
            {
                TestContext.Out.WriteLine("Subscription            : {0}", subscription.DisplayName);
                TestContext.Out.WriteLine("CurrentKeepAliveCount   : {0}", subscription.CurrentKeepAliveCount);
                TestContext.Out.WriteLine("CurrentPublishingEnabled: {0}", subscription.CurrentPublishingEnabled);
                TestContext.Out.WriteLine("CurrentPriority         : {0}", subscription.CurrentPriority);
                TestContext.Out.WriteLine("PublishTime             : {0}", subscription.PublishTime);
                TestContext.Out.WriteLine("LastNotificationTime    : {0}", subscription.LastNotificationTime);
                TestContext.Out.WriteLine("SequenceNumber          : {0}", subscription.SequenceNumber);
                TestContext.Out.WriteLine("NotificationCount       : {0}", subscription.NotificationCount);
                TestContext.Out.WriteLine("LastNotification        : {0}", subscription.LastNotification);
                TestContext.Out.WriteLine("Notifications           : {0}", subscription.Notifications.Count());
                TestContext.Out.WriteLine("OutstandingMessageWorker: {0}", subscription.OutstandingMessageWorkers);
            }

            var result = m_session.RemoveSubscriptions(subscriptions);
            Assert.True(result);

        }
        #endregion

        #region Private Methods
        #endregion
    }
}
