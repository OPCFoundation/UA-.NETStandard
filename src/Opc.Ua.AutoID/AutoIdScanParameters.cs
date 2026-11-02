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

namespace Opc.Ua.AutoID
{
    /// <summary>
    /// The runtime parameters for variable-triggered scanning (OPC 30010 6.1.3.1).
    /// </summary>
    /// <remarks>
    /// This SDK snapshot groups values from standard variables. It is not a wire DataType
    /// and does not replace the generated ScanSettings method-argument structure.
    /// </remarks>
    public sealed record AutoIdScanParameters
    {
        /// <summary>
        /// Gets the scan duration in milliseconds; zero means unlimited.
        /// </summary>
        public double Duration { get; init; }

        /// <summary>
        /// Gets the number of cycles; zero means unlimited.
        /// </summary>
        public int Cycles { get; init; }

        /// <summary>
        /// Gets whether scanning stops when data becomes available.
        /// </summary>
        public bool DataAvailable { get; init; }

        /// <summary>
        /// Gets the format of LastScanData.
        /// </summary>
        public string CodeType { get; init; } = string.Empty;

        /// <summary>
        /// Gets the selected code indexes in the CodeTypes EnumStrings property.
        /// </summary>
        public ArrayOf<uint> CodeTypes { get; init; }
    }
}
