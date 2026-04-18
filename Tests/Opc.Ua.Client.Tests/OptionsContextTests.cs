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

using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Tests for the option and state record types that are
    /// source-generated or manually defined in the Client library.
    /// </summary>
    [TestFixture]
    [Category("ClientOptions")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class OptionsContextTests
    {
        [Test]
        public void BrowserOptionsDefaultValues()
        {
            var options = new BrowserOptions();

            Assert.That(options.BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
            Assert.That(options.IncludeSubtypes, Is.True);
            Assert.That(options.ResultMask, Is.EqualTo((uint)BrowseResultMask.All));
            Assert.That(options.MaxReferencesReturned, Is.EqualTo(0u));
            Assert.That(options.ContinuationPointPolicy,
                Is.EqualTo(ContinuationPointPolicy.Default));
            Assert.That(options.MaxNodesPerBrowse, Is.EqualTo(0u));
            Assert.That(options.MaxBrowseContinuationPoints, Is.EqualTo((ushort)0));
            Assert.That(options.NodeClassMask, Is.EqualTo(0));
            Assert.That(options.ReferenceTypeId, Is.EqualTo(NodeId.Null));
            Assert.That(options.RequestHeader, Is.Null);
            Assert.That(options.View, Is.Null);
        }

        [Test]
        public void BrowserOptionsWithCustomValues()
        {
            var options = new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Inverse,
                IncludeSubtypes = false,
                MaxReferencesReturned = 100,
                NodeClassMask = 0xFF,
                ResultMask = 63,
                ContinuationPointPolicy = ContinuationPointPolicy.Balanced,
                MaxNodesPerBrowse = 50,
                MaxBrowseContinuationPoints = 5,
                ReferenceTypeId = new NodeId(33)
            };

            Assert.That(options.BrowseDirection, Is.EqualTo(BrowseDirection.Inverse));
            Assert.That(options.IncludeSubtypes, Is.False);
            Assert.That(options.MaxReferencesReturned, Is.EqualTo(100u));
            Assert.That(options.NodeClassMask, Is.EqualTo(0xFF));
            Assert.That(options.ResultMask, Is.EqualTo(63u));
            Assert.That(options.ContinuationPointPolicy,
                Is.EqualTo(ContinuationPointPolicy.Balanced));
            Assert.That(options.MaxNodesPerBrowse, Is.EqualTo(50u));
            Assert.That(options.MaxBrowseContinuationPoints, Is.EqualTo((ushort)5));
        }

        [Test]
        public void BrowserOptionsRecordEquality()
        {
            var a = new BrowserOptions
            {
                MaxReferencesReturned = 10,
                BrowseDirection = BrowseDirection.Both
            };
            var b = new BrowserOptions
            {
                MaxReferencesReturned = 10,
                BrowseDirection = BrowseDirection.Both
            };

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void BrowserOptionsRecordInequality()
        {
            var a = new BrowserOptions { MaxReferencesReturned = 10 };
            var b = new BrowserOptions { MaxReferencesReturned = 20 };

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void BrowserOptionsWithExpression()
        {
            var original = new BrowserOptions
            {
                MaxReferencesReturned = 100,
                BrowseDirection = BrowseDirection.Forward
            };

            BrowserOptions modified = original with { MaxReferencesReturned = 200 };

            Assert.That(modified.MaxReferencesReturned, Is.EqualTo(200u));
            Assert.That(modified.BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
            Assert.That(original.MaxReferencesReturned, Is.EqualTo(100u));
        }

        [Test]
        public void SubscriptionOptionsDefaultValues()
        {
            var options = new SubscriptionOptions();

            Assert.That(options.DisplayName, Is.EqualTo("Subscription"));
            Assert.That(options.PublishingInterval, Is.EqualTo(0));
            Assert.That(options.KeepAliveCount, Is.EqualTo(0u));
            Assert.That(options.LifetimeCount, Is.EqualTo(0u));
            Assert.That(options.MaxNotificationsPerPublish, Is.EqualTo(0u));
            Assert.That(options.PublishingEnabled, Is.False);
            Assert.That(options.Priority, Is.EqualTo((byte)0));
            Assert.That(options.TimestampsToReturn,
                Is.EqualTo(TimestampsToReturn.Both));
            Assert.That(options.MaxMessageCount, Is.EqualTo(10));
            Assert.That(options.MinLifetimeInterval, Is.EqualTo(0u));
            Assert.That(options.DisableMonitoredItemCache, Is.False);
            Assert.That(options.SequentialPublishing, Is.False);
            Assert.That(options.RepublishAfterTransfer, Is.False);
            Assert.That(options.TransferId, Is.EqualTo(0u));
        }

        [Test]
        public void SubscriptionOptionsWithCustomValues()
        {
            var options = new SubscriptionOptions
            {
                DisplayName = "TestSub",
                PublishingInterval = 500,
                KeepAliveCount = 10,
                LifetimeCount = 30,
                MaxNotificationsPerPublish = 1000,
                PublishingEnabled = true,
                Priority = 200,
                TimestampsToReturn = TimestampsToReturn.Source,
                MaxMessageCount = 50,
                MinLifetimeInterval = 60000,
                DisableMonitoredItemCache = true,
                SequentialPublishing = true,
                RepublishAfterTransfer = true,
                TransferId = 42
            };

            Assert.That(options.DisplayName, Is.EqualTo("TestSub"));
            Assert.That(options.PublishingInterval, Is.EqualTo(500));
            Assert.That(options.KeepAliveCount, Is.EqualTo(10u));
            Assert.That(options.LifetimeCount, Is.EqualTo(30u));
            Assert.That(options.MaxNotificationsPerPublish, Is.EqualTo(1000u));
            Assert.That(options.PublishingEnabled, Is.True);
            Assert.That(options.Priority, Is.EqualTo((byte)200));
            Assert.That(options.TimestampsToReturn,
                Is.EqualTo(TimestampsToReturn.Source));
            Assert.That(options.MaxMessageCount, Is.EqualTo(50));
            Assert.That(options.MinLifetimeInterval, Is.EqualTo(60000u));
            Assert.That(options.DisableMonitoredItemCache, Is.True);
            Assert.That(options.SequentialPublishing, Is.True);
            Assert.That(options.RepublishAfterTransfer, Is.True);
            Assert.That(options.TransferId, Is.EqualTo(42u));
        }

        [Test]
        public void SubscriptionOptionsRecordEquality()
        {
            var a = new SubscriptionOptions
            {
                DisplayName = "Test",
                PublishingInterval = 100
            };
            var b = new SubscriptionOptions
            {
                DisplayName = "Test",
                PublishingInterval = 100
            };

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void SubscriptionOptionsWithExpression()
        {
            var original = new SubscriptionOptions
            {
                DisplayName = "Original",
                PublishingInterval = 100
            };

            SubscriptionOptions modified = original with { PublishingInterval = 200 };

            Assert.That(modified.DisplayName, Is.EqualTo("Original"));
            Assert.That(modified.PublishingInterval, Is.EqualTo(200));
        }

        [Test]
        public void MonitoredItemOptionsDefaultValues()
        {
            var options = new MonitoredItemOptions();

            Assert.That(options.DisplayName, Is.EqualTo("MonitoredItem"));
            Assert.That(options.StartNodeId, Is.EqualTo(NodeId.Null));
            Assert.That(options.RelativePath, Is.Null);
            Assert.That(options.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(options.AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(options.IndexRange, Is.Null);
            Assert.That(options.Encoding, Is.EqualTo(QualifiedName.Null));
            Assert.That(options.MonitoringMode,
                Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(options.SamplingInterval, Is.EqualTo(-1));
            Assert.That(options.Filter, Is.Null);
            Assert.That(options.QueueSize, Is.EqualTo(0u));
            Assert.That(options.DiscardOldest, Is.True);
        }

        [Test]
        public void MonitoredItemOptionsWithCustomValues()
        {
            var options = new MonitoredItemOptions
            {
                DisplayName = "Tank1.Level",
                StartNodeId = new NodeId(1000, 2),
                RelativePath = "Level",
                NodeClass = NodeClass.Object,
                AttributeId = Attributes.EventNotifier,
                IndexRange = "0:9",
                MonitoringMode = MonitoringMode.Sampling,
                SamplingInterval = 500,
                QueueSize = 10,
                DiscardOldest = false
            };

            Assert.That(options.DisplayName, Is.EqualTo("Tank1.Level"));
            Assert.That(options.StartNodeId, Is.EqualTo(new NodeId(1000, 2)));
            Assert.That(options.RelativePath, Is.EqualTo("Level"));
            Assert.That(options.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(options.AttributeId, Is.EqualTo(Attributes.EventNotifier));
            Assert.That(options.IndexRange, Is.EqualTo("0:9"));
            Assert.That(options.MonitoringMode,
                Is.EqualTo(MonitoringMode.Sampling));
            Assert.That(options.SamplingInterval, Is.EqualTo(500));
            Assert.That(options.QueueSize, Is.EqualTo(10u));
            Assert.That(options.DiscardOldest, Is.False);
        }

        [Test]
        public void MonitoredItemOptionsRecordEquality()
        {
            var a = new MonitoredItemOptions
            {
                DisplayName = "Item1",
                SamplingInterval = 100
            };
            var b = new MonitoredItemOptions
            {
                DisplayName = "Item1",
                SamplingInterval = 100
            };

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void MonitoredItemOptionsWithExpression()
        {
            var original = new MonitoredItemOptions
            {
                DisplayName = "Orig",
                SamplingInterval = 100
            };

            MonitoredItemOptions modified = original with { SamplingInterval = 500 };

            Assert.That(modified.DisplayName, Is.EqualTo("Orig"));
            Assert.That(modified.SamplingInterval, Is.EqualTo(500));
        }

        [Test]
        public void SessionOptionsDefaultValues()
        {
            var options = new SessionOptions();

            Assert.That(options.SessionName, Is.Null);
            Assert.That(options.Identity, Is.Null);
            Assert.That(options.ConfiguredEndpoint, Is.Null);
            Assert.That(options.CheckDomain, Is.False);
        }

        [Test]
        public void SessionOptionsWithCustomValues()
        {
            var options = new SessionOptions
            {
                SessionName = "TestSession",
                CheckDomain = true
            };

            Assert.That(options.SessionName, Is.EqualTo("TestSession"));
            Assert.That(options.CheckDomain, Is.True);
        }

        [Test]
        public void SessionOptionsRecordEquality()
        {
            var a = new SessionOptions { SessionName = "S1", CheckDomain = true };
            var b = new SessionOptions { SessionName = "S1", CheckDomain = true };

            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void SessionStateInheritsFromSessionOptions()
        {
            var options = new SessionOptions
            {
                SessionName = "TestSession",
                CheckDomain = true
            };

            var state = new SessionState(options);

            Assert.That(state.SessionName, Is.EqualTo("TestSession"));
            Assert.That(state.CheckDomain, Is.True);
            Assert.That(state.SessionId, Is.EqualTo(NodeId.Null));
            Assert.That(state.AuthenticationToken, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void SessionStateDefaultValues()
        {
            var state = new SessionState();

            Assert.That(state.Timestamp, Is.Not.EqualTo(default(System.DateTime)));
            Assert.That(state.ServerNonce.IsNull, Is.True);
            Assert.That(state.UserIdentityTokenPolicy, Is.Null);
            Assert.That(state.ServerEccEphemeralKey.IsNull, Is.True);
            Assert.That(state.Subscriptions.IsEmpty, Is.True);
        }

        [Test]
        public void SessionConfigurationInheritsFromSessionState()
        {
            var state = new SessionState
            {
                SessionName = "Test",
                SessionId = new NodeId(100)
            };

            var config = new SessionConfiguration(state);

            Assert.That(config.SessionName, Is.EqualTo("Test"));
            Assert.That(config.SessionId, Is.EqualTo(new NodeId(100)));
        }

        [Test]
        public void SubscriptionStateInheritsFromOptions()
        {
            var options = new SubscriptionOptions
            {
                DisplayName = "TestSub",
                PublishingInterval = 500,
                PublishingEnabled = true
            };

            var state = new SubscriptionState(options)
            {
                MonitoredItems = [],
                CurrentPublishingInterval = 500.0,
                CurrentKeepAliveCount = 10,
                CurrentLifetimeCount = 30
            };

            Assert.That(state.DisplayName, Is.EqualTo("TestSub"));
            Assert.That(state.PublishingInterval, Is.EqualTo(500));
            Assert.That(state.PublishingEnabled, Is.True);
            Assert.That(state.CurrentPublishingInterval, Is.EqualTo(500.0));
            Assert.That(state.CurrentKeepAliveCount, Is.EqualTo(10u));
            Assert.That(state.CurrentLifetimeCount, Is.EqualTo(30u));
            Assert.That(state.MonitoredItems, Is.Not.Null);
        }

        [Test]
        public void MonitoredItemStateInheritsFromOptions()
        {
            var options = new MonitoredItemOptions
            {
                DisplayName = "TestItem",
                SamplingInterval = 200,
                QueueSize = 5
            };

            var state = new MonitoredItemState(options)
            {
                ServerId = 42,
                ClientId = 7,
                TriggeringItemId = 0,
                CacheQueueSize = 10
            };

            Assert.That(state.DisplayName, Is.EqualTo("TestItem"));
            Assert.That(state.SamplingInterval, Is.EqualTo(200));
            Assert.That(state.QueueSize, Is.EqualTo(5u));
            Assert.That(state.ServerId, Is.EqualTo(42u));
            Assert.That(state.ClientId, Is.EqualTo(7u));
            Assert.That(state.TriggeringItemId, Is.EqualTo(0u));
            Assert.That(state.CacheQueueSize, Is.EqualTo(10u));
            Assert.That(state.Timestamp, Is.Not.EqualTo(default(System.DateTime)));
        }

        [Test]
        public void MonitoredItemStateCollectionClone()
        {
            var item1 = new MonitoredItemState
            {
                DisplayName = "Item1",
                ServerId = 1
            };
            var item2 = new MonitoredItemState
            {
                DisplayName = "Item2",
                ServerId = 2
            };
            var collection = new MonitoredItemStateCollection { item1, item2 };

            var clone = (MonitoredItemStateCollection)collection.Clone();

            Assert.That(clone, Has.Count.EqualTo(2));
            Assert.That(clone[0].DisplayName, Is.EqualTo("Item1"));
            Assert.That(clone[1].DisplayName, Is.EqualTo("Item2"));
        }

        [Test]
        public void MonitoredItemStateCollectionConstructors()
        {
            var empty = new MonitoredItemStateCollection();
            Assert.That(empty, Has.Count.EqualTo(0));

            var withCapacity = new MonitoredItemStateCollection(10);
            Assert.That(withCapacity, Has.Count.EqualTo(0));

            var item = new MonitoredItemState { DisplayName = "X" };
            var fromEnumerable = new MonitoredItemStateCollection(
                new[] { item });
            Assert.That(fromEnumerable, Has.Count.EqualTo(1));
        }

        [Test]
        public void SubscriptionStateCollectionClone()
        {
            var sub = new SubscriptionState
            {
                DisplayName = "Sub1",
                MonitoredItems = []
            };
            var collection = new SubscriptionStateCollection { sub };

            var clone = (SubscriptionStateCollection)collection.Clone();

            Assert.That(clone, Has.Count.EqualTo(1));
            Assert.That(clone[0].DisplayName, Is.EqualTo("Sub1"));
        }

        [Test]
        public void SubscriptionStateCollectionConstructors()
        {
            var empty = new SubscriptionStateCollection();
            Assert.That(empty, Has.Count.EqualTo(0));

            var withCapacity = new SubscriptionStateCollection(10);
            Assert.That(withCapacity, Has.Count.EqualTo(0));

            var sub = new SubscriptionState
            {
                DisplayName = "S",
                MonitoredItems = []
            };
            var fromEnumerable = new SubscriptionStateCollection(
                new[] { sub });
            Assert.That(fromEnumerable, Has.Count.EqualTo(1));
        }

        [Test]
        public void NodeSetExportOptionsDefault()
        {
            NodeSetExportOptions options = NodeSetExportOptions.Default;

            Assert.That(options.ExportValues, Is.False);
            Assert.That(options.ExportParentNodeId, Is.False);
            Assert.That(options.ExportUserContext, Is.False);
        }

        [Test]
        public void NodeSetExportOptionsComplete()
        {
            NodeSetExportOptions options = NodeSetExportOptions.Complete;

            Assert.That(options.ExportValues, Is.True);
            Assert.That(options.ExportParentNodeId, Is.True);
            Assert.That(options.ExportUserContext, Is.True);
        }

        [Test]
        public void NodeSetExportOptionsCustom()
        {
            var options = new NodeSetExportOptions
            {
                ExportValues = true,
                ExportParentNodeId = false,
                ExportUserContext = true
            };

            Assert.That(options.ExportValues, Is.True);
            Assert.That(options.ExportParentNodeId, Is.False);
            Assert.That(options.ExportUserContext, Is.True);
        }
    }
}
