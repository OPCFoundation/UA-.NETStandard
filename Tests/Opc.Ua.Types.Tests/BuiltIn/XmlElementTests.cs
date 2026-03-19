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

#nullable enable

using NUnit.Framework;
using System;
using System.Xml.Linq;

#pragma warning disable CA1508 // Avoid dead conditional code

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class XmlElementTests
    {
        [Test]
        public void XmlElementOuterXmlShouldReturnCorrectValue()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            Assert.That(xmlElement.OuterXml, Is.EqualTo(xmlString));
        }

        [Test]
        public void XmlElementIsEmptyShouldReturnTrueForEmptyElement()
        {
            XmlElement xmlElement = XmlElement.Empty;
            Assert.That(xmlElement.IsEmpty, Is.True);
        }

        [Test]
        public void XmlElementFromNullXmlElementShouldBeEmpty()
        {
            var xmlElement = new XmlElement((System.Xml.XmlElement?)null);
            Assert.That(xmlElement.IsEmpty, Is.True);
        }

        [Test]
        public void XmlElementFromNullXElementShouldBeEmpty()
        {
            var xmlElement = new XmlElement((XElement?)null);
            Assert.That(xmlElement.IsEmpty, Is.True);
        }

        [Test]
        public void XmlElementIsEmptyShouldReturnFalseForNonEmptyElement()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            Assert.That(xmlElement.IsEmpty, Is.False);
        }

        [Test]
        public void XmlElementEqualsBadTypeShouldBeFalse()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(xmlElement.Equals(0), Is.False);
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
        }

        [Test]
        public void XmlElementEqualsUtf8StringShouldReturnTrueForEqualStrings()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            Assert.That(xmlElement, Is.EqualTo(xmlString));
            Assert.That(xmlElement, Is.EqualTo((object)xmlString));
            Assert.That(xmlElement, Is.EqualTo(xmlString));
            Assert.That(xmlElement, Is.EqualTo(xmlString));
        }

        [Test]
        public void XmlElementEqualsUtf8StringShouldReturnFalseForDifferentStrings()
        {
            const string xmlString1 = "<root></root>";
            const string xmlString2 = "<root><child/></root>";
            var xmlElement = new XmlElement(xmlString1);

            Assert.That(xmlElement, Is.Not.EqualTo(xmlString2));
            Assert.That(xmlElement, Is.Not.EqualTo((object)xmlString2));
            Assert.That(xmlElement, Is.Not.EqualTo(xmlString2));
            Assert.That(xmlElement, Is.Not.EqualTo(xmlString2));
        }

        [Test]
        public void XmlElementEqualsStringShouldReturnTrueForEqualStrings()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            Assert.That(xmlElement, Is.EqualTo(xmlString));
            Assert.That(xmlElement, Is.EqualTo((object)xmlString));
            Assert.That(xmlElement, Is.EqualTo(xmlString));
            Assert.That(xmlElement, Is.EqualTo(xmlString));
        }

        [Test]
        public void XmlElementEqualsStringShouldReturnFalseForDifferentStrings()
        {
            const string xmlString1 = "<root></root>";
            const string xmlString2 = "<root><child/></root>";
            var xmlElement = new XmlElement(xmlString1);

            Assert.That(xmlElement, Is.Not.EqualTo(xmlString2));
            Assert.That(xmlElement, Is.Not.EqualTo((object)xmlString2));
            Assert.That(xmlElement, Is.Not.EqualTo(xmlString2));
            Assert.That(xmlElement, Is.Not.EqualTo(xmlString2));
        }

        [Test]
        public void XmlElementEqualsXmlElementNodeShouldReturnTrueForEqualNodes()
        {
            const string xmlString = "<root></root>";
            var xmlDocument = new System.Xml.XmlDocument();
#pragma warning disable CA3075 // Insecure DTD processing in XML
            xmlDocument.LoadXml(xmlString);
#pragma warning restore CA3075 // Insecure DTD processing in XML
            System.Xml.XmlElement? xmlElementNode = xmlDocument.DocumentElement;
            var xmlElement = new XmlElement(xmlElementNode);

            Assert.That(xmlElement, Is.EqualTo(xmlElementNode));
            Assert.That(xmlElement, Is.EqualTo((object?)xmlElementNode));
            Assert.That(xmlElement, Is.EqualTo(xmlElementNode));
            Assert.That(xmlElement, Is.EqualTo(xmlElementNode));
        }

        [Test]
        public void XmlElementEqualsNullXmlElementNodeShouldReturnFalse()
        {
            System.Xml.XmlElement? xmlElementNode = null;
            var xmlElement = new XmlElement("<root></root>");

            Assert.That(xmlElement, Is.Not.EqualTo(xmlElementNode));
            Assert.That(xmlElement, Is.Not.EqualTo((object?)xmlElementNode));
            Assert.That(xmlElement, Is.Not.EqualTo(xmlElementNode));
            Assert.That(xmlElement, Is.Not.EqualTo(xmlElementNode));
        }

        [Test]
        public void XmlElementEqualsXmlElementNodeShouldReturnFalseForDifferentNodes()
        {
            const string xmlString1 = "<root></root>";
            const string xmlString2 = "<root><child/></root>";
            var xmlDocument1 = new System.Xml.XmlDocument();
            var xmlDocument2 = new System.Xml.XmlDocument();
#pragma warning disable CA3075 // Insecure DTD processing in XML
            xmlDocument1.LoadXml(xmlString1);
            xmlDocument2.LoadXml(xmlString2);
#pragma warning restore CA3075 // Insecure DTD processing in XML
            System.Xml.XmlElement? xmlElementNode1 = xmlDocument1.DocumentElement;
            System.Xml.XmlElement? xmlElementNode2 = xmlDocument2.DocumentElement;
            var xmlElement = new XmlElement(xmlElementNode1);

            Assert.That(xmlElement, Is.Not.EqualTo(xmlElementNode2));
            Assert.That(xmlElement, Is.Not.EqualTo((object?)xmlElementNode2));
            Assert.That(xmlElement, Is.Not.EqualTo(xmlElementNode2));
            Assert.That(xmlElement, Is.Not.EqualTo(xmlElementNode2));
        }

        [Test]
        public void XmlElementEqualsXElementShouldReturnTrueForEqualElements()
        {
            const string xmlString = "<root></root>";
            var xElement = XElement.Parse(xmlString);
            var xmlElement = new XmlElement(xElement);

            Assert.That(xmlElement, Is.EqualTo(xElement));
            Assert.That(xmlElement, Is.EqualTo((object)xElement));
            Assert.That(xmlElement, Is.EqualTo(xElement));
            Assert.That(xmlElement, Is.EqualTo(xElement));
        }

        [Test]
        public void XmlElementEqualsXElementShouldReturnFalseForDifferentElements()
        {
            const string xmlString1 = "<root></root>";
            const string xmlString2 = "<root><child/></root>";
            var xElement1 = XElement.Parse(xmlString1);
            var xElement2 = XElement.Parse(xmlString2);
            var xmlElement = new XmlElement(xElement1);

            Assert.That(xmlElement, Is.Not.EqualTo(xElement2));
            Assert.That(xmlElement, Is.Not.EqualTo((object?)xElement2));
            Assert.That(xmlElement, Is.Not.EqualTo(xElement2));
            Assert.That(xmlElement, Is.Not.EqualTo(xElement2));
        }

        [Test]
        public void XmlElementEqualsXElementShouldReturnFalseForNull()
        {
            XElement? nullElement = null;
            var xmlElement = (XmlElement)"<root></root>";

            Assert.That(xmlElement, Is.Not.EqualTo(nullElement));
            Assert.That(xmlElement, Is.Not.EqualTo((object?)nullElement));
            Assert.That(xmlElement, Is.Not.EqualTo(nullElement));
            Assert.That(xmlElement, Is.Not.EqualTo(nullElement));
        }

        [Test]
        public void XmlElementEqualsXmlElementShouldReturnTrueForEqualElements()
        {
            const string xmlString = "<root></root>";
            var xmlElement1 = new XmlElement(xmlString);
            var xmlElement2 = new XmlElement(xmlString);

            Assert.That(xmlElement1, Is.EqualTo(xmlElement2));
            Assert.That(xmlElement1, Is.EqualTo((object)xmlElement2));
            Assert.That(xmlElement1, Is.EqualTo(xmlElement2));
            Assert.That(xmlElement1, Is.EqualTo(xmlElement2));
        }

        [Test]
        public void XmlElementEqualsXmlElementShouldReturnFalseForDifferentElements()
        {
            const string xmlString1 = "<root></root>";
            const string xmlString2 = "<root><child/></root>";
            var xmlElement1 = new XmlElement(xmlString1);
            var xmlElement2 = new XmlElement(xmlString2);

            Assert.That(xmlElement1, Is.Not.EqualTo(xmlElement2));
            Assert.That(xmlElement1, Is.Not.EqualTo((object?)xmlElement2));
            Assert.That(xmlElement1, Is.Not.EqualTo(xmlElement2));
            Assert.That(xmlElement1, Is.Not.EqualTo(xmlElement2));
        }

        [Test]
        public void XmlElementToStringShouldReturnCorrectString()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            Assert.That(xmlElement.ToString(), Is.EqualTo(xmlString));
        }

        [Test]
        public void XmlElementGetHashCodeShouldReturnCorrectHashCode()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            Assert.That(
                xmlElement.GetHashCode(),
                Is.EqualTo(xmlString.GetHashCode(StringComparison.Ordinal)));
        }

        [Test]
        public void XmlElementEqualsObjectShouldReturnTrueForEqualObjects()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            Assert.That(xmlElement, Is.EqualTo(xmlString));
            Assert.That(xmlElement, Is.EqualTo((object)xmlString));
        }

        [Test]
        public void XmlElementEqualsObjectShouldReturnFalseForDifferentObjects()
        {
            const string xmlString1 = "<root></root>";
            const string xmlString2 = "<root><child/></root>";
            var xmlElement = new XmlElement(xmlString1);

            Assert.That(xmlElement, Is.Not.EqualTo(xmlString2));
            Assert.That(xmlElement, Is.Not.EqualTo((object)xmlString2));
        }

        [Test]
        public void AsXmlElementShouldReturnXmlElementNode()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            System.Xml.XmlElement? result = xmlElement.AsXmlElement();

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.OuterXml, Is.EqualTo(xmlString));
        }

        [Test]
        public void AsXmlElementShouldReturnNullForInvalidXml()
        {
            const string xmlString = "<root>";
            var xmlElement = new XmlElement(xmlString);

            System.Xml.XmlElement? result = xmlElement.AsXmlElement();

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ToXmlElementShouldReturnXmlElementNode()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            var result = xmlElement.ToXmlElement();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.OuterXml, Is.EqualTo(xmlString));
        }

        [Test]
        public void ToXmlElementShouldThrowXmlExceptionForInvalidXml()
        {
            const string xmlString = "<root>";
            var xmlElement = new XmlElement(xmlString);

            Assert.That(() => xmlElement.ToXmlElement(),
                Throws.TypeOf<System.Xml.XmlException>());
        }

        [Test]
        public void ToXmlElementShouldThrowXmlExceptionForWhitespace()
        {
            const string xmlString = "    ";
            var xmlElement = new XmlElement(xmlString);

            Assert.That(() => xmlElement.ToXmlElement(),
                Throws.TypeOf<System.Xml.XmlException>());
        }

        [Test]
        public void AsXElementShouldReturnXElement()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            XElement? result = xmlElement.AsXElement();

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ToString(), Is.EqualTo(xmlString));
        }

        [Test]
        public void AsXElementShouldReturnNullForInvalidXml()
        {
            const string xmlString = "<root>";
            var xmlElement = new XmlElement(xmlString);

            XElement? result = xmlElement.AsXElement();

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ToXElementShouldReturnXElement()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            var result = xmlElement.ToXElement();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ToString(), Is.EqualTo(xmlString));
        }

        [Test]
        public void ToXElementShouldThrowXmlExceptionForInvalidXml()
        {
            const string xmlString = "<root>";
            var xmlElement = new XmlElement(xmlString);

            Assert.That(() => xmlElement.ToXmlElement(),
                Throws.TypeOf<System.Xml.XmlException>());
        }

        [Test]
        public void IsValidShouldReturnTrueForValidXml()
        {
            const string xmlString = "<root></root>";
            var xmlElement = new XmlElement(xmlString);

            bool result = xmlElement.IsValid;

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsValidShouldReturnFalseForInvalidXml()
        {
            const string xmlString = "<root>";
            var xmlElement = new XmlElement(xmlString);

            bool result = xmlElement.IsValid;

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsValidShouldReturnFalseForEmptyXml()
        {
            bool result = XmlElement.Empty.IsValid;
            Assert.That(result, Is.False);
        }

        [Test]
        public void ExplicitConversionFromUtf8StringTest()
        {
            const string str = "<root><child>value</child></root>";
            var xmlElement = (XmlElement)str;

            Assert.That(xmlElement.OuterXml,
                Is.EqualTo("<root><child>value</child></root>"));
        }

        [Test]
        public void ExplicitConversionToStringTest()
        {
            var xmlElement = new XmlElement("<root><child>value</child></root>");
            string? str = (string?)xmlElement;
            Assert.That(str, Is.EqualTo("<root><child>value</child></root>"));
        }

        [Test]
        public void ExplicitConversionFromStringTest()
        {
            const string str = "<root><child>value</child></root>";
            var xmlElement = (XmlElement)str;
            Assert.That(xmlElement.OuterXml,
                Is.EqualTo("<root><child>value</child></root>"));
        }

        [Test]
        public void ExplicitConversionToXmlElementNodeTest()
        {
            var xmlElement = new XmlElement("<root><child>value</child></root>");
            var xmlElementNode = (System.Xml.XmlElement?)xmlElement;
            Assert.That(xmlElementNode, Is.Not.Null);
            Assert.That(xmlElementNode!.OuterXml,
                Is.EqualTo("<root><child>value</child></root>"));
        }

        [Test]
        public void ExplicitConversionFromXmlElementNodeTest()
        {
            var xmlDocument = new System.Xml.XmlDocument();
#pragma warning disable CA3075 // Insecure DTD processing in XML
            xmlDocument.LoadXml("<root><child>value</child></root>");
#pragma warning restore CA3075 // Insecure DTD processing in XML
            System.Xml.XmlElement? xmlElementNode = xmlDocument.DocumentElement;
            var xmlElement = (XmlElement)xmlElementNode;
            Assert.That(xmlElement.OuterXml,
                Is.EqualTo("<root><child>value</child></root>"));
        }

        [Test]
        public void ExplicitConversionToXElementTest()
        {
            var xmlElement = new XmlElement("<root><child>value</child></root>");
            var xElement = (XElement?)xmlElement;
            Assert.That(xElement, Is.Not.Null);
            Assert.That(xmlElement, Is.EqualTo(xElement));
        }

        [Test]
        public void ExplicitConversionFromXElementTest()
        {
            var xElement = XElement.Parse(
                "<root><child>value</child></root>");
            var xmlElement = (XmlElement)xElement;
            Assert.That(xmlElement, Is.EqualTo(xElement));
        }
    }
}
