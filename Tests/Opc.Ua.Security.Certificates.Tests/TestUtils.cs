/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Tests
{
    #region Asset Helpers
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
    public class AssetCollection<T> : List<T> where T : IAsset, new()
    {
        public AssetCollection() { }
        public AssetCollection(IEnumerable<T> collection) : base(collection) { }
        public AssetCollection(int capacity) : base(capacity) { }
        public static AssetCollection<T> ToAssetCollection(T[] values)
        {
            return values != null ? new AssetCollection<T>(values) : new AssetCollection<T>();
        }

        public AssetCollection(IEnumerable<string> filelist) : base()
        {
            foreach (var file in filelist)
            {
                Add(file);
            }
        }

        public void Add(string path)
        {
            byte[] blob = File.ReadAllBytes(path);
            T asset = new T();
            asset.Initialize(blob, path);
            Add(asset);
        }
    }
    #endregion

    #region TestUtils
    /// <summary>
    /// Test helpers.
    /// </summary>
    public static class TestUtils
    {
        public static string[] EnumerateTestAssets(string searchPattern)
        {
            var assetsPath = Utils.GetAbsoluteDirectoryPath("Assets", true, false, false);
            if (assetsPath != null)
            {
                return Directory.EnumerateFiles(assetsPath, searchPattern).ToArray();
            }
            return Array.Empty<string>();
        }

        public static void ValidateSelSignedBasicConstraints(X509Certificate2 certificate)
        {
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(certificate.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.False(basicConstraintsExtension.CertificateAuthority);
            Assert.True(basicConstraintsExtension.Critical);
            Assert.False(basicConstraintsExtension.HasPathLengthConstraint);
        }
    }
    #endregion
}
