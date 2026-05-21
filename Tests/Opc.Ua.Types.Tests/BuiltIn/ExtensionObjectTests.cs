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

#pragma warning disable IDE0028 // Simplify collection initialization
#pragma warning disable IDE0305 // Simplify collection initialization

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
    public class ExtensionObjectTests
    {
        /// <summary>
        /// Validate ExtensionObject special cases and constructors.
        /// </summary>
        [Test]
        public void TestExtensionObject()
        {
            // Validate the default constructor
            var extensionObject_Default = new ExtensionObject();
            Assert.That(extensionObject_Default.IsNull, Is.True);
            Assert.That(extensionObject_Default.TypeId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(extensionObject_Default.Encoding, Is.EqualTo(ExtensionObjectEncoding.None));
            Assert.That(extensionObject_Default.IsNull, Is.True);
            // Constructor by ExtensionObject
            var extensionObject = new ExtensionObject(ExpandedNodeId.Null);
            Assert.That(extensionObject.TypeId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(extensionObject.Encoding, Is.EqualTo(ExtensionObjectEncoding.None));
            Assert.That(extensionObject.TryGetValue(out IEncodeable enc), Is.False);
            Assert.That(extensionObject.TryGetAsBinary(out ByteString _), Is.False);
            Assert.That(extensionObject.TryGetAsXml(out XmlElement _), Is.False);
            Assert.That(extensionObject.TryGetAsJson(out string _), Is.False);
            Assert.That(extensionObject.IsNull, Is.True);
            // static extensions
            Assert.That(ExtensionObject.ToEncodeable(default), Is.Null);
            // constructor by ExpandedNodeId
            extensionObject = new ExtensionObject(ExpandedNodeId.Null);
            Assert.That(extensionObject.GetHashCode(), Is.Zero);
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(ExtensionObject.ToArray(null, typeof(object)), Is.Null);
            Assert.That(ExtensionObject.ToList<object>(null), Is.Null);
            Assert.Throws<ServiceResultException>(
                () => new ExtensionObject(default, new object()));
            Assert.Throws<ServiceResultException>(
                () => new ExtensionObject(default, new byte[] { 1, 2, 3 }));
#pragma warning restore CS0618 // Type or member is obsolete
            // constructor by object
            ByteString bytes = [1, 2, 3];
            extensionObject = new ExtensionObject(default, bytes);
            Assert.That(extensionObject.IsNull, Is.False);
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(extensionObject.Equals(extensionObject), Is.True);
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            // string extension
            string extensionObjectString = extensionObject.ToString();
            Assert
                .Throws<FormatException>(() => extensionObject.ToString("123", null));
            Assert.That(extensionObjectString, Is.Not.Null);
            // IsEqual operator
            ExtensionObject clonedExtensionObject = extensionObject.WithTypeId(new ExpandedNodeId(333));
            Assert.That(extensionObject, Is.Not.EqualTo(clonedExtensionObject));
            Assert.That(extensionObject, Is.Not.EqualTo(extensionObject_Default));
            Assert.That(extensionObject, Is.Not.EqualTo(new object()));
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(clonedExtensionObject.Equals(clonedExtensionObject), Is.True);
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(extensionObject.TypeId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(
                extensionObject.TypeId.GetHashCode(),
                Is.EqualTo(ExpandedNodeId.Null.GetHashCode()));
            Assert.That(extensionObject.Encoding, Is.EqualTo(ExtensionObjectEncoding.Binary));
            Assert.That(extensionObject.TryGetAsBinary(out ByteString bs) ? bs : default, Is.EqualTo(bytes));
            // default value is null
            Assert.That(TypeInfo.GetDefaultValue(BuiltInType.ExtensionObject), Is.EqualTo(ExtensionObject.Null));
        }

        /// <summary>
        /// Test helper encodeable exposing three distinct node ids for
        /// <see cref="IEncodeable.TypeId"/>, <see cref="IEncodeable.BinaryEncodingId"/>,
        /// and <see cref="IEncodeable.XmlEncodingId"/>.
        /// </summary>
        private sealed class TestEncodeable : IEncodeable
        {
            public TestEncodeable(int value = 0)
            {
                Value = value;
            }

            public int Value { get; }

            public ExpandedNodeId TypeId => new(200000);
            public ExpandedNodeId BinaryEncodingId => new(200001);
            public ExpandedNodeId XmlEncodingId => new(200002);

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeable other && other.Value == Value;
            }

            public override int GetHashCode()
            {
                return Value;
            }

            public override bool Equals(object obj)
            {
                return obj is TestEncodeable other && other.Value == Value;
            }

            public object Clone()
            {
                return new TestEncodeable(Value);
            }
        }

        /// <summary>
        /// Second test helper encodeable with a different set of three node ids,
        /// used to verify that unrelated types do not falsely compare equal.
        /// </summary>
        private sealed class OtherEncodeable : IEncodeable
        {
            public ExpandedNodeId TypeId => new(400000);
            public ExpandedNodeId BinaryEncodingId => new(400001);
            public ExpandedNodeId XmlEncodingId => new(400002);

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is OtherEncodeable;
            }

            public object Clone()
            {
                return new OtherEncodeable();
            }
        }

        /// <summary>
        /// Equals returns true when both sides carry the same encodeable's TypeId.
        /// </summary>
        [Test]
        public void EqualsMatchesWhenTypeIdEqualsBodyTypeId()
        {
            var body = new TestEncodeable(42);
            var left = new ExtensionObject(body);
            var right = new ExtensionObject(body);
            Assert.That(left, Is.EqualTo(right));
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        /// <summary>
        /// Equals returns true when one ExtensionObject's TypeId is the encodeable's
        /// TypeId and the other's TypeId is the encodeable's BinaryEncodingId.
        /// </summary>
        [Test]
        public void EqualsMatchesWhenTypeIdEqualsBodyBinaryEncodingId()
        {
            var body = new TestEncodeable(42);
            var left = new ExtensionObject(body);
            var right = new ExtensionObject(body.BinaryEncodingId, new TestEncodeable(42));
            Assert.That(left, Is.EqualTo(right));
            Assert.That(right, Is.EqualTo(left));
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        /// <summary>
        /// Equals returns true when one ExtensionObject's TypeId is the encodeable's
        /// TypeId and the other's TypeId is the encodeable's XmlEncodingId.
        /// </summary>
        [Test]
        public void EqualsMatchesWhenTypeIdEqualsBodyXmlEncodingId()
        {
            var body = new TestEncodeable(42);
            var left = new ExtensionObject(body);
            var right = new ExtensionObject(body.XmlEncodingId, new TestEncodeable(42));
            Assert.That(left, Is.EqualTo(right));
            Assert.That(right, Is.EqualTo(left));
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        /// <summary>
        /// Equals returns true when the two ExtensionObjects use different encoding
        /// ids (Binary vs Xml) for the same encodeable body.
        /// </summary>
        [Test]
        public void EqualsMatchesAcrossEncodingIds()
        {
            var body = new TestEncodeable(7);
            var left = new ExtensionObject(body.BinaryEncodingId, new TestEncodeable(7));
            var right = new ExtensionObject(body.XmlEncodingId, new TestEncodeable(7));
            Assert.That(left, Is.EqualTo(right));
            Assert.That(right, Is.EqualTo(left));
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        /// <summary>
        /// Equals returns false when the TypeId match is satisfied but the body
        /// payloads differ.
        /// </summary>
        [Test]
        public void EqualsRejectsWhenTypeIdMatchesButBodiesDiffer()
        {
            var bodyA = new TestEncodeable(1);
            var bodyB = new TestEncodeable(2);
            var left = new ExtensionObject(bodyA);
            var right = new ExtensionObject(bodyA.BinaryEncodingId, bodyB);
            Assert.That(left, Is.Not.EqualTo(right));
            Assert.That(right, Is.Not.EqualTo(left));
        }

        /// <summary>
        /// Equals returns false when one side's TypeId is not any of the other
        /// body's three node ids, and the bodies themselves are of unrelated
        /// encodeable types.
        /// </summary>
        [Test]
        public void EqualsRejectsWhenTypeIdUnrelated()
        {
            var unrelatedTypeId = new ExpandedNodeId(999999);
            var left = new ExtensionObject(new TestEncodeable(42));
            var right = new ExtensionObject(unrelatedTypeId, new OtherEncodeable());
            Assert.That(left, Is.Not.EqualTo(right));
            Assert.That(right, Is.Not.EqualTo(left));
        }

        /// <summary>
        /// Equals returns false when one body is an encodeable and the other side
        /// wraps a completely different encodeable type, even if the wrapper TypeId
        /// happens to be unrelated.
        /// </summary>
        [Test]
        public void EqualsRejectsWhenEncodeableTypesDiffer()
        {
            var left = new ExtensionObject(new TestEncodeable(1));
            var right = new ExtensionObject(new OtherEncodeable());
            Assert.That(left, Is.Not.EqualTo(right));
            Assert.That(right, Is.Not.EqualTo(left));
        }

        /// <summary>
        /// Equals retains strict TypeId equality when the body is not an
        /// <see cref="IEncodeable"/>; encoding-id relaxation does not apply to
        /// ByteString, XmlElement, or string bodies.
        /// </summary>
        [Test]
        public void EqualsStrictWhenBodyIsNotEncodeable()
        {
            var encodeable = new TestEncodeable(42);
            ByteString bytes = [1, 2, 3];
            var binary = new ExtensionObject(encodeable.TypeId, bytes);
            var binaryDifferentId = new ExtensionObject(encodeable.BinaryEncodingId, bytes);
            Assert.That(binary, Is.Not.EqualTo(binaryDifferentId));

            const string json = "{\"value\":42}";
            var jsonExt = new ExtensionObject(encodeable.TypeId, json);
            var jsonDifferentId = new ExtensionObject(encodeable.XmlEncodingId, json);
            Assert.That(jsonExt, Is.Not.EqualTo(jsonDifferentId));
        }

        /// <summary>
        /// GetHashCode produces the same value for any wrapper TypeId in the
        /// inner encodeable's id set, matching the relaxed Equals semantics.
        /// </summary>
        [Test]
        public void GetHashCodeStableAcrossEncodingIds()
        {
            var body = new TestEncodeable(5);
            var byTypeId = new ExtensionObject(body);
            var byBinary = new ExtensionObject(body.BinaryEncodingId, new TestEncodeable(5));
            var byXml = new ExtensionObject(body.XmlEncodingId, new TestEncodeable(5));
            Assert.That(byBinary.GetHashCode(), Is.EqualTo(byTypeId.GetHashCode()));
            Assert.That(byXml.GetHashCode(), Is.EqualTo(byTypeId.GetHashCode()));
        }

        /// <summary>
        /// The == and != operators honour the relaxed TypeId equality.
        /// </summary>
        [Test]
        public void OperatorEqualsHonoursRelaxedTypeId()
        {
            var body = new TestEncodeable(11);
            var left = new ExtensionObject(body);
            var right = new ExtensionObject(body.XmlEncodingId, new TestEncodeable(11));
            Assert.That(left, Is.EqualTo(right));
            Assert.That(left, Is.EqualTo(right));
        }

        /// <summary>
        /// Equals is symmetric for the relaxed encoding-id cases.
        /// </summary>
        [Test]
        public void EqualsSymmetric()
        {
            var body = new TestEncodeable(99);
            var byTypeId = new ExtensionObject(body);
            var byBinary = new ExtensionObject(body.BinaryEncodingId, new TestEncodeable(99));
            var byXml = new ExtensionObject(body.XmlEncodingId, new TestEncodeable(99));
            Assert.That(byTypeId.Equals(byBinary), Is.EqualTo(byBinary.Equals(byTypeId)));
            Assert.That(byTypeId.Equals(byXml), Is.EqualTo(byXml.Equals(byTypeId)));
            Assert.That(byBinary.Equals(byXml), Is.EqualTo(byXml.Equals(byBinary)));
        }
    }
}
