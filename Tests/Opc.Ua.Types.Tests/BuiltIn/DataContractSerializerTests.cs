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

using System.IO;
using System.Runtime.Serialization;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;

using StatusCodeConstants = Opc.Ua.Types.StatusCodes;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class DataContractSerializerTests
    {
        [Test]
        public void SerializerSupportsScalarSurrogates()
        {
            SurrogateScalarContract instance = SurrogateTestData.CreateScalarContract(0);

            SurrogateScalarContract clone = Roundtrip(instance);

            SurrogateTestData.AssertScalarContractEqual(instance, clone);
        }

        [Test]
        public void SerializerSupportsArrayOfSurrogates()
        {
            SurrogateArrayOfContract instance = SurrogateTestData.CreateArrayOfContract(0);

            SurrogateArrayOfContract clone = Roundtrip(instance);

            SurrogateTestData.AssertArrayOfContractEqual(instance, clone);
        }

        [Test]
        public void SerializerSupportsMatrixOfSurrogates()
        {
            SurrogateMatrixOfContract instance = SurrogateTestData.CreateMatrixOfContract(0);

            SurrogateMatrixOfContract clone = Roundtrip(instance);

            SurrogateTestData.AssertMatrixOfContractEqual(instance, clone);
        }

        [Test]
        public void SerializerSupportsNestedSurrogateGraph()
        {
            SurrogateGraphContract instance = SurrogateTestData.CreateGraphContract();

            SurrogateGraphContract clone = Roundtrip(instance);

            SurrogateTestData.AssertGraphContractEqual(instance, clone);
        }

        [Test]
        public void SerializerCorrectlySerializesArrayOfArguments()
        {
            ArrayOf<Argument> instance = [new Argument(), new Argument()];
            string serializedData = GetSerializedData(instance);
            TestContext.Out.WriteLine(serializedData);
            Assert.That(serializedData, Does.StartWith(
                "<ListOfArgument xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\"><Argument xmlns"));
        }

        [Test]
        public void SerializerCorrectlySerializesArrayOfUInt32()
        {
            ArrayOf<uint> instance = [0u, 1u, 2u];
            string serializedData = GetSerializedData(instance);
            TestContext.Out.WriteLine(serializedData);
            Assert.That(serializedData, Does.StartWith(
                "<ListOfUInt32 xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\"><UInt32>0</UInt32>"));
        }

        private static string GetSerializedData<T>(T instance)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);

            DataContractSerializer serializer = CoreUtils.CreateDataContractSerializer<T>(context);
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, instance);

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static T Roundtrip<T>(T value)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);

            DataContractSerializer serializer = CoreUtils.CreateDataContractSerializer<T>(context);

            using var stream = new MemoryStream();
            serializer.WriteObject(stream, value);
            stream.Position = 0;
            return (T)serializer.ReadObject(stream);
        }
    }

    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public sealed class SurrogateScalarContract
    {
        [DataMember(Order = 1)]
        public NodeId NodeId { get; set; }

        [DataMember(Order = 2)]
        public ExpandedNodeId ExpandedNodeId { get; set; }

        [DataMember(Order = 3)]
        public Uuid Uuid { get; set; }

        [DataMember(Order = 4)]
        public StatusCode StatusCode { get; set; }

        [DataMember(Order = 5)]
        public QualifiedName QualifiedName { get; set; }

        [DataMember(Order = 6)]
        public Variant Variant { get; set; }

        [DataMember(Order = 7)]
        public LocalizedText LocalizedText { get; set; }

        [DataMember(Order = 8)]
        public ByteString ByteString { get; set; }

        [DataMember(Order = 9)]
        public DateTimeUtc DateTime { get; set; }
    }

    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public sealed class SurrogateArrayOfContract
    {
        [DataMember(Order = 1)]
        public ArrayOf<NodeId> NodeIds { get; set; }

        [DataMember(Order = 2)]
        public ArrayOf<ExpandedNodeId> ExpandedNodeIds { get; set; }

        [DataMember(Order = 3)]
        public ArrayOf<Uuid> Uuids { get; set; }

        [DataMember(Order = 4)]
        public ArrayOf<StatusCode> StatusCodes { get; set; }

        [DataMember(Order = 5)]
        public ArrayOf<QualifiedName> QualifiedNames { get; set; }

        [DataMember(Order = 6)]
        public ArrayOf<Variant> Variants { get; set; }

        [DataMember(Order = 7)]
        public ArrayOf<LocalizedText> LocalizedTexts { get; set; }

        [DataMember(Order = 8)]
        public ArrayOf<ByteString> ByteStrings { get; set; }

        [DataMember(Order = 9)]
        public ArrayOf<DateTimeUtc> DateTimes { get; set; }
    }

    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public sealed class SurrogateMatrixOfContract
    {
        [DataMember(Order = 1)]
        public MatrixOf<NodeId> NodeIds { get; set; }

        [DataMember(Order = 2)]
        public MatrixOf<ExpandedNodeId> ExpandedNodeIds { get; set; }

        [DataMember(Order = 3)]
        public MatrixOf<Uuid> Uuids { get; set; }

        [DataMember(Order = 4)]
        public MatrixOf<StatusCode> StatusCodes { get; set; }

        [DataMember(Order = 5)]
        public MatrixOf<QualifiedName> QualifiedNames { get; set; }

        [DataMember(Order = 6)]
        public MatrixOf<Variant> Variants { get; set; }

        [DataMember(Order = 7)]
        public MatrixOf<LocalizedText> LocalizedTexts { get; set; }

        [DataMember(Order = 8)]
        public MatrixOf<ByteString> ByteStrings { get; set; }

        [DataMember(Order = 9)]
        public MatrixOf<DateTimeUtc> DateTimes { get; set; }
    }

    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public sealed class SurrogateGraphContract
    {
        [DataMember(Order = 1)]
        public SurrogateScalarContract Scalars { get; set; }

        [DataMember(Order = 2)]
        public SurrogateArrayOfContract Arrays { get; set; }

        [DataMember(Order = 3)]
        public SurrogateMatrixOfContract Matrices { get; set; }

        [DataMember(Order = 4)]
        public SurrogateScalarContract[] AdditionalScalars { get; set; }

        [DataMember(Order = 5)]
        public SurrogateMatrixOfContract[] AdditionalMatrices { get; set; }

        [DataMember(Order = 6)]
        public SurrogateArrayOfContract[] AdditionalArrays { get; set; }
    }

    internal static class SurrogateTestData
    {
        public static SurrogateScalarContract CreateScalarContract(int index)
        {
            return new SurrogateScalarContract
            {
                NodeId = new NodeId((uint)(1000 + index), (ushort)(1 + index)),
                ExpandedNodeId = new ExpandedNodeId(
                    (uint)(2000 + index),
                    (ushort)(2 + index),
                    Namespaces.OpcUa,
                    (uint)(index + 1)),
                Uuid = Uuid.NewUuid(),
                StatusCode = StatusCodeConstants.BadUnexpectedError,
                QualifiedName = new QualifiedName($"qn-{index}", (ushort)(index + 1)),
                Variant = new Variant(index),
                LocalizedText = new LocalizedText("en-US", $"text-{index}"),
                ByteString = ByteString.From((byte)index, (byte)(index + 1), (byte)(index + 2)),
                DateTime = DateTimeUtc.Now
            };
        }

        public static SurrogateArrayOfContract CreateArrayOfContract(int index)
        {
            return new SurrogateArrayOfContract
            {
                NodeIds =
                [
                    new NodeId((uint)(10 + index), 1),
                    new NodeId($"node-{index}", 2)
                ],
                ExpandedNodeIds =
                [
                    new ExpandedNodeId((uint)(20 + index), 3, Namespaces.OpcUa, (uint)(index + 1))
                ],
                Uuids =
                [
                    Uuid.NewUuid(),
                    Uuid.NewUuid()
                ],
                StatusCodes =
                [
                    StatusCodeConstants.Good,
                    StatusCodeConstants.BadEncodingError
                ],
                QualifiedNames =
                [
                    new QualifiedName($"coll-qn-{index}", 4)
                ],
                Variants =
                [
                    new Variant($"variant-{index}"),
                    new Variant(index + 42)
                ],
                LocalizedTexts =
                [
                    new LocalizedText("en-US", $"localized-{index}"),
                    new LocalizedText("de-DE", $"lokalisiert-{index}")
                ],
                ByteStrings =
                [
                    ByteString.From((byte)index, (byte)(index + 1), (byte)(index + 2)),
                    ByteString.From((byte)index, (byte)(index + 2), (byte)(index + 3))
                ],
                DateTimes =
                [
                    DateTimeUtc.Now,
                    DateTimeUtc.Now
                ]
            };
        }

        public static SurrogateMatrixOfContract CreateMatrixOfContract(int index)
        {
            return new SurrogateMatrixOfContract
            {
                NodeIds =
                ArrayOf.Wrapped([
                    new NodeId((uint)(10 + index), 1),
                    new NodeId((uint)(20 + index), 1),
                    new NodeId((uint)(30 + index), 1),
                    new NodeId($"node-{index}", 2)
                ]).ToMatrix(2, 2),
                ExpandedNodeIds =
                ArrayOf.Wrapped([
                    new ExpandedNodeId((uint)(20 + index), 3, Namespaces.OpcUa, (uint)(index + 1)),
                    new ExpandedNodeId((uint)(30 + index), 3, Namespaces.OpcUa, (uint)(index + 2)),
                    new ExpandedNodeId((uint)(40 + index), 3, Namespaces.OpcUa, (uint)(index + 3)),
                    new ExpandedNodeId((uint)(50 + index), 3, Namespaces.OpcUa, (uint)(index + 4))
                ]).ToMatrix(2, 2),
                Uuids =
                ArrayOf.Wrapped([
                    Uuid.NewUuid(),
                    Uuid.NewUuid(),
                    Uuid.NewUuid(),
                    Uuid.NewUuid()
                ]).ToMatrix(2, 2),
                StatusCodes =
                ArrayOf.Wrapped([
                    StatusCodeConstants.Good,
                    StatusCodeConstants.BadArgumentsMissing,
                    StatusCodeConstants.Good,
                    StatusCodeConstants.BadEncodingError
                ]).ToMatrix(2, 2),
                QualifiedNames =
                ArrayOf.Wrapped([
                    new QualifiedName($"coll-qn-{index}", 4),
                    new QualifiedName($"coll-qn-{index + 1}", 4),
                    new QualifiedName($"coll-qn-{index + 2}", 4),
                    new QualifiedName($"coll-qn-{index + 3}", 4)
                ]).ToMatrix(2, 2),
                Variants =
                ArrayOf.Wrapped([
                    new Variant($"variant-{index+ 1}"),
                    new Variant(index + 0.6f),
                    new Variant($"variant-{index}"),
                    new Variant(index + 42)
                ]).ToMatrix(2, 2),
                LocalizedTexts =
                ArrayOf.Wrapped([
                    new LocalizedText("en-US", $"localized-{index}"),
                    new LocalizedText("en-US", $"localized-{index}"),
                    new LocalizedText("en-US", $"localized-{index}"),
                    new LocalizedText("de-DE", $"lokalisiert-{index}")
                ]).ToMatrix(2, 2),
                ByteStrings =
                ArrayOf.Wrapped([
                    ByteString.From((byte)index, (byte)(index + 1), (byte)(index + 2)),
                    ByteString.From((byte)index, (byte)(index + 2), (byte)(index + 4)),
                    ByteString.From((byte)index, (byte)(index + 1), (byte)(index + 2)),
                    ByteString.From((byte)index, (byte)(index + 2), (byte)(index + 3))
                ]).ToMatrix(2, 2),
                DateTimes =
                ArrayOf.Wrapped([
                    DateTimeUtc.Now,
                    DateTimeUtc.Now,
                    DateTimeUtc.Now,
                    DateTimeUtc.Now
                ]).ToMatrix(2, 2)
            };
        }

        public static SurrogateGraphContract CreateGraphContract()
        {
            return new SurrogateGraphContract
            {
                Scalars = CreateScalarContract(0),
                Arrays = CreateArrayOfContract(0),
                Matrices = CreateMatrixOfContract(0),
                AdditionalScalars =
                [
                    CreateScalarContract(1),
                    CreateScalarContract(2)
                ],
                AdditionalArrays =
                [
                    CreateArrayOfContract(1)
                ],
                AdditionalMatrices =
                [
                    CreateMatrixOfContract(1)
                ]
            };
        }

        public static void AssertScalarContractEqual(
            SurrogateScalarContract expected,
            SurrogateScalarContract actual)
        {
            Assert.That(actual, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(CoreUtils.IsEqual(expected.NodeId, actual.NodeId), Is.True, "NodeId mismatch");
                Assert.That(
                    CoreUtils.IsEqual(expected.ExpandedNodeId, actual.ExpandedNodeId),
                    Is.True,
                    "ExpandedNodeId mismatch");
                Assert.That(actual.Uuid, Is.EqualTo(expected.Uuid), "Uuid mismatch");
                Assert.That(actual.StatusCode, Is.EqualTo(expected.StatusCode), "StatusCode mismatch");
                Assert.That(
                    CoreUtils.IsEqual(expected.QualifiedName, actual.QualifiedName),
                    Is.True,
                    "QualifiedName mismatch");
                Assert.That(CoreUtils.IsEqual(expected.Variant, actual.Variant), Is.True, "Variant mismatch");
                Assert.That(
                    CoreUtils.IsEqual(expected.LocalizedText, actual.LocalizedText),
                    Is.True,
                    "LocalizedText mismatch");
            }
        }

        public static void AssertArrayOfContractEqual(
            SurrogateArrayOfContract expected,
            SurrogateArrayOfContract actual)
        {
            Assert.That(actual, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(actual.NodeIds, Is.EqualTo(expected.NodeIds),
                    "NodeIds mismatch");
                Assert.That(actual.ExpandedNodeIds, Is.EqualTo(expected.ExpandedNodeIds),
                    "ExpandedNodeIds mismatch");
                Assert.That(actual.Uuids, Is.EqualTo(expected.Uuids),
                    "Uuids mismatch");
                Assert.That(actual.StatusCodes, Is.EqualTo(expected.StatusCodes),
                    "StatusCodes mismatch");
                Assert.That(actual.QualifiedNames, Is.EqualTo(expected.QualifiedNames),
                    "QualifiedNames mismatch");
                Assert.That(actual.Variants, Is.EqualTo(expected.Variants),
                    "Variants mismatch");
                Assert.That(actual.LocalizedTexts, Is.EqualTo(expected.LocalizedTexts),
                    "LocalizedTexts mismatch");
            }
        }

        public static void AssertMatrixOfContractEqual(
            SurrogateMatrixOfContract expected,
            SurrogateMatrixOfContract actual)
        {
            Assert.That(actual, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(actual.NodeIds, Is.EqualTo(expected.NodeIds),
                    "NodeIds mismatch");
                Assert.That(actual.ExpandedNodeIds, Is.EqualTo(expected.ExpandedNodeIds),
                    "ExpandedNodeIds mismatch");
                Assert.That(actual.Uuids, Is.EqualTo(expected.Uuids),
                    "Uuids mismatch");
                Assert.That(actual.StatusCodes, Is.EqualTo(expected.StatusCodes),
                    "StatusCodes mismatch");
                Assert.That(actual.QualifiedNames, Is.EqualTo(expected.QualifiedNames),
                    "QualifiedNames mismatch");
                Assert.That(actual.Variants, Is.EqualTo(expected.Variants),
                    "Variants mismatch");
                Assert.That(actual.LocalizedTexts, Is.EqualTo(expected.LocalizedTexts),
                    "LocalizedTexts mismatch");
            }
        }

        public static void AssertGraphContractEqual(
            SurrogateGraphContract expected,
            SurrogateGraphContract actual)
        {
            Assert.That(actual, Is.Not.Null);

            AssertScalarContractEqual(expected.Scalars, actual.Scalars);
            AssertArrayOfContractEqual(expected.Arrays, actual.Arrays);
            AssertMatrixOfContractEqual(expected.Matrices, actual.Matrices);

            Assert.That(
                actual.AdditionalScalars.Length,
                Is.EqualTo(expected.AdditionalScalars.Length),
                "AdditionalScalars length mismatch");

            for (int ii = 0; ii < expected.AdditionalScalars.Length; ii++)
            {
                AssertScalarContractEqual(expected.AdditionalScalars[ii], actual.AdditionalScalars[ii]);
            }

            Assert.That(
                actual.AdditionalArrays.Length,
                Is.EqualTo(expected.AdditionalArrays.Length),
                "AdditionalArrays length mismatch");

            for (int ii = 0; ii < expected.AdditionalArrays.Length; ii++)
            {
                AssertArrayOfContractEqual(
                    expected.AdditionalArrays[ii],
                    actual.AdditionalArrays[ii]);
            }

            Assert.That(
                actual.AdditionalMatrices.Length,
                Is.EqualTo(expected.AdditionalMatrices.Length),
                "AdditionalMatrices length mismatch");

            for (int ii = 0; ii < expected.AdditionalMatrices.Length; ii++)
            {
                AssertMatrixOfContractEqual(
                    expected.AdditionalMatrices[ii],
                    actual.AdditionalMatrices[ii]);
            }
        }
    }
}
