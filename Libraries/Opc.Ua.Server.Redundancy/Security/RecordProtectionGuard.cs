/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Server.Redundancy
{
    /// <summary>
    /// Fail-closed resolution of the <see cref="IRecordProtector"/> that
    /// protects mirrored distributed state (session entries, subscription
    /// definitions, retransmission queues and continuation-point envelopes).
    /// </summary>
    /// <remarks>
    /// This protects extension state beyond OPC 10000-4 §6.6. Mirrored records carry session secrets
    /// (server/client nonces), user
    /// identity tokens and previously-sent notifications. When the shared
    /// backend is an external, network-reachable store, writing those records
    /// without authenticated encryption would expose secrets and allow a party
    /// with store access to forge records. This guard therefore refuses to mirror
    /// state to a non in-memory store unless a protector is configured, instead
    /// of silently falling back to the no-op <see cref="NullRecordProtector"/>.
    /// A deployment that knowingly accepts unprotected storage (for example a
    /// throwaway test) can register <see cref="NullRecordProtector"/> explicitly
    /// as an auditable opt-in.
    /// </remarks>
    internal static class RecordProtectionGuard
    {
        /// <summary>
        /// Resolves the registered <see cref="IRecordProtector"/>, throwing when
        /// mirrored state is backed by an external (non in-memory) shared store
        /// but no protector is configured.
        /// </summary>
        /// <param name="services">The service provider.</param>
        /// <returns>
        /// The registered protector, or <c>null</c> when an in-memory store is
        /// used and no protector is configured (the store then applies the no-op
        /// protector, which is safe for in-process state).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// An external shared store is configured without an
        /// <see cref="IRecordProtector"/>.
        /// </exception>
        public static IRecordProtector? ResolveProtectorOrThrow(IServiceProvider services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            ISharedKeyValueStore store = services.GetRequiredService<ISharedKeyValueStore>();
            return ResolveProtectorOrThrow(services, store.GetType());
        }

        /// <summary>
        /// Resolves the registered <see cref="IRecordProtector"/> for a known
        /// mirrored store type.
        /// </summary>
        /// <param name="services">The service provider.</param>
        /// <param name="storeType">The mirrored store type.</param>
        /// <returns>
        /// The registered protector, or <c>null</c> when the store type is an
        /// in-memory store and no protector is configured.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> or <paramref name="storeType"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The mirrored store type is external and no <see cref="IRecordProtector"/> is registered.
        /// </exception>
        internal static IRecordProtector? ResolveProtectorOrThrow(
            IServiceProvider services,
            Type storeType)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (storeType == null)
            {
                throw new ArgumentNullException(nameof(storeType));
            }

            IRecordProtector? protector = services.GetService<IRecordProtector>();
            if (protector != null)
            {
                return protector;
            }

            if (!typeof(InMemorySharedKeyValueStore).IsAssignableFrom(storeType))
            {
                throw new InvalidOperationException(
                    $"Distributed state mirroring is configured with an external shared key/value store " +
                    $"('{storeType.Name}') but no IRecordProtector is registered. Mirrored records " +
                    "contain session secrets, user identity tokens and notifications; without a protector they " +
                    "would be written with neither confidentiality nor integrity protection. Configure a record " +
                    "protector (for example DistributedAddressSpaceOptions.RecordProtectorFactory returning an " +
                    "AesCbcHmacRecordProtector with a managed key, or register an IRecordProtector in DI). To " +
                    "knowingly accept unprotected storage, register NullRecordProtector explicitly.");
            }

            return null;
        }
    }
}
