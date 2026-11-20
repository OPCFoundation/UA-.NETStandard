// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Options that control how a server builds dynamic stand-in encodeables
    /// for DataTypes that were loaded from a NodeSet at runtime.
    /// </summary>
    public sealed class ServerComplexTypeOptions
    {
        /// <summary>
        /// When <c>true</c> (the default) the server loads stand-in encodeables
        /// for custom DataTypes on startup. Set to <c>false</c> to opt out, which
        /// maps to <see cref="StandardServer.LoadComplexTypes"/>.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// When <c>true</c> only enumeration types are loaded and structured
        /// types are skipped. Defaults to <c>false</c>.
        /// </summary>
        public bool OnlyEnumTypes { get; set; }

        /// <summary>
        /// When <c>true</c> loading throws if a custom DataType could not be
        /// turned into a stand-in encodeable. When <c>false</c> the failure is
        /// logged and loading continues. Defaults to <c>false</c>.
        /// </summary>
        public bool ThrowOnError { get; set; }
    }
}
