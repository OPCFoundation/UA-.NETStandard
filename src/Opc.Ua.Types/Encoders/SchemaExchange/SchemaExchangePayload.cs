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

using System;
using System.Collections.Generic;
using System.IO;

namespace Opc.Ua
{
    /// <summary>
    /// Shared limits and exception classification for decoding untrusted
    /// schema-exchange payloads (announcements and requests). The
    /// <c>Decode</c> entry points parse bytes received from a peer, so they
    /// bound their allocations with these caps and convert the structural
    /// exceptions that a malformed payload can raise into a single
    /// <see cref="FormatException"/> decode failure.
    /// </summary>
    internal static class SchemaExchangePayload
    {
        /// <summary>
        /// The maximum length, in bytes, accepted for a raw SchemaId
        /// fingerprint read from an untrusted payload.
        /// </summary>
        public const int MaxSchemaIdBytes = 256;

        /// <summary>
        /// The maximum length, in bytes, accepted for a schema document read
        /// from an untrusted payload.
        /// </summary>
        public const int MaxSchemaBytes = 8 * 1024 * 1024;

        /// <summary>
        /// The maximum number of SchemaIds accepted in a single request read
        /// from an untrusted payload.
        /// </summary>
        public const int MaxSchemaIds = 4096;

        /// <summary>
        /// Determines whether an exception indicates a malformed payload (as
        /// opposed to a caller contract violation such as a null argument, a
        /// resource-exhaustion condition, or a cancellation). Decode entry
        /// points convert these into a single <see cref="FormatException"/> so
        /// that a malformed peer payload yields one documented decode failure
        /// instead of an assortment of runtime exceptions.
        /// </summary>
        /// <param name="ex">
        /// The exception to classify.
        /// </param>
        /// <returns>
        /// <c>true</c> when the exception represents malformed input.
        /// </returns>
        public static bool IsMalformedPayload(Exception ex)
        {
            return ex is InvalidCastException
                or IndexOutOfRangeException
                or ArgumentOutOfRangeException
                or KeyNotFoundException
                or EndOfStreamException
                or InvalidDataException
                or OverflowException
                or NotSupportedException
                or InvalidOperationException;
        }
    }
}
