// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Opc.Ua.Identity;

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
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <c>null</c>.</exception>
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
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> or <paramref name="registry"/> is <c>null</c>.</exception>
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
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/>, <paramref name="provider"/>, or <paramref name="passwords"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the X.509 configuration is invalid (e.g. both or neither of SubjectName/Thumbprint are set).</exception>
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
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> or <paramref name="provider"/> is <c>null</c>.</exception>
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
