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

using System.IO;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A session configuration stores all the information
    /// needed to reconnect a session with a new secure channel.
    /// </summary>
    public partial record class SessionOptions : IEncodeable, IJsonEncodeable
    {
        /// <summary>
        /// The session name used by the client.
        /// </summary>
        public string? SessionName { get; set; }

        /// <summary>
        /// The identity used to create the session.
        /// </summary>
        public IUserIdentity? Identity { get; set; }

        /// <summary>
        /// The configured endpoint for the secure channel.
        /// </summary>
        public ConfiguredEndpoint? ConfiguredEndpoint { get; set; }

        /// <summary>
        /// If the client is configured to check the certificate domain.
        /// </summary>
        public bool CheckDomain { get; set; }

        #region IEncodeable Members

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => ExpandedNodeId.Null;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ExpandedNodeId.Null;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.WriteString("SessionName", SessionName);
            EncodeUserIdentity(encoder, Identity);
            EncodeConfiguredEndpoint(encoder, ConfiguredEndpoint);
            encoder.WriteBoolean("CheckDomain", CheckDomain);
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            SessionName = decoder.ReadString("SessionName");
            Identity = DecodeUserIdentity(decoder);
            ConfiguredEndpoint = DecodeConfiguredEndpoint(decoder);
            CheckDomain = decoder.ReadBoolean("CheckDomain");
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            return encodeable is SessionOptions other && Equals(other);
        }

        /// <inheritdoc/>
        object System.ICloneable.Clone()
        {
            return this with { };
        }

        #endregion

        #region ConfiguredEndpoint Encoding Helpers

        private static void EncodeUserIdentity(
            IEncoder encoder,
            IUserIdentity? identity)
        {
            UserIdentityToken? token = identity?.TokenHandler?.Token;
            encoder.WriteExtensionObject("IdentityToken",
                token != null
                    ? new ExtensionObject(token)
                    : ExtensionObject.Null);
        }

        private static IUserIdentity? DecodeUserIdentity(
            IDecoder decoder)
        {
            ExtensionObject eo = decoder.ReadExtensionObject("IdentityToken");
            if (!eo.IsNull &&
                eo.TryGetEncodeable(out IEncodeable tokenBody) &&
                tokenBody is UserIdentityToken uit)
            {
                return new UserIdentity(uit);
            }

            return null;
        }

        private static void EncodeConfiguredEndpoint(
            IEncoder encoder,
            ConfiguredEndpoint? endpoint)
        {
            bool hasEndpoint = endpoint != null;
            encoder.WriteBoolean("HasConfiguredEndpoint", hasEndpoint);
            if (!hasEndpoint)
            {
                return;
            }

            encoder.WriteExtensionObject("EndpointDescription",
                endpoint!.Description != null
                    ? new ExtensionObject(endpoint.Description)
                    : ExtensionObject.Null);

            EncodeEndpointConfiguration(encoder, endpoint.Configuration);

            encoder.WriteBoolean("UpdateBeforeConnect", endpoint.UpdateBeforeConnect);
            encoder.WriteEnumerated("BinaryEncodingSupport", endpoint.BinaryEncodingSupport);
            encoder.WriteInt32("SelectedUserTokenPolicyIndex", endpoint.SelectedUserTokenPolicyIndex);

            encoder.WriteExtensionObject("EndpointUserIdentity",
                endpoint.UserIdentity != null
                    ? new ExtensionObject(endpoint.UserIdentity)
                    : ExtensionObject.Null);

            bool hasReverseConnect = endpoint.ReverseConnect != null;
            encoder.WriteBoolean("HasReverseConnect", hasReverseConnect);
            if (hasReverseConnect)
            {
                encoder.WriteBoolean("ReverseConnectEnabled", endpoint.ReverseConnect!.Enabled);
                encoder.WriteString("ReverseConnectServerUri", endpoint.ReverseConnect.ServerUri);
                encoder.WriteString("ReverseConnectThumbprint", endpoint.ReverseConnect.Thumbprint);
            }

            encoder.WriteXmlElementArray("Extensions", endpoint.Extensions);
        }

        private static ConfiguredEndpoint? DecodeConfiguredEndpoint(
            IDecoder decoder)
        {
            bool hasEndpoint = decoder.ReadBoolean("HasConfiguredEndpoint");
            if (!hasEndpoint)
            {
                return null;
            }

            EndpointDescription? description =
                decoder.ReadEncodeableAsExtensionObject<EndpointDescription>(
                    "EndpointDescription");

            EndpointConfiguration configuration =
                DecodeEndpointConfiguration(decoder);

            bool updateBeforeConnect = decoder.ReadBoolean("UpdateBeforeConnect");
            BinaryEncodingSupport binaryEncodingSupport =
                decoder.ReadEnumerated<BinaryEncodingSupport>("BinaryEncodingSupport");
            int selectedUserTokenPolicyIndex = decoder.ReadInt32("SelectedUserTokenPolicyIndex");

            ExtensionObject userIdEo = decoder.ReadExtensionObject("EndpointUserIdentity");
            UserIdentityToken? userIdentity = null;
            if (!userIdEo.IsNull &&
                userIdEo.TryGetEncodeable(out IEncodeable uidBody) &&
                uidBody is UserIdentityToken uit)
            {
                userIdentity = uit;
            }

            ReverseConnectEndpoint? reverseConnect = null;
            bool hasReverseConnect = decoder.ReadBoolean("HasReverseConnect");
            if (hasReverseConnect)
            {
                reverseConnect = new ReverseConnectEndpoint
                {
                    Enabled = decoder.ReadBoolean("ReverseConnectEnabled"),
                    ServerUri = decoder.ReadString("ReverseConnectServerUri"),
                    Thumbprint = decoder.ReadString("ReverseConnectThumbprint")
                };
            }

            ArrayOf<XmlElement> extensions = decoder.ReadXmlElementArray("Extensions");

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
            IEncoder encoder,
            EndpointConfiguration? config)
        {
            bool hasConfig = config != null;
            encoder.WriteBoolean("HasEndpointConfiguration", hasConfig);
            if (!hasConfig)
            {
                return;
            }

            encoder.WriteInt32("OperationTimeout", config!.OperationTimeout);
            encoder.WriteBoolean("UseBinaryEncoding", config.UseBinaryEncoding);
            encoder.WriteInt32("MaxMessageSize", config.MaxMessageSize);
            encoder.WriteInt32("MaxBufferSize", config.MaxBufferSize);
            encoder.WriteInt32("ChannelLifetime", config.ChannelLifetime);
            encoder.WriteInt32("SecurityTokenLifetime", config.SecurityTokenLifetime);
            encoder.WriteInt32("MaxArrayLength", config.MaxArrayLength);
            encoder.WriteInt32("MaxByteStringLength", config.MaxByteStringLength);
            encoder.WriteInt32("MaxStringLength", config.MaxStringLength);
            encoder.WriteInt32("MaxEncodingNestingLevels", config.MaxEncodingNestingLevels);
            encoder.WriteInt32("MaxDecoderRecoveries", config.MaxDecoderRecoveries);
        }

        private static EndpointConfiguration DecodeEndpointConfiguration(
            IDecoder decoder)
        {
            bool hasConfig = decoder.ReadBoolean("HasEndpointConfiguration");
            if (!hasConfig)
            {
                return EndpointConfiguration.Create();
            }

            return new EndpointConfiguration
            {
                OperationTimeout = decoder.ReadInt32("OperationTimeout"),
                UseBinaryEncoding = decoder.ReadBoolean("UseBinaryEncoding"),
                MaxMessageSize = decoder.ReadInt32("MaxMessageSize"),
                MaxBufferSize = decoder.ReadInt32("MaxBufferSize"),
                ChannelLifetime = decoder.ReadInt32("ChannelLifetime"),
                SecurityTokenLifetime = decoder.ReadInt32("SecurityTokenLifetime"),
                MaxArrayLength = decoder.ReadInt32("MaxArrayLength"),
                MaxByteStringLength = decoder.ReadInt32("MaxByteStringLength"),
                MaxStringLength = decoder.ReadInt32("MaxStringLength"),
                MaxEncodingNestingLevels = decoder.ReadInt32("MaxEncodingNestingLevels"),
                MaxDecoderRecoveries = decoder.ReadInt32("MaxDecoderRecoveries")
            };
        }

        #endregion
    }

    /// <summary>
    /// A session state stores not just configuration but
    /// also the subscription states
    /// </summary>
    public partial record class SessionState : SessionOptions
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public SessionState()
        {
        }

        /// <summary>
        /// Creates a session state
        /// </summary>
        public SessionState(SessionOptions options)
            : base(options)
        {
        }

        /// <summary>
        /// When the session configuration was created.
        /// </summary>
        public DateTimeUtc Timestamp { get; set; } = DateTimeUtc.Now;

        /// <summary>
        /// The session id assigned by the server.
        /// </summary>
        public NodeId SessionId { get; set; }

        /// <summary>
        /// The authentication token used by the server to identify the session.
        /// </summary>
        public NodeId AuthenticationToken { get; set; }

        /// <summary>
        /// The raw bytes of the last server nonce received.
        /// Persisting bytes avoids object-serialization ambiguity for Nonce internals.
        /// </summary>
        public ByteString ServerNonce { get; set; }

        /// <summary>
        /// The raw bytes of the client nonce used when the session was created.
        /// Required for enhanced-policy activate signatures during reconnect.
        /// </summary>
        public ByteString ClientNonce { get; set; }

        /// <summary>
        /// The user identity token policy which was used to create the session.
        /// </summary>
        public string? UserIdentityTokenPolicy { get; set; }

        /// <summary>
        /// The raw bytes of the last server ECC ephemeral key received.
        /// </summary>
        public ByteString ServerEccEphemeralKey { get; set; }

        /// <summary>
        /// Allows the list of subscriptions to be saved/restored
        /// when the object is serialized.
        /// </summary>
        public ArrayOf<SubscriptionState> Subscriptions { get; set; }

        #region IEncodeable Members

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => ExpandedNodeId.Null;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

        /// <inheritdoc/>
        public override ExpandedNodeId JsonEncodingId => ExpandedNodeId.Null;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.WriteDateTime("Timestamp", Timestamp);
            encoder.WriteNodeId("SessionId", SessionId);
            encoder.WriteNodeId("AuthenticationToken", AuthenticationToken);
            encoder.WriteByteString("ServerNonce", ServerNonce);
            encoder.WriteByteString("ClientNonce", ClientNonce);
            encoder.WriteString("UserIdentityTokenPolicy", UserIdentityTokenPolicy);
            encoder.WriteByteString("ServerEccEphemeralKey", ServerEccEphemeralKey);
            encoder.WriteEncodeableArray("Subscriptions", Subscriptions);
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            Timestamp = decoder.ReadDateTime("Timestamp");
            SessionId = decoder.ReadNodeId("SessionId");
            AuthenticationToken = decoder.ReadNodeId("AuthenticationToken");
            ServerNonce = decoder.ReadByteString("ServerNonce");
            ClientNonce = decoder.ReadByteString("ClientNonce");
            UserIdentityTokenPolicy = decoder.ReadString("UserIdentityTokenPolicy");
            ServerEccEphemeralKey = decoder.ReadByteString("ServerEccEphemeralKey");
            Subscriptions = decoder.ReadEncodeableArray<SubscriptionState>("Subscriptions");
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            return encodeable is SessionState other && Equals(other);
        }

        #endregion
    }

    /// <summary>
    /// A session configuration stores all the information
    /// needed to reconnect a session with a new secure channel.
    /// </summary>
    public partial record class SessionConfiguration : SessionState
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public SessionConfiguration()
        {
        }

        /// <summary>
        /// Creates a session configuration
        /// </summary>
        public SessionConfiguration(SessionState state)
            : base(state)
        {
        }

        /// <summary>
        /// Creates the session configuration from a stream.
        /// </summary>
        public static SessionConfiguration? Create(Stream stream, ITelemetryContext telemetry)
        {
            var context = ServiceMessageContext.Create(telemetry);
            using var decoder = new BinaryDecoder(stream, context, true);
            ArrayOf<string> nsUris = decoder.ReadStringArray(null);
            ArrayOf<string> serverUris = decoder.ReadStringArray(null);
            context.NamespaceUris = new NamespaceTable(nsUris.Memory.ToArray());
            context.ServerUris = new StringTable(serverUris.Memory.ToArray());
            var config = new SessionConfiguration();
            config.Decode(decoder);
            return config;
        }
    }
}
