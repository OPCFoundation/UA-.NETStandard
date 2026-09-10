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
using System.Linq;

namespace Opc.Ua.OneFuzz
{
    internal static class ContractPaths
    {
        internal static string Resolve(string root, string relative)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relative);
            string[] segments = relative.Replace('\\', '/').Split('/');
            if (Path.IsPathRooted(relative) ||
                segments.Any(static part => part.Length == 0 || part is "." or ".." ||
                    part.IndexOfAny([':', '*', '?']) >= 0))
            {
                throw new InvalidDataException($"Expected a portable relative path, not '{relative}'.");
            }

            string current = Path.GetFullPath(root);
            foreach (string segment in segments)
            {
                string next = Path.Combine(current, segment);
                if (!Directory.EnumerateFileSystemEntries(current)
                    .Any(path => string.Equals(Path.GetFileName(path), segment, StringComparison.Ordinal)))
                {
                    throw new FileNotFoundException(
                        $"Required path is missing or has incorrect case: {relative}", next);
                }

                if ((File.GetAttributes(next) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"Links are not allowed in a relocatable drop: {relative}");
                }

                current = next;
            }

            return current;
        }

        internal static string Relative(string root, string path)
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
            {
                throw new InvalidDataException($"Dependency escaped the published drop: {path}");
            }

            return relative;
        }

        internal static List<string> CorpusFiles(string root, TargetSpec target)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string relative in target.Corpus)
            {
                string path = Resolve(root, relative);
                if (File.Exists(path))
                {
                    result.Add(path);
                    continue;
                }

                List<string> files = EnumerateFiles(path);
                if (files.Count == 0)
                {
                    throw new InvalidDataException($"Required corpus is empty for {target.Id}: {relative}");
                }

                result.UnionWith(files);
            }

            if (result.Count == 0)
            {
                throw new InvalidDataException($"Target {target.Id} has no corpus files.");
            }

            return [.. result.Order(StringComparer.Ordinal)];
        }

        internal static List<string> EnumerateFiles(string directory)
        {
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(directory);
            while (pending.TryPop(out string? current))
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(current))
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException($"Links are not allowed in a relocatable drop: {path}");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(path);
                    }
                    else
                    {
                        files.Add(path);
                    }
                }
            }

            files.Sort(StringComparer.Ordinal);
            return files;
        }
    }
}
