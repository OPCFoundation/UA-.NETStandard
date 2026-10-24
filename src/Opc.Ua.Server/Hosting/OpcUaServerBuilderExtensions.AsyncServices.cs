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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Redundancy;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    public static partial class OpcUaServerBuilderExtensions
    {
        /// <summary>
        /// Explicitly enables the experimental RSE manager Methods at runtime, without a build switch.
        /// Existing transport and resource-isolation configuration supplies the finite admission limits.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder WithExperimentalAsyncServices(
            this IOpcUaServerBuilder builder, Action<AsyncServiceExecutionOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddOptions<OpcUaServerOptions>().Configure(options =>
            {
                options.AsyncServices.Enabled = true;
                configure?.Invoke(options.AsyncServices);
            });
            builder.Services.TryAddSingleton<IAsyncServiceExecutionProviderFactory>(services =>
                new DefaultAsyncServiceExecutionProviderFactory(
                    services.GetService<IAsyncServiceAuthorizationPolicy>(),
                    services.GetService<IRecoverableExecutionOwnerProvider>(),
                    services.GetService<IRecoverableExecutionEligibilityProvider>(),
                    services.GetService<ISharedKeyValueStore>(),
                    services.GetService<IRecordProtector>(),
                    services.GetService<IRecoverableExecutionClock>(),
                    services.GetService<IRecoverableExecutionCodecQualifier>()));
            return builder;
        }

        /// <summary>
        /// Registers explicit stable-owner mappings without enabling recoverable execution or a recovery mode.
        /// Certificate renewal requires an explicit alias and does not bypass normal trust validation.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder WithRecoverableExecutionOwners(
            this IOpcUaServerBuilder builder,
            Uuid authorityId,
            ArrayOf<RecoverableExecutionApplicationMapping> applications,
            ArrayOf<RecoverableExecutionUserMapping> users)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var provider = new ConfiguredRecoverableExecutionOwnerProvider(authorityId, applications, users);
            builder.Services.AddSingleton<IRecoverableExecutionOwnerProvider>(provider);
            return builder;
        }

        /// <summary>
        /// Registers application-qualified restart timing without enabling a recovery mode.
        /// An unchecked wall clock or a reset process clock does not satisfy this contract.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder WithRecoverableExecutionClock(
            this IOpcUaServerBuilder builder,
            IRecoverableExecutionClock clock)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }
            builder.Services.AddSingleton(clock);
            return builder;
        }

        /// <summary>
        /// Registers application-owned reservations that qualify independent inputs and self-contained responses.
        /// Registration does not enable Owner or Durable recovery by itself.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder WithRecoverableExecutionEligibility(
            this IOpcUaServerBuilder builder,
            IRecoverableExecutionEligibilityProvider provider)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            builder.Services.AddSingleton(provider);
            return builder;
        }

        /// <summary>
        /// Registers explicit revisions for custom codecs used in protected durable payloads.
        /// Registration does not enable a recovery mode or qualify otherwise unknown type definitions.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder WithRecoverableExecutionCodecQualifier(
            this IOpcUaServerBuilder builder,
            IRecoverableExecutionCodecQualifier qualifier)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (qualifier == null)
            {
                throw new ArgumentNullException(nameof(qualifier));
            }
            builder.Services.AddSingleton(qualifier);
            return builder;
        }
    }
}
