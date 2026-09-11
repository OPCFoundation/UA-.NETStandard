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

#if !NET6_0_OR_GREATER
using System.IO;

namespace System.Security.Cryptography
{
    /// <summary>
    /// SHA-256 one-shot hashing on targets without the framework static methods.
    /// </summary>
    public static class Polyfills
    {
        extension(SHA256)
        {
            /// <summary>
            /// Computes the SHA-256 digest of the supplied bytes.
            /// </summary>
            public static byte[] HashData(byte[] source)
            {
                if (source is null)
                {
                    throw new ArgumentNullException(nameof(source));
                }
                using var algorithm = SHA256.Create();
                return algorithm.ComputeHash(source);
            }

            /// <summary>
            /// Computes the SHA-256 digest from the current stream position
            /// without taking ownership of the stream.
            /// </summary>
            public static byte[] HashData(Stream source)
            {
                if (source is null)
                {
                    throw new ArgumentNullException(nameof(source));
                }
                using var algorithm = SHA256.Create();
                return algorithm.ComputeHash(source);
            }
        }
    }
}
#endif
