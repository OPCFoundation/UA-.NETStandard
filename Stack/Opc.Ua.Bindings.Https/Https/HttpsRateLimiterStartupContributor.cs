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
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Adds ASP.NET Core rate limiting to an HTTPS transport listener's isolated Kestrel host.
    /// </summary>
    internal sealed class HttpsRateLimiterStartupContributor :
        IHttpsListenerStartupContributor,
        IHttpsListenerServiceContributor
    {
        private const string DefaultLimiterPartitionKey = "OpcUaHttpsTransport";

        private readonly Action<RateLimiterOptions> m_configure;

        /// <summary>
        /// Initializes a rate limiter contributor with the default limiter.
        /// </summary>
        public HttpsRateLimiterStartupContributor()
            : this(ConfigureDefaultRateLimiter)
        {
        }

        /// <summary>
        /// Initializes a rate limiter contributor with caller-supplied options.
        /// </summary>
        /// <param name="configure">The rate limiter options callback.</param>
        public HttpsRateLimiterStartupContributor(Action<RateLimiterOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            m_configure = configure;
        }

        /// <inheritdoc/>
        public void ConfigureServices(IServiceCollection services, HttpsTransportListener listener)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(listener);

            services.AddRateLimiter(m_configure);
        }

        /// <inheritdoc/>
        public void Configure(IApplicationBuilder appBuilder, HttpsTransportListener listener)
        {
            ArgumentNullException.ThrowIfNull(appBuilder);
            ArgumentNullException.ThrowIfNull(listener);

            appBuilder.UseRateLimiter();
        }

        private static void ConfigureDefaultRateLimiter(RateLimiterOptions options)
        {
            options.RejectionStatusCode = 429;
            PartitionedRateLimiter<HttpContext> limiter = CreateDefaultGlobalLimiter();
            options.GlobalLimiter = limiter;
        }

        private static PartitionedRateLimiter<HttpContext> CreateDefaultGlobalLimiter()
        {
            return PartitionedRateLimiter.Create<HttpContext, string>(
                _ => RateLimitPartition.GetFixedWindowLimiter(
                    DefaultLimiterPartitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromSeconds(1),
                        QueueLimit = 0
                    }));
        }
    }
}
#endif
