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

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions for the OPC UA PubSub
    /// Security Key Service (SKS) — both the client side
    /// (<see cref="OpcUaSecurityKeyServiceClient"/>) and the
    /// in-process server side
    /// (<see cref="InMemoryPubSubKeyServiceServer"/>).
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.4">
    /// Part 14 §8.4 Security Key Service</see>. The client is what a
    /// publisher / subscriber uses to pull keys from a remote SKS;
    /// the server hosts groups locally for testing or for
    /// single-process scenarios.
    /// </remarks>
    public static class PubSubSecurityServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a
        /// <see cref="PullSecurityKeyProvider"/> for one SecurityGroup that
        /// pulls keys from a remote Security Key Service through the supplied
        /// <see cref="ISecurityKeyService"/> and exposes it as an
        /// <see cref="IPubSubSecurityKeyProvider"/> so the fail-closed
        /// <see cref="IPubSubSecurityWrapperResolver"/> can source keys for
        /// secured connections. A hosted service started with the generic host
        /// drives the provider's initial pull and background refresh loop.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="securityPolicyUri">
        /// PubSub security policy URI the group's keys use (see
        /// <see cref="PubSubSecurityPolicyRegistry"/>).
        /// </param>
        /// <param name="securityKeyServiceFactory">
        /// Factory that resolves the <see cref="ISecurityKeyService"/> used to
        /// call <c>GetSecurityKeys</c> (for example an
        /// <see cref="OpcUaSecurityKeyServiceClient"/>).
        /// </param>
        /// <param name="configure">
        /// Optional callback to tune the per-group
        /// <see cref="PullSecurityKeyProviderOptions"/>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="securityKeyServiceFactory"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="securityGroupId"/>
        /// is empty, or <paramref name="securityPolicyUri"/> is empty or not a
        /// known PubSub security policy.</exception>
        public static IOpcUaBuilder AddPubSubSecurityKeyServiceClient(
            this IOpcUaBuilder builder,
            string securityGroupId,
            string securityPolicyUri,
            Func<IServiceProvider, ISecurityKeyService> securityKeyServiceFactory,
            Action<PullSecurityKeyProviderOptions>? configure = null)
        {
            return RegisterPullSecurityKeyProvider(
                builder,
                securityGroupId,
                securityPolicyUri,
                securityKeyServiceFactory,
                ownsSecurityKeyService: false,
                configure);
        }

        /// <summary>
        /// Registers a Pull Security Key Service client on the fluent PubSub builder.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="securityPolicyUri">PubSub security policy URI.</param>
        /// <param name="securityKeyServiceFactory">Security Key Service factory.</param>
        /// <param name="configure">Optional provider options callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        public static IPubSubBuilder AddSecurityKeyServiceClient(
            this IPubSubBuilder builder,
            string securityGroupId,
            string securityPolicyUri,
            Func<IServiceProvider, ISecurityKeyService> securityKeyServiceFactory,
            Action<PullSecurityKeyProviderOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.OpcUaBuilder.AddPubSubSecurityKeyServiceClient(
                securityGroupId,
                securityPolicyUri,
                securityKeyServiceFactory,
                configure);
            return builder;
        }

        /// <summary>
        /// Registers a
        /// <see cref="PullSecurityKeyProvider"/> for one SecurityGroup that
        /// pulls keys from the remote Security Key Service reachable at
        /// <paramref name="endpoint"/>. Builds an
        /// <see cref="OpcUaSecurityKeyServiceClient"/> over an OPC UA session
        /// resolved from the application's <see cref="ApplicationConfiguration"/>
        /// (which must be registered in the container).
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="securityPolicyUri">
        /// PubSub security policy URI the group's keys use.
        /// </param>
        /// <param name="endpoint">Remote SKS endpoint description.</param>
        /// <param name="configure">
        /// Optional callback to tune the per-group
        /// <see cref="PullSecurityKeyProviderOptions"/>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="endpoint"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddPubSubSecurityKeyServiceClient(
            this IOpcUaBuilder builder,
            string securityGroupId,
            string securityPolicyUri,
            EndpointDescription endpoint,
            Action<PullSecurityKeyProviderOptions>? configure = null)
        {
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            return RegisterPullSecurityKeyProvider(
                builder,
                securityGroupId,
                securityPolicyUri,
                sp => new OpcUaSecurityKeyServiceClient(
                    endpoint,
                    sp.GetRequiredService<ApplicationConfiguration>(),
                    sp.GetRequiredService<ITelemetryContext>(),
                    sp.GetService<TimeProvider>() ?? TimeProvider.System),
                ownsSecurityKeyService: true,
                configure);
        }

        /// <summary>
        /// Registers an OPC UA Pull Security Key Service client on the fluent PubSub builder.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="securityPolicyUri">PubSub security policy URI.</param>
        /// <param name="endpoint">Remote SKS endpoint description.</param>
        /// <param name="configure">Optional provider options callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        public static IPubSubBuilder AddSecurityKeyServiceClient(
            this IPubSubBuilder builder,
            string securityGroupId,
            string securityPolicyUri,
            EndpointDescription endpoint,
            Action<PullSecurityKeyProviderOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.OpcUaBuilder.AddPubSubSecurityKeyServiceClient(
                securityGroupId,
                securityPolicyUri,
                endpoint,
                configure);
            return builder;
        }

        private static IOpcUaBuilder RegisterPullSecurityKeyProvider(
            IOpcUaBuilder builder,
            string securityGroupId,
            string securityPolicyUri,
            Func<IServiceProvider, ISecurityKeyService> securityKeyServiceFactory,
            bool ownsSecurityKeyService,
            Action<PullSecurityKeyProviderOptions>? configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (string.IsNullOrEmpty(securityGroupId))
            {
                throw new ArgumentException("SecurityGroupId must be non-empty.", nameof(securityGroupId));
            }
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                throw new ArgumentException("SecurityPolicyUri must be non-empty.", nameof(securityPolicyUri));
            }
            if (securityKeyServiceFactory is null)
            {
                throw new ArgumentNullException(nameof(securityKeyServiceFactory));
            }
            if (PubSubSecurityPolicyRegistry.GetByUri(securityPolicyUri) is null)
            {
                throw new ArgumentException(
                    $"Unknown PubSub security policy URI '{securityPolicyUri}'.",
                    nameof(securityPolicyUri));
            }

            if (configure is null)
            {
                builder.Services.AddOptions<PullSecurityKeyProviderOptions>(securityGroupId);
            }
            else
            {
                builder.Services.AddOptions<PullSecurityKeyProviderOptions>(securityGroupId).Configure(configure);
            }

            builder.Services.AddSingleton<IPubSubSecurityKeyProvider>(sp =>
            {
                IPubSubSecurityPolicy policy = PubSubSecurityPolicyRegistry.GetByUri(securityPolicyUri)!;
                PullSecurityKeyProviderOptions options = sp
                    .GetRequiredService<IOptionsMonitor<PullSecurityKeyProviderOptions>>()
                    .Get(securityGroupId);
                return new PullSecurityKeyProvider(
                    securityGroupId,
                    securityKeyServiceFactory(sp),
                    policy,
                    options,
                    sp.GetRequiredService<ITelemetryContext>(),
                    sp.GetService<TimeProvider>() ?? TimeProvider.System,
                    ownsSecurityKeyService);
            });
            builder.Services.AddHostedService<PubSubSecurityKeyProviderStarter>();
            return builder;
        }

        /// <summary>
        /// Registers a SetSecurityKeys push target provider for one SecurityGroup.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        public static IOpcUaBuilder AddPubSubSecurityKeyPushTarget(
            this IOpcUaBuilder builder,
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

            builder.Services.AddSingleton(sp => new PushSecurityKeyProvider(
                securityGroupId,
                sp.GetService<ITelemetryContext>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System));
            builder.Services.AddSingleton<IPubSubSecurityKeyProvider>(
                sp => sp.GetRequiredService<PushSecurityKeyProvider>());
            return builder;
        }

        /// <summary>
        /// Registers a SetSecurityKeys push target provider on the fluent PubSub builder.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        public static IPubSubBuilder AddSecurityKeyPushTarget(
            this IPubSubBuilder builder,
            string securityGroupId)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.OpcUaBuilder.AddPubSubSecurityKeyPushTarget(securityGroupId);
            return builder;
        }

        /// <summary>
        /// Registers an
        /// <see cref="InMemoryPubSubKeyServiceServer"/> as a
        /// singleton in the container. Apply
        /// <paramref name="configure"/> to seed groups at
        /// construction time.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="configure">Optional configuration callback.</param>
        public static IOpcUaBuilder AddPubSubSecurityKeyServiceServer(
            this IOpcUaBuilder builder,
            Action<InMemoryPubSubKeyServiceServer>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<IPubSubSecurityKeyStore, InMemoryPubSubSecurityKeyStore>();
            builder.Services.TryAddSingleton(sp =>
            {
                var server = new InMemoryPubSubKeyServiceServer(
                    sp.GetService<TimeProvider>() ?? TimeProvider.System,
                    sp.GetRequiredService<ITelemetryContext>(),
                    keyStore: sp.GetRequiredService<IPubSubSecurityKeyStore>());
                configure?.Invoke(server);
                return server;
            });
            builder.Services.TryAddSingleton<IPubSubKeyServiceServer>(sp =>
                sp.GetRequiredService<InMemoryPubSubKeyServiceServer>());
            return builder;
        }

        /// <summary>
        /// Registers an in-process Security Key Service server on the fluent PubSub builder.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configure">Optional configuration callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        public static IPubSubBuilder AddSecurityKeyServiceServer(
            this IPubSubBuilder builder,
            Action<InMemoryPubSubKeyServiceServer>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.OpcUaBuilder.AddPubSubSecurityKeyServiceServer(configure);
            return builder;
        }
    }
}
