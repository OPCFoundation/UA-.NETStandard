/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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

using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("Utils")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class UtilsTests
    {
        #region Test Methods
        /// <summary>
        /// Convert to and from little endian hex string.
        /// </summary>
        [Test]
        public void ToHexFromHexLittleEndian()
        {
            byte[] blob = new byte[] { 0, 1, 2, 3, 4, 5, 6, 255 };
            string hex = "00010203040506FF";
            var hexutil = Utils.ToHexString(blob);
            Assert.AreEqual(hex, hexutil);
            var byteblob = Utils.FromHexString(hex);
            Assert.AreEqual(blob, byteblob);
            var byteblob2 = Utils.FromHexString(hexutil);
            Assert.AreEqual(blob, byteblob2);
            var hexutil2 = Utils.ToHexString(byteblob);
            Assert.AreEqual(hex, hexutil2);
        }

        /// <summary>
        /// Convert to and from little endian hex string.
        /// </summary>
        [Test]
        public void ToHexEndianessValidation()
        {
            // definition as big endian 64,206(0xFACE) 
            var bigEndian = new byte[] { 64206 / 256, 64206 % 256 };
            // big endian is written as FA CE.
            Assert.AreEqual("FACE", Utils.ToHexString(bigEndian, false));
            // In Little Endian it's written as CE FA
            Assert.AreEqual("CEFA", Utils.ToHexString(bigEndian, true));
            // definition as little endian 64,206(0xFACE) 
            var littleEndian = new byte[] { 64206 & 0xff, 64206 >> 8 };
            // big endian is written as FA CE.
            Assert.AreEqual("FACE", Utils.ToHexString(littleEndian, true));
            // In Little Endian it's written as CE FA
            Assert.AreEqual("CEFA", Utils.ToHexString(littleEndian, false));        }

        /// <summary>
        /// Convert to big endian hex string.
        /// </summary>
        public void ToHexBigEndian()
        {
            byte[] blob = new byte[] { 0, 1, 2, 3, 4, 5, 6, 255 };
            string hex = "FF06050403020100";
            var hexutil = Utils.ToHexString(blob, true);
            Assert.AreEqual(hex, hexutil);
        }
        #endregion
    }

}
