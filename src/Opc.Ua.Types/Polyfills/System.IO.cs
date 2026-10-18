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

namespace System.IO
{
    /// <summary>
    /// Polyfills for System.IO methods that are not available in .NET Standard 2.0 or .NET Framework.
    /// </summary>
    public static class Polyfills
    {
#if NETSTANDARD2_0 || NETFRAMEWORK
        extension(Path)
        {
            /// <summary>
            /// Determines whether a path is independent of the current directory and drive.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
            public static bool IsPathFullyQualified(string path)
            {
                if (path is null)
                {
                    throw new ArgumentNullException(nameof(path));
                }
                if (Path.DirectorySeparatorChar == '/')
                {
                    return path.Length > 0 && path[0] == '/';
                }
                if (path.Length < 2)
                {
                    return false;
                }
                if (path[0] is '\\' or '/')
                {
                    return path[1] is '\\' or '/' or '?';
                }
                return path.Length >= 3 &&
                    path[0] is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') &&
                    path[1] == ':' &&
                    path[2] is '\\' or '/';
            }
        }

        /// <summary>
        /// Contains a character in a string using a specified comparison type.
        /// </summary>
        public static void Write(this TextWriter target, ReadOnlySpan<char> value)
        {
            target.Write(value.ToString());
        }

        /// <summary>
        /// Writes a span of bytes through a pooled buffer. The binary encoder
        /// writes every string, array and byte string through here.
        /// </summary>
        public static void Write(this BinaryWriter target, ReadOnlySpan<byte> value)
        {
            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(value.Length);
            try
            {
                value.CopyTo(buffer);
                target.Write(buffer, 0, value.Length);
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }
#endif
    }
}
