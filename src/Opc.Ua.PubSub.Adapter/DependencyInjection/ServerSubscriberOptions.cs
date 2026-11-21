// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.DependencyInjection
{
    /// <summary>
    /// Options that configure an external-server PubSub subscriber wired through
    /// <c>AddServerAsSubscriber</c>. The subscriber writes the values
    /// received for each configured DataSetReader back to an external OPC UA
    /// server.
    /// </summary>
    /// <remarks>
    /// Simple properties are bindable from <c>IConfiguration</c>. Object-typed
    /// members, such as <see cref="ServerConnectionOptions.ApplicationConfiguration"/>
    /// and <see cref="ServerConnectionOptions.UserIdentity"/>, must be supplied
    /// from code.
    /// </remarks>
    public sealed class ServerSubscriberOptions
    {
        /// <summary>
        /// The connection options describing the external OPC UA server the
        /// subscriber writes to.
        /// </summary>
        public ServerConnectionOptions Connection { get; set; } = new();
    }
}
