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
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Opc.Ua.XRegistry.Connector
{
    internal sealed class XRegistryConnectorAuthentication(
        ISecretRegistry secrets, SecretIdentifier? inboundCredential, bool allowAnonymousReads)
    {
        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            string? authorization = context.Request.Headers.Authorization;
            if (!string.IsNullOrEmpty(authorization))
            {
                if (!AuthenticationHeaderValue.TryParse(authorization, out AuthenticationHeaderValue? value) ||
                    !value.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
                    value.Parameter is null ||
                    inboundCredential is null ||
                    !context.Request.IsHttps)
                {
                    Reject(context);
                    return;
                }
                using ISecret? expected = await secrets.GetAsync(inboundCredential, context.RequestAborted)
                    .ConfigureAwait(false);
                byte[] supplied = Encoding.UTF8.GetBytes(value.Parameter);
                bool authenticated;
                try
                {
                    authenticated = expected is not null &&
                        CryptographicOperations.FixedTimeEquals(supplied, expected.Bytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(supplied);
                }
                if (!authenticated)
                {
                    Reject(context);
                    return;
                }
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, inboundCredential.Name),
                        new Claim(ClaimTypes.Name, inboundCredential.Name),
                        new Claim(ClaimTypes.Role, "xregistry.write")
                    ], "xregistry-secret"));
            }
            else if (!allowAnonymousReads ||
                !(HttpMethods.IsGet(context.Request.Method) ||
                    HttpMethods.IsHead(context.Request.Method) ||
                    HttpMethods.IsOptions(context.Request.Method)))
            {
                Reject(context);
                return;
            }
            await next(context).ConfigureAwait(false);
        }

        private static void Reject(HttpContext context)
        {
            context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer realm=\"xregistry\"";
        }
    }
}
