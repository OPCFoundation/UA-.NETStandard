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
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.WotCon.Server.Hosting
{
    /// <summary>
    /// Fluent helper returned by
    /// <c>Microsoft.Extensions.DependencyInjection.OpcUaWotConServerBuilderExtensions.AddWotConServer</c>;
    /// allows chained registration of WoT asset protocol bindings and the
    /// optional server-level discovery provider.
    /// </summary>
    /// <remarks>
    /// The WoT Connectivity feature is hosted as a node manager attached
    /// to the regular OPC UA server registered via
    /// <c>.AddServer(...)</c>. It is therefore registered <em>against</em>
    /// the same service collection, but composes additional asset
    /// provider factories and an optional discovery provider that are
    /// resolved at server start.
    /// </remarks>
    public interface IWotConServerBuilder
    {
        /// <summary>
        /// Underlying service collection. Use it to register additional
        /// services the WoT Connectivity node manager, asset provider
        /// factories or discovery provider may need.
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Registers an <see cref="IWotAssetProviderFactory"/> as a
        /// singleton. Multiple factories may be registered; they are
        /// merged into <see cref="WotConnectivityServerOptions.Bindings"/>
        /// at server start in registration order.
        /// </summary>
        /// <typeparam name="TFactory">The factory implementation type.</typeparam>
        /// <returns>The same builder for chaining.</returns>
        IWotConServerBuilder AddAssetProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>()
            where TFactory : class, IWotAssetProviderFactory;

        /// <summary>
        /// Registers an <see cref="IWotAssetProviderFactory"/> produced by
        /// the supplied factory delegate. Use this overload to compose
        /// providers from other DI services.
        /// </summary>
        /// <param name="factory">Delegate producing the asset provider
        /// factory.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/>
        /// is <c>null</c>.</exception>
        IWotConServerBuilder AddAssetProvider(Func<IServiceProvider, IWotAssetProviderFactory> factory);

        /// <summary>
        /// Registers an <see cref="IWotAssetDiscoveryProvider"/> as a
        /// singleton. At most one discovery provider may be active; the
        /// registration is added via
        /// <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton{TService, TImplementation}(IServiceCollection)"/>
        /// so a user-supplied registration takes precedence.
        /// </summary>
        /// <typeparam name="T">The discovery provider implementation
        /// type.</typeparam>
        /// <returns>The same builder for chaining.</returns>
        IWotConServerBuilder AddDiscoveryProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IWotAssetDiscoveryProvider;
    }
}
