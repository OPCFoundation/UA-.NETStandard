// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            string assetsPath = Utils.GetAbsoluteDirectoryPath(folder, true, false, false)!;
            if (assetsPath != null)
            {
                return [.. Directory.EnumerateFiles(assetsPath, searchPattern, SearchOption.AllDirectories)];
            }
            return [];
        }

        public static string[] DiscoverTestcaseEncoderSuffixes(string folder)
        {
            string? assetsPath = Utils.GetAbsoluteDirectoryPath(folder, true, false, false);
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
                string parent = Path.GetDirectoryName(testcasesRoot)!;
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
