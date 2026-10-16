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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Frozen unversioned classic monitored-item wire contract, including its
    /// options. Do not inherit current options or append fields to this type.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    internal sealed partial record class MonitoredItemStateV0
    {
        /// <summary>
        /// Saved display name.
        /// </summary>
        [DataTypeField(Order = 1)]
        public partial string DisplayName { get; init; }

        /// <summary>
        /// Saved starting node.
        /// </summary>
        [DataTypeField(Order = 2)]
        public partial NodeId StartNodeId { get; init; }

        /// <summary>
        /// Saved relative browse path.
        /// </summary>
        [DataTypeField(Order = 3)]
        public partial string? RelativePath { get; init; }

        /// <summary>
        /// Saved node class.
        /// </summary>
        [DataTypeField(Order = 4)]
        public partial NodeClass NodeClass { get; init; }

        /// <summary>
        /// Saved attribute identifier.
        /// </summary>
        [DataTypeField(Order = 5)]
        public partial uint AttributeId { get; init; }

        /// <summary>
        /// Saved index range.
        /// </summary>
        [DataTypeField(Order = 6)]
        public partial string? IndexRange { get; init; }

        /// <summary>
        /// Saved data encoding.
        /// </summary>
        [DataTypeField(Order = 7)]
        public partial QualifiedName Encoding { get; init; }

        /// <summary>
        /// Saved monitoring mode.
        /// </summary>
        [DataTypeField(Order = 8)]
        public partial MonitoringMode MonitoringMode { get; init; }

        /// <summary>
        /// Saved sampling interval.
        /// </summary>
        [DataTypeField(Order = 9)]
        public partial int SamplingInterval { get; init; }

        /// <summary>
        /// Saved filter.
        /// </summary>
        [DataTypeField(Order = 10, StructureHandling = StructureHandling.ExtensionObject)]
        public partial MonitoringFilter? Filter { get; init; }

        /// <summary>
        /// Saved server queue size.
        /// </summary>
        [DataTypeField(Order = 11)]
        public partial uint QueueSize { get; init; }

        /// <summary>
        /// Saved discard policy.
        /// </summary>
        [DataTypeField(Order = 12)]
        public partial bool DiscardOldest { get; init; }

        /// <summary>
        /// Saved server identifier.
        /// </summary>
        [DataTypeField(Order = 13)]
        public partial uint ServerId { get; init; }

        /// <summary>
        /// Saved client handle.
        /// </summary>
        [DataTypeField(Order = 14)]
        public partial uint ClientId { get; init; }

        /// <summary>
        /// Saved timestamp.
        /// </summary>
        [DataTypeField(Order = 15)]
        public partial DateTimeUtc Timestamp { get; init; }

        /// <summary>
        /// Saved triggering identifier.
        /// </summary>
        [DataTypeField(Order = 16)]
        public partial uint TriggeringItemId { get; init; }

        /// <summary>
        /// Saved triggered identifiers.
        /// </summary>
        [DataTypeField(Order = 17)]
        public partial ArrayOf<uint> TriggeredItems { get; init; }

        /// <summary>
        /// Saved client cache queue size.
        /// </summary>
        [DataTypeField(Order = 18)]
        public partial uint CacheQueueSize { get; init; }

        /// <summary>
        /// Upgrades the old body without inventing a resolved target.
        /// </summary>
        internal MonitoredItemState ToCurrent()
        {
            return new MonitoredItemState
            {
                DisplayName = DisplayName,
                StartNodeId = StartNodeId,
                RelativePath = RelativePath,
                NodeClass = NodeClass,
                AttributeId = AttributeId,
                IndexRange = IndexRange,
                Encoding = Encoding,
                MonitoringMode = MonitoringMode,
                SamplingInterval = SamplingInterval,
                Filter = Filter,
                QueueSize = QueueSize,
                DiscardOldest = DiscardOldest,
                ServerId = ServerId,
                ClientId = ClientId,
                Timestamp = Timestamp,
                TriggeringItemId = TriggeringItemId,
                TriggeredItems = TriggeredItems,
                CacheQueueSize = CacheQueueSize,
                ResolvedNodeId = NodeId.Null
            };
        }
    }

    /// <summary>
    /// Frozen base fields of the unversioned classic subscription contract.
    /// The base/derived boundary determines binary field order.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    internal partial record class SubscriptionOptionsV0
    {
        /// <summary>
        /// Saved display name.
        /// </summary>
        [DataTypeField(Order = 1)]
        public partial string DisplayName { get; init; }

        /// <summary>
        /// Saved publishing interval.
        /// </summary>
        [DataTypeField(Order = 2)]
        public partial int PublishingInterval { get; init; }

        /// <summary>
        /// Saved keep-alive count.
        /// </summary>
        [DataTypeField(Order = 3)]
        public partial uint KeepAliveCount { get; init; }

        /// <summary>
        /// Saved lifetime count.
        /// </summary>
        [DataTypeField(Order = 4)]
        public partial uint LifetimeCount { get; init; }

        /// <summary>
        /// Saved notification limit.
        /// </summary>
        [DataTypeField(Order = 5)]
        public partial uint MaxNotificationsPerPublish { get; init; }

        /// <summary>
        /// Saved publishing mode.
        /// </summary>
        [DataTypeField(Order = 6)]
        public partial bool PublishingEnabled { get; init; }

        /// <summary>
        /// Saved priority.
        /// </summary>
        [DataTypeField(Order = 7)]
        public partial byte Priority { get; init; }

        /// <summary>
        /// Saved timestamp selection.
        /// </summary>
        [DataTypeField(Order = 8)]
        public partial TimestampsToReturn TimestampsToReturn { get; init; }

        /// <summary>
        /// Saved cache message limit.
        /// </summary>
        [DataTypeField(Order = 9)]
        public partial int MaxMessageCount { get; init; }

        /// <summary>
        /// Saved minimum lifetime.
        /// </summary>
        [DataTypeField(Order = 12)]
        public partial uint MinLifetimeInterval { get; init; }

        /// <summary>
        /// Saved cache policy.
        /// </summary>
        [DataTypeField(Order = 13)]
        public partial bool DisableMonitoredItemCache { get; init; }

        /// <summary>
        /// Saved sequential publishing policy.
        /// </summary>
        [DataTypeField(Order = 14)]
        public partial bool SequentialPublishing { get; init; }

        /// <summary>
        /// Saved republish policy.
        /// </summary>
        [DataTypeField(Order = 15)]
        public partial bool RepublishAfterTransfer { get; init; }

        /// <summary>
        /// Saved transferable identifier.
        /// </summary>
        [DataTypeField(Order = 16)]
        public partial uint TransferId { get; init; }
    }

    /// <summary>
    /// Frozen unversioned classic subscription body with frozen nested items.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    internal sealed partial record class SubscriptionStateV0 : SubscriptionOptionsV0
    {
        /// <summary>
        /// Saved monitored items.
        /// </summary>
        [DataTypeField(Order = 11, StructureHandling = StructureHandling.Inline)]
        public partial ArrayOf<MonitoredItemStateV0> MonitoredItems { get; init; }

        /// <summary>
        /// Revised publishing interval.
        /// </summary>
        [DataTypeField(Order = 20)]
        public partial double CurrentPublishingInterval { get; init; }

        /// <summary>
        /// Revised keep-alive count.
        /// </summary>
        [DataTypeField(Order = 21)]
        public partial uint CurrentKeepAliveCount { get; init; }

        /// <summary>
        /// Revised lifetime count.
        /// </summary>
        [DataTypeField(Order = 22)]
        public partial uint CurrentLifetimeCount { get; init; }

        /// <summary>
        /// Saved timestamp.
        /// </summary>
        [DataTypeField(Order = 23)]
        public partial DateTimeUtc Timestamp { get; init; }

        /// <summary>
        /// Upgrades this body and every nested item.
        /// </summary>
        internal SubscriptionState ToCurrent()
        {
            return new SubscriptionState
            {
                DisplayName = DisplayName,
                PublishingInterval = PublishingInterval,
                KeepAliveCount = KeepAliveCount,
                LifetimeCount = LifetimeCount,
                MaxNotificationsPerPublish = MaxNotificationsPerPublish,
                PublishingEnabled = PublishingEnabled,
                Priority = Priority,
                TimestampsToReturn = TimestampsToReturn,
                MaxMessageCount = MaxMessageCount,
                MinLifetimeInterval = MinLifetimeInterval,
                DisableMonitoredItemCache = DisableMonitoredItemCache,
                SequentialPublishing = SequentialPublishing,
                RepublishAfterTransfer = RepublishAfterTransfer,
                TransferId = TransferId,
                MonitoredItems = MonitoredItems.IsNull
                    ? default : MonitoredItems.ConvertAll(item => item.ToCurrent()),
                CurrentPublishingInterval = CurrentPublishingInterval,
                CurrentKeepAliveCount = CurrentKeepAliveCount,
                CurrentLifetimeCount = CurrentLifetimeCount,
                Timestamp = Timestamp
            };
        }
    }

    /// <summary>
    /// Frozen unversioned session-configuration body, including nested classic
    /// subscriptions. Session options and state fields precede subscriptions.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    internal sealed partial record class SessionConfigurationV0
    {
        /// <summary>
        /// Saved session name.
        /// </summary>
        [DataTypeField(Order = 1)]
        public partial string? SessionName { get; init; }

        /// <summary>
        /// Saved identity token.
        /// </summary>
        [DataTypeField(Order = 2, StructureHandling = StructureHandling.ExtensionObject)]
        public partial UserIdentityToken? IdentityToken { get; init; }

        /// <summary>
        /// Saved endpoint configuration.
        /// </summary>
        [DataTypeField(Order = 3, StructureHandling = StructureHandling.Inline)]
        public partial ConfiguredEndpoint? ConfiguredEndpoint { get; init; }

        /// <summary>
        /// Saved domain checking policy.
        /// </summary>
        [DataTypeField(Order = 4)]
        public partial bool CheckDomain { get; init; }

        /// <summary>
        /// Saved timestamp.
        /// </summary>
        [DataTypeField(Order = 10)]
        public partial DateTimeUtc Timestamp { get; init; }

        /// <summary>
        /// Saved session identifier.
        /// </summary>
        [DataTypeField(Order = 11)]
        public partial NodeId SessionId { get; init; }

        /// <summary>
        /// Saved authentication token.
        /// </summary>
        [DataTypeField(Order = 12)]
        public partial NodeId AuthenticationToken { get; init; }

        /// <summary>
        /// Saved server nonce.
        /// </summary>
        [DataTypeField(Order = 13)]
        public partial ByteString ServerNonce { get; init; }

        /// <summary>
        /// Saved client nonce.
        /// </summary>
        [DataTypeField(Order = 14)]
        public partial ByteString ClientNonce { get; init; }

        /// <summary>
        /// Saved identity policy identifier.
        /// </summary>
        [DataTypeField(Order = 15)]
        public partial string? UserIdentityTokenPolicy { get; init; }

        /// <summary>
        /// Saved server ephemeral key.
        /// </summary>
        [DataTypeField(Order = 16)]
        public partial ByteString ServerEccEphemeralKey { get; init; }

        /// <summary>
        /// Saved subscriptions.
        /// </summary>
        [DataTypeField(Order = 20, StructureHandling = StructureHandling.Inline)]
        public partial ArrayOf<SubscriptionStateV0> Subscriptions { get; init; }

        /// <summary>
        /// Upgrades the complete legacy configuration.
        /// </summary>
        internal SessionConfiguration ToCurrent()
        {
            return new SessionConfiguration
            {
                SessionName = SessionName,
                IdentityToken = IdentityToken,
                ConfiguredEndpoint = ConfiguredEndpoint,
                CheckDomain = CheckDomain,
                Timestamp = Timestamp,
                SessionId = SessionId,
                AuthenticationToken = AuthenticationToken,
                ServerNonce = ServerNonce,
                ClientNonce = ClientNonce,
                UserIdentityTokenPolicy = UserIdentityTokenPolicy,
                ServerEccEphemeralKey = ServerEccEphemeralKey,
                Subscriptions = Subscriptions.IsNull
                    ? default : Subscriptions.ConvertAll(subscription => subscription.ToCurrent())
            };
        }
    }
}
