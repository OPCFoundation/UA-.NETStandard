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
using System.IO;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml;

namespace Opc.Ua.Client
{
    [JsonSerializable(typeof(SessionOptions))]
    [JsonSerializable(typeof(SessionState))]
    [JsonSerializable(typeof(SessionConfiguration))]
    internal partial class SessionConfigurationContext : JsonSerializerContext;

    /// <summary>
    /// A session configuration stores all the information
    /// needed to reconnect a session with a new secure channel.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [KnownType(typeof(UserIdentityToken))]
    [KnownType(typeof(AnonymousIdentityToken))]
    [KnownType(typeof(X509IdentityToken))]
    [KnownType(typeof(IssuedIdentityToken))]
    [KnownType(typeof(UserIdentity))]
    public record class SessionOptions
    {
        /// <summary>
        /// The session name used by the client.
        /// </summary>
        [DataMember(IsRequired = true, Order = 20)]
        public string? SessionName { get; init; }

        /// <summary>
        /// The identity used to create the session.
        /// </summary>
        [DataMember(IsRequired = true, Order = 50)]
        public IUserIdentity? Identity { get; init; }

        /// <summary>
        /// The configured endpoint for the secure channel.
        /// </summary>
        [DataMember(IsRequired = true, Order = 60)]
        public ConfiguredEndpoint? ConfiguredEndpoint { get; init; }

        /// <summary>
        /// If the client is configured to check the certificate domain.
        /// </summary>
        [DataMember(IsRequired = false, Order = 70)]
        public bool CheckDomain { get; init; }
    }

    /// <summary>
    /// A session state stores not just configuration but
    /// also the subscription states
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [KnownType(typeof(UserIdentityToken))]
    [KnownType(typeof(AnonymousIdentityToken))]
    [KnownType(typeof(X509IdentityToken))]
    [KnownType(typeof(IssuedIdentityToken))]
    [KnownType(typeof(UserIdentity))]
    public record class SessionState : SessionOptions
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
        [DataMember(IsRequired = true, Order = 10)]
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// The session id assigned by the server.
        /// </summary>
        [DataMember(IsRequired = true, Order = 30)]
        public NodeId SessionId { get; init; } = NodeId.Null;

        /// <summary>
        /// The authentication token used by the server to identify the session.
        /// </summary>
        [DataMember(IsRequired = true, Order = 40)]
        public NodeId AuthenticationToken { get; init; } = NodeId.Null;

        /// <summary>
        /// The last server nonce received.
        /// </summary>
        [DataMember(IsRequired = true, Order = 80)]
        public Nonce? ServerNonce { get; init; }

        /// <summary>
        /// The user identity token policy which was used to create the session.
        /// </summary>
        [DataMember(IsRequired = true, Order = 90)]
        public string? UserIdentityTokenPolicy { get; init; }

        /// <summary>
        /// The last server ecc ephemeral key received.
        /// </summary>
        [DataMember(IsRequired = false, Order = 100)]
        public Nonce? ServerEccEphemeralKey { get; init; }

        /// <summary>
        /// Allows the list of subscriptions to be saved/restored
        /// when the object is serialized.
        /// </summary>
        [DataMember(Order = 200)]
        public SubscriptionStateCollection? Subscriptions { get; init; }
    }

    /// <summary>
    /// A session configuration stores all the information
    /// needed to reconnect a session with a new secure channel.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [KnownType(typeof(UserIdentityToken))]
    [KnownType(typeof(AnonymousIdentityToken))]
    [KnownType(typeof(X509IdentityToken))]
    [KnownType(typeof(IssuedIdentityToken))]
    [KnownType(typeof(UserIdentity))]
    public record class SessionConfiguration : SessionState
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
            // secure settings
            XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
            using var reader = XmlReader.Create(stream, settings);
            var serializer = new DataContractSerializer(typeof(SessionConfiguration));
            using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
            return (SessionConfiguration?)serializer.ReadObject(reader);
        }
    }
}
