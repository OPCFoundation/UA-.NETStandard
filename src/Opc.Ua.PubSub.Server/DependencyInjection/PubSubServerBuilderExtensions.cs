// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Server;
using Opc.Ua.PubSub.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent extensions on <see cref="IPubSubServerBuilder"/> that
    /// wire optional collaborators (in-memory SKS server) into the
    /// service collection backing
    /// <see cref="OpcUaServerBuilderPubSubExtensions.AddPubSub(Opc.Ua.Server.Hosting.IOpcUaServerBuilder, Action{PubSubServerOptions})"/>.
    /// </summary>
    public static class PubSubServerBuilderExtensions
    {
        /// <summary>
        /// Registers an
        /// <see cref="InMemoryPubSubKeyServiceServer"/> as the
        /// container's singleton
        /// <see cref="IPubSubKeyServiceServer"/> and flips
        /// <see cref="PubSubServerOptions.ExposeSecurityKeyService"/>
        /// to <see langword="true"/> so the node manager mounts the
        /// SKS methods.
        /// </summary>
        /// <param name="builder">PubSub server builder.</param>
        /// <param name="configure">
        /// Optional callback applied to the in-memory server
        /// instance immediately after construction. Useful for
        /// seeding initial SecurityGroups.
        /// </param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <see langword="null"/>.
        /// </exception>
        public static IPubSubServerBuilder WithSecurityKeyServiceServer(
            this IPubSubServerBuilder builder,
            Action<InMemoryPubSubKeyServiceServer>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));
            builder.Services.TryAddSingleton<IPubSubSecurityKeyStore, InMemoryPubSubSecurityKeyStore>();

            builder.Services.TryAddSingleton(sp =>
            {
                var server = new InMemoryPubSubKeyServiceServer(
                    sp.GetService<TimeProvider>() ?? TimeProvider.System,
                    sp.GetRequiredService<ITelemetryContext>(),
                    keyStore: sp.GetRequiredService<IPubSubSecurityKeyStore>(),
                    securityPolicies: sp.GetService<IPubSubSecurityPolicyRegistry>());
                configure?.Invoke(server);
                return server;
            });
            builder.Services.TryAddSingleton<IPubSubKeyServiceServer>(
                sp => sp.GetRequiredService<InMemoryPubSubKeyServiceServer>());

            return builder.ExposeSecurityKeyService();
        }

        /// <summary>
        /// Registers a push-side key provider populated by SetSecurityKeys for one SecurityGroup.
        /// </summary>
        /// <param name="builder">PubSub server builder.</param>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static IPubSubServerBuilder WithSecurityKeyPushTarget(
            this IPubSubServerBuilder builder,
            string securityGroupId)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (string.IsNullOrEmpty(securityGroupId))
            {
                throw new ArgumentException("SecurityGroupId must be non-empty.", nameof(securityGroupId));
            }

            builder.Services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));
            builder.Services.AddSingleton(sp => new PushSecurityKeyProvider(
                securityGroupId,
                sp.GetRequiredService<ITelemetryContext>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System));
            builder.Services.AddSingleton<IPubSubSecurityKeyProvider>(
                sp => sp.GetRequiredService<PushSecurityKeyProvider>());
            return builder;
        }

        /// <summary>
        /// Registers server Method handlers for every target in a PublishedActionMethod.
        /// </summary>
        /// <param name="builder">PubSub server builder.</param>
        /// <param name="dataSetWriterId">DataSetWriterId that owns the action metadata.</param>
        /// <param name="publishedAction">PublishedActionMethod metadata to bind.</param>
        /// <param name="connectionName">Optional PubSub connection name used for runtime routing.</param>
        /// <param name="serviceIdentity">
        /// Optional identity the bound Methods execute under (SA-ACT-02). When
        /// <see langword="null"/> the Methods run as an explicit <em>Anonymous</em>
        /// identity and a warning is logged at bind time.
        /// </param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="publishedAction"/> is <see langword="null"/>.
        /// </exception>
        public static IPubSubServerBuilder WithActionMethodHandlers(
            this IPubSubServerBuilder builder,
            ushort dataSetWriterId,
            PublishedActionMethodDataType publishedAction,
            string connectionName = "",
            IUserIdentity? serviceIdentity = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (publishedAction is null)
            {
                throw new ArgumentNullException(nameof(publishedAction));
            }

            builder.Services.AddSingleton(new PubSubActionMethodRegistration(
                dataSetWriterId,
                publishedAction,
                connectionName,
                serviceIdentity));
            return builder;
        }
    }
}
