// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Server.Hosting
{
    /// <summary>
    /// Fluent helper returned by
    /// <c>Microsoft.Extensions.DependencyInjection.OpcUaServerBuilderPubSubExtensions.AddPubSub</c>;
    /// allows chained registration of optional PubSub features
    /// (Security Key Service, default SecurityGroup, custom
    /// diagnostics binding) on the OPC UA server.
    /// </summary>
    /// <remarks>
    /// The PubSub server feature is hosted as a node manager
    /// attached to the regular OPC UA server registered via
    /// <c>.AddServer(...)</c>. It is therefore registered
    /// <em>against</em> the same service collection, but composes
    /// additional services (the SKS server and the default
    /// SecurityGroup seed) that are resolved at server start.
    /// </remarks>
    public interface IPubSubServerBuilder
    {
        /// <summary>
        /// Underlying service collection. Use it to register
        /// additional services consumed by the PubSub server node
        /// manager (e.g. a custom
        /// <see cref="IPubSubKeyServiceServer"/> implementation).
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Adjusts the <see cref="PubSubServerOptions"/> via an
        /// imperative callback. Multiple calls compose; the last
        /// configuration wins for the same property.
        /// </summary>
        /// <param name="configure">Mutation callback.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configure"/> is <see langword="null"/>.
        /// </exception>
        IPubSubServerBuilder Configure(Action<PubSubServerOptions> configure);

        /// <summary>
        /// Marks the host as an SKS for other Publishers and
        /// Subscribers by setting
        /// <see cref="PubSubServerOptions.ExposeSecurityKeyService"/>
        /// to <see langword="true"/>. The matching
        /// <see cref="IPubSubKeyServiceServer"/> implementation must
        /// already be registered (or registered via
        /// <see cref="PubSubServerBuilderExtensions.WithSecurityKeyServiceServer"/>).
        /// </summary>
        /// <returns>The same builder for chaining.</returns>
        IPubSubServerBuilder ExposeSecurityKeyService();

        /// <summary>
        /// Configures a SecurityGroup that will be created on
        /// start-up when the SKS is exposed. No-op when the
        /// SecurityGroup already exists.
        /// </summary>
        /// <param name="id">SecurityGroup identifier.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="id"/> is empty.
        /// </exception>
        IPubSubServerBuilder WithDefaultSecurityGroup(string id);
    }
}
