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

using System;
using System.Xml;
using Moq;
using NUnit.Framework;

#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages
#pragma warning disable IDE0004 // Remove Unnecessary Cast
#pragma warning disable CA2263 // Prefer generic overload when type is known

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the <see cref="EnumValue"/> struct.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EnumValueTests
    {
        [Test]
        public void ConstructorWithValueAndSymbolSetsProperties()
        {
            var ev = new EnumValue(42, "MySymbol");
            Assert.That(ev.Value, Is.EqualTo(42));
            Assert.That(ev.Symbol, Is.EqualTo("MySymbol"));
        }

        [Test]
        public void ConstructorWithValueAndNullSymbolSetsNullSymbol()
        {
            var ev = new EnumValue(5);
            Assert.That(ev.Value, Is.EqualTo(5));
            Assert.That(ev.Symbol, Is.Null);
        }

        [Test]
        public void ConstructorWithValueAndEmptySymbolReturnsNullSymbol()
        {
            var ev = new EnumValue(5, string.Empty);
            Assert.That(ev.Value, Is.EqualTo(5));
            Assert.That(ev.Symbol, Is.Null);
        }

        [Test]
        public void ConstructorWithEnumTypeSetsSource()
        {
            var ev = new EnumValue(1, typeof(NodeClass));
            Assert.That(ev.Value, Is.EqualTo(1));
            Assert.That(ev.Symbol, Is.EqualTo("Object"));
        }

        [Test]
        public void ConstructorWithNonEnumTypeThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _ = new EnumValue(1, typeof(string)));
        }

        [Test]
        public void ConstructorWithNullTypeSetsNoSource()
        {
            var ev = new EnumValue(7, (Type?)null);
            Assert.That(ev.Value, Is.EqualTo(7));
            Assert.That(ev.Symbol, Is.Null);
        }

        [Test]
        public void ConstructorWithIntTypeSetsNoSource()
        {
            var ev = new EnumValue(7, typeof(int));
            Assert.That(ev.Value, Is.EqualTo(7));
            Assert.That(ev.Symbol, Is.Null);
        }

        [Test]
        public void ConstructorWithIEnumeratedTypeSetsSource()
        {
            var mockType = new Mock<IEnumeratedType>();
            mockType.Setup(t => t.TryGetSymbol(2, out It.Ref<string?>.IsAny))
                .Callback(new TryGetSymbolDelegate((v, out s) => s = "Variable"))
                .Returns(true);

            var ev = new EnumValue(2, mockType.Object);
            Assert.That(ev.Value, Is.EqualTo(2));
            Assert.That(ev.Symbol, Is.EqualTo("Variable"));
        }

        [Test]
        public void SymbolFromStringSourceReturnsString()
        {
            var ev = new EnumValue(0, "TestSymbol");
            Assert.That(ev.Symbol, Is.EqualTo("TestSymbol"));
        }

        [Test]
        public void SymbolFromTypeSourceReturnsEnumName()
        {
            var ev = new EnumValue(2, typeof(NodeClass));
            Assert.That(ev.Symbol, Is.EqualTo("Variable"));
        }

        [Test]
        public void SymbolFromTypeSourceWithInvalidValueReturnsNull()
        {
            var ev = new EnumValue(9999, typeof(NodeClass));
            Assert.That(ev.Symbol, Is.Null);
        }

        [Test]
        public void SymbolFromNullSourceReturnsNull()
        {
            var ev = new EnumValue(42);
            Assert.That(ev.Symbol, Is.Null);
        }

        [Test]
        public void SymbolFromIEnumeratedTypeSourceThatFailsReturnsNull()
        {
            var mockType = new Mock<IEnumeratedType>();
            mockType.Setup(t => t.TryGetSymbol(It.IsAny<int>(), out It.Ref<string?>.IsAny))
                .Returns(false);

            var ev = new EnumValue(99, mockType.Object);
            Assert.That(ev.Symbol, Is.Null);
        }

        [Test]
        public void XmlNameFromStringSourceReturnsQualifiedName()
        {
            var ev = new EnumValue(0, "TestSymbol");
            Assert.That(ev.XmlName, Is.Not.Null);
            Assert.That(ev.XmlName!.Name, Is.EqualTo("TestSymbol"));
        }

        [Test]
        public void XmlNameFromEmptyStringReturnsNull()
        {
            var ev = new EnumValue(0, string.Empty);
            Assert.That(ev.XmlName, Is.Null);
        }

        [Test]
        public void XmlNameFromTypeSourceReturnsXmlName()
        {
            var ev = new EnumValue(1, typeof(NodeClass));
            Assert.That(ev.XmlName, Is.Not.Null);
        }

        [Test]
        public void XmlNameFromIEnumeratedTypeReturnsXmlName()
        {
            var expectedXmlName = new XmlQualifiedName("TestEnum", "http://test");
            var mockType = new Mock<IEnumeratedType>();
            mockType.Setup(t => t.XmlName).Returns(expectedXmlName);

            var ev = new EnumValue(0, mockType.Object);
            Assert.That(ev.XmlName, Is.EqualTo(expectedXmlName));
        }

        [Test]
        public void XmlNameFromNullSourceReturnsNull()
        {
            var ev = new EnumValue(0);
            Assert.That(ev.XmlName, Is.Null);
        }

        [Test]
        public void FromGenericEnumCreatesCorrectValue()
        {
            var ev = EnumValue.From(NodeClass.Variable);
            Assert.That(ev.Value, Is.EqualTo(2));
            Assert.That(ev.Symbol, Is.EqualTo("Variable"));
        }

        [Test]
        public void FromIntAndTypeCreatesCorrectValue()
        {
            var ev = EnumValue.From(1, typeof(NodeClass));
            Assert.That(ev.Value, Is.EqualTo(1));
            Assert.That(ev.Symbol, Is.EqualTo("Object"));
        }

        [Test]
        public void FromIntWithNullTypeCreatesValue()
        {
            var ev = EnumValue.From(42);
            Assert.That(ev.Value, Is.EqualTo(42));
            Assert.That(ev.Symbol, Is.Null);
        }

        [Test]
        public void FromObjectAndTypeCreatesCorrectValue()
        {
            var ev = EnumValue.From((object)NodeClass.Object, typeof(NodeClass));
            Assert.That(ev.Value, Is.EqualTo(1));
            Assert.That(ev.Symbol, Is.EqualTo("Object"));
        }

        [Test]
        public void FromGenericArrayCreatesArrayOfEnumValues()
        {
            ArrayOf<NodeClass> source = new NodeClass[]
            {
                NodeClass.Object, NodeClass.Variable
            };
            ArrayOf<EnumValue> result = EnumValue.From(source);
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Value, Is.EqualTo(1));
            Assert.That(result[1].Value, Is.EqualTo(2));
        }

        [Test]
        public void FromIntArrayAndTypeCreatesArrayOfEnumValues()
        {
            ArrayOf<int> source = new int[] { 1, 2 };
            ArrayOf<EnumValue> result = EnumValue.From(source, typeof(NodeClass));
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Value, Is.EqualTo(1));
            Assert.That(result[1].Value, Is.EqualTo(2));
        }

        [Test]
        public void FromNullIntArrayReturnsDefault()
        {
            ArrayOf<int> source = default;
            ArrayOf<EnumValue> result = EnumValue.From(source, typeof(NodeClass));
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void FromGenericMatrixCreatesMatrixOfEnumValues()
        {
            MatrixOf<NodeClass> source = new NodeClass[]
            {
                NodeClass.Object, NodeClass.Variable,
                NodeClass.Method, NodeClass.ObjectType
            }.ToMatrixOf(2, 2);

            int[] expected = [2, 2];
            MatrixOf<EnumValue> result = EnumValue.From(source);
            Assert.That(result.Count, Is.EqualTo(4));
            Assert.That(result.Dimensions, Is.EqualTo(expected));
        }

        [Test]
        public void FromIntMatrixAndTypeCreatesMatrix()
        {
            MatrixOf<int> source = s_testIntValues.ToMatrixOf(2, 2);
            MatrixOf<EnumValue> result = EnumValue.From(source, typeof(NodeClass));
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void FromNullIntMatrixReturnsDefault()
        {
            MatrixOf<int> source = default;
            MatrixOf<EnumValue> result = EnumValue.From(source, typeof(NodeClass));
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void GetDefaultGenericReturnsFirstEnumValue()
        {
            var ev = EnumValue.GetDefault<NodeClass>();
            Assert.That(ev.Value, Is.Zero);
        }

        [Test]
        public void GetDefaultTypeReturnsFirstEnumValue()
        {
            var ev = EnumValue.GetDefault(typeof(NodeClass));
            Assert.That(ev.Value, Is.Zero);
        }

        [Test]
        public void FromArrayWithNullReturnsDefault()
        {
            ArrayOf<EnumValue> result = EnumValue.FromArray(null, typeof(NodeClass));
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void FromArrayWithValidArrayCreatesEnumValues()
        {
            var array = new NodeClass[] { NodeClass.Object, NodeClass.Variable };
            ArrayOf<EnumValue> result = EnumValue.FromArray(array, typeof(NodeClass));
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Value, Is.EqualTo(1));
            Assert.That(result[1].Value, Is.EqualTo(2));
        }

        [Test]
        public void FromArrayWithArrayTypeExtractsElementType()
        {
            var array = new NodeClass[] { NodeClass.Object };
            ArrayOf<EnumValue> result = EnumValue.FromArray(array, typeof(NodeClass[]));
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Symbol, Is.EqualTo("Object"));
        }

        [Test]
        public void FromMatrixWithNullReturnsDefault()
        {
            MatrixOf<EnumValue> result = EnumValue.FromMatrix(null, typeof(NodeClass));
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void FromMatrixWithValidMultiDimArrayCreatesMatrix()
        {
            var array = new NodeClass[,]
            {
                { NodeClass.Object, NodeClass.Variable },
                { NodeClass.Method, NodeClass.ObjectType }
            };
            MatrixOf<EnumValue> result = EnumValue.FromMatrix(array, typeof(NodeClass));
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ToGenericConvertsToEnum()
        {
            var ev = EnumValue.From(NodeClass.Variable);
            NodeClass result = ev.To<NodeClass>();
            Assert.That(result, Is.EqualTo(NodeClass.Variable));
        }

        [Test]
        public void ToObjectWithTypeSourceReturnsEnumObject()
        {
            var ev = new EnumValue(1, typeof(NodeClass));
            object result = ev.ToObject();
            Assert.That(result, Is.TypeOf<NodeClass>());
            Assert.That((NodeClass)result, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        public void ToObjectWithoutTypeSourceReturnsInt()
        {
            var ev = new EnumValue(42, "TestSymbol");
            object result = ev.ToObject();
            Assert.That(result, Is.TypeOf<int>());
            Assert.That((int)result, Is.EqualTo(42));
        }

        [Test]
        public void EqualsEnumValueWithSameValueReturnsTrue()
        {
            var ev1 = new EnumValue(1, "Object");
            var ev2 = new EnumValue(1, typeof(NodeClass));
            Assert.That(ev1.Equals(ev2), Is.True);
        }

        [Test]
        public void EqualsEnumValueWithDifferentValueReturnsFalse()
        {
            var ev1 = new EnumValue(1);
            var ev2 = new EnumValue(2);
            Assert.That(ev1.Equals(ev2), Is.False);
        }

        [Test]
        public void EqualsStringMatchesSymbolCaseInsensitive()
        {
            var ev = new EnumValue(1, "Object");
            Assert.That(ev.Equals("Object"), Is.True);
            Assert.That(ev.Equals("object"), Is.True);
            Assert.That(ev.Equals("OBJECT"), Is.True);
            Assert.That(ev.Equals("Variable"), Is.False);
        }

        [Test]
        public void EqualsIntMatchesValue()
        {
            var ev = new EnumValue(42);
            Assert.That(ev.Equals(42), Is.True);
            Assert.That(ev.Equals(43), Is.False);
        }

        [Test]
        public void EqualsObjectWithStringDelegatesToEqualsString()
        {
            var ev = new EnumValue(1, "Object");
            Assert.That(ev.Equals((object)"Object"), Is.True);
            Assert.That(ev.Equals((object)"Variable"), Is.False);
        }

        [Test]
        public void EqualsObjectWithIntDelegatesToEqualsInt()
        {
            var ev = new EnumValue(42);
            Assert.That(ev.Equals((object)42), Is.True);
            Assert.That(ev.Equals((object)43), Is.False);
        }

        [Test]
        public void EqualsObjectWithEnumValueDelegatesToEqualsEnumValue()
        {
            var ev1 = new EnumValue(1);
            var ev2 = new EnumValue(1);
            Assert.That(ev1.Equals((object)ev2), Is.True);
        }

        [Test]
        public void EqualsObjectWithOtherTypeReturnsFalse()
        {
            var ev = new EnumValue(1);
            Assert.That(ev.Equals((object)1.0), Is.False);
            Assert.That(ev.Equals((object?)null), Is.False);
        }

        [Test]
        public void EqualsStringWithNullSymbolReturnsFalse()
        {
            var ev = new EnumValue(1);
            Assert.That(ev.Equals("Object"), Is.False);
        }

        [Test]
        public void OperatorEqualEnumValues()
        {
            var ev1 = new EnumValue(1);
            var ev2 = new EnumValue(1);
            var ev3 = new EnumValue(2);
            Assert.That(ev1 == ev2, Is.True);
            Assert.That(ev1 == ev3, Is.False);
        }

        [Test]
        public void OperatorNotEqualEnumValues()
        {
            var ev1 = new EnumValue(1);
            var ev2 = new EnumValue(2);
            Assert.That(ev1 != ev2, Is.True);
            Assert.That(ev1 != new EnumValue(1), Is.False);
        }

        [Test]
        public void OperatorEqualInt()
        {
            var ev = new EnumValue(42);
            Assert.That(ev == 42, Is.True);
            Assert.That(ev == 43, Is.False);
        }

        [Test]
        public void OperatorNotEqualInt()
        {
            var ev = new EnumValue(42);
            Assert.That(ev != 43, Is.True);
            Assert.That(ev != 42, Is.False);
        }

        [Test]
        public void OperatorEqualString()
        {
            var ev = new EnumValue(1, "Object");
            Assert.That(ev == "Object", Is.True);
            Assert.That(ev == "Variable", Is.False);
        }

        [Test]
        public void OperatorNotEqualString()
        {
            var ev = new EnumValue(1, "Object");
            Assert.That(ev != "Variable", Is.True);
            Assert.That(ev != "Object", Is.False);
        }

        [Test]
        public void ExplicitCastToIntReturnsValue()
        {
            var ev = new EnumValue(42);
            int result = (int)ev;
            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void ExplicitCastFromIntCreatesEnumValue()
        {
            var ev = (EnumValue)42;
            Assert.That(ev.Value, Is.EqualTo(42));
            Assert.That(ev.Symbol, Is.Null);
        }

        [Test]
        public void ToStringWithSymbolReturnsSymbolAndValue()
        {
            var ev = new EnumValue(1, "Object");
            Assert.That(ev.ToString(), Is.EqualTo("Object_1"));
        }

        [Test]
        public void ToStringWithTypeSourceReturnsSymbolAndValue()
        {
            var ev = new EnumValue(2, typeof(NodeClass));
            Assert.That(ev.ToString(), Is.EqualTo("Variable_2"));
        }

        [Test]
        public void ToStringWithoutSymbolReturnsValueOnly()
        {
            var ev = new EnumValue(42);
            Assert.That(ev.ToString(), Is.EqualTo("42"));
        }

        [Test]
        public void GetHashCodeReturnsValue()
        {
            var ev = new EnumValue(42);
            Assert.That(ev.GetHashCode(), Is.EqualTo(42));
        }

        [Test]
        public void GetHashCodeSameForEqualValues()
        {
            var ev1 = new EnumValue(7, "A");
            var ev2 = new EnumValue(7, typeof(NodeClass));
            Assert.That(ev1.GetHashCode(), Is.EqualTo(ev2.GetHashCode()));
        }

        [Test]
        public void DefaultEnumValueHasZeroValueAndNullSymbol()
        {
            EnumValue ev = default;
            Assert.That(ev.Value, Is.Zero);
            Assert.That(ev.Symbol, Is.Null);
        }

        private delegate void TryGetSymbolDelegate(int value, out string? symbol);
        private static readonly int[] s_testIntValues = [1, 2, 4, 8];
    }
}
