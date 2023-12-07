/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;

namespace Opc.Ua.Client
{
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
    public class SessionConfiguration
    {
        /// <summary>
        /// Creates a session configuration
        /// </summary>
        internal SessionConfiguration(ISession session, byte[] serverNonce, NodeId authenthicationToken)
        {
            Timestamp = DateTime.UtcNow;
            SessionName = session.SessionName;
            SessionId = session.SessionId;
            AuthenticationToken = authenthicationToken;
            Identity = session.Identity;
            ConfiguredEndpoint = session.ConfiguredEndpoint;
            CheckDomain = session.CheckDomain;
            ServerNonce = serverNonce;
        }

        /// <summary>
        /// Creates the session configuration from a stream.
        /// </summary>
        public static SessionConfiguration Create(Stream stream)
        {
            // secure settings
            XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(SessionConfiguration));
                SessionConfiguration sessionConfiguration = (SessionConfiguration)serializer.ReadObject(reader);
                return sessionConfiguration;
            }
        }

        /// <summary>
        /// When the session configuration was created.
        /// </summary>
        [DataMember(IsRequired = true, Order = 10)]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The session name used by the client.
        /// </summary>
        [DataMember(IsRequired = true, Order = 20)]
        public string SessionName { get; set; }

        /// <summary>
        /// The session id assigned by the server.
        /// </summary>
        [DataMember(IsRequired = true, Order = 30)]
        public NodeId SessionId { get; set; }

        /// <summary>
        /// The authentication token used by the server to identify the session.
        /// </summary>
        [DataMember(IsRequired = true, Order = 40)]
        public NodeId AuthenticationToken { get; set; }

        /// <summary>
        /// The identity used to create the session.
        /// </summary>
        [DataMember(IsRequired = true, Order = 50)]
        public IUserIdentity Identity { get; set; }

        /// <summary>
        /// The configured endpoint for the secure channel.
        /// </summary>
        [DataMember(IsRequired = true, Order = 60)]
        public ConfiguredEndpoint ConfiguredEndpoint { get; set; }

        /// <summary>
        /// If the client is configured to check the certificate domain.
        /// </summary>
        [DataMember(IsRequired = false, Order = 70)]
        public bool CheckDomain { get; set; }

        /// <summary>
        /// The last server nonce received.
        /// </summary>
        [DataMember(IsRequired = true, Order = 80)]
        public byte[] ServerNonce { get; set; }
    }
}
