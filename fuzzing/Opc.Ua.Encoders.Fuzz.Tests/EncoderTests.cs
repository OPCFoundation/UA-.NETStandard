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
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing
{
    [TestFixture]
    [Category("Fuzzing")]
    public class EncoderTests : FuzzTargetTestsBase
    {
        [DatapointSource]
        public static readonly FuzzTargetFunction[] FuzzableFunctions =
            CreateFuzzTargetFunctions(typeof(FuzzableCode));

        protected override Type FuzzableCodeType => typeof(FuzzableCode);

        [Test]
        public void MessageContextIsInitializedWithoutTestSetup()
        {
            ServiceMessageContext firstContext = FuzzableCode.MessageContext;
            ServiceMessageContext secondContext = FuzzableCode.MessageContext;

            Assert.That(firstContext, Is.Not.Null);
            Assert.That(firstContext, Is.SameAs(secondContext));
            Assert.That(firstContext.Factory, Is.Not.Null);
        }

        [Test]
        public void JsonSemanticOracleAllowsNodeNullCollectionCanonicalization()
        {
            var node = new VariableNode
            {
                RolePermissions = default,
                UserRolePermissions = default,
                References = default,
                ArrayDimensions = default
            };

            Assert.DoesNotThrow(
                () => FuzzableCode.FuzzJsonRoundTripCore(node, JsonEncoderOptions.Verbose));
        }

        [Test]
        public void JsonSemanticOracleAllowsNullQualifiedNameCanonicalization()
        {
            var node = new VariableNode
            {
                BrowseName = new QualifiedName(string.Empty)
            };

            Assert.That(node.BrowseName.IsNull, Is.True);
            Assert.That(node.BrowseName.Equals(QualifiedName.Null), Is.False);
            Assert.DoesNotThrow(
                () => FuzzableCode.FuzzJsonRoundTripCore(node, JsonEncoderOptions.Verbose));
        }

        [Test]
        public void JsonRoundTripAllowsUnencodableQualifiedNameRejection()
        {
            var request = new ReadRequest
            {
                NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(1000),
                        AttributeId = Attributes.Value,
                        DataEncoding = new QualifiedName(null, ushort.MaxValue)
                    }
                ]
            };

            Assert.DoesNotThrow(
                () => FuzzableCode.FuzzJsonRoundTripCore(request, JsonEncoderOptions.Verbose));
        }

        [Test]
        public void JsonRoundTripAllowsDroppedDataValuePicosecondsWithoutTimestamp()
        {
            // Picoseconds without their timestamp are ignored (Part 6 5.2.2.17) and dropped.
            var response = new ReadResponse
            {
                Results =
                [
                    new DataValue(
                        Variant.From("value"),
                        StatusCodes.Good,
                        DateTimeUtc.MinValue,
                        DateTimeUtc.MinValue,
                        sourcePicoseconds: 0,
                        serverPicoseconds: 1)
                ]
            };

            Assert.DoesNotThrow(
                () => FuzzableCode.FuzzJsonRoundTripCore(response, JsonEncoderOptions.Verbose));
        }

        [Test]
        public void BinaryJsonEncoderCrashAssetRejectsInvalidDiagnosticInfoIndex()
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Repo",
                "crash-b80bc430b8d713aaa78b02877f62c2aa8bb30dbc");
            byte[] input = File.ReadAllBytes(path);

            ServiceResultException ex;
            using (var stream = new MemoryStream(input, writable: false))
            {
                ex = Assert.Throws<ServiceResultException>(
                    () => FuzzableCode.FuzzBinaryDecoderCore(stream, throwAll: true));
            }

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain(nameof(DiagnosticInfo.NamespaceUri)));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzBinaryJsonEncoderCompact(input));
        }

        [TestCase("crash-0bdcbfafc13981ae6f665ad52cabc1cbe21b0238")]
        [TestCase("crash-20c4ef4a1203a261c7d5c467784d57c26e79de78")]
        [TestCase("crash-27c611759c3c9b10b426593594d2ebb7e849567d")]
        [TestCase("crash-9fb462b02468734a09dfa0e0611114c1491b90e6")]
        [TestCase("crash-d0b313b0eaf151f70b46c6a521e193449d05d349")]
        public void NonElementXmlBodyCrashAssetIsADecodingErrorWithoutARuntimeException(string asset)
        {
            // Nightly crash corpus inputs whose ns=0 ExtensionObject XML body is not an XML
            // element. The decoder dereferenced a null element and, since B1-2, reported the
            // NullReferenceException inside its BadDecodingError, which every binary target
            // treats as a contract violation.
            byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Repo", asset));

            ServiceResultException ex;
            using (var stream = new MemoryStream(input, writable: false))
            {
                ex = Assert.Throws<ServiceResultException>(
                    () => FuzzableCode.FuzzBinaryDecoderCore(stream, throwAll: true));
            }

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
            {
                Assert.That(inner, Is.InstanceOf<ServiceResultException>().Or.InstanceOf<System.Xml.XmlException>());
            }
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzBinaryDecoder(input));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzBinaryEncoderIndempotentSegmented(input));
        }

        [TestCase(typeof(ReadRequest))]
        [TestCase(typeof(ReadResponse))]
        [TestCase(typeof(WriteRequest))]
        [TestCase(typeof(PublishResponse))]
        [TestCase(typeof(ThreeDVector))]
        public void GeneratedProtocolCoverageMatchesTheBuildMode(Type type)
        {
#if OPCUA_FUZZING_COVERAGE
            const bool excluded = false;
#else
            const bool excluded = true;
#endif
            Assert.That(
                type.IsDefined(typeof(System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute), false),
                Is.EqualTo(excluded),
                "FuzzCoverage must affect the generated production types, not only the test assembly.");
        }
    }
}
