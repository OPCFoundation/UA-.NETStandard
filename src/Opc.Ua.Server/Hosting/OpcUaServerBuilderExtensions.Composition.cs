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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Identity;
using Opc.Ua.Server;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    public static partial class OpcUaServerBuilderExtensions
    {
        /// <summary>
        /// Configures the hosted server's BuildInfo. Unspecified product identity comes from the
        /// effective application configuration; unspecified version numbers describe the stack.
        /// A directly registered ServerProperties takes precedence over these options.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder ConfigureServerProperties(
            this IOpcUaServerBuilder builder,
            Action<ServerProperties> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            builder.Services.AddOptions<ServerProperties>().Configure(configure);
            return builder;
        }

        /// <summary>
        /// Registers an existing authenticator without transferring ownership of the instance.
        /// Advertised user-token policies are not changed.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddIdentityAuthenticator(
            this IOpcUaServerBuilder builder,
            IUserTokenAuthenticator instance)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (instance is null)
            {
                throw new ArgumentNullException(nameof(instance));
            }
            return builder.AddIdentityAuthenticator((_, _) => instance);
        }

        /// <summary>
        /// Registers an authenticator factory evaluated with the effective server certificate validator.
        /// Advertised user-token policies are not changed.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddIdentityAuthenticator(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, ICertificateValidatorEx?, IUserTokenAuthenticator> factory)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            builder.Services.AddSingleton(new OpcUaServerIdentityAuthenticatorRegistration(
                (services, validator) =>
                [
                    factory(services, validator) ??
                        throw new InvalidOperationException("The identity authenticator factory returned null.")
                ]));
            return builder;
        }

        /// <summary>
        /// Registers a singleton task run after the hosted server starts.
        /// Repeated registration of the same task type is idempotent.
        /// </summary>
        /// <typeparam name="TTask">The startup task implementation type.</typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddStartupTask<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTask>(
                this IOpcUaServerBuilder builder)
            where TTask : class, IServerStartupTask
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IServerStartupTask, TTask>());
            return builder;
        }

        /// <summary>
        /// Registers an asynchronous callback run after the hosted server starts.
        /// Callbacks are awaited in registration order; failure aborts startup.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddStartupTask(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, IServerContext, CancellationToken, ValueTask> callback)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }
            builder.Services.AddSingleton<IServerStartupTask>(
                services => new DelegateServerStartupTask(services, callback));
            return builder;
        }

        /// <summary>
        /// Registers an asynchronous node-manager factory instance for the regular hosted server.
        /// The caller retains ownership of the factory; the server owns the node manager it creates.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddNodeManager(
            this IOpcUaServerBuilder builder,
            IAsyncNodeManagerFactory factory)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            builder.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(factory));
            return builder;
        }

        /// <summary>
        /// Registers a legacy node-manager factory instance for the regular hosted server.
        /// The caller retains ownership of the factory; the server owns the node manager it creates.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddNodeManager(
            this IOpcUaServerBuilder builder,
            INodeManagerFactory factory)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            builder.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(factory));
            return builder;
        }

        /// <summary>
        /// Registers factories produced once per start after the effective application configuration is loaded.
        /// An empty result is valid. The caller retains ownership of the factories.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddNodeManagers(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, ApplicationConfiguration, ArrayOf<IAsyncNodeManagerFactory>> factories)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (factories is null)
            {
                throw new ArgumentNullException(nameof(factories));
            }
            builder.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(factories));
            return builder;
        }

        /// <summary>
        /// Configures opt-in startup materialization of standard alias categories on the DI server.
        /// Later store mutations update queries, not the materialized browse tree.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder ConfigureAliasNames(
            this IOpcUaServerBuilder builder,
            Action<AliasNameServerOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            builder.Services.AddOptions<AliasNameServerOptions>().Configure(configure);
            return builder;
        }

        /// <summary>
        /// Configures the server-owned resource manager after its default status-code text has loaded.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder ConfigureResources(
            this IOpcUaServerBuilder builder,
            Action<ResourceManager> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureResources((_, resources) => configure(resources));
        }

        /// <summary>
        /// Configures the server-owned resource manager using services from the container.
        /// Callbacks run in registration order after the default status-code text has loaded.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder ConfigureResources(
            this IOpcUaServerBuilder builder,
            Action<IServiceProvider, ResourceManager> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            builder.Services.AddSingleton(new OpcUaServerResourceRegistration(configure));
            return builder;
        }
    }
}
