/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client.AliasNames
{
    /// <summary>
    /// Tunables for an <see cref="AliasNameClient"/>.
    /// </summary>
    public sealed class AliasNameClientOptions
    {
        /// <summary>
        /// When <c>true</c>, <see cref="AliasNameClient.FindAliasVerboseAsync"/>
        /// is allowed to perform the call even when the category's
        /// <c>FindAliasVerbose</c> method has not been discovered yet
        /// (allowing the server to report <c>BadNotImplemented</c> at the
        /// service level rather than throwing client-side). Default
        /// <c>false</c> — the client throws
        /// <see cref="System.NotSupportedException"/> for categories
        /// without the optional method.
        /// </summary>
        public bool AllowVerboseProbe { get; set; }

        /// <summary>
        /// Returns a deep clone of the options. The
        /// <see cref="AliasNameClient"/> constructor takes a snapshot to
        /// prevent later mutation from affecting in-flight calls.
        /// </summary>
        internal AliasNameClientOptions Clone()
        {
            return new AliasNameClientOptions
            {
                AllowVerboseProbe = AllowVerboseProbe
            };
        }
    }
}
