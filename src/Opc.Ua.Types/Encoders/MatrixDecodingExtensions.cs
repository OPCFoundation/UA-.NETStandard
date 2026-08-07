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
using Opc.Ua.Types;

namespace Opc.Ua
{    /// <summary>
    /// Shared helpers for reconstructing a <see cref="MatrixOf{T}"/> from decoded
    /// wire data. Used by the experimental Avro and Arrow decoders so that an
    /// attacker- or corruption-controlled dimension set is rejected consistently
    /// as <see cref="StatusCodes.BadDecodingError"/> rather than leaking the
    /// <see cref="ArgumentException"/> thrown by the <see cref="MatrixOf{T}"/>
    /// constructor.
    /// </summary>
    internal static class MatrixDecodingExtensions
    {
        /// <summary>
        /// Builds a matrix from decoded values and wire dimensions, enforcing the
        /// OPC UA matrix contract: a matrix has at least two dimensions and the
        /// product of the dimensions equals the number of decoded elements. Any
        /// violation is surfaced as <see cref="StatusCodes.BadDecodingError"/>.
        /// </summary>
        /// <typeparam name="T">The element type of the matrix.</typeparam>
        /// <param name="values">The decoded, flattened element array.</param>
        /// <param name="dimensions">The dimensions read from the wire.</param>
        public static MatrixOf<T> ToDecodedMatrix<T>(this ArrayOf<T> values, int[] dimensions)
        {
            if (dimensions is not { Length: >= 2 })
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "A matrix must have at least two dimensions.");
            }

            try
            {
                return values.ToMatrix(dimensions);
            }
            catch (ArgumentException ex)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "The encoded matrix dimensions are invalid: {0}",
                    ex.Message);
            }
        }
    }
}
