/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Identity;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Represents a public DI registration deposited by <c>AddIdentityAugmenter&lt;T&gt;()</c> for deferred
    /// identity-augmenter creation.
    /// </summary>
    /// <remarks>
    /// Hosted server pipelines consume registrations of this type from the service collection so augmenter instances
    /// can be created after server configuration is available.
    /// </remarks>
    public sealed class OpcUaServerIdentityAugmenterRegistration
    {
        private readonly Func<IServiceProvider, IIdentityAugmenter> m_factory;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpcUaServerIdentityAugmenterRegistration"/> class.
        /// </summary>
        /// <param name="factory">Factory that creates an identity augmenter for a service provider.</param>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <c>null</c>.</exception>
        public OpcUaServerIdentityAugmenterRegistration(
            Func<IServiceProvider, IIdentityAugmenter> factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Creates the configured identity augmenter.
        /// </summary>
        /// <param name="services">Service provider used to resolve dependencies.</param>
        /// <returns>The identity augmenter created by the registration factory.</returns>
        public IIdentityAugmenter CreateAugmenter(IServiceProvider services)
        {
            return m_factory(services);
        }
    }
}
