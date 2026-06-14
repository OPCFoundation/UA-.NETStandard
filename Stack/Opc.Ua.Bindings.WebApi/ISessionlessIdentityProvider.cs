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
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Opc.Ua;

namespace Opc.Ua.Bindings.WebApi
{
    /// <summary>
    /// Maps the ASP.NET Core authentication result on the inbound
    /// <see cref="HttpContext"/> to an OPC UA <see cref="IUserIdentity"/>
    /// that flows into the dispatcher through
    /// <see cref="WebApiInvocationContext.Identity"/>. Sessionless REST
    /// services use this identity for role-based access; session
    /// services attach it to the
    /// <see cref="RequestHeader.AuthenticationToken"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations are pure mappings — they do not perform
    /// authentication themselves. The ASP.NET Core authentication
    /// middleware (JwtBearer / Basic / mutual-TLS / etc.) populates
    /// <see cref="HttpContext.User"/> upstream; this provider's job is
    /// to translate the resulting <see cref="ClaimsPrincipal"/> into
    /// the OPC UA identity model. Returning <c>null</c> from
    /// <see cref="Resolve(HttpContext)"/> is equivalent to
    /// <see cref="UserIdentity()"/> (anonymous) — callers may either
    /// route on that distinction or treat <c>null</c> identities as
    /// anonymous.
    /// </para>
    /// </remarks>
    public interface ISessionlessIdentityProvider
    {
        /// <summary>
        /// Returns the OPC UA identity for the supplied
        /// <paramref name="context"/>, or <c>null</c> to fall back to
        /// the controller's <see cref="UserIdentity()"/> default.
        /// </summary>
        /// <param name="context">The inbound HTTP context.</param>
        /// <returns>The mapped identity, or <c>null</c> for anonymous.</returns>
        IUserIdentity? Resolve(HttpContext context);
    }

    /// <summary>
    /// Default <see cref="ISessionlessIdentityProvider"/> that maps the
    /// ASP.NET Core <see cref="ClaimsPrincipal"/> to a username-based
    /// <see cref="UserIdentity"/> when authenticated, and to
    /// <see cref="UserIdentity()"/> (anonymous) otherwise. The mapping
    /// is intentionally conservative: callers should register a custom
    /// provider for richer mappings (e.g. JWT claim → role assignment).
    /// </summary>
    public sealed class DefaultSessionlessIdentityProvider : ISessionlessIdentityProvider
    {
        /// <inheritdoc/>
        public IUserIdentity? Resolve(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Anonymous when the auth pipeline did not produce an
            // authenticated principal — controllers will treat null
            // identical to UserIdentity() (anonymous token).
            if (context.User.Identity is not { IsAuthenticated: true })
            {
                return null;
            }

            string? name = context.User.Identity.Name
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(name))
            {
                // Authenticated but no usable username claim — fall
                // back to anonymous. A custom provider should be
                // registered to map richer identities.
                return new UserIdentity();
            }

            // OPC UA UserName tokens carry a password; for upstream
            // authentication models (JWT / mutual-TLS) the password is
            // not available here, so we wire up a UserNameIdentityToken
            // with an empty password marker. The server's
            // IUserTokenAuthenticator is expected to honor the upstream
            // authentication outcome rather than re-verify the
            // password, OR a custom provider should be registered.
            byte[] password = Encoding.UTF8.GetBytes(string.Empty);
            return new UserIdentity(name, password);
        }
    }
}
