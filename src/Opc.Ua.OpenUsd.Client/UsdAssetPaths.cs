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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Policy for USD asset paths that come from an untrusted source (an OPC UA
    /// server) and are later resolved by a native USD library.
    /// </summary>
    /// <remarks>
    /// Native USD resolves an asset path against the file system and, on Windows,
    /// against UNC shares, so a server-chosen <c>@\\host\share\x.usd@</c> leaks the
    /// user's credentials and feeds attacker content to the native parsers. These
    /// helpers admit only plain relative paths that stay beneath the authoring
    /// layer's directory, and find every asset path a USDA layer contains without
    /// trusting a grammar the native parser might read differently: anything that
    /// could hide text from this scanner (C-style comments, bare carriage returns,
    /// control characters, triple-delimited asset paths) is refused outright.
    /// </remarks>
    internal static class UsdAssetPaths
    {
        /// <summary>
        /// Longest asset path or prim path accepted.
        /// </summary>
        public const int MaximumPathLength = 512;

        /// <summary>
        /// Normalizes a composition reference such as <c>@pump.usda@&lt;/Pump&gt;</c>.
        /// </summary>
        /// <param name="reference">Reference as supplied (for example by a server).</param>
        /// <param name="normalized">The re-emitted reference when accepted.</param>
        /// <returns><c>true</c> when the reference is a relative layer path inside the
        /// authoring directory, optionally followed by an absolute prim path.</returns>
        public static bool TryNormalizeReference(string? reference, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrEmpty(reference) || reference!.Length > 2 * MaximumPathLength)
            {
                return false;
            }
            string text = reference.Trim();
            if (text.Length < 3 || text[0] != '@' || text[1] == '@')
            {
                return false;
            }
            int close = text.IndexOf('@', 1);
            if (close < 0)
            {
                return false;
            }
            string path = text.Substring(1, close - 1);
            string rest = text.Substring(close + 1);
            if (!IsSafeRelativePath(path) || !HasLayerExtension(path))
            {
                return false;
            }
            string? primPath = null;
            if (rest.Length > 0)
            {
                if (rest.Length < 3 || rest[0] != '<' || rest[rest.Length - 1] != '>')
                {
                    return false;
                }
                primPath = rest.Substring(1, rest.Length - 2);
                if (!IsAbsolutePrimPath(primPath))
                {
                    return false;
                }
            }
            normalized = primPath == null ? "@" + path + "@" : "@" + path + "@<" + primPath + ">";
            return true;
        }

        /// <summary>
        /// Whether <paramref name="path"/> is a relative asset path whose segments are
        /// plain file names, so it can only resolve beneath the authoring layer.
        /// </summary>
        /// <param name="path">The asset path without its <c>@</c> delimiters.</param>
        /// <returns><c>true</c> when the path is safe to hand to a native resolver.</returns>
        public static bool IsSafeRelativePath(string? path)
        {
            if (string.IsNullOrEmpty(path) || path!.Length > MaximumPathLength)
            {
                return false;
            }
            string relative = path.StartsWith("./", StringComparison.Ordinal) ? path.Substring(2) : path;
            if (relative.Length == 0)
            {
                return false;
            }
            foreach (string segment in relative.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment[segment.Length - 1] == '.' || IsDeviceName(segment))
                {
                    return false;
                }
                foreach (char c in segment)
                {
                    if (!IsAsciiLetterOrDigit(c) && c != '_' && c != '-' && c != '.')
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Resolves a safe relative asset path against the directory of the layer
        /// that authored it.
        /// </summary>
        /// <param name="layerPath">Full path of the authoring layer.</param>
        /// <param name="assetPath">A path accepted by <see cref="IsSafeRelativePath"/>.</param>
        /// <returns>The full local path.</returns>
        public static string Resolve(string layerPath, string assetPath)
        {
            string relative = assetPath.StartsWith("./", StringComparison.Ordinal)
                ? assetPath.Substring(2)
                : assetPath;
            string directory = Path.GetDirectoryName(Path.GetFullPath(layerPath)) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>
        /// Whether the path names a USD layer format.
        /// </summary>
        /// <param name="path">Asset path.</param>
        /// <returns><c>true</c> for <c>.usd</c>, <c>.usda</c>, <c>.usdc</c> and <c>.usdz</c>.</returns>
        public static bool HasLayerExtension(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".usda", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".usd", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".usdc", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".usdz", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns every asset path authored in a USDA text layer.
        /// </summary>
        /// <param name="text">The layer text.</param>
        /// <returns>The asset paths, without their <c>@</c> delimiters. Empty asset
        /// paths (<c>@@</c>) are not returned.</returns>
        /// <exception cref="InvalidDataException">The text is not USDA, or contains a
        /// construct that a native parser could read differently from this scanner,
        /// or a construct that resolves files through a string instead of an asset
        /// path (value clips).</exception>
        public static IReadOnlyList<string> ScanUsdaAssetPaths(string text)
        {
            if (text == null || !text.StartsWith("#usda 1.0", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The layer is not a USDA 1.0 text layer.");
            }
            // Native Sdf ends a line (and so a line comment) at CR, LF or CRLF. Refusing
            // every CR that does not start a CRLF, and every other control character,
            // leaves LF as the only line end for both readers.
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if ((character == '\r' && (index + 1 >= text.Length || text[index + 1] != '\n')) ||
                    (character < ' ' && character is not '\r' and not '\n' and not '\t') ||
                    character == '\u007f')
                {
                    throw new InvalidDataException(
                        "Bare carriage returns and control characters are not admitted in a USDA layer.");
                }
            }
            var assets = new List<string>();
            var word = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (IsWordChar(c))
                {
                    word.Append(c);
                    i++;
                    continue;
                }
                CheckWord(word);
                switch (c)
                {
                    case '#':
                        i = SkipLine(text, i);
                        break;
                    case '/' when i + 1 < text.Length && text[i + 1] == '/':
                        i = SkipLine(text, i);
                        break;
                    case '/' when i + 1 < text.Length && text[i + 1] == '*':
                        // Sdf block comments do not nest.
                        int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        if (end < 0)
                        {
                            throw new InvalidDataException("A USDA block comment is not terminated.");
                        }
                        i = end + 2;
                        break;
                    case '/':
                        throw new InvalidDataException("A USDA layer contains a stray '/'.");
                    case '"':
                    case '\'':
                        i = SkipString(text, i);
                        break;
                    case '@':
                        i = ReadAsset(text, i, assets);
                        break;
                    case '<':
                        i = SkipPath(text, i);
                        break;
                    default:
                        i++;
                        break;
                }
            }
            CheckWord(word);
            return assets;
        }

        private static int SkipLine(string text, int start)
        {
            int end = text.IndexOf('\n', start);
            return end < 0 ? text.Length : end + 1;
        }

        private static void CheckWord(StringBuilder word)
        {
            if (word.Length == 0)
            {
                return;
            }
            string value = word.ToString();
            word.Clear();
            if (value.Equals("clips", StringComparison.Ordinal) ||
                value.Equals("templateAssetPath", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Value clips resolve files through strings and are not admitted.");
            }
        }

        private static int SkipString(string text, int start)
        {
            char quote = text[start];
            bool triple = start + 2 < text.Length && text[start + 1] == quote && text[start + 2] == quote;
            int i = start + (triple ? 3 : 1);
            while (i < text.Length)
            {
                char c = text[i];
                if (!triple && c is '\n' or '\r')
                {
                    throw new InvalidDataException("A single-quoted USDA string cannot span lines.");
                }
                if (c == '\\')
                {
                    if (!triple && i + 1 < text.Length && text[i + 1] is '\r' or '\n')
                    {
                        throw new InvalidDataException("A single-quoted USDA string cannot span lines.");
                    }
                    i += 2;
                    continue;
                }
                if (c == quote && (!triple ||
                    (i + 2 < text.Length && text[i + 1] == quote && text[i + 2] == quote)))
                {
                    return i + (triple ? 3 : 1);
                }
                i++;
            }
            throw new InvalidDataException("A USDA string is not terminated.");
        }

        private static int ReadAsset(string text, int start, List<string> assets)
        {
            if (start + 2 < text.Length && text[start + 1] == '@' && text[start + 2] == '@')
            {
                throw new InvalidDataException("Triple-delimited asset paths are not admitted.");
            }
            int close = text.IndexOf('@', start + 1);
            if (close < 0)
            {
                throw new InvalidDataException("A USDA asset path is not terminated.");
            }
            string value = text.Substring(start + 1, close - start - 1);
            if (value.Length > MaximumPathLength || value.IndexOfAny(['\r', '\n']) >= 0)
            {
                throw new InvalidDataException("A USDA asset path is unbounded or spans lines.");
            }
            if (value.Length > 0)
            {
                assets.Add(value);
            }
            return close + 1;
        }

        private static int SkipPath(string text, int start)
        {
            int close = text.IndexOf('>', start + 1);
            if (close < 0 || close - start > MaximumPathLength)
            {
                throw new InvalidDataException("A USDA path is not terminated or is unbounded.");
            }
            for (int i = start + 1; i < close; i++)
            {
                if (text[i] is '\r' or '\n' or '@' or '"' or '\'' or '#')
                {
                    throw new InvalidDataException("A USDA path contains a delimiter.");
                }
            }
            return close + 1;
        }

        private static bool IsAbsolutePrimPath(string path)
        {
            if (path.Length < 2 || path.Length > MaximumPathLength || path[0] != '/')
            {
                return false;
            }
            foreach (string segment in path.Substring(1).Split('/'))
            {
                if (segment.Length == 0 || (!IsAsciiLetter(segment[0]) && segment[0] != '_'))
                {
                    return false;
                }
                foreach (char c in segment)
                {
                    if (!IsAsciiLetterOrDigit(c) && c != '_')
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool IsDeviceName(string segment)
        {
            string device = segment.Split('.')[0];
            if (device.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return device.Length == 4 && device[3] >= '0' && device[3] <= '9' &&
                (device.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                    device.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsWordChar(char c)
        {
            return IsAsciiLetterOrDigit(c) || c == '_';
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool IsAsciiLetterOrDigit(char c)
        {
            return IsAsciiLetter(c) || (c >= '0' && c <= '9');
        }
    }
}
