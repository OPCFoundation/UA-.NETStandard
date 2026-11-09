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

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Maps a trusted MCP caller scope to an opaque host-local diagnostics owner key.
    /// </summary>
    internal static class PubSubDiagnosticsCaller
    {
        /// <summary>
        /// Gets the stable host-local key for the current caller or trusted in-process host.
        /// </summary>
        /// <param name="callers">Trusted request identity context.</param>
        /// <returns>A non-empty opaque owner key.</returns>
        /// <exception cref="ServiceResultException">
        /// The request has no authenticated caller.
        /// </exception>
        internal static string GetOwnerKey(McpCallerContext callers)
        {
            ArgumentNullException.ThrowIfNull(callers);
            if (callers.HasRequestScope && callers.Current is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied,
                    "PubSub diagnostics require an authenticated MCP caller.");
            }

            McpCallerIdentity? caller = callers.Current;
            return caller is null
                ? "trusted-host"
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"caller:{caller.Issuer.Length}:{caller.Issuer}:{caller.Subject.Length}:{caller.Subject}");
        }

        /// <summary>
        /// Gets the caller key from a configured service provider, using trusted-host scope for direct host calls.
        /// </summary>
        /// <param name="services">The MCP host services.</param>
        /// <returns>A non-empty opaque owner key.</returns>
        internal static string GetOwnerKey(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            return GetOwnerKey(services.GetService(typeof(McpCallerContext)) as McpCallerContext ??
                new McpCallerContext());
        }
    }
}
