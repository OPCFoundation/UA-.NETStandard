/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Security.Cryptography;

namespace Opc.Ua.OneFuzz.TestTarget
{
    /// <summary>
    /// Real BCL-only runtime dependency, published separately by the contract tests.
    /// </summary>
    public static class FixtureDependency
    {
        /// <summary>
        /// Hashes the bytes actually delivered to a callback.
        /// </summary>
        public static string Hash(ReadOnlySpan<byte> input)
        {
            return Convert.ToHexStringLower(SHA256.HashData(input));
        }
    }
}
