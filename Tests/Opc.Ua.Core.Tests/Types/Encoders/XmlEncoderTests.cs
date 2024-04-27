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
        #endregion
    }
}
