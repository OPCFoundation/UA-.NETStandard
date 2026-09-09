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

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Raised by a differential fuzz target when a value survives decoding but does not
    /// re-encode to an equivalent representation.
    /// <para>
    /// This is a fidelity finding, not a robustness one. Nothing crashed, hung or corrupted
    /// memory: the stack accepted the input and produced a value whose encoded form differs
    /// between two encodings or between two generations of the same encoding.
    /// </para>
    /// <para>
    /// The distinction matters because the two properties hold over different input sets.
    /// Robustness is expected for arbitrary bytes and is therefore enforced everywhere.
    /// Fidelity is a property of well formed values: an arbitrary mutated blob can decode
    /// into a value that no encoding is required to represent losslessly, so fidelity is
    /// enforced against curated inputs and continuous fuzzing rather than against historical
    /// crash corpora collected for unrelated targets.
    /// </para>
    /// </summary>
    public sealed class EncodingFidelityException : InvalidOperationException
    {
        /// <summary>
        /// Creates the exception with the supplied message.
        /// </summary>
        public EncodingFidelityException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates the exception with the supplied message and inner exception.
        /// </summary>
        public EncodingFidelityException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates the exception with a default message.
        /// </summary>
        public EncodingFidelityException()
            : base("Encoding fidelity check failed.")
        {
        }
    }
}
