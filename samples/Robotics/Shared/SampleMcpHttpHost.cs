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
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Primitives;
using Opc.Ua.Mcp;
using HttpStatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace Opc.Ua.Samples.Robotics
{
    /// <summary>
    /// Composes loopback HTTP sample hosting with browser DNS-rebinding protection.
    /// </summary>
    internal static class SampleMcpHttpHost
    {
        /// <summary>
        /// Grants the sample transport access to its one provisioned session, without administrative capabilities.
        /// </summary>
        internal static McpExecutionOptions CreateExecutionOptions(string sessionName, bool useStdioTransport)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
            var caller = new McpCallerIdentity(
                sessionName,
                useStdioTransport ? McpCallerContext.LocalIssuer : HttpCallerIssuer,
                IsLocal: useStdioTransport);
            return new McpExecutionOptions
            {
                LocalCallerId = useStdioTransport ? sessionName : null,
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = caller,
                        SessionNames = [sessionName]
                    }
                ]
            };
        }

        /// <summary>
        /// Builds a host that accepts only the selected loopback authorities and local HTTP origins.
        /// Native local clients may omit Origin; this is not authentication against other local processes.
        /// </summary>
        internal static WebApplication Build(WebApplicationBuilder builder, int port, string sessionName)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
            ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

            string portText = port.ToString(CultureInfo.InvariantCulture);
            var authorities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"localhost:{portText}",
                $"127.0.0.1:{portText}",
                $"[::1]:{portText}"
            };
            if (port == 80)
            {
                authorities.Add("localhost");
                authorities.Add("127.0.0.1");
                authorities.Add("[::1]");
            }

            var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string authority in authorities)
            {
                origins.Add($"http://{authority}");
            }

            WebApplication app = builder.Build();
            app.Urls.Clear();
            app.Urls.Add($"http://localhost:{portText}");
            app.Use((context, next) =>
            {
                // Compare actual headers to fixed authorities, never DNS results or forwarded headers.
                bool allowedHost = context.Request.Headers.TryGetValue("Host", out StringValues host) &&
                    host.Count == 1 &&
                    host[0] is string authority &&
                    authorities.Contains(authority);
                bool allowedOrigin = !context.Request.Headers.TryGetValue("Origin", out StringValues origin) ||
                    (origin.Count == 1 &&
                        origin[0] is string value &&
                        origins.Contains(value));
                if (!allowedHost || !allowedOrigin)
                {
                    context.Response.StatusCode = HttpStatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                // The embedding sample trusts local clients only after the browser boundary has passed.
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, sessionName, ClaimValueTypes.String, HttpCallerIssuer)],
                    authenticationType: "LoopbackSample"));
                return next(context);
            });
            return app;
        }

        private const string HttpCallerIssuer = "urn:opcfoundation:mcp:loopback-sample";
    }
}
