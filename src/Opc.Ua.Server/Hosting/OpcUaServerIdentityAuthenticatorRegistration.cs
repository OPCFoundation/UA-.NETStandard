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
using System.Collections.Generic;
using Opc.Ua.Identity;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Represents a public DI registration deposited by <c>AddIdentityAuthenticator&lt;T&gt;()</c> for deferred
    /// identity-authenticator creation.
    /// </summary>
    /// <remarks>
    /// Hosted server pipelines consume registrations of this type from the service collection so authenticator
    /// instances can be created after server configuration is available.
    /// </remarks>
    public sealed class OpcUaServerIdentityAuthenticatorRegistration
    {
        private readonly Func<
            IServiceProvider,
            ICertificateValidatorEx?,
            IEnumerable<IUserTokenAuthenticator>> m_factory;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpcUaServerIdentityAuthenticatorRegistration"/> class.
        /// </summary>
        /// <param name="factory">
        /// Factory that creates authenticators for a service provider and certificate validator.
        /// </param>
        /// <param name="isFallback">
        /// Whether the registration should only apply when no other authenticator registration exists.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <c>null</c>.</exception>
        public OpcUaServerIdentityAuthenticatorRegistration(
            Func<IServiceProvider, ICertificateValidatorEx?, IEnumerable<IUserTokenAuthenticator>> factory,
            bool isFallback = false)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
            IsFallback = isFallback;
        }

        /// <summary>
        /// Whether the registration is the built-in anonymous fallback
        /// used only when no other authenticator registration exists.
        /// </summary>
        public bool IsFallback { get; }

        /// <summary>
        /// Creates the configured identity authenticators.
        /// </summary>
        /// <param name="services">Service provider used to resolve dependencies.</param>
        /// <param name="certificateValidator">Certificate validator supplied by the server configuration.</param>
        /// <returns>The identity authenticators created by the registration factory.</returns>
        public IEnumerable<IUserTokenAuthenticator> CreateAuthenticators(
            IServiceProvider services,
            ICertificateValidatorEx? certificateValidator)
        {
            return m_factory(services, certificateValidator);
        }
    }
}
