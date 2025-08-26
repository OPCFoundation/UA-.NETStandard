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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UtilsTests
    {
        /// <summary>
        /// Convert to and from little endian hex string.
        /// </summary>
        [Test]
        public void ToHexFromHexLittleEndian()
        {
            byte[] blob = [0, 1, 2, 3, 4, 5, 6, 255];
            const string hex = "00010203040506FF";
            string hexutil = Utils.ToHexString(blob);
            Assert.AreEqual(hex, hexutil);
            byte[] byteblob = Utils.FromHexString(hex);
            Assert.AreEqual(blob, byteblob);
            byte[] byteblob2 = Utils.FromHexString(hexutil);
            Assert.AreEqual(blob, byteblob2);
            string hexutil2 = Utils.ToHexString(byteblob);
            Assert.AreEqual(hex, hexutil2);
        }

        /// <summary>
        /// Convert to and from little endian hex string.
        /// </summary>
        [Test]
        public void ToHexEndianessValidation()
        {
            // definition as big endian 64,206(0xFACE)
            byte[] bigEndian = [64206 / 256, 64206 % 256];
            // big endian is written as FA CE.
            Assert.AreEqual("FACE", Utils.ToHexString(bigEndian, false));
            // In Little Endian it's written as CE FA
            Assert.AreEqual("CEFA", Utils.ToHexString(bigEndian, true));
            // definition as little endian 64,206(0xFACE)
            byte[] littleEndian = [64206 & 0xff, 64206 >> 8];
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
            byte[] blob = [0, 1, 2, 3, 4, 5, 6, 255];
            const string hex = "FF06050403020100";
            string hexutil = Utils.ToHexString(blob, true);
            Assert.AreEqual(hex, hexutil);
        }

        [Test]
        public void Trace()
        {
            Utils.TraceDebug(string.Empty);
            Utils.TraceDebug(null);
            Utils.Trace(
                new ServiceResultException(StatusCodes.BadAggregateConfigurationRejected),
                "Exception {0}",
                1);
            Utils.TraceExceptionMessage(
                new ServiceResultException(StatusCodes.BadEdited_OutOfRange),
                "Exception {0} {1}",
                2,
                3);
            Utils.Trace(
                new ServiceResultException(StatusCodes.BadAggregateConfigurationRejected),
                "Exception {0} {1}",
                true,
                2,
                3);
            Utils.Trace(
                new ServiceResultException(StatusCodes.BadEdited_OutOfRange),
                "Exception {0} {1}",
                false,
                2,
                3);
            Utils.Trace(Utils.TraceMasks.Information, "Exception {0} {1}", 2, 3);
        }

        [Test]
        public void AreDomainsEqual()
        {
            var uri1 = new Uri("opc.tcp://host1:4840");
            var uri1_dupe = new Uri("opc.tcp://host1:4840");
            var uri2 = new Uri("opc.tcp://localhost:4840");
            var uri2_dupe = new Uri($"opc.tcp://{Utils.GetHostName()}:4840");

            // uri compare resolves localhost
            Assert.True(Utils.AreDomainsEqual(uri1, uri1_dupe));
            Assert.True(Utils.AreDomainsEqual(uri2, uri2_dupe));
            Assert.True(Utils.AreDomainsEqual(uri2_dupe, uri2));
            Assert.True(Utils.AreDomainsEqual(uri1, uri1));
            Assert.True(Utils.AreDomainsEqual(uri2, uri2));

            // string compare doesn't resolve localhost
            Assert.True(Utils.AreDomainsEqual(uri1.ToString(), uri1_dupe.ToString()));
            Assert.False(Utils.AreDomainsEqual(uri2.ToString(), uri2_dupe.ToString()));
            Assert.False(Utils.AreDomainsEqual(uri1.ToString(), null));
            Assert.False(Utils.AreDomainsEqual(uri2.ToString(), null));
            Assert.False(Utils.AreDomainsEqual(uri1.ToString(), string.Empty));
            Assert.False(Utils.AreDomainsEqual(uri2.ToString(), string.Empty));
            Assert.False(Utils.AreDomainsEqual(null, uri1.ToString()));
            Assert.False(Utils.AreDomainsEqual(null, uri2.ToString()));
            Assert.False(Utils.AreDomainsEqual(string.Empty, uri1.ToString()));
            Assert.False(Utils.AreDomainsEqual(string.Empty, uri2.ToString()));

            Assert.False(Utils.AreDomainsEqual((Uri)null, null));
            Assert.False(Utils.AreDomainsEqual((string)null, null));
            Assert.False(Utils.AreDomainsEqual(uri1, uri2));
            Assert.False(Utils.AreDomainsEqual(uri1.ToString(), uri2.ToString()));
        }

        public class TestClone
        {
            private readonly object m_object;

            public TestClone(object value)
            {
                m_object = value;
            }

            public object Clone()
            {
                return new TestClone(m_object);
            }
        }

        public class TestNoClone
        {
            private readonly object m_object;

            public TestNoClone(object value)
            {
                m_object = value;
            }

            public object NoClone()
            {
                return new TestNoClone(m_object);
            }
        }

        public class TestMemberwiseClone
        {
            private readonly object m_object;

            public TestMemberwiseClone(object value)
            {
                m_object = value;
            }

            public object Clone()
            {
                return new TestMemberwiseClone(m_object);
            }
        }

        [Test]
        public void Clone()
        {
            var testClone = new TestClone(1);
            Assert.NotNull(Utils.Clone(testClone));
            var testMemberwiseClone = new TestMemberwiseClone(2);
            Assert.NotNull(Utils.Clone(testMemberwiseClone));
            var testNoClone = new TestNoClone(3);
            NUnit.Framework.Assert.Throws<NotSupportedException>(() => Utils.Clone(testNoClone));
        }

        [Test]
        public void IsEqualUserIdentity()
        {
            var anonymousIdentity1 = new AnonymousIdentityToken();
            var anonymousIdentity2 = new AnonymousIdentityToken();

            Assert.True(Utils.IsEqualUserIdentity(anonymousIdentity1, anonymousIdentity1));
            Assert.True(Utils.IsEqualUserIdentity(anonymousIdentity1, anonymousIdentity2));
            Assert.False(Utils.IsEqualUserIdentity(anonymousIdentity1, null));
            Assert.False(Utils.IsEqualUserIdentity(null, anonymousIdentity2));

            var user1 = new UserNameIdentityToken
            {
                UserName = "user1",
                Password = Encoding.ASCII.GetBytes("pass1".ToCharArray())
            };
            var user1_dupe = new UserNameIdentityToken
            {
                UserName = "user1",
                Password = Encoding.ASCII.GetBytes("pass1".ToCharArray())
            };
            var user2 = new UserNameIdentityToken
            {
                UserName = "user2",
                Password = Encoding.ASCII.GetBytes("pass2".ToCharArray())
            };
            Assert.True(Utils.IsEqualUserIdentity(user1, user1_dupe));
            Assert.True(Utils.IsEqualUserIdentity(user1, user1));
            Assert.False(Utils.IsEqualUserIdentity(user1, user2));
            Assert.False(Utils.IsEqualUserIdentity(null, user2));
            Assert.False(Utils.IsEqualUserIdentity(user1, null));
        }

        /// <summary>
        /// Parse simple plain path string containing only numeric chars.
        /// </summary>
        [Test]
        public void RelativePathParseNumericStringNonDeep()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/11";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse deep path string containing only numeric chars.
        /// </summary>
        [Test]
        public void RelativePathParseNumericStringDeepPath()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/123/789";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse deep path string containing alphanumeric chars, staring with numeric chars.
        /// </summary>
        [Test]
        public void RelativePathParseAlphanumericStringPath()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/123A/78B9";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse deep path string containing alphanumeric chars (mixed), starting with alphabetical chars.
        /// </summary>
        [Test]
        public void RelativePathParseAlphanumericStringPath2()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/AA123A/bb78B9";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse deep path string containing only alphabetical chars.
        /// </summary>
        [Test]
        public void RelativePathParseAlphaStringPath()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/abc/def";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse deep path string containing only alphabetical chars with namespace index
        /// </summary>
        [Test]
        public void RelativePathParseAlphanumericWithNamespaceIndexStringPath()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/1:abc/2:def";
            Assert.AreEqual(str, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse path string containing two Namespaces, translate indexes
        /// </summary>
        [Theory]
        [TestCase("<#2:HasChild>", "<#3:HasChild>")]
        [TestCase("<!2:HasChild>", "<!3:HasChild>")]
        [TestCase(".2:NodeVersion", ".3:NodeVersion")]
        [TestCase("/1:abc/2:def", "/1:abc/3:def")]
        public void RelativePathParseTranslateNamespaceIndexReferenceType(
            string input,
            string output)
        {
            var currentTable = new NamespaceTable([Namespaces.OpcUa, "1", Namespaces.OpcUaGds]);
            var targetTable = new NamespaceTable([Namespaces.OpcUa, "1", "2", Namespaces.OpcUaGds]);

            var typeTable = new TypeTable(new NamespaceTable());
            typeTable.AddReferenceSubtype(
                ReferenceTypeIds.HasChild,
                NodeId.Null,
                new QualifiedName("HasChild", 3));
            Assert.AreEqual(
                output,
                RelativePath.Parse(input, typeTable, currentTable, targetTable).Format(typeTable));
        }

        /// <summary>
        /// Parse path string containing two Namespaces with missing namespace indexes in either currentTable or targetTable.
        /// </summary>
        [Theory]
        [TestCase(
            new string[] { Namespaces.OpcUa, "2", Namespaces.OpcUaGds },
            new string[] { Namespaces.OpcUa, "2", "3" },
            "/1:abc/2:def"
        )]
        [TestCase(
            new string[] { Namespaces.OpcUa, "2", Namespaces.OpcUaGds },
            new string[] { Namespaces.OpcUa, "2", "3" },
            "<#2:HasChild>"
        )]
        [TestCase(
            new string[] { Namespaces.OpcUa, "2", Namespaces.OpcUaGds },
            new string[] { Namespaces.OpcUa, "2", "3", "4", "5" },
            "/1:abc/4:def"
        )]
        public void RelativePathParseInvalidNamespaceIndex(
            string[] currentNamespaces,
            string[] targetNamespaces,
            string path)
        {
            var currentTable = new NamespaceTable([.. currentNamespaces]);
            var targetTable = new NamespaceTable([.. targetNamespaces]);

            var typeTable = new TypeTable(new NamespaceTable());
            ServiceResultException sre = NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                RelativePath.Parse(path, typeTable, currentTable, targetTable).Format(typeTable));
            Assert.AreEqual(
                (StatusCode)StatusCodes.BadIndexRangeInvalid,
                (StatusCode)sre.StatusCode);
        }

        /// <summary>
        /// Validate that XmlDocument DtdProcessing is protected against
        /// exponential entity expansion in this version of .NET.
        /// </summary>
        [Test]
        public void ExponentialEntityExpansionProcessing()
        {
            var xmlEEXX = new StringBuilder();
            xmlEEXX.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>")
                .AppendLine("<!DOCTYPE lolz [<!ENTITY lol \"lol\">")
                .AppendLine(
                    "<!ENTITY lol1 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\" >")
                .AppendLine("<!ENTITY lol2 \"&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;\" >")
                .AppendLine("<!ENTITY lol3 \"&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;\" >")
                .AppendLine("<!ENTITY lol4 \"&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;\" >")
                .AppendLine("<!ENTITY lol5 \"&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;\" >")
                .AppendLine("<!ENTITY lol6 \"&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;\" >")
                .AppendLine("<!ENTITY lol7 \"&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;\" >")
                .AppendLine("<!ENTITY lol8 \"&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;\" >")
                .AppendLine("<!ENTITY lol9 \"&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;\" >]>")
                .AppendLine("<lolz>&lol9;</lolz>");

            // Validate the default reader (expansion limited at 10000000 bytes)
            TestContext.Out.WriteLine("Testing XmlDocument.LoadXml.");
            XmlException ex = NUnit.Framework.Assert.Throws<XmlException>(() =>
            {
                var document = new XmlDocument();
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA3075 // Insecure DTD processing in XML
#pragma warning restore IDE0079 // Remove unnecessary suppression
                document.LoadXml(xmlEEXX.ToString());
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning restore CA3075 // Insecure DTD processing in XML
#pragma warning restore IDE0079 // Remove unnecessary suppression
            });
            TestContext.Out.WriteLine(ex.Message);

            // Validate the InnerXml default (expansion limited at 10000000 bytes)
            TestContext.Out.WriteLine("Testing XmlDocument.InnerXml.");
            ex = NUnit.Framework.Assert.Throws<XmlException>(() =>
            {
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA3075 // Insecure DTD processing in XML
#pragma warning restore IDE0079 // Remove unnecessary suppression
                var document = new XmlDocument { InnerXml = xmlEEXX.ToString() };
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning restore CA3075 // Insecure DTD processing in XML
#pragma warning restore IDE0079 // Remove unnecessary suppression
            });
            TestContext.Out.WriteLine(ex.Message);

            // Validate the default Xml Reader settings prohibit Dtd (recommended)
            TestContext.Out.WriteLine("Testing XmlDocument.Load with default xml reader.");
            using (var stream = new StringReader(xmlEEXX.ToString()))
            using (var reader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings()))
            {
                ex = NUnit.Framework.Assert.Throws<XmlException>(() =>
                {
                    var document = new XmlDocument();
                    document.Load(reader);
                });
                TestContext.Out.WriteLine(ex.Message);
            }

            // Validate the LoadInnerXml helper settings prohibit Dtd (recommended)
            TestContext.Out.WriteLine("Testing LoadInnerXml helper.");
            ex = NUnit.Framework.Assert.Throws<XmlException>(() =>
            {
                var document = new XmlDocument();
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

            var bnList = new List<string>
            {
                BrowseNames.ApplicationInstanceCertificate,
                BrowseNames.AudioDataType,
                BrowseNames.ContinuationPoint,
                BrowseNames.Image,
                BrowseNames.ImageBMP,
                BrowseNames.ImageGIF,
                BrowseNames.ImageJPG,
                BrowseNames.ImagePNG
            };
            foreach (string name in bnList)
            {
                object staticValue = typeof(DataTypeIds)
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .First(f => f.Name == name)
                    .GetValue(null);
                Assert.AreEqual(
                    BuiltInType.ByteString,
                    TypeInfo.GetBuiltInType((NodeId)staticValue));
            }

            Assert.AreEqual(
                BuiltInType.NodeId,
                TypeInfo.GetBuiltInType(DataTypeIds.SessionAuthenticationToken));
            Assert.AreEqual(BuiltInType.Double, TypeInfo.GetBuiltInType(DataTypeIds.Duration));

            bnList = [BrowseNames.IntegerId, BrowseNames.Index, BrowseNames.VersionTime, BrowseNames
                .Counter];
            foreach (string name in bnList)
            {
                object staticValue = typeof(DataTypeIds)
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .First(f => f.Name == name)
                    .GetValue(null);
                Assert.AreEqual(BuiltInType.UInt32, TypeInfo.GetBuiltInType((NodeId)staticValue));
            }

            Assert.AreEqual(
                BuiltInType.UInt64,
                TypeInfo.GetBuiltInType(DataTypeIds.BitFieldMaskDataType));

            bnList =
            [
                BrowseNames.DateString,
                BrowseNames.DecimalString,
                BrowseNames.DurationString,
                BrowseNames.LocaleId,
                BrowseNames.NormalizedString,
                BrowseNames.NumericRange,
                BrowseNames.TimeString
            ];
            foreach (string name in bnList)
            {
                object staticValue = typeof(DataTypeIds)
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .First(f => f.Name == name)
                    .GetValue(null);
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

        /// <summary>
        /// Parse a path containing non-escaped hash character.
        /// </summary>
        [Test]
        public void RelativePathParseNonEscapedHash()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/abc#def";
            NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse a path containing correctly escaped hash character.
        /// </summary>
        [Test]
        public void RelativePathParseEscapedHash()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/abc&#def";
            const string expected = "/abc#def";
            Assert.AreEqual(expected, RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse a path containing correctly escaped hash character followed by exclamation.
        /// </summary>
        [Test]
        public void RelativePathParseEscapedHashFollowedByExclamation()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/abc&#!def";
            NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse a path containing correctly escaped hash character by exclamation within the reference type delimiters.
        /// </summary>
        [Test]
        public void RelativePathParseEscapedHashFollowedByExclamationInReferenceType()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "<abc&#!def>";
            NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                RelativePath.Parse(str, typeTable).Format(typeTable));
        }

        /// <summary>
        /// Parse a path containing incorrectly escaped character sequence.
        /// </summary>
        [Test]
        public void RelativePathParseInvalidEscapeSequence()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/abc&$!def";
            NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                RelativePath.Parse(str, typeTable).Format(typeTable));
        }
    }
}
