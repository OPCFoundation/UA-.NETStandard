// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Text;
using Opc.Ua.Schema.Types;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Type dictionary extensions
    /// </summary>
    internal static class TypeDictionaryExtensions
    {
        /// <summary>
        /// Creates a description from a documentation element.
        /// </summary>
        public static string? GetDescription(this Documentation? documentation)
        {
            if (documentation == null || documentation.Text == null)
            {
                return null;
            }

            var buffer = new StringBuilder();

            for (int ii = 0; ii < documentation.Text.Length; ii++)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(' ');
                }

                buffer.Append(documentation.Text[ii]);
            }

            return buffer.ToString();
        }
    }
}
