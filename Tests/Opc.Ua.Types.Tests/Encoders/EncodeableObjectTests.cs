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
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Unit tests for the <see cref="EncodeableObject"/> class.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EncodeableObjectTests
    {
        private static ServiceMessageContext CreateContext()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            return ServiceMessageContext.CreateEmpty(telemetryContext);
        }

        /// <summary>
        /// Concrete subclass of EncodeableObject for testing virtual methods.
        /// </summary>
        private sealed class TestEncodeableObject : EncodeableObject
        {
            public override ExpandedNodeId TypeId => new(200000);
            public override ExpandedNodeId BinaryEncodingId => new(200001);
            public override ExpandedNodeId XmlEncodingId => new(200002);
        }

        /// <summary>
        /// IEncodeable that throws during encoding, used to exercise the
        /// exception handler in ApplyDataEncoding.
        /// </summary>
        private sealed class ThrowingEncodeable : IEncodeable
        {
            public ExpandedNodeId TypeId => new(300000);
            public ExpandedNodeId BinaryEncodingId => new(300001);
            public ExpandedNodeId XmlEncodingId => new(300002);

            public void Encode(IEncoder encoder)
            {
                throw new InvalidOperationException("Test exception");
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return false;
            }

            public object Clone()
            {
                return new ThrowingEncodeable();
            }
        }

        /// <summary>
        /// Creates an Argument encodeable suitable for round-trip encoding tests.
        /// </summary>
        private static Argument CreateTestArgument(string name = "TestArg")
        {
            return new Argument(name, new NodeId(1), -1, "Test Description");
        }

        [Test]
        public void EncodeVirtualMethodDoesNotThrow()
        {
            // Arrange
            var obj = new TestEncodeableObject();
            var encoder = new Mock<IEncoder>();

            // Act & Assert — base implementation is empty
            Assert.DoesNotThrow(() => obj.Encode(encoder.Object));
        }

        [Test]
        public void DecodeVirtualMethodDoesNotThrow()
        {
            // Arrange
            var obj = new TestEncodeableObject();
            var decoder = new Mock<IDecoder>();

            // Act & Assert — base implementation is empty
            Assert.DoesNotThrow(() => obj.Decode(decoder.Object));
        }

        [Test]
        public void IsEqualThrowsNotImplementedException()
        {
            // Arrange
            var obj = new TestEncodeableObject();
            var other = new TestEncodeableObject();

            // Act & Assert
            NotImplementedException ex = Assert.Throws<NotImplementedException>(() => obj.IsEqual(other));
            Assert.That(ex.Message, Does.Contain("Subclass must implement"));
        }

        [Test]
        public void CloneReturnsShallowCopy()
        {
            // Arrange
            var original = new TestEncodeableObject();

            // Act
            object clone = original.Clone();

            // Assert
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone, Is.TypeOf<TestEncodeableObject>());
        }

        [Test]
        public void MemberwiseCloneReturnsNewInstance()
        {
            // Arrange
            var original = new TestEncodeableObject();

            // Act
            object clone = original.MemberwiseClone();

            // Assert
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone, Is.TypeOf<TestEncodeableObject>());
        }

        [Test]
        public void ApplyDataEncodingNullDataEncodingReturnsGood()
        {
            // Arrange — QualifiedName.Null triggers the early-return guard
            IServiceMessageContext context = CreateContext();
            var value = new Variant(42);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, QualifiedName.Null, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyDataEncodingNullValueReturnsGood()
        {
            // Arrange — a default Variant is null
            IServiceMessageContext context = CreateContext();
            Variant value = default;
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyDataEncodingNonZeroNamespaceReturnsBadDataEncodingUnsupported()
        {
            // Arrange — non-zero namespace index is unsupported
            IServiceMessageContext context = CreateContext();
            Argument arg = CreateTestArgument();
            var value = new Variant(new ExtensionObject(arg));
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary, 1);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDataEncodingUnsupported));
        }

        [Test]
        public void ApplyDataEncodingInvalidEncodingNameReturnsBadDataEncodingInvalid()
        {
            // Arrange — name is neither "Default XML" nor "Default Binary"
            IServiceMessageContext context = CreateContext();
            Argument arg = CreateTestArgument();
            var value = new Variant(new ExtensionObject(arg));
            var dataEncoding = new QualifiedName("InvalidEncoding");

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDataEncodingInvalid));
        }

        [Test]
        public void ApplyDataEncodingScalarBinaryReturnsGood()
        {
            // Arrange
            IServiceMessageContext context = CreateContext();
            Argument arg = CreateTestArgument();
            var value = new Variant(new ExtensionObject(arg));
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.TryGetValue(out ExtensionObject _), Is.True);
        }

        [Test]
        public void ApplyDataEncodingScalarXmlReturnsGood()
        {
            // Arrange
            IServiceMessageContext context = CreateContext();
            Argument arg = CreateTestArgument();
            var value = new Variant(new ExtensionObject(arg));
            var dataEncoding = new QualifiedName(BrowseNames.DefaultXml);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.TryGetValue(out ExtensionObject _), Is.True);
        }

        [Test]
        public void ApplyDataEncodingArrayBinaryReturnsGood()
        {
            // Arrange — array of valid encodeables with binary encoding
            IServiceMessageContext context = CreateContext();
            ArrayOf<ExtensionObject> extensions = new ExtensionObject[]
            {
                new(CreateTestArgument("Arg1")),
                new(CreateTestArgument("Arg2"))
            }.ToArrayOf();
            var value = new Variant(extensions);
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.TryGetValue(out ArrayOf<ExtensionObject> resultArray), Is.True);
            Assert.That(resultArray.Count, Is.EqualTo(2));
        }

        [Test]
        public void ApplyDataEncodingArrayXmlReturnsGood()
        {
            // Arrange — array path with XML encoding
            IServiceMessageContext context = CreateContext();
            ArrayOf<ExtensionObject> extensions = new ExtensionObject[]
            {
                new(CreateTestArgument())
            }.ToArrayOf();
            var value = new Variant(extensions);
            var dataEncoding = new QualifiedName(BrowseNames.DefaultXml);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ApplyDataEncodingArrayWithNullExtensionCausesException()
        {
            // Arrange — a null (IsNull=true) extension sets encodeables[i]=null,
            //   which then throws during encoding, exercising the catch block.
            IServiceMessageContext context = CreateContext();
            ArrayOf<ExtensionObject> extensions = new ExtensionObject[]
            {
                new() // IsNull = true
            }.ToArrayOf();
            var value = new Variant(extensions);
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert — NRE caught by the exception handler
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void ApplyDataEncodingArrayWithNonEncodeableReturnsBadTypeMismatch()
        {
            // Arrange — extension with ByteString body is not an IEncodeable
            IServiceMessageContext context = CreateContext();
            var nonEncodeableExt = new ExtensionObject(
                new ExpandedNodeId(999),
                ByteString.From(new byte[] { 1, 2, 3 }));
            ArrayOf<ExtensionObject> extensions = new ExtensionObject[] { nonEncodeableExt }.ToArrayOf();
            var value = new Variant(extensions);
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void ApplyDataEncodingNonExtensionObjectReturnsBadDataEncodingUnsupported()
        {
            // Arrange — an int variant is neither array-of nor scalar ExtensionObject
            IServiceMessageContext context = CreateContext();
            var value = new Variant(42);
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDataEncodingUnsupported));
        }

        [Test]
        public void ApplyDataEncodingScalarExceptionReturnsBadTypeMismatch()
        {
            // Arrange — ThrowingEncodeable throws from Encode(), exercising
            //   the catch block via the scalar path.
            IServiceMessageContext context = CreateContext();
            var value = new Variant(new ExtensionObject(new ThrowingEncodeable()));
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary);

            // Act
            ServiceResult result = EncodeableObject.ApplyDataEncoding(context, dataEncoding, ref value);

            // Assert
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void EncodeStaticWithXmlReturnsXmlExtensionObject()
        {
            // Arrange
            IServiceMessageContext context = CreateContext();
            Argument arg = CreateTestArgument();

            // Act
            ExtensionObject result = EncodeableObject.Encode(context, arg, useXml: true);

            // Assert
            Assert.That(result.IsNull, Is.False);
        }

        [Test]
        public void EncodeStaticWithBinaryReturnsBinaryExtensionObject()
        {
            // Arrange
            IServiceMessageContext context = CreateContext();
            Argument arg = CreateTestArgument();

            // Act
            ExtensionObject result = EncodeableObject.Encode(context, arg, useXml: false);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.IsNull, Is.False);
        }

        [Test]
        public void EncodeXmlReturnsValidXmlElement()
        {
            // Arrange
            IServiceMessageContext context = CreateContext();
            Argument arg = CreateTestArgument();

            // Act
            XmlElement result = EncodeableObject.EncodeXml(arg, context);

            // Assert
            Assert.That(result.IsNull, Is.False);
        }

        [Test]
        public void EncodeBinaryReturnsValidByteString()
        {
            // Arrange
            IServiceMessageContext context = CreateContext();
            Argument arg = CreateTestArgument();

            // Act
            ByteString result = EncodeableObject.EncodeBinary(arg, context);

            // Assert
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.Length, Is.GreaterThan(0));
        }
    }
}
