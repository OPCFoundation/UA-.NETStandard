/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Wrapper carrying a node-manager factory registered under the
    /// "regular" OPC UA server feature
    /// (<see cref="IOpcUaServerBuilder.AddNodeManager{TFactory}"/> /
    /// <see cref="IOpcUaServerBuilder.AddSyncNodeManager{TFactory}"/>).
    /// </summary>
    /// <remarks>
    /// The hosted service resolves <c>IEnumerable&lt;OpcUaServerNodeManagerRegistration&gt;</c>
    /// — never the bare <see cref="IAsyncNodeManagerFactory"/> /
    /// <see cref="INodeManagerFactory"/> services — so node managers
    /// registered under the regular server feature do not bleed into
    /// other server features (GDS, LDS, WotCon) hosted in the same
    /// container.
    /// </remarks>
    public sealed class OpcUaServerNodeManagerRegistration
    {
        /// <summary>
        /// Creates a registration carrying an asynchronous factory.
        /// </summary>
        public OpcUaServerNodeManagerRegistration(IAsyncNodeManagerFactory factory)
        {
            AsyncFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Creates a registration carrying a synchronous factory.
        /// </summary>
        public OpcUaServerNodeManagerRegistration(INodeManagerFactory factory)
        {
            SyncFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        internal OpcUaServerNodeManagerRegistration(
            Func<IServiceProvider, ApplicationConfiguration, ArrayOf<IAsyncNodeManagerFactory>> factories)
        {
            m_factories = factories ?? throw new ArgumentNullException(nameof(factories));
        }

        /// <summary>
        /// The asynchronous factory instance, or <c>null</c> for a synchronous or deferred registration.
        /// </summary>
        public IAsyncNodeManagerFactory? AsyncFactory { get; }

        /// <summary>
        /// The synchronous factory instance, or <c>null</c> for an asynchronous or deferred registration.
        /// </summary>
        public INodeManagerFactory? SyncFactory { get; }

        internal ArrayOf<IAsyncNodeManagerFactory> ResolveAsyncFactories(
            IServiceProvider services,
            ApplicationConfiguration configuration)
        {
            return m_factories != null
                ? m_factories(services, configuration)
                : AsyncFactory is { } factory ? [factory] : [];
        }

        private readonly Func<IServiceProvider, ApplicationConfiguration, ArrayOf<IAsyncNodeManagerFactory>>?
            m_factories;
    }
}
