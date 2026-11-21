// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using Opc.Ua;

[assembly: CLSCompliant(false)]

namespace GmsSample
{
    /// <summary>
    /// Parses finite sample options without reflection-based configuration binding.
    /// </summary>
    internal static class SampleArguments
    {
        /// <summary>
        /// Reads a required nonempty option value.
        /// </summary>
        /// <exception cref="ArgumentException">The value is missing.</exception>
        internal static string ReadValue(ArrayOf<string> arguments, ref int index)
        {
            string option = arguments[index];
            if (++index >= arguments.Count ||
                string.IsNullOrWhiteSpace(arguments[index]) ||
                arguments[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"A value is required after {option}.", nameof(arguments));
            }
            return arguments[index];
        }

        /// <summary>
        /// Reads an invariant integer within inclusive resource bounds.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not within the requested bounds.</exception>
        internal static int ReadInteger(ArrayOf<string> arguments, ref int index, int minimum, int maximum)
        {
            string value = ReadValue(arguments, ref index);
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ||
                number < minimum ||
                number > maximum)
            {
                throw new ArgumentException($"Expected an integer in [{minimum}, {maximum}].", nameof(arguments));
            }
            return number;
        }
    }
}
