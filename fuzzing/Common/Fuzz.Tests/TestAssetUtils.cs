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
    /// Initializes a replay asset from its file contents and source path.
    /// </summary>
    public interface IAsset
    {
        /// <summary>
        /// Populates the asset with input bytes and the path identifying their source file.
        /// </summary>
        /// <param name="blob">Contents read from the asset file.</param>
        /// <param name="path">Path identifying the source asset.</param>
        void Initialize(byte[] blob, string path);
    }

    /// <summary>
    /// Stores typed replay assets and supports loading files into newly initialized assets.
    /// </summary>
    /// <typeparam name="T">The asset type.</typeparam>
    public class AssetCollection<T> : List<T>
        where T : IAsset, new()
    {
        /// <summary>
        /// Creates an empty collection ready to receive replay assets.
        /// </summary>
        public AssetCollection()
        {
        }

        /// <summary>
        /// Copies existing replay assets into the collection in enumeration order.
        /// </summary>
        /// <param name="collection">Assets to include in the new collection.</param>
        public AssetCollection(IEnumerable<T> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Creates an empty asset collection with the requested initial capacity.
        /// </summary>
        /// <param name="capacity">Number of assets that can be stored before the collection grows.</param>
        public AssetCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Copies array entries into an asset collection, returning an empty collection for a null array.
        /// </summary>
        /// <param name="values">Existing assets to copy, or null for an empty collection.</param>
        /// <returns>A new collection containing the supplied assets in order.</returns>
        public static AssetCollection<T> ToAssetCollection(T[] values)
        {
            return values != null ? [.. values] : [];
        }

        /// <summary>
        /// Reads each supplied file into a newly initialized asset and preserves file enumeration order.
        /// </summary>
        /// <param name="filelist">Paths of the input files to load.</param>
        /// <returns>The initialized assets, one per supplied file.</returns>
        public static AssetCollection<T> CreateFromFiles(IEnumerable<string> filelist)
        {
            var result = new AssetCollection<T>();
            foreach (string file in filelist)
            {
                result.Add(file);
            }
            return result;
        }

        /// <summary>
        /// Reads a file, initializes a new asset with its bytes and path, and appends it to the collection.
        /// </summary>
        /// <param name="path">Path of the file to load as an asset.</param>
        public void Add(string path)
        {
            byte[] blob = File.ReadAllBytes(path);
            var asset = new T();
            asset.Initialize(blob, path);
            Add(asset);
        }
    }

    /// <summary>
    /// Discovers replay input files and encoder-specific testcase directories.
    /// </summary>
    public static class TestUtils
    {
        /// <summary>
        /// Recursively lists matching test assets, optionally rejecting a missing or empty inventory.
        /// </summary>
        /// <param name="folder">Relative or absolute directory to resolve for the asset search.</param>
        /// <param name="searchPattern">File-name pattern applied recursively below the resolved directory.</param>
        /// <param name="requireNonEmpty">Whether the inventory must contain at least one matching file.</param>
        /// <returns>Matching file paths, or an empty array when an optional inventory has no matches.</returns>
        /// <exception cref="InvalidOperationException">
        /// The inventory is required but the directory is missing or contains no matching files.
        /// </exception>
        public static string[] EnumerateTestAssets(string folder, string searchPattern, bool requireNonEmpty = false)
        {
            string assetsPath = Utils.GetAbsoluteDirectoryPath(folder, true, false, false);
            string[] files = [];
            if (assetsPath != null)
            {
                files = [.. Directory.EnumerateFiles(assetsPath, searchPattern, SearchOption.AllDirectories)];
            }
            if (requireNonEmpty && files.Length == 0)
            {
                throw new InvalidOperationException("Required good-seed inventory is missing or empty.");
            }
            return files;
        }

        /// <summary>
        /// Discovers encoder suffixes from testcase subdirectories and, for a Testcases root,
        /// sibling Testcases.* directories, removing duplicates and sorting without regard to case.
        /// </summary>
        /// <param name="folder">Testcase root directory to resolve.</param>
        /// <returns>Encoder suffixes, or an empty array when the testcase root is missing.</returns>
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
