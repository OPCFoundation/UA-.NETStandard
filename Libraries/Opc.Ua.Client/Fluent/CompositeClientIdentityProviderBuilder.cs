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

#nullable enable

using System;
using System.Collections.Generic;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Builder for composing client identity providers in registration order.
    /// </summary>
    public sealed class CompositeClientIdentityProviderBuilder
    {
        /// <summary>
        /// Adds an already-created provider.
        /// </summary>
        public CompositeClientIdentityProviderBuilder Add(
            IClientIdentityProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            m_providers.Add(provider);
            return this;
        }

        /// <summary>
        /// Adds anonymous identity support.
        /// </summary>
        public CompositeClientIdentityProviderBuilder AddAnonymous()
        {
            return Add(new AnonymousIdentityProvider());
        }

        /// <summary>
        /// Adds username/password identity support.
        /// </summary>
        public CompositeClientIdentityProviderBuilder AddUserName(
            string username,
            ISecretRegistry registry,
            SecretIdentifier passwordId)
        {
            return Add(new UserNamePasswordIdentityProvider(
                username,
                registry,
                passwordId));
        }

        /// <summary>
        /// Adds username/password identity support from options.
        /// </summary>
        public CompositeClientIdentityProviderBuilder AddUserName(
            Action<UserNameClientIdentityOptions> configure,
            ISecretRegistry registry)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var options = new UserNameClientIdentityOptions();
            configure(options);
            ValidateRequired(options.UserName, "UserName.UserName");
            ValidateRequired(options.SecretName, "UserName.SecretName");
            ValidateRequired(options.SecretStoreType, "UserName.SecretStoreType");

            return AddUserName(
                options.UserName,
                registry,
                new SecretIdentifier(
                    options.SecretName,
                    options.SecretStoreType,
                    options.SecretStorePath));
        }

        /// <summary>
        /// Adds X.509 user-certificate identity support.
        /// </summary>
        public CompositeClientIdentityProviderBuilder AddX509(
            CertificateIdentifier certificateId,
            ICertificatePasswordProvider passwordProvider,
            ICertificateProvider certificateProvider)
        {
            return Add(new X509ClientIdentityProvider(
                certificateId,
                passwordProvider,
                certificateProvider));
        }

        /// <summary>
        /// Adds X.509 user-certificate identity support from options.
        /// </summary>
        public CompositeClientIdentityProviderBuilder AddX509(
            Action<X509ClientIdentityOptions> configure,
            ICertificateProvider provider,
            ICertificatePasswordProvider passwords)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (passwords == null)
            {
                throw new ArgumentNullException(nameof(passwords));
            }

            var options = new X509ClientIdentityOptions();
            configure(options);
            ValidateRequired(options.StoreType, "X509.StoreType");
            ValidateRequired(options.StorePath, "X509.StorePath");
            if (!string.IsNullOrWhiteSpace(options.SubjectName) &&
                !string.IsNullOrWhiteSpace(options.Thumbprint))
            {
                throw new InvalidOperationException(
                    "X509.SubjectName and X509.Thumbprint are mutually exclusive.");
            }
            if (string.IsNullOrWhiteSpace(options.SubjectName) &&
                string.IsNullOrWhiteSpace(options.Thumbprint))
            {
                throw new InvalidOperationException(
                    "Either X509.SubjectName or X509.Thumbprint must be configured.");
            }

            var certificateId = new CertificateIdentifier
            {
                StoreType = options.StoreType,
                StorePath = options.StorePath,
                SubjectName = options.SubjectName,
                Thumbprint = options.Thumbprint
            };
            return AddX509(certificateId, passwords, provider);
        }

        /// <summary>
        /// Adds issued-token identity support.
        /// </summary>
        public CompositeClientIdentityProviderBuilder AddIssuedToken(
            IAccessTokenProvider accessTokenProvider)
        {
            return Add(new IssuedTokenIdentityProvider(accessTokenProvider));
        }

        /// <summary>
        /// Adds issued-token identity support from options.
        /// </summary>
        public CompositeClientIdentityProviderBuilder AddIssuedToken(
            Action<IssuedTokenClientIdentityOptions> configure,
            IAccessTokenProvider provider)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            var options = new IssuedTokenClientIdentityOptions();
            configure(options);
            ValidateRequired(options.ProfileUri, "IssuedToken.ProfileUri");
            return Add(new IssuedTokenIdentityProvider(provider, options.ProfileUri));
        }

        /// <summary>
        /// Creates the composite provider.
        /// </summary>
        public CompositeClientIdentityProvider Build()
        {
            return new CompositeClientIdentityProvider(m_providers);
        }

        private static void ValidateRequired(string value, string optionName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    optionName + " must be configured.");
            }
        }

        private readonly List<IClientIdentityProvider> m_providers = [];
    }
}
