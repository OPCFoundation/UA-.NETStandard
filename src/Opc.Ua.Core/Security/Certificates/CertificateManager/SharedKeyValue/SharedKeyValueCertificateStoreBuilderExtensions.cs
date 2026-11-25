// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua;
using Opc.Ua.Redundancy;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers freshness-protected shared certificate stores.
    /// </summary>
    public static class SharedKeyValueCertificateStoreBuilderExtensions
    {
        /// <summary>
        /// Registers the <see cref="SharedKeyValueCertificateStoreProvider"/> for <c>kv:</c> certificate paths.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <remarks>
        /// The provider resolves the view registered with
        /// <see cref="SharedRecordFreshnessServiceCollectionExtensions.AddSharedRecordFreshness"/> and an
        /// authenticating <see cref="IRecordProtector"/>. It does not enroll the authority or migrate raw records.
        /// </remarks>
        public static IOpcUaBuilder AddSharedKeyValueCertificateStore(this IOpcUaBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddSingleton<ICertificateStoreProvider>(services =>
                new SharedKeyValueCertificateStoreProvider(
                    services.GetRequiredService<FreshnessProtectedKeyValueStore>(),
                    services.GetRequiredService<IRecordProtector>()));
            return builder;
        }
    }
}
