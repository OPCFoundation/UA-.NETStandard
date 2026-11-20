// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Adapter.DependencyInjection
{
    /// <summary>
    /// Options that configure an external-server PubSub action responder wired
    /// through <c>AddServerAsActionResponder</c>. Inbound PubSub Action
    /// requests targeting one of the configured <see cref="Targets"/> are mapped
    /// to OPC UA method calls on an external server through <see cref="MethodMap"/>.
    /// </summary>
    /// <remarks>
    /// Simple properties are bindable from <c>IConfiguration</c>. Object-typed
    /// members, such as <see cref="MethodMap"/>, <see cref="Targets"/>,
    /// <see cref="ServerConnectionOptions.ApplicationConfiguration"/> and
    /// <see cref="ServerConnectionOptions.UserIdentity"/>, must be supplied from
    /// code.
    /// </remarks>
    public sealed class ServerActionResponderOptions
    {
        /// <summary>
        /// The connection options describing the external OPC UA server whose
        /// methods are invoked for the actions.
        /// </summary>
        public ServerConnectionOptions Connection { get; set; } = new();

        /// <summary>
        /// The map that resolves each handled action target to the external
        /// object and method to call.
        /// </summary>
        public ActionMethodMap MethodMap { get; set; } = new();

        /// <summary>
        /// The action targets the responder is registered for. The same handler
        /// (backed by <see cref="MethodMap"/>) serves every target in the list.
        /// </summary>
        public IList<PubSubActionTarget> Targets { get; set; }
            = [];

        /// <summary>
        /// When <see langword="true"/> the responder is allowed to serve the
        /// actions over an unsecured connection. Defaults to
        /// <see langword="false"/>.
        /// </summary>
        public bool AllowUnsecured { get; set; }
    }
}
