// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Creates a complex-type system for a connected client session.
    /// </summary>
    public interface IComplexTypeSystemFactory
    {
        /// <summary>
        /// Creates a complex-type system bound to <paramref name="session"/>.
        /// The caller owns the returned system and disposes it once no more
        /// types are loaded through it; types already loaded stay registered.
        /// </summary>
        ComplexTypeSystem Create(ISession session);
    }
}
