// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
