/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Globalization;
using Opc.Ua;

[assembly: CLSCompliant(false)]

namespace MachineToolSample
{
    /// <summary>
    /// Parses bounded arguments without reflection-based configuration binding.
    /// </summary>
    internal static class SampleArguments
    {
        /// <summary>
        /// Consumes a required option value.
        /// </summary>
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
        /// Consumes an invariant integer within the supplied inclusive bounds.
        /// </summary>
        internal static int ReadInteger(ArrayOf<string> arguments, ref int index, int minimum, int maximum)
        {
            string value = ReadValue(arguments, ref index);
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ||
                number < minimum || number > maximum)
            {
                throw new ArgumentException(
                    $"Expected an integer in [{minimum}, {maximum}], not '{value}'.", nameof(arguments));
            }
            return number;
        }
    }
}
