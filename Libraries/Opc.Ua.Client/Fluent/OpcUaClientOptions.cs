/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Configuration;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Top-level options for
    /// <see cref="Microsoft.Extensions.DependencyInjection.OpcUaClientBuilderExtensions.AddClient(IOpcUaBuilder,System.Action{OpcUaClientOptions})"/>.
    /// </summary>
    public sealed class OpcUaClientOptions
    {
        /// <summary>
        /// Application name used when
        /// <see cref="ConfigureApplication(Action{IApplicationConfigurationBuilderClientSelected})"/>
        /// builds an <see cref="ApplicationConfiguration"/> internally.
        /// </summary>
        public string ApplicationName { get; set; } = DefaultApplicationName;

        /// <summary>
        /// Application URI used when
        /// <see cref="ConfigureApplication(Action{IApplicationConfigurationBuilderClientSelected})"/>
        /// builds an <see cref="ApplicationConfiguration"/> internally.
        /// </summary>
        public string ApplicationUri { get; set; } = string.Empty;

        /// <summary>
        /// Product URI used when
        /// <see cref="ConfigureApplication(Action{IApplicationConfigurationBuilderClientSelected})"/>
        /// builds an <see cref="ApplicationConfiguration"/> internally.
        /// </summary>
        public string ProductUri { get; set; } = string.Empty;

        /// <summary>
        /// The application configuration. Required.
        /// </summary>
        public ApplicationConfiguration? Configuration { get; set; }

        /// <summary>
        /// Default <see cref="ManagedSessionOptions"/> used by the
        /// session factory delegate registered with DI.
        /// </summary>
        public ManagedSessionOptions Session { get; set; } = new();

        /// <summary>
        /// Client identity-provider configuration bound from
        /// <c>OpcUa:Client:Identity</c>.
        /// </summary>
        public OpcUaClientIdentityOptions Identity { get; set; } = new();

        /// <summary>
        /// Client-side reverse-connect configuration. When non-null the
        /// DI container registers a singleton
        /// <see cref="ReverseConnectManager"/> that binds the configured
        /// listener endpoints on first resolution and surfaces inbound
        /// reverse-hello messages via
        /// <see cref="ReverseConnectManager.WaitForConnectionAsync"/>.
        /// The values are also written into
        /// <see cref="ClientConfiguration.ReverseConnect"/> on
        /// <see cref="Configuration"/> so the same data is observable
        /// through the application-configuration surface.
        /// </summary>
        public ClientReverseConnectOptions? ReverseConnect { get; set; }

        internal Action<IApplicationConfigurationBuilderClientSelected>?
            ApplicationConfigurationBuilder { get; set; }

        internal ApplicationInstance? BuiltApplicationInstance { get; set; }

        internal bool ValidateBuiltConfiguration { get; set; }

        /// <summary>
        /// Configures an internally-created <see cref="ApplicationConfiguration"/>
        /// using the fluent application-configuration builder.
        /// </summary>
        /// <remarks>
        /// Use this instead of assigning <see cref="Configuration"/> directly
        /// when you want <c>AddClient(...)</c> to create the application
        /// configuration from <see cref="ApplicationName"/>,
        /// <see cref="ApplicationUri"/>, and <see cref="ProductUri"/>.
        /// Missing security configuration is reported later by options
        /// validation and before the first DI-created connection.
        /// </remarks>
        /// <param name="configure">
        /// Callback that configures the client application builder. The
        /// callback must add application certificates.
        /// </param>
        /// <returns>The same options instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public OpcUaClientOptions ConfigureApplication(
            Action<IApplicationConfigurationBuilderClientSelected> configure)
        {
            ApplicationConfigurationBuilder = configure
                ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }

        internal const string DefaultApplicationName = "OpcUaClient";
    }
}
