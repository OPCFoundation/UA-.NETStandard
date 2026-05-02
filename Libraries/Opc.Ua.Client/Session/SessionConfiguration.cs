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
        public partial string? SessionName { get; init; }

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
        public partial ConfiguredEndpoint? ConfiguredEndpoint { get; init; }

        /// <summary>
        /// If the client is configured to check the certificate domain.
        /// </summary>
        [DataTypeField(Order = 4)]
        public partial bool CheckDomain { get; init; }
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
            var context = ServiceMessageContext.Create(telemetry);
            context.Factory.Builder.AddOpcUaClientDataTypes();
            using var decoder = new BinaryDecoder(stream, context, true);
            ArrayOf<string?> nsUris = decoder.ReadStringArray(null);
            ArrayOf<string?> serverUris = decoder.ReadStringArray(null);
            // Namespace and server URI tables on the wire are encoded as nullable string
            // arrays, but the runtime contract requires non-null entries; the bangs reflect
            // that the decoder yields no null elements for these fields.
            context.NamespaceUris = new NamespaceTable(nsUris.Memory.ToArray()!);
            context.ServerUris = new StringTable(serverUris.Memory.ToArray()!);
            var config = new SessionConfiguration();
            config.Decode(decoder);
            return config;
        }
    }
}
