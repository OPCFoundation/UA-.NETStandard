// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

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
    /// <typeparam name="T"></typeparam>
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
        public static string[] EnumerateTestAssets(string searchPattern)
        {
            string assetsPath = Utils.GetAbsoluteDirectoryPath("Assets", true, false, false)!;
            if (assetsPath != null)
            {
                return [.. Directory.EnumerateFiles(assetsPath, searchPattern)];
            }
            return [];
        }

        public static void ValidateSelSignedBasicConstraints(Certificate certificate)
        {
            X509BasicConstraintsExtension basicConstraintsExtension =
                certificate.Extensions.FindExtension<X509BasicConstraintsExtension>()!;
            Assert.That(basicConstraintsExtension, Is.Not.Null);
            Assert.That(basicConstraintsExtension.CertificateAuthority, Is.False);
            Assert.That(basicConstraintsExtension.Critical, Is.True);
            Assert.That(basicConstraintsExtension.HasPathLengthConstraint, Is.False);
        }
    }
}
