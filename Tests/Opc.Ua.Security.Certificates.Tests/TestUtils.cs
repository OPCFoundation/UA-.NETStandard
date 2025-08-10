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

using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
    public class AssetCollection<T> : List<T>
        where T : IAsset, new()
    {
        public AssetCollection() { }

        public AssetCollection(IEnumerable<T> collection)
            : base(collection) { }

        public AssetCollection(int capacity)
            : base(capacity) { }

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
            string assetsPath = Utils.GetAbsoluteDirectoryPath("Assets", true, false, false);
            if (assetsPath != null)
            {
                return [.. Directory.EnumerateFiles(assetsPath, searchPattern)];
            }
            return [];
        }

        public static void ValidateSelSignedBasicConstraints(X509Certificate2 certificate)
        {
            X509BasicConstraintsExtension basicConstraintsExtension =
                certificate.Extensions.FindExtension<X509BasicConstraintsExtension>();
            Assert.NotNull(basicConstraintsExtension);
            Assert.False(basicConstraintsExtension.CertificateAuthority);
            Assert.True(basicConstraintsExtension.Critical);
            Assert.False(basicConstraintsExtension.HasPathLengthConstraint);
        }
    }
}
