// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client
{
    /// <summary>
    /// X.509 user-certificate client identity options.
    /// </summary>
    public sealed class X509ClientIdentityOptions
    {
        /// <summary>
        /// Certificate store type.
        /// </summary>
        public string StoreType { get; set; } = string.Empty;

        /// <summary>
        /// Certificate store path.
        /// </summary>
        public string StorePath { get; set; } = string.Empty;

        /// <summary>
        /// Certificate subject name selector.
        /// </summary>
        public string? SubjectName { get; set; }

        /// <summary>
        /// Certificate thumbprint selector.
        /// </summary>
        public string? Thumbprint { get; set; }
    }
}
