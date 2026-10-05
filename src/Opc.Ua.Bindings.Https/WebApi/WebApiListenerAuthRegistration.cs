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
