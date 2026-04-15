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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Provides binary encode/decode methods for session and subscription
    /// state types, replacing DataContractSerializer usage.
    /// </summary>
    internal static class SessionStateEncoder
    {
        internal static void EncodeMonitoredItemState(
            BinaryEncoder encoder,
            MonitoredItemState state)
        {
            // MonitoredItemOptions fields (base)
            encoder.WriteString(null, state.DisplayName);
            encoder.WriteNodeId(null, state.StartNodeId);
            encoder.WriteString(null, state.RelativePath);
            encoder.WriteEnumerated(null, state.NodeClass);
            encoder.WriteUInt32(null, state.AttributeId);
            encoder.WriteString(null, state.IndexRange);
            encoder.WriteQualifiedName(null, state.Encoding);
            encoder.WriteEnumerated(null, state.MonitoringMode);
            encoder.WriteInt32(null, state.SamplingInterval);
            encoder.WriteExtensionObject(null,
                state.Filter != null
                    ? new ExtensionObject(state.Filter)
                    : ExtensionObject.Null);
            encoder.WriteUInt32(null, state.QueueSize);
            encoder.WriteBoolean(null, state.DiscardOldest);

            // MonitoredItemState fields
            encoder.WriteUInt32(null, state.ServerId);
            encoder.WriteUInt32(null, state.ClientId);
            encoder.WriteDateTime(null, state.Timestamp);
            encoder.WriteUInt32(null, state.TriggeringItemId);
            encoder.WriteUInt32Array(null, state.TriggeredItems);
            encoder.WriteUInt32(null, state.CacheQueueSize);
        }

        internal static MonitoredItemState DecodeMonitoredItemState(
            BinaryDecoder decoder)
        {
            // MonitoredItemOptions fields (base)
            string displayName = decoder.ReadString(null);
            NodeId startNodeId = decoder.ReadNodeId(null);
            string? relativePath = decoder.ReadString(null);
            NodeClass nodeClass = decoder.ReadEnumerated<NodeClass>(null);
            uint attributeId = decoder.ReadUInt32(null);
            string? indexRange = decoder.ReadString(null);
            QualifiedName encoding = decoder.ReadQualifiedName(null);
            MonitoringMode monitoringMode =
                decoder.ReadEnumerated<MonitoringMode>(null);
            int samplingInterval = decoder.ReadInt32(null);

            ExtensionObject filterEo = decoder.ReadExtensionObject(null);
            MonitoringFilter? filter = null;
            if (!filterEo.IsNull &&
                filterEo.TryGetEncodeable(out IEncodeable filterBody))
            {
                filter = filterBody as MonitoringFilter;
            }

            uint queueSize = decoder.ReadUInt32(null);
            bool discardOldest = decoder.ReadBoolean(null);

            // MonitoredItemState fields
            uint serverId = decoder.ReadUInt32(null);
            uint clientId = decoder.ReadUInt32(null);
            DateTime timestamp = (DateTime)decoder.ReadDateTime(null);
            uint triggeringItemId = decoder.ReadUInt32(null);
            ArrayOf<uint> triggeredItems = decoder.ReadUInt32Array(null);
            uint cacheQueueSize = decoder.ReadUInt32(null);

            return new MonitoredItemState
            {
                DisplayName = displayName,
                StartNodeId = startNodeId,
                RelativePath = relativePath,
                NodeClass = nodeClass,
                AttributeId = attributeId,
                IndexRange = indexRange,
                Encoding = encoding,
                MonitoringMode = monitoringMode,
                SamplingInterval = samplingInterval,
                Filter = filter,
                QueueSize = queueSize,
                DiscardOldest = discardOldest,
                ServerId = serverId,
                ClientId = clientId,
                Timestamp = timestamp,
                TriggeringItemId = triggeringItemId,
                TriggeredItems = triggeredItems,
                CacheQueueSize = cacheQueueSize
            };
        }

        internal static void EncodeSubscriptionState(
            BinaryEncoder encoder,
            SubscriptionState state)
        {
            // SubscriptionOptions fields (base)
            encoder.WriteString(null, state.DisplayName);
            encoder.WriteInt32(null, state.PublishingInterval);
            encoder.WriteUInt32(null, state.KeepAliveCount);
            encoder.WriteUInt32(null, state.LifetimeCount);
            encoder.WriteUInt32(null, state.MaxNotificationsPerPublish);
            encoder.WriteBoolean(null, state.PublishingEnabled);
            encoder.WriteByte(null, state.Priority);
            encoder.WriteEnumerated(null, state.TimestampsToReturn);
            encoder.WriteInt32(null, state.MaxMessageCount);
            encoder.WriteUInt32(null, state.MinLifetimeInterval);
            encoder.WriteBoolean(null, state.DisableMonitoredItemCache);
            encoder.WriteBoolean(null, state.SequentialPublishing);
            encoder.WriteBoolean(null, state.RepublishAfterTransfer);
            encoder.WriteUInt32(null, state.TransferId);

            // SubscriptionState fields
            int itemCount = state.MonitoredItems?.Count ?? 0;
            encoder.WriteInt32(null, itemCount);
            if (state.MonitoredItems != null)
            {
                foreach (MonitoredItemState item in state.MonitoredItems)
                {
                    EncodeMonitoredItemState(encoder, item);
                }
            }

            encoder.WriteDouble(null, state.CurrentPublishingInterval);
            encoder.WriteUInt32(null, state.CurrentKeepAliveCount);
            encoder.WriteUInt32(null, state.CurrentLifetimeCount);
            encoder.WriteDateTime(null, state.Timestamp);
        }

        internal static SubscriptionState DecodeSubscriptionState(
            BinaryDecoder decoder)
        {
            // SubscriptionOptions fields (base)
            string displayName = decoder.ReadString(null);
            int publishingInterval = decoder.ReadInt32(null);
            uint keepAliveCount = decoder.ReadUInt32(null);
            uint lifetimeCount = decoder.ReadUInt32(null);
            uint maxNotificationsPerPublish = decoder.ReadUInt32(null);
            bool publishingEnabled = decoder.ReadBoolean(null);
            byte priority = decoder.ReadByte(null);
            TimestampsToReturn timestampsToReturn =
                decoder.ReadEnumerated<TimestampsToReturn>(null);
            int maxMessageCount = decoder.ReadInt32(null);
            uint minLifetimeInterval = decoder.ReadUInt32(null);
            bool disableMonitoredItemCache = decoder.ReadBoolean(null);
            bool sequentialPublishing = decoder.ReadBoolean(null);
            bool republishAfterTransfer = decoder.ReadBoolean(null);
            uint transferId = decoder.ReadUInt32(null);

            // MonitoredItems
            int itemCount = decoder.ReadInt32(null);
            var monitoredItems = new MonitoredItemStateCollection(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                monitoredItems.Add(DecodeMonitoredItemState(decoder));
            }

            double currentPublishingInterval = decoder.ReadDouble(null);
            uint currentKeepAliveCount = decoder.ReadUInt32(null);
            uint currentLifetimeCount = decoder.ReadUInt32(null);
            DateTime timestamp = (DateTime)decoder.ReadDateTime(null);

            return new SubscriptionState
            {
                DisplayName = displayName,
                PublishingInterval = publishingInterval,
                KeepAliveCount = keepAliveCount,
                LifetimeCount = lifetimeCount,
                MaxNotificationsPerPublish = maxNotificationsPerPublish,
                PublishingEnabled = publishingEnabled,
                Priority = priority,
                TimestampsToReturn = timestampsToReturn,
                MaxMessageCount = maxMessageCount,
                MinLifetimeInterval = minLifetimeInterval,
                DisableMonitoredItemCache = disableMonitoredItemCache,
                SequentialPublishing = sequentialPublishing,
                RepublishAfterTransfer = republishAfterTransfer,
                TransferId = transferId,
                MonitoredItems = monitoredItems,
                CurrentPublishingInterval = currentPublishingInterval,
                CurrentKeepAliveCount = currentKeepAliveCount,
                CurrentLifetimeCount = currentLifetimeCount,
                Timestamp = timestamp
            };
        }

        internal static void EncodeSessionConfiguration(
            BinaryEncoder encoder,
            SessionConfiguration config)
        {
            // SessionOptions fields
            encoder.WriteString(null, config.SessionName);
            EncodeUserIdentity(encoder, config.Identity);
            EncodeConfiguredEndpoint(encoder, config.ConfiguredEndpoint);
            encoder.WriteBoolean(null, config.CheckDomain);

            // SessionState fields
            encoder.WriteDateTime(null, config.Timestamp);
            encoder.WriteNodeId(null, config.SessionId);
            encoder.WriteNodeId(null, config.AuthenticationToken);
            encoder.WriteByteString(null, config.ServerNonce);
            encoder.WriteByteString(null, config.ClientNonce);
            encoder.WriteString(null, config.UserIdentityTokenPolicy);
            encoder.WriteByteString(null, config.ServerEccEphemeralKey);

            // Subscriptions
            int subCount = config.Subscriptions?.Count ?? 0;
            encoder.WriteInt32(null, subCount);
            if (config.Subscriptions != null)
            {
                foreach (SubscriptionState sub in config.Subscriptions)
                {
                    EncodeSubscriptionState(encoder, sub);
                }
            }
        }

        internal static SessionConfiguration DecodeSessionConfiguration(
            BinaryDecoder decoder)
        {
            // SessionOptions fields
            string? sessionName = decoder.ReadString(null);
            IUserIdentity? identity = DecodeUserIdentity(decoder);
            ConfiguredEndpoint? configuredEndpoint =
                DecodeConfiguredEndpoint(decoder);
            bool checkDomain = decoder.ReadBoolean(null);

            // SessionState fields
            DateTime timestamp = (DateTime)decoder.ReadDateTime(null);
            NodeId sessionId = decoder.ReadNodeId(null);
            NodeId authenticationToken = decoder.ReadNodeId(null);

            string securityPolicy =
                configuredEndpoint?.Description?.SecurityPolicyUri
                ?? string.Empty;

            ByteString serverNonce = decoder.ReadByteString(null);
            ByteString clientNonce = decoder.ReadByteString(null);
            string? userIdentityTokenPolicy = decoder.ReadString(null);
            ByteString serverEccEphemeralKey = decoder.ReadByteString(null);

            // Subscriptions
            int subCount = decoder.ReadInt32(null);
            SubscriptionStateCollection? subscriptions = null;
            if (subCount > 0)
            {
                subscriptions = new SubscriptionStateCollection(subCount);
                for (int i = 0; i < subCount; i++)
                {
                    subscriptions.Add(DecodeSubscriptionState(decoder));
                }
            }

            return new SessionConfiguration
            {
                SessionName = sessionName,
                Identity = identity,
                ConfiguredEndpoint = configuredEndpoint,
                CheckDomain = checkDomain,
                Timestamp = timestamp,
                SessionId = sessionId,
                AuthenticationToken = authenticationToken,
                ServerNonce = serverNonce.IsNull ? null : serverNonce.ToArray(),
                ClientNonce = clientNonce.IsNull ? null : clientNonce.ToArray(),
                UserIdentityTokenPolicy = userIdentityTokenPolicy,
                ServerEccEphemeralKey =
                    serverEccEphemeralKey.IsNull ? null : serverEccEphemeralKey.ToArray(),
                Subscriptions = subscriptions
            };
        }

        private static void EncodeUserIdentity(
            BinaryEncoder encoder,
            IUserIdentity? identity)
        {
            UserIdentityToken? token = identity?.TokenHandler?.Token;
            encoder.WriteExtensionObject(null,
                token != null
                    ? new ExtensionObject(token)
                    : ExtensionObject.Null);
        }

        private static UserIdentity? DecodeUserIdentity(
            BinaryDecoder decoder)
        {
            ExtensionObject eo = decoder.ReadExtensionObject(null);
            if (!eo.IsNull &&
                eo.TryGetEncodeable(out IEncodeable tokenBody) &&
                tokenBody is UserIdentityToken uit)
            {
                return new UserIdentity(uit);
            }

            return null;
        }

        private static void EncodeConfiguredEndpoint(
            BinaryEncoder encoder,
            ConfiguredEndpoint? endpoint)
        {
            bool hasEndpoint = endpoint != null;
            encoder.WriteBoolean(null, hasEndpoint);
            if (!hasEndpoint)
            {
                return;
            }

            // EndpointDescription (IEncodeable)
            encoder.WriteExtensionObject(null,
                endpoint!.Description != null
                    ? new ExtensionObject(endpoint.Description)
                    : ExtensionObject.Null);

            // EndpointConfiguration
            EncodeEndpointConfiguration(encoder, endpoint.Configuration);

            encoder.WriteBoolean(null, endpoint.UpdateBeforeConnect);
            encoder.WriteEnumerated(null, endpoint.BinaryEncodingSupport);
            encoder.WriteInt32(null, endpoint.SelectedUserTokenPolicyIndex);

            // UserIdentity on endpoint (UserIdentityToken, IEncodeable)
            encoder.WriteExtensionObject(null,
                endpoint.UserIdentity != null
                    ? new ExtensionObject(endpoint.UserIdentity)
                    : ExtensionObject.Null);

            // ReverseConnect
            bool hasReverseConnect = endpoint.ReverseConnect != null;
            encoder.WriteBoolean(null, hasReverseConnect);
            if (hasReverseConnect)
            {
                encoder.WriteBoolean(null, endpoint.ReverseConnect!.Enabled);
                encoder.WriteString(null,
                    endpoint.ReverseConnect.ServerUri);
                encoder.WriteString(null,
                    endpoint.ReverseConnect.Thumbprint);
            }

            // Extensions
            encoder.WriteXmlElementArray(null, endpoint.Extensions);
        }

        private static ConfiguredEndpoint? DecodeConfiguredEndpoint(
            BinaryDecoder decoder)
        {
            bool hasEndpoint = decoder.ReadBoolean(null);
            if (!hasEndpoint)
            {
                return null;
            }

            // EndpointDescription
            EndpointDescription? description =
                decoder.ReadEncodeableAsExtensionObject<EndpointDescription>(
                    null);

            // EndpointConfiguration
            EndpointConfiguration configuration =
                DecodeEndpointConfiguration(decoder);

            bool updateBeforeConnect = decoder.ReadBoolean(null);
            BinaryEncodingSupport binaryEncodingSupport =
                decoder.ReadEnumerated<BinaryEncodingSupport>(null);
            int selectedUserTokenPolicyIndex = decoder.ReadInt32(null);

            // UserIdentity on endpoint
            ExtensionObject userIdEo = decoder.ReadExtensionObject(null);
            UserIdentityToken? userIdentity = null;
            if (!userIdEo.IsNull &&
                userIdEo.TryGetEncodeable(out IEncodeable uidBody) &&
                uidBody is UserIdentityToken uit)
            {
                userIdentity = uit;
            }

            // ReverseConnect
            ReverseConnectEndpoint? reverseConnect = null;
            bool hasReverseConnect = decoder.ReadBoolean(null);
            if (hasReverseConnect)
            {
                reverseConnect = new ReverseConnectEndpoint
                {
                    Enabled = decoder.ReadBoolean(null),
                    ServerUri = decoder.ReadString(null),
                    Thumbprint = decoder.ReadString(null)
                };
            }

            // Extensions
            ArrayOf<XmlElement> extensions = decoder.ReadXmlElementArray(null);

            return new ConfiguredEndpoint(
                null,
                description ?? new EndpointDescription(),
                configuration)
            {
                UpdateBeforeConnect = updateBeforeConnect,
                BinaryEncodingSupport = binaryEncodingSupport,
                SelectedUserTokenPolicyIndex = selectedUserTokenPolicyIndex,
                UserIdentity = userIdentity,
                ReverseConnect = reverseConnect,
                Extensions = extensions
            };
        }

        private static void EncodeEndpointConfiguration(
            BinaryEncoder encoder,
            EndpointConfiguration? config)
        {
            bool hasConfig = config != null;
            encoder.WriteBoolean(null, hasConfig);
            if (!hasConfig)
            {
                return;
            }

            encoder.WriteInt32(null, config!.OperationTimeout);
            encoder.WriteBoolean(null, config.UseBinaryEncoding);
            encoder.WriteInt32(null, config.MaxMessageSize);
            encoder.WriteInt32(null, config.MaxBufferSize);
            encoder.WriteInt32(null, config.ChannelLifetime);
            encoder.WriteInt32(null, config.SecurityTokenLifetime);
            encoder.WriteInt32(null, config.MaxArrayLength);
            encoder.WriteInt32(null, config.MaxByteStringLength);
            encoder.WriteInt32(null, config.MaxStringLength);
            encoder.WriteInt32(null, config.MaxEncodingNestingLevels);
            encoder.WriteInt32(null, config.MaxDecoderRecoveries);
        }

        private static EndpointConfiguration DecodeEndpointConfiguration(
            BinaryDecoder decoder)
        {
            bool hasConfig = decoder.ReadBoolean(null);
            if (!hasConfig)
            {
                return EndpointConfiguration.Create();
            }

            return new EndpointConfiguration
            {
                OperationTimeout = decoder.ReadInt32(null),
                UseBinaryEncoding = decoder.ReadBoolean(null),
                MaxMessageSize = decoder.ReadInt32(null),
                MaxBufferSize = decoder.ReadInt32(null),
                ChannelLifetime = decoder.ReadInt32(null),
                SecurityTokenLifetime = decoder.ReadInt32(null),
                MaxArrayLength = decoder.ReadInt32(null),
                MaxByteStringLength = decoder.ReadInt32(null),
                MaxStringLength = decoder.ReadInt32(null),
                MaxEncodingNestingLevels = decoder.ReadInt32(null),
                MaxDecoderRecoveries = decoder.ReadInt32(null)
            };
        }
    }
}
