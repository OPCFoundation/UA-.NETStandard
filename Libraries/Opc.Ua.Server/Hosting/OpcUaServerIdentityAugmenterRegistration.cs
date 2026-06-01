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
