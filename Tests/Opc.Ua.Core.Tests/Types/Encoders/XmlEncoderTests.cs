/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Tests for the XML encoder and decoder class.
    /// </summary>
    [TestFixture, Category("XmlEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class XmlEncoderTests
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "SYSLIB1045:Convert to 'GeneratedRegexAttribute'.", Justification = "Tests")]
        static Regex REValue = new Regex("Value>([^<]*)<");

        #region Test Methods
        /// <summary>
        /// Validate the encoding and decoding of the float special values.
        /// </summary>
        [Test]
        [TestCase(Single.PositiveInfinity, "INF")]
        [TestCase(Single.NegativeInfinity, "-INF")]
        [TestCase(Single.NaN, "NaN")]
        public void EncodeDecodeFloat(float binaryValue, string expectedXmlValue)
        {
            // Encode
            var context = new ServiceMessageContext();
            string actualXmlValue;
            using (IEncoder xmlEncoder = new XmlEncoder(new XmlQualifiedName("FloatSpecialValues", Namespaces.OpcUaXsd), null, context))
            {
                xmlEncoder.PushNamespace(Namespaces.OpcUaXsd);
                xmlEncoder.WriteFloat("Value", binaryValue);
                xmlEncoder.PopNamespace();
                actualXmlValue = xmlEncoder.CloseAndReturnText();
            }

            // Check encode result against expected XML value
            Match m = REValue.Match(actualXmlValue);
            Assert.True(m.Success);
            Assert.True(m.Groups.Count == 2);
            Assert.AreEqual(m.Groups[1].Value, expectedXmlValue);

            // Decode
            float actualBinaryValue;
            using (XmlReader reader = XmlReader.Create(new StringReader(actualXmlValue)))
            {
                using (IDecoder xmlDecoder = new XmlDecoder(null, reader, context))
                {
                    actualBinaryValue = xmlDecoder.ReadFloat("Value");
                }
            }

            // Check decode result against input value
            if (Single.IsNaN(actualBinaryValue)) // NaN is not equal to anything!
                Assert.True(Single.IsNaN(binaryValue));
            else
                Assert.AreEqual(actualBinaryValue, binaryValue);
        }

        /// <summary>
        /// Validate the encoding and decoding of the double special values.
        /// </summary>
        [Test]
        [TestCase(Double.PositiveInfinity, "INF")]
        [TestCase(Double.NegativeInfinity, "-INF")]
        [TestCase(Double.NaN, "NaN")]
        public void EncodeDecodeDouble(double binaryValue, string expectedXmlValue)
        {
            // Encode
            var context = new ServiceMessageContext();
            string actualXmlValue;
            using (IEncoder xmlEncoder = new XmlEncoder(new XmlQualifiedName("DoubleSpecialValues", Namespaces.OpcUaXsd), null, context))
            {
                xmlEncoder.PushNamespace(Namespaces.OpcUaXsd);
                xmlEncoder.WriteDouble("Value", binaryValue);
                xmlEncoder.PopNamespace();
                actualXmlValue = xmlEncoder.CloseAndReturnText();
            }

            // Check encode result against expected XML value
            Match m = REValue.Match(actualXmlValue);
            Assert.True(m.Success);
            Assert.True(m.Groups.Count == 2);
            Assert.AreEqual(m.Groups[1].Value, expectedXmlValue);

            // Decode
            double actualBinaryValue;
            using (XmlReader reader = XmlReader.Create(new StringReader(actualXmlValue)))
            {
                using (IDecoder xmlDecoder = new XmlDecoder(null, reader, context))
                {
                    actualBinaryValue = xmlDecoder.ReadDouble("Value");
                }
            }

            // Check decode result against input value
            if (Double.IsNaN(actualBinaryValue)) // NaN is not equal to anything!
                Assert.True(Double.IsNaN(binaryValue));
            else
                Assert.AreEqual(actualBinaryValue, binaryValue);
        }

        /// <summary>
        /// Validate the encoding and decoding of the a variant that consists of a matrix.
        /// </summary>
        [Test]
        public void EncodeDecodeVariantMatrix()
        {
            var value = new Matrix(
                new int[] { 1, 2, 3, 4 },
                BuiltInType.Int32,
                new int[] { 2, 2 }
            );
            var variant = new Variant(value);

            string expected = "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<uax:VariantTest xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">\r\n  <uax:Test>\r\n    <uax:Value>\r\n      <uax:Matrix>\r\n        <uax:Dimensions>\r\n          <uax:Int32>2</uax:Int32>\r\n          <uax:Int32>2</uax:Int32>\r\n        </uax:Dimensions>\r\n        <uax:Elements>\r\n          <uax:Int32>1</uax:Int32>\r\n          <uax:Int32>2</uax:Int32>\r\n          <uax:Int32>3</uax:Int32>\r\n          <uax:Int32>4</uax:Int32>\r\n        </uax:Elements>\r\n      </uax:Matrix>\r\n    </uax:Value>\r\n  </uax:Test>\r\n</uax:VariantTest>";

            // Encode
            var context = new ServiceMessageContext();
            string actualXmlValue;
            using (IEncoder xmlEncoder = new XmlEncoder(new XmlQualifiedName("VariantTest", Namespaces.OpcUaXsd), null, context))
            {
                xmlEncoder.PushNamespace(Namespaces.OpcUaXsd);
                xmlEncoder.WriteVariant("Test", variant);
                xmlEncoder.PopNamespace();
                actualXmlValue = xmlEncoder.CloseAndReturnText();
            }

            // Check encode result against expected XML value
            Assert.AreEqual(expected.Replace("\r", "").Replace("\n", ""), actualXmlValue.Replace("\r", "").Replace("\n", ""));

            // Decode
            Variant actualVariant;
            using (XmlReader reader = XmlReader.Create(new StringReader(actualXmlValue)))
            {
                using (IDecoder xmlDecoder = new XmlDecoder(null, reader, context))
                {
                    actualVariant = xmlDecoder.ReadVariant("Test");
                }
            }

            // Check decode result against input value
            Assert.AreEqual(actualVariant, variant);
        }
        #endregion
    }
}
