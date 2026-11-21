// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET8_0_OR_GREATER
using System;
using Microsoft.AspNetCore.Authentication;

namespace Opc.Ua.Bindings.WebApi
{
    /// <summary>
    /// Records an authentication scheme registered by one of the
    /// <c>AddWebApi*Auth()</c> opt-ins. The HTTPS listener serves the
    /// REST routes from its own Kestrel host and service container;
    /// <see cref="WebApiHttpsStartupContributor"/> replays every
    /// recorded registration into that container so the scheme is
    /// enforced where the requests arrive.
    /// </summary>
    internal sealed class WebApiListenerAuthRegistration
    {
        /// <summary>
        /// Creates a registration.
        /// </summary>
        /// <param name="schemeName">The name of the registered scheme.</param>
        /// <param name="register">Adds the scheme to an authentication builder.</param>
        public WebApiListenerAuthRegistration(string schemeName, Action<AuthenticationBuilder> register)
        {
            ArgumentNullException.ThrowIfNull(schemeName);
            ArgumentNullException.ThrowIfNull(register);
            SchemeName = schemeName;
            Register = register;
        }

        /// <summary>
        /// The name of the registered scheme.
        /// </summary>
        public string SchemeName { get; }

        /// <summary>
        /// Adds the scheme to an authentication builder.
        /// </summary>
        public Action<AuthenticationBuilder> Register { get; }
    }
}
#endif
