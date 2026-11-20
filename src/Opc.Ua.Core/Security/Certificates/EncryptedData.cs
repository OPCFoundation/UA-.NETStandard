/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Stores a block of encrypted data.
    /// </summary>
    public class EncryptedData
    {
        /// <summary>
        /// The algorithm used to encrypt the data.
        /// </summary>
        public string? Algorithm { get; set; }

        /// <summary>
        /// The encrypted data.
        /// </summary>
        public byte[]? Data { get; set; }
    }
}
