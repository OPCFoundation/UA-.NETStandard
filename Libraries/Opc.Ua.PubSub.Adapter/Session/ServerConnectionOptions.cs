/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.PubSub.Adapter.Session
{
    /// <summary>
    /// Configuration for an <see cref="IServerSession"/> that connects
    /// the PubSub adapters to an external OPC UA server through a managed
    /// client session. The simple value-typed members are bindable from
    /// <c>IConfiguration</c>; the object-typed members
    /// (<see cref="ApplicationConfiguration"/>, <see cref="UserIdentity"/>)
    /// are supplied in code.
    /// </summary>
    public sealed class ServerConnectionOptions
    {
        /// <summary>
        /// The endpoint or discovery URL of the external OPC UA server, for
        /// example <c>opc.tcp://localhost:4840</c>.
        /// </summary>
        public string EndpointUrl { get; set; } = string.Empty;

        /// <summary>
        /// The message security mode requested for the session. Defaults to
        /// <see cref="MessageSecurityMode.SignAndEncrypt"/>.
        /// </summary>
        public MessageSecurityMode SecurityMode { get; set; }
            = MessageSecurityMode.SignAndEncrypt;

        /// <summary>
        /// The security policy URI requested for the session. When <c>null</c>
        /// (the default) the most secure policy advertised by the server for
        /// the requested <see cref="SecurityMode"/> is selected automatically.
        /// </summary>
        public string? SecurityPolicyUri { get; set; }

        /// <summary>
        /// An explicit user identity to activate the session with. When set it
        /// takes precedence over <see cref="UserName"/> /
        /// <see cref="Password"/>. When <c>null</c> and no user name is
        /// supplied an anonymous identity is used.
        /// </summary>
        public IUserIdentity? UserIdentity { get; set; }

        /// <summary>
        /// The user name for user-name/password authentication. Ignored when
        /// <see cref="UserIdentity"/> is set or when the value is empty
        /// (anonymous).
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// The password for user-name/password authentication. Used together
        /// with <see cref="UserName"/>.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// The session name reported to the server. Defaults to
        /// <c>Opc.Ua.PubSub.Adapter</c>.
        /// </summary>
        public string SessionName { get; set; } = "Opc.Ua.PubSub.Adapter";

        /// <summary>
        /// The requested session timeout in milliseconds. Defaults to
        /// <c>60000</c>.
        /// </summary>
        public uint SessionTimeout { get; set; } = 60000;

        /// <summary>
        /// The application configuration used to create the client session.
        /// When <c>null</c> a minimal client configuration is built
        /// automatically from <see cref="ApplicationName"/>. A configuration
        /// with a valid application instance certificate must be supplied for
        /// secured connections.
        /// </summary>
        public ApplicationConfiguration? ApplicationConfiguration { get; set; }

        /// <summary>
        /// The application name used when an
        /// <see cref="ApplicationConfiguration"/> is built automatically.
        /// Defaults to <c>Opc.Ua.PubSub.Adapter</c>.
        /// </summary>
        public string ApplicationName { get; set; } = "Opc.Ua.PubSub.Adapter";
    }
}
