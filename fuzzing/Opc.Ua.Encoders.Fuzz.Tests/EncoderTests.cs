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
        public void JsonRoundTripAllowsUnencodableDataValuePicosecondsRejection()
        {
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
