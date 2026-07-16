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

namespace Opc.Ua.Tests
{
    /// <summary>
    /// The interface to initialize an asset.
    /// </summary>
    public interface IAsset
    {
        void Initialize(byte[] blob, string path);
    }

    /// <summary>
    /// Create a collection of test assets.
    /// </summary>
    /// <typeparam name="T">The asset type.</typeparam>
    public class AssetCollection<T> : List<T>
        where T : IAsset, new()
    {
        public AssetCollection()
        {
        }

        public AssetCollection(IEnumerable<T> collection)
            : base(collection)
        {
        }

        public AssetCollection(int capacity)
            : base(capacity)
        {
        }

        public static AssetCollection<T> ToAssetCollection(T[] values)
        {
            return values != null ? [.. values] : [];
        }

        public static AssetCollection<T> CreateFromFiles(IEnumerable<string> filelist)
        {
            var result = new AssetCollection<T>();
            foreach (string file in filelist)
            {
                result.Add(file);
            }
            return result;
        }

        public void Add(string path)
        {
            byte[] blob = File.ReadAllBytes(path);
            var asset = new T();
            asset.Initialize(blob, path);
            Add(asset);
        }
    }

    /// <summary>
    /// Test helpers.
    /// </summary>
    public static class TestUtils
    {
        public static string[] EnumerateTestAssets(string folder, string searchPattern)
        {
            string assetsPath = Utils.GetAbsoluteDirectoryPath(folder, true, false, false);
            if (assetsPath != null)
            {
                return [.. Directory.EnumerateFiles(assetsPath, searchPattern, SearchOption.AllDirectories)];
            }
            return [];
        }

        public static string[] DiscoverTestcaseEncoderSuffixes(string folder)
        {
            string assetsPath = Utils.GetAbsoluteDirectoryPath(folder, true, false, false);
            if (assetsPath == null)
            {
                return [];
            }

            return DiscoverTestcaseEncoderSuffixesFromPath(assetsPath);
        }

        private static string[] DiscoverTestcaseEncoderSuffixesFromPath(string testcasesRoot)
        {
            if (!Directory.Exists(testcasesRoot))
            {
                return [];
            }

            string rootName = Path.GetFileName(
                testcasesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            IEnumerable<string> sourceDirectories = Directory.EnumerateDirectories(testcasesRoot);

            if (string.Equals(rootName, "Testcases", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(testcasesRoot);
                if (parent != null)
                {
                    sourceDirectories = sourceDirectories.Concat(
                        Directory.EnumerateDirectories(parent, "Testcases.*"));
                }
            }

            return
            [
                .. sourceDirectories
                    .Select(path => GetTestcaseSuffix(rootName, path))
                    .Where(suffix => !string.IsNullOrEmpty(suffix))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(suffix => suffix, StringComparer.OrdinalIgnoreCase)
            ];
        }

        private static string GetTestcaseSuffix(string rootName, string path)
        {
            string directoryName = Path.GetFileName(path);
            string prefix = rootName + ".";
            if (directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return directoryName[rootName.Length..];
            }

            return "." + directoryName;
        }
    }
}
