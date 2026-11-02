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
 *
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
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// Fluent registration for the provider-backed Part 22 server model.
    /// </summary>
    public static class NetworkModelServerBuilderExtensions
    {
        /// <summary>
        /// Registers one provider-backed Part 22 address-space manager.
        /// Mapping mutation is denied unless the application's explicit policy allows it.
        /// </summary>
        /// <param name="builder">The regular OPC UA server builder.</param>
        /// <param name="provider">Application-owned snapshot provider.</param>
        /// <param name="authorizeMappingEdits">Policy evaluated for every mapping-method call.</param>
        /// <returns>The builder for fluent composition.</returns>
        public static IOpcUaServerBuilder AddNetworkModel(
            this IOpcUaServerBuilder builder,
            INetworkModelProvider provider,
            Func<ISystemContext, bool>? authorizeMappingEdits = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(provider);

            var factory = new NetworkModelNodeManagerFactory(
                provider,
                authorizeMappingEdits ?? (static _ => false));
            return builder.AddNodeManager(factory);
        }

        /// <summary>
        /// Resolves an application-owned provider from dependency injection when the server starts.
        /// Mapping mutation is denied unless the application's explicit policy allows it.
        /// </summary>
        public static IOpcUaServerBuilder AddNetworkModel(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, INetworkModelProvider> providerFactory,
            Func<ISystemContext, bool>? authorizeMappingEdits = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(providerFactory);
            return builder.AddNodeManagers((services, _) =>
            [
                new NetworkModelNodeManagerFactory(
                    providerFactory(services) ??
                        throw new InvalidOperationException("The network model provider factory returned null."),
                    authorizeMappingEdits ?? (static _ => false))
            ]);
        }
    }
}
