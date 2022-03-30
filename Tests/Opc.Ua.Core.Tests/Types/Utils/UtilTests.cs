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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
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
        #region Misc
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
            Assert.AreEqual("CEFA", Utils.ToHexString(littleEndian, false));
        }

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

        #region RelativePath.Parse
        /// <summary>
        /// Parse simple plain path string containing only numeric chars.
        /// </summary>
        [Test]
        public void RelativePathParseNumericStringNonDeep()
        {
            TypeTable typeTable = new TypeTable(new NamespaceTable());
            string str = "/11";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse deep path string containing only numeric chars.
        /// </summary>
        [Test]
        public void RelativePathParseNumericStringDeepPath()
        {
            TypeTable typeTable = new TypeTable(new NamespaceTable());
            string str = "/123/789";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse deep path string containing alphanumeric chars, staring with numeric chars.
        /// </summary>
        [Test]
        public void RelativePathParseAlphanumericStringPath()
        {
            TypeTable typeTable = new TypeTable(new NamespaceTable());
            string str = "/123A/78B9";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse deep path string containing alphanumeric chars (mixed), starting with alphabetical chars.
        /// </summary>
        [Test]
        public void RelativePathParseAlphanumericStringPath2()
        {
            TypeTable typeTable = new TypeTable(new NamespaceTable());
            string str = "/AA123A/bb78B9";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse deep path string containing only alphabetical chars.
        /// </summary>
        [Test]
        public void RelativePathParseAlphaStringPath()
        {
            TypeTable typeTable = new TypeTable(new NamespaceTable());
            string str = "/abc/def";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse deep path string containing only alphabetical chars with namespace index
        /// </summary>
        [Test]
        public void RelativePathParseAlphanumericWithNamespaceIndexStringPath()
        {
            TypeTable typeTable = new TypeTable(new NamespaceTable());
            string str = "/1:abc/2:def";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Validate that XmlDocument DtdProcessing is protected against
        /// exponential entity expansion in this version of .NET.
        /// </summary>
        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3075:Insecure DTD processing in XML",
            Justification =
                "Validate that XmlDocument DtdProcessing is protected against" +
                "exponential entity expansion in this version of .NET.")]
        public void ExponentialEntityExpansionProcessing()
        {
            StringBuilder xmlEEXX = new StringBuilder();
            xmlEEXX.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
            xmlEEXX.AppendLine("<!DOCTYPE lolz [<!ENTITY lol \"lol\">");
            xmlEEXX.AppendLine("<!ENTITY lol1 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\" >");
            xmlEEXX.AppendLine("<!ENTITY lol2 \"&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;\" >");
            xmlEEXX.AppendLine("<!ENTITY lol3 \"&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;\" >");
            xmlEEXX.AppendLine("<!ENTITY lol4 \"&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;\" >");
            xmlEEXX.AppendLine("<!ENTITY lol5 \"&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;\" >");
            xmlEEXX.AppendLine("<!ENTITY lol6 \"&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;\" >");
            xmlEEXX.AppendLine("<!ENTITY lol7 \"&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;\" >");
            xmlEEXX.AppendLine("<!ENTITY lol8 \"&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;\" >");
            xmlEEXX.AppendLine("<!ENTITY lol9 \"&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;\" >]>");
            xmlEEXX.AppendLine("<lolz>&lol9;</lolz>");

            // Validate the default reader (expansion limited at 10000000 bytes)
            TestContext.Out.WriteLine("Testing XmlDocument.LoadXml.");
            var ex = Assert.Throws<XmlException>(() => {
                XmlDocument document = new XmlDocument();
                document.LoadXml(xmlEEXX.ToString());
            });
            TestContext.Out.WriteLine(ex.Message);

            // Validate the InnerXml default (expansion limited at 10000000 bytes)
            TestContext.Out.WriteLine("Testing XmlDocument.InnerXml.");
            ex = Assert.Throws<XmlException>(() => {
                XmlDocument document = new XmlDocument();
                document.InnerXml = xmlEEXX.ToString();
            });
            TestContext.Out.WriteLine(ex.Message);

            // Validate the default Xml Reader settings prohibit Dtd (recommended)
            TestContext.Out.WriteLine("Testing XmlDocument.Load with default xml reader.");
            using (StringReader stream = new StringReader(xmlEEXX.ToString()))
            using (XmlReader reader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings()))
            {
                ex = Assert.Throws<XmlException>(() => {
                    XmlDocument document = new XmlDocument();
                    document.Load(reader);
                });
                TestContext.Out.WriteLine(ex.Message);
            }

            // Validate the LoadInnerXml helper settings prohibit Dtd (recommended)
            TestContext.Out.WriteLine("Testing LoadInnerXml helper.");
            ex = Assert.Throws<XmlException>(() => {
                XmlDocument document = new XmlDocument();
                document.LoadInnerXml(xmlEEXX.ToString());
            });
            TestContext.Out.WriteLine(ex.Message);

        }

        /// <summary>
        /// Test the built-in type of the Simple data types
        /// </summary>
        [Test]
        public void BuiltInTypeOfSimpleDataTypes()

        {
            Assert.AreEqual(BuiltInType.DateTime, TypeInfo.GetBuiltInType(DataTypeIds.UtcTime));

            List<string> bnList = new List<string> { BrowseNames.ApplicationInstanceCertificate, BrowseNames.AudioDataType,
                BrowseNames.ContinuationPoint, BrowseNames.Image,
                BrowseNames.ImageBMP, BrowseNames.ImageGIF,
                BrowseNames.ImageJPG, BrowseNames.ImagePNG };
            foreach (string name in bnList)
            {
                var staticValue = typeof(DataTypeIds).GetFields(BindingFlags.Public | BindingFlags.Static).First(f => f.Name == name).GetValue(null);
                Assert.AreEqual(BuiltInType.ByteString, TypeInfo.GetBuiltInType((NodeId)staticValue));
            }

            Assert.AreEqual(BuiltInType.NodeId, TypeInfo.GetBuiltInType(DataTypeIds.SessionAuthenticationToken));
            Assert.AreEqual(BuiltInType.Double, TypeInfo.GetBuiltInType(DataTypeIds.Duration));

            bnList = new List<string> { BrowseNames.IntegerId, BrowseNames.Index, BrowseNames.VersionTime, BrowseNames.Counter };
            foreach (string name in bnList)
            {
                var staticValue = typeof(DataTypeIds).GetFields(BindingFlags.Public | BindingFlags.Static).First(f => f.Name == name).GetValue(null);
                Assert.AreEqual(BuiltInType.UInt32, TypeInfo.GetBuiltInType((NodeId)staticValue));
            }

            Assert.AreEqual(BuiltInType.UInt64, TypeInfo.GetBuiltInType(DataTypeIds.BitFieldMaskDataType));

            bnList = new List<string> { BrowseNames.DateString, BrowseNames.DecimalString,
                BrowseNames.DurationString, BrowseNames.LocaleId,
                BrowseNames.NormalizedString, BrowseNames.NumericRange,
                BrowseNames.TimeString };
            foreach (string name in bnList)
            {
                var staticValue = typeof(DataTypeIds).GetFields(BindingFlags.Public | BindingFlags.Static).First(f => f.Name == name).GetValue(null);
                Assert.AreEqual(BuiltInType.String, TypeInfo.GetBuiltInType((NodeId)staticValue));
            }
        }

        [Test]
        public void ValidateXmlWriterSettings()
        {
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            Assert.AreEqual(Encoding.UTF8, settings.Encoding);
            Assert.AreEqual(false, settings.CloseOutput);
            Assert.AreEqual(true, settings.Indent);
            Assert.AreEqual(ConformanceLevel.Document, settings.ConformanceLevel);
            Assert.AreEqual(false, settings.OmitXmlDeclaration);
            Assert.AreEqual("  ", settings.IndentChars);
        }

        [Test]
        public void ValidateXmlReaderSettings()
        {
            XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
            Assert.AreEqual(DtdProcessing.Prohibit, settings.DtdProcessing);
            //Assert.AreEqual(null, settings.XmlResolver);
            Assert.AreEqual(ConformanceLevel.Document, settings.ConformanceLevel);
            Assert.AreEqual(false, settings.CloseInput);
        }
        #endregion
    }

}
