// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.IO;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A session configuration stores all the information
    /// needed to reconnect a session with a new secure channel.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    public partial record class SessionOptions
    {
        /// <summary>
        /// The session name used by the client.
        /// </summary>
        [DataTypeField(Order = 1)]
        public partial string? SessionName { get; init; }

        /// <summary>
        /// The serialized user identity token. Use <see cref="Identity"/>
        /// for the high-level <see cref="IUserIdentity"/> wrapper.
        /// </summary>
        [DataTypeField(Order = 2, StructureHandling = StructureHandling.ExtensionObject)]
        public UserIdentityToken? IdentityToken
        {
            get;
            set
            {
                field = value is UserNameIdentityToken userName
                    ? new UserNameIdentityToken { UserName = userName.UserName, PolicyId = userName.PolicyId }
                    : value;
                m_identity = null;
            }
        }

        /// <summary>
        /// The identity used to create the session.
        /// Credentials supplied here are retained only in memory. After loading
        /// a snapshot, resupply credentials here or through <see cref="ISession.RenewUserIdentity"/>.
        /// </summary>
        [IgnoreDataMember]
        [JsonIgnore]
        public IUserIdentity? Identity
        {
            get => m_identity ?? (IdentityToken != null ? new UserIdentity(IdentityToken) : null);
            set
            {
                IdentityToken = value?.TokenHandler?.Token;
                m_identity = value;
            }
        }

        /// <summary>
        /// The configured endpoint for the secure channel.
        /// </summary>
        [DataTypeField(Order = 3, StructureHandling = StructureHandling.Inline)]
        public partial ConfiguredEndpoint? ConfiguredEndpoint { get; init; }

        /// <summary>
        /// If the client is configured to check the certificate domain.
        /// </summary>
        [DataTypeField(Order = 4)]
        public partial bool CheckDomain { get; init; }

        private IUserIdentity? m_identity;
    }

    /// <summary>
    /// A session state stores not just configuration but
    /// also the subscription states
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
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
        [DataTypeField(Order = 10)]
        public DateTimeUtc Timestamp { get; set; } = DateTimeUtc.Now;

        /// <summary>
        /// The session id assigned by the server.
        /// </summary>
        [DataTypeField(Order = 11)]
        public partial NodeId SessionId { get; init; }

        /// <summary>
        /// The authentication token used by the server to identify the session.
        /// </summary>
        [DataTypeField(Order = 12)]
        public partial NodeId AuthenticationToken { get; init; }

        /// <summary>
        /// The raw bytes of the last server nonce received.
        /// Persisting bytes avoids object-serialization ambiguity for Nonce internals.
        /// </summary>
        [DataTypeField(Order = 13)]
        public partial ByteString ServerNonce { get; init; }

        /// <summary>
        /// The raw bytes of the client nonce used when the session was created.
        /// Required for enhanced-policy activate signatures during reconnect.
        /// </summary>
        [DataTypeField(Order = 14)]
        public partial ByteString ClientNonce { get; init; }

        /// <summary>
        /// The user identity token policy which was used to create the session.
        /// </summary>
        [DataTypeField(Order = 15)]
        public partial string? UserIdentityTokenPolicy { get; init; }

        /// <summary>
        /// The raw bytes of the last server ECC ephemeral key received.
        /// </summary>
        [DataTypeField(Order = 16)]
        public partial ByteString ServerEccEphemeralKey { get; init; }

        /// <summary>
        /// Allows the list of subscriptions to be saved/restored
        /// when the object is serialized.
        /// </summary>
        [DataTypeField(Order = 20, StructureHandling = StructureHandling.Inline)]
        public partial ArrayOf<SubscriptionState> Subscriptions { get; init; }
    }

    /// <summary>
    /// A session configuration stores all the information
    /// needed to reconnect a session with a new secure channel.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaXsd)]
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
            return SnapshotCodec.LoadConfiguration(stream, telemetry);
        }
    }
}
