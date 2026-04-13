/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Xml;
using NUnit.Framework;

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
            Assert.That(hexutil, Is.EqualTo(hex));
            byte[] byteblob = Utils.FromHexString(hex);
            Assert.That(byteblob, Is.EqualTo(blob));
            byte[] byteblob2 = Utils.FromHexString(hexutil);
            Assert.That(byteblob2, Is.EqualTo(blob));
            string hexutil2 = Utils.ToHexString(byteblob);
            Assert.That(hexutil2, Is.EqualTo(hex));
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
            Assert.That(Utils.ToHexString(bigEndian, false), Is.EqualTo("FACE"));
            // In Little Endian it's written as CE FA
            Assert.That(Utils.ToHexString(bigEndian, true), Is.EqualTo("CEFA"));
            // definition as little endian 64,206(0xFACE)
            byte[] littleEndian = [64206 & 0xff, 64206 >> 8];
            // big endian is written as FA CE.
            Assert.That(Utils.ToHexString(littleEndian, true), Is.EqualTo("FACE"));
            // In Little Endian it's written as CE FA
            Assert.That(Utils.ToHexString(littleEndian, false), Is.EqualTo("CEFA"));
        }

        [Test]
        public void ToHexBigEndian()
        {
            byte[] blob = [0, 1, 2, 3, 4, 5, 6, 255];
            const string hex = "FF06050403020100";
            string hexutil = Utils.ToHexString(blob, true);
            Assert.That(hexutil, Is.EqualTo(hex));
        }

        [Test]
        public void AreDomainsEqual()
        {
            var uri1 = new Uri("opc.tcp://host1:4840");
            var uri1_dupe = new Uri("opc.tcp://host1:4840");
            var uri2 = new Uri("opc.tcp://localhost:4840");
            var uri2_dupe = new Uri($"opc.tcp://{Utils.GetHostName()}:4840");

            // uri compare resolves localhost
            Assert.That(Utils.AreDomainsEqual(uri1, uri1_dupe), Is.True);
            Assert.That(Utils.AreDomainsEqual(uri2, uri2_dupe), Is.True);
            Assert.That(Utils.AreDomainsEqual(uri2_dupe, uri2), Is.True);
            Assert.That(Utils.AreDomainsEqual(uri1, uri1), Is.True);
            Assert.That(Utils.AreDomainsEqual(uri2, uri2), Is.True);

            // string compare doesn't resolve localhost
            Assert.That(Utils.AreDomainsEqual(uri1.ToString(), uri1_dupe.ToString()), Is.True);
            Assert.That(Utils.AreDomainsEqual(uri2.ToString(), uri2_dupe.ToString()), Is.False);
            Assert.That(Utils.AreDomainsEqual(uri1.ToString(), null), Is.False);
            Assert.That(Utils.AreDomainsEqual(uri2.ToString(), null), Is.False);
            Assert.That(Utils.AreDomainsEqual(uri1.ToString(), string.Empty), Is.False);
            Assert.That(Utils.AreDomainsEqual(uri2.ToString(), string.Empty), Is.False);
            Assert.That(Utils.AreDomainsEqual(null, uri1.ToString()), Is.False);
            Assert.That(Utils.AreDomainsEqual(null, uri2.ToString()), Is.False);
            Assert.That(Utils.AreDomainsEqual(string.Empty, uri1.ToString()), Is.False);
            Assert.That(Utils.AreDomainsEqual(string.Empty, uri2.ToString()), Is.False);

            Assert.That(Utils.AreDomainsEqual((Uri)null, null), Is.False);
            Assert.That(Utils.AreDomainsEqual((string)null, null), Is.False);
            Assert.That(Utils.AreDomainsEqual(uri1, uri2), Is.False);
            Assert.That(Utils.AreDomainsEqual(uri1.ToString(), uri2.ToString()), Is.False);
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
        public void IsEqualUserIdentity()
        {
            var anonymousIdentity1 = new AnonymousIdentityToken();
            var anonymousIdentity2 = new AnonymousIdentityToken();

            Assert.That(Utils.IsEqualUserIdentity(anonymousIdentity1, anonymousIdentity1), Is.True);
            Assert.That(Utils.IsEqualUserIdentity(anonymousIdentity1, anonymousIdentity2), Is.True);
            Assert.That(Utils.IsEqualUserIdentity(anonymousIdentity1, null), Is.False);
            Assert.That(Utils.IsEqualUserIdentity(null, anonymousIdentity2), Is.False);

            var user1 = new UserNameIdentityToken
            {
                UserName = "user1",
                Password = Encoding.ASCII.GetBytes("pass1".ToCharArray()).ToByteString()
            };
            var user1_dupe = new UserNameIdentityToken
            {
                UserName = "user1",
                Password = Encoding.ASCII.GetBytes("pass1".ToCharArray()).ToByteString()
            };
            var user2 = new UserNameIdentityToken
            {
                UserName = "user2",
                Password = Encoding.ASCII.GetBytes("pass2".ToCharArray()).ToByteString()
            };
            Assert.That(Utils.IsEqualUserIdentity(user1, user1_dupe), Is.True);
            Assert.That(Utils.IsEqualUserIdentity(user1, user1), Is.True);
            Assert.That(Utils.IsEqualUserIdentity(user1, user2), Is.False);
            Assert.That(Utils.IsEqualUserIdentity(null, user2), Is.False);
            Assert.That(Utils.IsEqualUserIdentity(user1, null), Is.False);
        }

        /// <summary>
        /// Parse simple plain path string containing only numeric chars.
        /// </summary>
        [Test]
        public void RelativePathParseNumericStringNonDeep()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/11";
            Assert.That(RelativePath.Parse(str, typeTable).Format(typeTable), Is.EqualTo(str));
        }

        /// <summary>
        /// Parse deep path string containing only numeric chars.
        /// </summary>
        [Test]
        public void RelativePathParseNumericStringDeepPath()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/123/789";
            Assert.That(RelativePath.Parse(str, typeTable).Format(typeTable), Is.EqualTo(str));
        }

        /// <summary>
        /// Parse deep path string containing alphanumeric chars, staring with numeric chars.
        /// </summary>
        [Test]
        public void RelativePathParseAlphanumericStringPath()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/123A/78B9";
            Assert.That(RelativePath.Parse(str, typeTable).Format(typeTable), Is.EqualTo(str));
        }

        /// <summary>
        /// Parse deep path string containing alphanumeric chars (mixed), starting with alphabetical chars.
        /// </summary>
        [Test]
        public void RelativePathParseAlphanumericStringPath2()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/AA123A/bb78B9";
            Assert.That(RelativePath.Parse(str, typeTable).Format(typeTable), Is.EqualTo(str));
        }

        /// <summary>
        /// Parse deep path string containing only alphabetical chars.
        /// </summary>
        [Test]
        public void RelativePathParseAlphaStringPath()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/abc/def";
            Assert.That(RelativePath.Parse(str, typeTable).Format(typeTable), Is.EqualTo(str));
        }

        /// <summary>
        /// Parse deep path string containing only alphabetical chars with namespace index
        /// </summary>
        [Test]
        public void RelativePathParseAlphanumericWithNamespaceIndexStringPath()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/1:abc/2:def";
            Assert.That(RelativePath.Parse(str, typeTable).Format(typeTable), Is.EqualTo(str));
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
            Assert.That(
                RelativePath.Parse(input, typeTable, currentTable, targetTable).Format(typeTable),
                Is.EqualTo(output));
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
            ServiceResultException sre = Assert.Throws<ServiceResultException>(() =>
                RelativePath.Parse(path, typeTable, currentTable, targetTable).Format(typeTable));
            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
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
            XmlException ex = Assert.Throws<XmlException>(() =>
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
            ex = Assert.Throws<XmlException>(() =>
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
                ex = Assert.Throws<XmlException>(() =>
                {
                    var document = new XmlDocument();
                    document.Load(reader);
                });
                TestContext.Out.WriteLine(ex.Message);
            }

            // Validate the LoadInnerXml helper settings prohibit Dtd (recommended)
            TestContext.Out.WriteLine("Testing LoadInnerXml helper.");
            ex = Assert.Throws<XmlException>(() =>
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
            Assert.That(TypeInfo.GetBuiltInType(DataTypeIds.UtcTime), Is.EqualTo(BuiltInType.DateTime));

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
                NodeId staticValue = DataTypeIds.GetIdentifier(name);
                Assert.That(TypeInfo.GetBuiltInType(staticValue), Is.EqualTo(BuiltInType.ByteString));
            }

            Assert.That(
                TypeInfo.GetBuiltInType(DataTypeIds.SessionAuthenticationToken),
                Is.EqualTo(BuiltInType.NodeId));
            Assert.That(TypeInfo.GetBuiltInType(DataTypeIds.Duration), Is.EqualTo(BuiltInType.Double));

            bnList = [BrowseNames.IntegerId, BrowseNames.Index, BrowseNames.VersionTime, BrowseNames
                .Counter];
            foreach (string name in bnList)
            {
                NodeId nodeId = DataTypeIds.GetIdentifier(name);
                Assert.That(TypeInfo.GetBuiltInType(nodeId), Is.EqualTo(BuiltInType.UInt32));
            }

            Assert.That(
                TypeInfo.GetBuiltInType(DataTypeIds.BitFieldMaskDataType),
                Is.EqualTo(BuiltInType.UInt64));

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
                NodeId nodeId = DataTypeIds.GetIdentifier(name);
                Assert.That(TypeInfo.GetBuiltInType(nodeId), Is.EqualTo(BuiltInType.String));
            }
        }

        [Test]
        public void ValidateXmlWriterSettings()
        {
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            Assert.That(settings.Encoding, Is.EqualTo(Encoding.UTF8));
            Assert.That(settings.CloseOutput, Is.False);
            Assert.That(settings.Indent, Is.True);
            Assert.That(settings.ConformanceLevel, Is.EqualTo(ConformanceLevel.Document));
            Assert.That(settings.OmitXmlDeclaration, Is.False);
            Assert.That(settings.IndentChars, Is.EqualTo("  "));
        }

        [Test]
        public void ValidateXmlReaderSettings()
        {
            XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
            Assert.That(settings.DtdProcessing, Is.EqualTo(DtdProcessing.Prohibit));
            //Assert.AreEqual(null, settings.XmlResolver);
            Assert.That(settings.ConformanceLevel, Is.EqualTo(ConformanceLevel.Document));
            Assert.That(settings.CloseInput, Is.False);
        }

        /// <summary>
        /// Parse a path containing non-escaped hash character.
        /// </summary>
        [Test]
        public void RelativePathParseNonEscapedHash()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/abc#def";
            Assert.Throws<ServiceResultException>(() =>
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
            Assert.That(RelativePath.Parse(str, typeTable).Format(typeTable), Is.EqualTo(expected));
        }

        /// <summary>
        /// Parse a path containing correctly escaped hash character followed by exclamation.
        /// </summary>
        [Test]
        public void RelativePathParseEscapedHashFollowedByExclamation()
        {
            var typeTable = new TypeTable(new NamespaceTable());
            const string str = "/abc&#!def";
            Assert.Throws<ServiceResultException>(() =>
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
            Assert.Throws<ServiceResultException>(() =>
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
            Assert.Throws<ServiceResultException>(() =>
                RelativePath.Parse(str, typeTable).Format(typeTable));
        }
    }
}
