// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Interface to create application callbacks.
    /// </summary>
    public abstract class IApplicationMessageDlg
    {
        /// <summary>
        /// The application message.
        /// </summary>
        /// <param name="text">The text of the message.</param>
        /// <param name="ask">If the application should ask the user.</param>
        public abstract void Message(string text, bool ask = false);

        /// <summary>
        /// Show the message and return result.
        /// </summary>
        public abstract Task<bool> ShowAsync();
    }
}
