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
using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SessionStateEncoderTests
    {
        private ServiceMessageContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
            m_context.Factory.Builder.AddOpcUaClientDataTypes();
        }

        [Test]
        public void MonitoredItemStateRoundTripWithDefaults()
        {
            var original = new MonitoredItemState();

            MonitoredItemState decoded = RoundTrip(original);

            Assert.That(decoded.DisplayName, Is.EqualTo(original.DisplayName));
            Assert.That(decoded.StartNodeId, Is.EqualTo(original.StartNodeId));
            Assert.That(decoded.NodeClass, Is.EqualTo(original.NodeClass));
            Assert.That(decoded.AttributeId, Is.EqualTo(original.AttributeId));
            Assert.That(decoded.MonitoringMode, Is.EqualTo(original.MonitoringMode));
            Assert.That(decoded.SamplingInterval, Is.EqualTo(original.SamplingInterval));
            Assert.That(decoded.QueueSize, Is.EqualTo(original.QueueSize));
            Assert.That(decoded.DiscardOldest, Is.EqualTo(original.DiscardOldest));
            Assert.That(decoded.ServerId, Is.EqualTo(original.ServerId));
            Assert.That(decoded.ClientId, Is.EqualTo(original.ClientId));
            Assert.That(decoded.TriggeringItemId, Is.EqualTo(original.TriggeringItemId));
            Assert.That(decoded.CacheQueueSize, Is.EqualTo(original.CacheQueueSize));
        }

        [Test]
        public void MonitoredItemStateRoundTripWithAllFieldsPopulated()
        {
            var timestamp = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);
            var original = new MonitoredItemState
            {
                DisplayName = "TestItem",
                StartNodeId = new NodeId(42, 2),
                RelativePath = "Child1.Child2",
                NodeClass = NodeClass.Variable,
                AttributeId = Attributes.Value,
                IndexRange = "0:9",
                Encoding = new QualifiedName("DefaultBinary", 0),
                MonitoringMode = MonitoringMode.Sampling,
                SamplingInterval = 500,
                Filter = new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValue,
                    DeadbandType = (uint)DeadbandType.Absolute,
                    DeadbandValue = 1.5
                },
                QueueSize = 10,
                DiscardOldest = false,
                ServerId = 100,
                ClientId = 200,
                Timestamp = timestamp,
                TriggeringItemId = 50,
                TriggeredItems = [300, 301, 302],
                CacheQueueSize = 5
            };

            MonitoredItemState decoded = RoundTrip(original);

            Assert.That(decoded.DisplayName, Is.EqualTo("TestItem"));
            Assert.That(decoded.StartNodeId, Is.EqualTo(new NodeId(42, 2)));
            Assert.That(decoded.RelativePath, Is.EqualTo("Child1.Child2"));
            Assert.That(decoded.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(decoded.AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(decoded.IndexRange, Is.EqualTo("0:9"));
            Assert.That(decoded.Encoding, Is.EqualTo(new QualifiedName("DefaultBinary", 0)));
            Assert.That(decoded.MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
            Assert.That(decoded.SamplingInterval, Is.EqualTo(500));
            Assert.That(decoded.QueueSize, Is.EqualTo(10u));
            Assert.That(decoded.DiscardOldest, Is.False);
            Assert.That(decoded.ServerId, Is.EqualTo(100u));
            Assert.That(decoded.ClientId, Is.EqualTo(200u));
            Assert.That(decoded.Timestamp, Is.EqualTo(timestamp));
            Assert.That(decoded.TriggeringItemId, Is.EqualTo(50u));
            Assert.That(decoded.TriggeredItems.Count, Is.EqualTo(3));
            Assert.That(decoded.CacheQueueSize, Is.EqualTo(5u));
        }

        [Test]
        public void MonitoredItemStateRoundTripWithNullFilter()
        {
            var original = new MonitoredItemState
            {
                DisplayName = "NoFilter",
                Filter = null
            };

            MonitoredItemState decoded = RoundTrip(original);

            Assert.That(decoded.Filter, Is.Null);
            Assert.That(decoded.DisplayName, Is.EqualTo("NoFilter"));
        }

        [Test]
        public void MonitoredItemStateRoundTripWithEventFilter()
        {
            var original = new MonitoredItemState
            {
                DisplayName = "EventItem",
                NodeClass = NodeClass.Object,
                AttributeId = Attributes.EventNotifier,
                Filter = new EventFilter
                {
                    SelectClauses =
                    [
                        new SimpleAttributeOperand
                        {
                            TypeDefinitionId = ObjectTypeIds.BaseEventType,
                            BrowsePath = [new QualifiedName(BrowseNames.EventId)],
                            AttributeId = Attributes.Value
                        }
                    ]
                }
            };

            MonitoredItemState decoded = RoundTrip(original);

            Assert.That(decoded.Filter, Is.InstanceOf<EventFilter>());
            var eventFilter = (EventFilter)decoded.Filter!;
            Assert.That(eventFilter.SelectClauses.Count, Is.EqualTo(1));
        }

        [Test]
        public void SubscriptionStateRoundTripWithDefaults()
        {
            var original = new SubscriptionState();

            SubscriptionState decoded = RoundTrip(original);

            Assert.That(decoded.DisplayName, Is.EqualTo(original.DisplayName));
            Assert.That(decoded.PublishingInterval, Is.EqualTo(original.PublishingInterval));
            Assert.That(decoded.KeepAliveCount, Is.EqualTo(original.KeepAliveCount));
            Assert.That(decoded.LifetimeCount, Is.EqualTo(original.LifetimeCount));
            Assert.That(decoded.MaxNotificationsPerPublish, Is.EqualTo(original.MaxNotificationsPerPublish));
            Assert.That(decoded.PublishingEnabled, Is.EqualTo(original.PublishingEnabled));
            Assert.That(decoded.Priority, Is.EqualTo(original.Priority));
            Assert.That(decoded.TimestampsToReturn, Is.EqualTo(original.TimestampsToReturn));
            Assert.That(decoded.MaxMessageCount, Is.EqualTo(original.MaxMessageCount));
            Assert.That(decoded.MonitoredItems.IsEmpty, Is.True);

        }

        [Test]
        public void SubscriptionStateRoundTripWithMonitoredItems()
        {
            var timestamp = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var original = new SubscriptionState
            {
                DisplayName = "TestSubscription",
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 30,
                MaxNotificationsPerPublish = 100,
                PublishingEnabled = true,
                Priority = 5,
                TimestampsToReturn = TimestampsToReturn.Source,
                MaxMessageCount = 20,
                MinLifetimeInterval = 5000,
                DisableMonitoredItemCache = true,
                SequentialPublishing = true,
                RepublishAfterTransfer = true,
                TransferId = 42,
                MonitoredItems =
                [
                    new MonitoredItemState
                    {
                        DisplayName = "Item1",
                        StartNodeId = new NodeId(1),
                        ServerId = 10,
                        ClientId = 20
                    },
                    new MonitoredItemState
                    {
                        DisplayName = "Item2",
                        StartNodeId = new NodeId(2),
                        ServerId = 11,
                        ClientId = 21
                    }
                ],
                CurrentPublishingInterval = 1000.0,
                CurrentKeepAliveCount = 10,
                CurrentLifetimeCount = 30,
                Timestamp = timestamp
            };

            SubscriptionState decoded = RoundTrip(original);

            Assert.That(decoded.DisplayName, Is.EqualTo("TestSubscription"));
            Assert.That(decoded.PublishingInterval, Is.EqualTo(1000));
            Assert.That(decoded.KeepAliveCount, Is.EqualTo(10u));
            Assert.That(decoded.LifetimeCount, Is.EqualTo(30u));
            Assert.That(decoded.MaxNotificationsPerPublish, Is.EqualTo(100u));
            Assert.That(decoded.PublishingEnabled, Is.True);
            Assert.That(decoded.Priority, Is.EqualTo(5));
            Assert.That(decoded.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Source));
            Assert.That(decoded.MaxMessageCount, Is.EqualTo(20));
            Assert.That(decoded.MinLifetimeInterval, Is.EqualTo(5000u));
            Assert.That(decoded.DisableMonitoredItemCache, Is.True);
            Assert.That(decoded.SequentialPublishing, Is.True);
            Assert.That(decoded.RepublishAfterTransfer, Is.True);
            Assert.That(decoded.TransferId, Is.EqualTo(42u));
            Assert.That(decoded.MonitoredItems.Count, Is.EqualTo(2));
            Assert.That(decoded.MonitoredItems[0].DisplayName, Is.EqualTo("Item1"));
            Assert.That(decoded.MonitoredItems[1].DisplayName, Is.EqualTo("Item2"));
            Assert.That(decoded.CurrentPublishingInterval, Is.EqualTo(1000.0));
            Assert.That(decoded.CurrentKeepAliveCount, Is.EqualTo(10u));
            Assert.That(decoded.CurrentLifetimeCount, Is.EqualTo(30u));
            Assert.That(decoded.Timestamp, Is.EqualTo(timestamp));
        }

        [Test]
        public void SubscriptionStateRoundTripWithNullMonitoredItems()
        {
            var original = new SubscriptionState
            {
                DisplayName = "EmptySub"
            };

            SubscriptionState decoded = RoundTrip(original);

            Assert.That(decoded.MonitoredItems.IsEmpty, Is.True);

        }

        [Test]
        public void SessionConfigurationRoundTripWithMinimalFields()
        {
            var original = new SessionConfiguration
            {
                SessionName = "TestSession",
                CheckDomain = false,
                Timestamp = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = new NodeId(1000),
                AuthenticationToken = new NodeId(2000),
                Identity = null,
                ConfiguredEndpoint = null
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.SessionName, Is.EqualTo("TestSession"));
            Assert.That(decoded.CheckDomain, Is.False);
            Assert.That(decoded.SessionId, Is.EqualTo(new NodeId(1000)));
            Assert.That(decoded.AuthenticationToken, Is.EqualTo(new NodeId(2000)));
            Assert.That(decoded.Identity, Is.Null);
            Assert.That(decoded.Subscriptions.IsEmpty, Is.True);
        }

        [Test]
        public void SessionConfigurationRoundTripWithConfiguredEndpoint()
        {
            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                ServerCertificate = [],
                UserIdentityTokens =
                [
                    new UserTokenPolicy { TokenType = UserTokenType.Anonymous }
                ]
            };

            var endpointConfig = new EndpointConfiguration
            {
                OperationTimeout = 60000,
                UseBinaryEncoding = true,
                MaxMessageSize = 4194304,
                MaxBufferSize = 65535,
                ChannelLifetime = 300000,
                SecurityTokenLifetime = 3600000,
                MaxArrayLength = 65535,
                MaxByteStringLength = 1048576,
                MaxStringLength = 1048576,
                MaxEncodingNestingLevels = 200,
                MaxDecoderRecoveries = 10
            };

            var configuredEndpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfig)
            {
                UpdateBeforeConnect = true,
                BinaryEncodingSupport = BinaryEncodingSupport.Required,
                SelectedUserTokenPolicyIndex = 0
            };

            var original = new SessionConfiguration
            {
                SessionName = "EndpointSession",
                ConfiguredEndpoint = configuredEndpoint,
                CheckDomain = true,
                Timestamp = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = new NodeId("session1", 1),
                AuthenticationToken = new NodeId("auth1", 1)
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.SessionName, Is.EqualTo("EndpointSession"));
            Assert.That(decoded.CheckDomain, Is.True);
            Assert.That(decoded.ConfiguredEndpoint, Is.Not.Null);
            Assert.That(decoded.ConfiguredEndpoint!.Description.EndpointUrl,
                Is.EqualTo("opc.tcp://localhost:4840"));
            Assert.That(decoded.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None));
            Assert.That(decoded.ConfiguredEndpoint.UpdateBeforeConnect, Is.True);
            Assert.That(decoded.ConfiguredEndpoint.BinaryEncodingSupport,
                Is.EqualTo(BinaryEncodingSupport.Required));
            Assert.That(decoded.ConfiguredEndpoint.Configuration.OperationTimeout,
                Is.EqualTo(60000));
            Assert.That(decoded.ConfiguredEndpoint.Configuration.UseBinaryEncoding, Is.True);
            Assert.That(decoded.ConfiguredEndpoint.Configuration.MaxMessageSize,
                Is.EqualTo(4194304));
        }

        [Test]
        public void SessionConfigurationRoundTripWithNullEndpointConfiguration()
        {
            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };

            var configuredEndpoint = new ConfiguredEndpoint(null, endpointDescription, null);

            var original = new SessionConfiguration
            {
                SessionName = "NullConfigSession",
                ConfiguredEndpoint = configuredEndpoint,
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = NodeId.Null,
                AuthenticationToken = NodeId.Null
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.ConfiguredEndpoint, Is.Not.Null);
            Assert.That(decoded.ConfiguredEndpoint!.Configuration, Is.Not.Null);
        }

        [Test]
        public void SessionConfigurationRoundTripWithSubscriptions()
        {
            var timestamp = new DateTime(2025, 6, 15, 8, 0, 0, DateTimeKind.Utc);
            var original = new SessionConfiguration
            {
                SessionName = "SubSession",
                Timestamp = timestamp,
                SessionId = new NodeId(100),
                AuthenticationToken = new NodeId(200),
                Subscriptions = new List<SubscriptionState> {
                    new SubscriptionState
                    {
                        DisplayName = "Sub1",
                        PublishingInterval = 500,
                        MonitoredItems =
                        [
                            new MonitoredItemState
                            {
                                DisplayName = "MI1",
                                StartNodeId = new NodeId(10),
                                ServerId = 1
                            }
                        ]
                    }
                }
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.Subscriptions.IsEmpty, Is.False);
            Assert.That(decoded.Subscriptions.Count, Is.EqualTo(1));
            Assert.That(decoded.Subscriptions[0].DisplayName, Is.EqualTo("Sub1"));
            Assert.That(decoded.Subscriptions[0].PublishingInterval, Is.EqualTo(500));
            Assert.That(decoded.Subscriptions[0].MonitoredItems.Count, Is.EqualTo(1));
            Assert.That(decoded.Subscriptions[0].MonitoredItems[0].DisplayName, Is.EqualTo("MI1"));
        }

        [Test]
        public void SessionConfigurationRoundTripWithEmptySubscriptions()
        {
            var original = new SessionConfiguration
            {
                SessionName = "NoSubs",
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = NodeId.Null,
                AuthenticationToken = NodeId.Null
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.Subscriptions.IsEmpty, Is.True);
        }

        [Test]
        public void SessionConfigurationRoundTripWithAnonymousIdentity()
        {
            var identity = new UserIdentity();

            var original = new SessionConfiguration
            {
                SessionName = "AnonSession",
                Identity = identity,
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = new NodeId(1),
                AuthenticationToken = new NodeId(2)
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.Identity, Is.Not.Null);
            Assert.That(decoded.Identity!.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        [Test]
        public void SessionConfigurationRoundTripWithUserNameIdentity()
        {
            var identity = new UserIdentity("testuser", System.Text.Encoding.UTF8.GetBytes("testpass"));

            var original = new SessionConfiguration
            {
                SessionName = "UserSession",
                Identity = identity,
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = new NodeId(1),
                AuthenticationToken = new NodeId(2)
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.Identity, Is.Not.Null);
            Assert.That(decoded.Identity!.TokenType, Is.EqualTo(UserTokenType.UserName));
        }

        [Test]
        public void SessionConfigurationRoundTripWithReverseConnect()
        {
            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };

            var configuredEndpoint = new ConfiguredEndpoint(null, endpointDescription)
            {
                ReverseConnect = new ReverseConnectEndpoint
                {
                    Enabled = true,
                    ServerUri = "urn:testserver",
                    Thumbprint = "AABBCCDD"
                }
            };

            var original = new SessionConfiguration
            {
                SessionName = "ReverseSession",
                ConfiguredEndpoint = configuredEndpoint,
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = NodeId.Null,
                AuthenticationToken = NodeId.Null
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.ConfiguredEndpoint, Is.Not.Null);
            Assert.That(decoded.ConfiguredEndpoint!.ReverseConnect, Is.Not.Null);
            Assert.That(decoded.ConfiguredEndpoint.ReverseConnect!.Enabled, Is.True);
            Assert.That(decoded.ConfiguredEndpoint.ReverseConnect.ServerUri, Is.EqualTo("urn:testserver"));
            Assert.That(decoded.ConfiguredEndpoint.ReverseConnect.Thumbprint, Is.EqualTo("AABBCCDD"));
        }

        [Test]
        public void SessionConfigurationRoundTripWithoutReverseConnect()
        {
            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };

            var configuredEndpoint = new ConfiguredEndpoint(null, endpointDescription)
            {
                ReverseConnect = null
            };

            var original = new SessionConfiguration
            {
                SessionName = "NoReverseSession",
                ConfiguredEndpoint = configuredEndpoint,
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = NodeId.Null,
                AuthenticationToken = NodeId.Null
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.ConfiguredEndpoint, Is.Not.Null);
            Assert.That(decoded.ConfiguredEndpoint!.ReverseConnect?.Enabled, Is.Not.True);
        }

        [Test]
        public void SessionConfigurationRoundTripWithServerNonce()
        {
            byte[] nonceData = [0x01, 0x02, 0x03, 0x04, 0x05];
            var nonce = Nonce.CreateNonce(SecurityPolicies.None, nonceData);

            var original = new SessionConfiguration
            {
                SessionName = "NonceSession",
                ServerNonce = ByteString.From(nonce.Data),
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = NodeId.Null,
                AuthenticationToken = NodeId.Null
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.ServerNonce.IsNull, Is.False);
            Assert.That(decoded.ServerNonce.ToArray(), Is.EqualTo(nonceData));
        }

        [Test]
        public void SessionConfigurationRoundTripWithNullNonces()
        {
            var original = new SessionConfiguration
            {
                SessionName = "NullNonceSession",
                ServerNonce = default,
                ServerEccEphemeralKey = default,
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = NodeId.Null,
                AuthenticationToken = NodeId.Null
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.ServerNonce.IsNull, Is.True);
            Assert.That(decoded.ServerEccEphemeralKey.IsNull, Is.True);
        }

        [Test]
        public void SessionConfigurationRoundTripPreservesUserIdentityTokenPolicy()
        {
            var original = new SessionConfiguration
            {
                SessionName = "PolicySession",
                UserIdentityTokenPolicy = "http://opcfoundation.org/UA/SecurityPolicy#None",
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = NodeId.Null,
                AuthenticationToken = NodeId.Null
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.UserIdentityTokenPolicy,
                Is.EqualTo("http://opcfoundation.org/UA/SecurityPolicy#None"));
        }

        [Test]
        public void SessionConfigurationCreateFromStream()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var original = new SessionConfiguration
            {
                SessionName = "StreamSession",
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = new NodeId(999),
                AuthenticationToken = new NodeId(888)
            };

            byte[] data;
            using (var ms = new MemoryStream())
            {
                using (var encoder = new BinaryEncoder(ms, m_context, true))
                {
                    encoder.WriteStringArray(null, m_context.NamespaceUris.ToArray());
                    encoder.WriteStringArray(null, m_context.ServerUris.ToArray());
                    original.Encode(encoder);
                }
                data = ms.ToArray();
            }

            using var readStream = new MemoryStream(data);
            SessionConfiguration decoded = SessionConfiguration.Create(readStream, telemetry);

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.SessionName, Is.EqualTo("StreamSession"));
            Assert.That(decoded.SessionId, Is.EqualTo(new NodeId(999)));
            Assert.That(decoded.AuthenticationToken, Is.EqualTo(new NodeId(888)));
        }

        [Test]
        public void EndpointConfigurationRoundTripWithAllFields()
        {
            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };

            var endpointConfig = new EndpointConfiguration
            {
                OperationTimeout = 30000,
                UseBinaryEncoding = true,
                MaxMessageSize = 2097152,
                MaxBufferSize = 32768,
                ChannelLifetime = 150000,
                SecurityTokenLifetime = 1800000,
                MaxArrayLength = 32768,
                MaxByteStringLength = 524288,
                MaxStringLength = 524288,
                MaxEncodingNestingLevels = 100,
                MaxDecoderRecoveries = 5
            };

            var configuredEndpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfig);

            var original = new SessionConfiguration
            {
                SessionName = "ConfigTest",
                ConfiguredEndpoint = configuredEndpoint,
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SessionId = NodeId.Null,
                AuthenticationToken = NodeId.Null
            };

            SessionConfiguration decoded = RoundTrip(original);

            EndpointConfiguration cfg = decoded.ConfiguredEndpoint!.Configuration;
            Assert.That(cfg.OperationTimeout, Is.EqualTo(30000));
            Assert.That(cfg.UseBinaryEncoding, Is.True);
            Assert.That(cfg.MaxMessageSize, Is.EqualTo(2097152));
            Assert.That(cfg.MaxBufferSize, Is.EqualTo(32768));
            Assert.That(cfg.ChannelLifetime, Is.EqualTo(150000));
            Assert.That(cfg.SecurityTokenLifetime, Is.EqualTo(1800000));
            Assert.That(cfg.MaxArrayLength, Is.EqualTo(32768));
            Assert.That(cfg.MaxByteStringLength, Is.EqualTo(524288));
            Assert.That(cfg.MaxStringLength, Is.EqualTo(524288));
        }

        [Test]
        public void MonitoredItemStateRoundTripWithEmptyTriggeredItems()
        {
            var original = new MonitoredItemState
            {
                DisplayName = "EmptyTriggers",
                TriggeredItems = []
            };

            MonitoredItemState decoded = RoundTrip(original);

            Assert.That(decoded.TriggeredItems.Count, Is.EqualTo(0));
        }

        [Test]
        public void SessionConfigurationRoundTripFullWithMultipleSubscriptions()
        {
            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://server:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };

            var configuredEndpoint = new ConfiguredEndpoint(
                null, endpointDescription, EndpointConfiguration.Create());

            var timestamp = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);

            var original = new SessionConfiguration
            {
                SessionName = "FullSession",
                Identity = new UserIdentity(),
                ConfiguredEndpoint = configuredEndpoint,
                CheckDomain = true,
                Timestamp = timestamp,
                SessionId = new NodeId(Guid.NewGuid()),
                AuthenticationToken = new NodeId(Guid.NewGuid()),
                UserIdentityTokenPolicy = "policy1",
                Subscriptions = new List<SubscriptionState> {
                    new SubscriptionState
                    {
                        DisplayName = "Sub1",
                        PublishingInterval = 1000,
                        MonitoredItems =
                        [
                            new MonitoredItemState { DisplayName = "MI1" },
                            new MonitoredItemState { DisplayName = "MI2" }
                        ]
                    },
                    new SubscriptionState
                    {
                        DisplayName = "Sub2",
                        PublishingInterval = 2000,
                        MonitoredItems = []
                    }
                }
            };

            SessionConfiguration decoded = RoundTrip(original);

            Assert.That(decoded.SessionName, Is.EqualTo("FullSession"));
            Assert.That(decoded.CheckDomain, Is.True);
            Assert.That(decoded.Timestamp, Is.EqualTo(timestamp));
            Assert.That(decoded.UserIdentityTokenPolicy, Is.EqualTo("policy1"));
            Assert.That(decoded.Subscriptions.Count, Is.EqualTo(2));
            Assert.That(decoded.Subscriptions[0].MonitoredItems.Count, Is.EqualTo(2));
            Assert.That(decoded.Subscriptions[1].MonitoredItems, Is.Empty);
        }

[Test]
public void SubscriptionStateSaveLoadRoundTripMimicsSessionSaveLoad()
{
    // Create a subscription state with monitored items (same as Session.Save does)
    var monitoredItem = new MonitoredItemState
    {
        DisplayName = "TestItem",
        StartNodeId = new NodeId(42, 2),
        NodeClass = NodeClass.Variable,
        AttributeId = Attributes.Value,
        MonitoringMode = MonitoringMode.Reporting,
        SamplingInterval = -1,
        QueueSize = 1,
        DiscardOldest = true,
        ServerId = 100,
        ClientId = 200
    };

    var original = new SubscriptionState
    {
        DisplayName = "TestSub",
        PublishingInterval = 1000,
        KeepAliveCount = 5,
        LifetimeCount = 15,
        PublishingEnabled = true,
        TransferId = 999,
        MonitoredItems = [monitoredItem],
        CurrentPublishingInterval = 1000.0,
        CurrentKeepAliveCount = 5,
        CurrentLifetimeCount = 15
    };

    // Encode exactly like Session.Save does
    using var ms = new MemoryStream();
    using (var encoder = new BinaryEncoder(ms, m_context, true))
    {
        // Session.Save writes: nsUris, serverUris, count, then each state.Encode
        encoder.WriteStringArray(null, m_context.NamespaceUris.ToArrayOf());
        encoder.WriteStringArray(null, m_context.ServerUris.ToArrayOf());
        encoder.WriteInt32(null, 1); // count = 1
        original.Encode(encoder);
    }

    TestContext.Out.WriteLine($"Encoded {ms.Length} bytes");

    // Decode exactly like Session.Load does
    ms.Position = 0;
    using var decoder = new BinaryDecoder(ms, m_context);
    var nsUris = decoder.ReadStringArray(null);
    var serverUris = decoder.ReadStringArray(null);
    int count = decoder.ReadInt32(null);

    Assert.That(count, Is.EqualTo(1), "subscription count");

    var decoded = new SubscriptionState();
    decoded.Decode(decoder);

    TestContext.Out.WriteLine($"Decoded MonitoredItems.Count = {decoded.MonitoredItems.Count}");
    TestContext.Out.WriteLine($"Decoded DisplayName = {decoded.DisplayName}");
    TestContext.Out.WriteLine($"Decoded TransferId = {decoded.TransferId}");

    Assert.That(decoded.DisplayName, Is.EqualTo("TestSub"));
    Assert.That(decoded.TransferId, Is.EqualTo(999u));
    Assert.That(decoded.MonitoredItems.Count, Is.EqualTo(1), "MonitoredItems should have 1 item");
    Assert.That(decoded.MonitoredItems[0].DisplayName, Is.EqualTo("TestItem"));
    Assert.That(decoded.MonitoredItems[0].ServerId, Is.EqualTo(100u));
}

[Test]
public void SubscriptionStateSaveLoadWithoutClientTypesRegistered()
{
    // Use a plain context WITHOUT AddOpcUaClientDataTypes - like Session.Load does
    var plainContext = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
    // DO NOT call plainContext.Factory.Builder.AddOpcUaClientDataTypes();

    var monitoredItem = new MonitoredItemState
    {
        DisplayName = "TestItem",
        StartNodeId = new NodeId(42, 2),
        ServerId = 100,
        ClientId = 200
    };

    var original = new SubscriptionState
    {
        DisplayName = "TestSub",
        TransferId = 999,
        MonitoredItems = [monitoredItem]
    };

    using var ms = new MemoryStream();
    using (var encoder = new BinaryEncoder(ms, plainContext, true))
    {
        encoder.WriteStringArray(null, plainContext.NamespaceUris.ToArrayOf());
        encoder.WriteStringArray(null, plainContext.ServerUris.ToArrayOf());
        encoder.WriteInt32(null, 1);
        original.Encode(encoder);
    }

    TestContext.Out.WriteLine($"Encoded {ms.Length} bytes");

    ms.Position = 0;
    using var decoder = new BinaryDecoder(ms, plainContext);
    decoder.ReadStringArray(null);
    decoder.ReadStringArray(null);
    int count = decoder.ReadInt32(null);
    Assert.That(count, Is.EqualTo(1));

    var decoded = new SubscriptionState();
    decoded.Decode(decoder);

    TestContext.Out.WriteLine($"MonitoredItems.Count = {decoded.MonitoredItems.Count}");
    Assert.That(decoded.MonitoredItems.Count, Is.EqualTo(1), 
        "MonitoredItems lost without client types - this is the reconnect bug!");
    Assert.That(decoded.MonitoredItems[0].ServerId, Is.EqualTo(100u));
}

[Test]
public void SubscriptionStateDecodeDebugByteLevelVerification()
{
    // Create realistic data matching what a real subscription produces
    var items = new List<MonitoredItemState>();
    for (int i = 0; i < 10; i++)
    {
        items.Add(new MonitoredItemState
        {
            DisplayName = $"MonitoredItem {i}",
            StartNodeId = new NodeId((uint)(1880 + i), 3),
            NodeClass = NodeClass.Variable,
            AttributeId = Attributes.Value,
            MonitoringMode = MonitoringMode.Reporting,
            SamplingInterval = -1,
            QueueSize = 10,
            DiscardOldest = true,
            ServerId = (uint)(100 + i),
            ClientId = (uint)(200 + i)
        });
    }

    var original = new SubscriptionState
    {
        DisplayName = "Subscription",
        PublishingInterval = 1000,
        KeepAliveCount = 5,
        LifetimeCount = 15,
        PublishingEnabled = true,
        RepublishAfterTransfer = true,
        TransferId = 12345,
        MonitoredItems = items,
        CurrentPublishingInterval = 1000.0,
        CurrentKeepAliveCount = 5,
        CurrentLifetimeCount = 15
    };

    // Use a plain context (like Session uses)
    var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
    
    using var ms = new MemoryStream();
    using (var encoder = new BinaryEncoder(ms, context, true))
    {
        encoder.WriteInt32(null, 1); // count
        original.Encode(encoder);
    }

    var bytes = ms.ToArray();
    TestContext.Out.WriteLine($"Total bytes: {bytes.Length}");
    // Print first 100 bytes as hex
    TestContext.Out.WriteLine("First 100 bytes: " + 
        BitConverter.ToString(bytes, 0, Math.Min(100, bytes.Length)));

    ms.Position = 0;
    using var decoder = new BinaryDecoder(ms, context);
    int count = decoder.ReadInt32(null);
    Assert.That(count, Is.EqualTo(1));

    long posBeforeDecode = ms.Position;
    TestContext.Out.WriteLine($"Position before Decode: {posBeforeDecode}");

    var decoded = new SubscriptionState();
    decoded.Decode(decoder);

    long posAfterDecode = ms.Position;
    TestContext.Out.WriteLine($"Position after Decode: {posAfterDecode}");
    TestContext.Out.WriteLine($"Bytes consumed: {posAfterDecode - posBeforeDecode}");

    TestContext.Out.WriteLine($"Decoded DisplayName: {decoded.DisplayName}");
    TestContext.Out.WriteLine($"Decoded TransferId: {decoded.TransferId}");
    TestContext.Out.WriteLine($"Decoded MonitoredItems.Count: {decoded.MonitoredItems.Count}");
    for (int i = 0; i < decoded.MonitoredItems.Count; i++)
    {
        TestContext.Out.WriteLine($"  Item[{i}]: {decoded.MonitoredItems[i].DisplayName} ServerId={decoded.MonitoredItems[i].ServerId}");
    }

    Assert.That(decoded.DisplayName, Is.EqualTo("Subscription"));
    Assert.That(decoded.TransferId, Is.EqualTo(12345u));
    Assert.That(decoded.MonitoredItems.Count, Is.EqualTo(10), "10 monitored items expected");
    Assert.That(decoded.MonitoredItems[0].ServerId, Is.EqualTo(100u));
    Assert.That(decoded.MonitoredItems[9].ServerId, Is.EqualTo(109u));
}

        private T RoundTrip<T>(T original) where T : IEncodeable, new()
        {
            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_context, true))
            {
                original.Encode(encoder);
            }

            ms.Position = 0;
            using var decoder = new BinaryDecoder(ms, m_context);
            var decoded = new T();
            decoded.Decode(decoder);
            return decoded;
        }
    }
}
