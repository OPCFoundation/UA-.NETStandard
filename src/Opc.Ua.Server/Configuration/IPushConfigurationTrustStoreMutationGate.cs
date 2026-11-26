// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server
{
    /// <summary>
    /// Lets a PushManagement coordinator reject trust-store writes that cannot
    /// atomically enforce its ownership grant at publication.
    /// </summary>
    /// <remarks>
    /// This gate applies to the per-entry certificate and CRL APIs. Passing the
    /// gate does not fence those APIs. A distributed provider that supports
    /// trust mutations must publish through a sink that checks the current
    /// grant in the same atomic operation that makes the new revision visible.
    /// </remarks>
    public interface IPushConfigurationTrustStoreMutationGate
    {
        /// <summary>
        /// Validates whether the caller can use trust-store APIs that lack a publication fence.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The coordinator does not support a mutation through these APIs.
        /// </exception>
        void ValidateUnfencedTrustStoreMutation();
    }
}
