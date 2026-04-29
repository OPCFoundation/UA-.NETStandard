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
using NUnit.Framework;
using Opc.Ua.Tests;

#pragma warning disable NUnit2010

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for <see cref="ReferenceDescription"/>.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReferenceDescriptionTests
    {
        private static ReferenceDescription CreatePopulated()
        {
            return new ReferenceDescription
            {
                ReferenceTypeId = new NodeId(33),
                IsForward = false,
                NodeId = new ExpandedNodeId(44, 2),
                BrowseName = new QualifiedName("TestBrowse", 1),
                DisplayName = new LocalizedText("en", "TestDisplay"),
                NodeClass = NodeClass.Variable,
                TypeDefinition = new ExpandedNodeId(55, 3)
            };
        }

        [Test]
        public void DefaultConstructorSetsIsForwardTrue()
        {
            var rd = new ReferenceDescription();

            Assert.That(rd.IsForward, Is.True);
        }

        [Test]
        public void DefaultConstructorSetsNodeClassUnspecified()
        {
            var rd = new ReferenceDescription();

            Assert.That(rd.NodeClass, Is.EqualTo(NodeClass.Unspecified));
        }

        [Test]
        public void DefaultConstructorSetsNullDefaults()
        {
            var rd = new ReferenceDescription();

            Assert.That(rd.ReferenceTypeId, Is.Default);
            Assert.That(rd.NodeId, Is.Default);
            Assert.That(rd.BrowseName, Is.Default);
            Assert.That(rd.DisplayName, Is.Default);
            Assert.That(rd.TypeDefinition, Is.Default);
            Assert.That(rd.Unfiltered, Is.False);
        }

        [Test]
        public void TypeIdReturnsExpectedValue()
        {
            var rd = new ReferenceDescription();

            Assert.That(rd.TypeId, Is.EqualTo(DataTypeIds.ReferenceDescription));
        }

        [Test]
        public void BinaryEncodingIdReturnsExpectedValue()
        {
            var rd = new ReferenceDescription();

            Assert.That(rd.BinaryEncodingId, Is.EqualTo(ObjectIds.ReferenceDescription_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsExpectedValue()
        {
            var rd = new ReferenceDescription();

            Assert.That(rd.XmlEncodingId, Is.EqualTo(ObjectIds.ReferenceDescription_Encoding_DefaultXml));
        }


        [Test]
        public void EncodeDecodeRoundTripPreservesAllProperties()
        {
            ReferenceDescription original = CreatePopulated();
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new ReferenceDescription();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void EncodeDecodeRoundTripWithDefaultValues()
        {
            var original = new ReferenceDescription();
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new ReferenceDescription();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void IsEqualWithSameReferenceReturnsTrue()
        {
            ReferenceDescription rd = CreatePopulated();

            Assert.That(rd.IsEqual(rd), Is.True);
        }

        [Test]
        public void IsEqualWithEqualValuesReturnsTrue()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();

            Assert.That(rd1.IsEqual(rd2), Is.True);
        }

        [Test]
        public void IsEqualWithNullReturnsFalse()
        {
            ReferenceDescription rd = CreatePopulated();

            Assert.That(rd.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualWithWrongTypeReturnsFalse()
        {
            ReferenceDescription rd = CreatePopulated();
            var other = new Argument();

            Assert.That(rd.IsEqual(other), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentReferenceTypeIdReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.ReferenceTypeId = new NodeId(999);

            Assert.That(rd1.IsEqual(rd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentIsForwardReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.IsForward = !rd1.IsForward;

            Assert.That(rd1.IsEqual(rd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentNodeIdReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.NodeId = new ExpandedNodeId(777, 5);

            Assert.That(rd1.IsEqual(rd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentBrowseNameReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.BrowseName = new QualifiedName("Different", 1);

            Assert.That(rd1.IsEqual(rd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentDisplayNameReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.DisplayName = new LocalizedText("en", "Different");

            Assert.That(rd1.IsEqual(rd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentNodeClassReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.NodeClass = NodeClass.Object;

            Assert.That(rd1.IsEqual(rd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentTypeDefinitionReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.TypeDefinition = new ExpandedNodeId(888, 7);

            Assert.That(rd1.IsEqual(rd2), Is.False);
        }

        [Test]
        public void EqualsObjectWithNullReturnsFalse()
        {
            ReferenceDescription rd = CreatePopulated();

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(rd.Equals((object)null), Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualsObjectWithNonReferenceDescriptionReturnsFalse()
        {
            ReferenceDescription rd = CreatePopulated();

            Assert.That(rd.Equals("not a ReferenceDescription"), Is.False);
        }

        [Test]
        public void EqualsObjectWithEqualValueReturnsTrue()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();

            Assert.That(rd1.Equals((object)rd2), Is.True);
        }

        [Test]
        public void EqualsReferenceDescriptionWithEqualValueReturnsTrue()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();

            Assert.That(rd1.Equals(rd2), Is.True);
        }

        [Test]
        public void EqualsReferenceDescriptionWithNullReturnsFalse()
        {
            ReferenceDescription rd = CreatePopulated();

#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(rd.Equals((ReferenceDescription)null), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualsReferenceDescriptionWithDifferentReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.NodeClass = NodeClass.Method;

            Assert.That(rd1.Equals(rd2), Is.False);
        }

        [Test]
        public void GetHashCodeEqualForEqualObjects()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();

            Assert.That(rd1.GetHashCode(), Is.EqualTo(rd2.GetHashCode()));
        }

        [Test]
        public void GetHashCodeDiffersForDifferentObjects()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.NodeId = new ExpandedNodeId(999, 9);

            // Hash codes are not guaranteed to differ, but for distinct values they typically do
            Assert.That(rd1.GetHashCode(), Is.Not.EqualTo(rd2.GetHashCode()));
        }

        [Test]
        public void OperatorEqualWithEqualValuesReturnsTrue()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();

            Assert.That(rd1 == rd2, Is.True);
        }

        [Test]
        public void OperatorEqualWithDifferentValuesReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.IsForward = true;

            Assert.That(rd1 == rd2, Is.False);
        }

        [Test]
        public void OperatorEqualWithBothNullReturnsTrue()
        {
            ReferenceDescription rd1 = null;
            ReferenceDescription rd2 = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(rd1 == rd2, Is.True);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void OperatorEqualWithOneNullReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(rd1 == rd2, Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void OperatorNotEqualWithDifferentValuesReturnsTrue()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();
            rd2.NodeClass = NodeClass.DataType;

            Assert.That(rd1 != rd2, Is.True);
        }

        [Test]
        public void OperatorNotEqualWithEqualValuesReturnsFalse()
        {
            ReferenceDescription rd1 = CreatePopulated();
            ReferenceDescription rd2 = CreatePopulated();

            Assert.That(rd1 != rd2, Is.False);
        }

        [Test]
        public void CloneProducesEqualCopy()
        {
            ReferenceDescription original = CreatePopulated();

            var clone = (ReferenceDescription)original.Clone();

            Assert.That(original.IsEqual(clone), Is.True);
        }

        [Test]
        public void CloneProducesIndependentCopy()
        {
            ReferenceDescription original = CreatePopulated();

            var clone = (ReferenceDescription)original.Clone();
            clone.DisplayName = new LocalizedText("de", "Modified");

            Assert.That(original.DisplayName.Text, Is.EqualTo("TestDisplay"));
        }

        [Test]
        public void MemberwiseCloneProducesEqualCopy()
        {
            ReferenceDescription original = CreatePopulated();

            var clone = (ReferenceDescription)original.MemberwiseClone();

            Assert.That(original.IsEqual(clone), Is.True);
        }

        [Test]
        public void MemberwiseCloneProducesIndependentCopy()
        {
            ReferenceDescription original = CreatePopulated();

            var clone = (ReferenceDescription)original.MemberwiseClone();
            clone.BrowseName = new QualifiedName("Modified", 9);

            Assert.That(original.BrowseName.Name, Is.EqualTo("TestBrowse"));
        }

        [Test]
        public void ToStringReturnsDisplayNameTextWhenSet()
        {
            var rd = new ReferenceDescription
            {
                DisplayName = new LocalizedText("en", "MyDisplay"),
                BrowseName = new QualifiedName("MyBrowse", 1)
            };

            Assert.That(rd.ToString(), Is.EqualTo("MyDisplay"));
        }

        [Test]
        public void ToStringReturnsBrowseNameWhenDisplayNameEmpty()
        {
            var rd = new ReferenceDescription
            {
                DisplayName = LocalizedText.Null,
                BrowseName = new QualifiedName("FallbackBrowse", 1)
            };

            Assert.That(rd.ToString(), Is.EqualTo("FallbackBrowse"));
        }

        [Test]
        public void ToStringReturnsUnknownWhenBothEmpty()
        {
            var rd = new ReferenceDescription
            {
                NodeClass = NodeClass.Object
            };

            Assert.That(rd.ToString(), Is.EqualTo("(unknown object)"));
        }

        [Test]
        public void ToStringReturnsUnknownWithDefaultNodeClass()
        {
            var rd = new ReferenceDescription();

            Assert.That(rd.ToString(), Is.EqualTo("(unknown unspecified)"));
        }

        [Test]
        public void ToStringWithFormatThrowsFormatException()
        {
            var rd = new ReferenceDescription();

            Assert.That(() => rd.ToString("G", null), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ToStringWithNullFormatReturnsExpected()
        {
            var rd = new ReferenceDescription
            {
                DisplayName = new LocalizedText("en", "Formatted")
            };

            string result = rd.ToString(null, null);

            Assert.That(result, Is.EqualTo("Formatted"));
        }
    }
}
