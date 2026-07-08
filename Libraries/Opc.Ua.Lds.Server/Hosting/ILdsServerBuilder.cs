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

namespace Opc.Ua.Lds.Server.Hosting
{
    /// <summary>
    /// Fluent helper returned by
    /// <see cref="OpcUaLdsServerBuilderExtensions.AddLdsServer(IOpcUaBuilder, System.Action{LdsServerOptions})"/>;
    /// exposes the underlying <see cref="IServiceCollection"/> so callers can
    /// register additional services that the hosted Local Discovery Server
    /// may need (custom telemetry, certificate password providers, etc.).
    /// </summary>
    /// <remarks>
    /// Unlike <c>IOpcUaServerBuilder</c>, an LDS has no node-manager surface
    /// to attach. This interface intentionally exposes only
    /// <see cref="Services"/>; future LDS-specific extensions can be added
    /// as extension methods without breaking existing consumers.
    /// </remarks>
    public interface ILdsServerBuilder
    {
        /// <summary>
        /// Underlying service collection. Use it to register additional
        /// services the hosted LDS may need.
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Registers the LDS registration store implementation.
        /// </summary>
        /// <typeparam name="T">The store implementation type.</typeparam>
        ILdsServerBuilder AddRegistrationStore<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IRegisteredServerStore;

        /// <summary>
        /// Registers the LDS registration store factory.
        /// </summary>
        ILdsServerBuilder AddRegistrationStore(Func<IServiceProvider, IRegisteredServerStore> factory);

        /// <summary>
        /// Registers the LDS-ME multicast discovery factory.
        /// </summary>
        /// <typeparam name="T">The multicast discovery factory type.</typeparam>
        ILdsServerBuilder AddMulticastDiscovery<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, ILdsMulticastDiscoveryFactory;

        /// <summary>
        /// Registers the LDS-ME multicast discovery factory delegate.
        /// </summary>
        ILdsServerBuilder AddMulticastDiscovery(Func<IServiceProvider, ILdsMulticastDiscoveryFactory> factory);
    }
}
