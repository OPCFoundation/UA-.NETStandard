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
using Opc.Ua;
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

            builder.Services.TryAddSingleton(sp =>
            {
                var server = new InMemoryPubSubKeyServiceServer(
                    sp.GetService<TimeProvider>() ?? TimeProvider.System,
                    sp.GetRequiredService<ITelemetryContext>());
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
            return builder;
        }

        /// <summary>
        /// Registers server Method handlers for every target in a PublishedActionMethod.
        /// </summary>
        /// <param name="builder">PubSub server builder.</param>
        /// <param name="dataSetWriterId">DataSetWriterId that owns the action metadata.</param>
        /// <param name="publishedAction">PublishedActionMethod metadata to bind.</param>
        /// <param name="connectionName">Optional PubSub connection name used for runtime routing.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="publishedAction"/> is <see langword="null"/>.
        /// </exception>
        public static IPubSubServerBuilder WithActionMethodHandlers(
            this IPubSubServerBuilder builder,
            ushort dataSetWriterId,
            PublishedActionMethodDataType publishedAction,
            string connectionName = "")
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
                connectionName));
            return builder;
        }
    }
}
