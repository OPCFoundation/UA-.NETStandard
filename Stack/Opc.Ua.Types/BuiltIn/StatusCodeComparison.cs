/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua
{
    /// <summary>
    /// Specifies how two <see cref="StatusCode"/> values are compared for equality.
    /// </summary>
    public enum StatusCodeComparison
    {
        /// <summary>
        /// Compare only the 16 code bits (bits 16 - 31) of the status code.
        /// The info, flag and additional bits are ignored. This is the
        /// comparison used by the equality operators and the default
        /// <see cref="StatusCode.Equals(StatusCode)"/> overload, because the
        /// non-code bits are almost never relevant when comparing against a
        /// well known <c>StatusCodes</c> value.
        /// </summary>
        CodeBitsOnly,

        /// <summary>
        /// Compare the entire 32-bit status value, including the info, flag and
        /// additional bits. Use this when an exact match of all bits is required.
        /// </summary>
        AllBits
    }
}
