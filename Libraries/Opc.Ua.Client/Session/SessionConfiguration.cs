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
    [DataType(Namespace = Namespaces.OpcUaXsd)]
    public partial record class SessionOptions
    {
        /// <summary>
        /// The session name used by the client.
        /// </summary>
        [DataTypeField(Order = 1)]
        public string? SessionName { get; set; }

        /// <summary>
        /// The serialized user identity token. Use <see cref="Identity"/>
        /// for the high-level <see cref="IUserIdentity"/> wrapper.
        /// </summary>
        [DataTypeField(Order = 2, StructureHandling = StructureHandling.ExtensionObject)]
        public UserIdentityToken? IdentityToken { get; set; }

        /// <summary>
        /// The identity used to create the session.
        /// This is a convenience property that wraps <see cref="IdentityToken"/>.
        /// </summary>
        public IUserIdentity? Identity
        {
            get => IdentityToken != null ? new UserIdentity(IdentityToken) : null;
            set => IdentityToken = value?.TokenHandler?.Token;
        }

        /// <summary>
        /// The configured endpoint for the secure channel.
        /// </summary>
        [DataTypeField(Order = 3, StructureHandling = StructureHandling.Inline)]
        public ConfiguredEndpoint? ConfiguredEndpoint { get; set; }

        /// <summary>
        /// If the client is configured to check the certificate domain.
        /// </summary>
        [DataTypeField(Order = 4)]
        public bool CheckDomain { get; set; }
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
        public NodeId SessionId { get; set; }

        /// <summary>
        /// The authentication token used by the server to identify the session.
        /// </summary>
        [DataTypeField(Order = 12)]
        public NodeId AuthenticationToken { get; set; }

        /// <summary>
        /// The raw bytes of the last server nonce received.
        /// Persisting bytes avoids object-serialization ambiguity for Nonce internals.
        /// </summary>
        [DataTypeField(Order = 13)]
        public ByteString ServerNonce { get; set; }

        /// <summary>
        /// The raw bytes of the client nonce used when the session was created.
        /// Required for enhanced-policy activate signatures during reconnect.
        /// </summary>
        [DataTypeField(Order = 14)]
        public ByteString ClientNonce { get; set; }

        /// <summary>
        /// The user identity token policy which was used to create the session.
        /// </summary>
        [DataTypeField(Order = 15)]
        public string? UserIdentityTokenPolicy { get; set; }

        /// <summary>
        /// The raw bytes of the last server ECC ephemeral key received.
        /// </summary>
        [DataTypeField(Order = 16)]
        public ByteString ServerEccEphemeralKey { get; set; }

        /// <summary>
        /// Allows the list of subscriptions to be saved/restored
        /// when the object is serialized.
        /// </summary>
        [DataTypeField(Order = 20, StructureHandling = StructureHandling.Inline)]
        public ArrayOf<SubscriptionState> Subscriptions { get; set; }
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
            var context = ServiceMessageContext.Create(telemetry);
            context.Factory.Builder.AddOpcUaClientDataTypes();
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
