/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that the response size estimates are lower bounds of the binary encoding,
    /// so that a response byte budget equal to the message size limit refuses only
    /// responses the encoder refuses as well.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class ResponseSizeEstimatorTests
    {
        // the arrays are test data, created once per test case.
#pragma warning disable CA1861
        private static IEnumerable<TestCaseData> Values()
        {
            yield return new TestCaseData(Variant.Null).SetName("Null");
            yield return new TestCaseData(new Variant(true)).SetName("Boolean");
            yield return new TestCaseData(new Variant((sbyte)-1)).SetName("SByte");
            yield return new TestCaseData(new Variant((byte)1)).SetName("Byte");
            yield return new TestCaseData(new Variant((short)-1)).SetName("Int16");
            yield return new TestCaseData(new Variant((ushort)1)).SetName("UInt16");
            yield return new TestCaseData(new Variant(-1)).SetName("Int32");
            yield return new TestCaseData(new Variant(1u)).SetName("UInt32");
            yield return new TestCaseData(new Variant(-1L)).SetName("Int64");
            yield return new TestCaseData(new Variant(1ul)).SetName("UInt64");
            yield return new TestCaseData(new Variant(1.5f)).SetName("Float");
            yield return new TestCaseData(new Variant(1.5)).SetName("Double");
            yield return new TestCaseData(new Variant(DateTimeUtc.Now)).SetName("DateTime");
            yield return new TestCaseData(new Variant(Uuid.NewUuid())).SetName("Guid");
            yield return new TestCaseData(new Variant((StatusCode)StatusCodes.BadNodeIdUnknown)).SetName("StatusCode");
            yield return new TestCaseData(new Variant("Ünïcode text")).SetName("String");
            yield return new TestCaseData(new Variant(new ByteString(new byte[300]))).SetName("ByteString");
            yield return new TestCaseData(new Variant(new NodeId("node", 2))).SetName("NodeId");
            yield return new TestCaseData(new Variant(new QualifiedName("name", 1))).SetName("QualifiedName");
            yield return new TestCaseData(new Variant((ArrayOf<bool>)new[] { true, false })).SetName("BooleanArray");
            yield return new TestCaseData(new Variant((ArrayOf<sbyte>)new sbyte[] { 1, 2 })).SetName("SByteArray");
            yield return new TestCaseData(new Variant((ArrayOf<byte>)new byte[] { 1, 2, 3 })).SetName("ByteArray");
            yield return new TestCaseData(new Variant((ArrayOf<short>)new short[] { 1, 2 })).SetName("Int16Array");
            yield return new TestCaseData(new Variant((ArrayOf<ushort>)new ushort[] { 1, 2 })).SetName("UInt16Array");
            yield return new TestCaseData(new Variant((ArrayOf<int>)new[] { 1, 2 })).SetName("Int32Array");
            yield return new TestCaseData(new Variant((ArrayOf<uint>)new uint[1000])).SetName("UInt32Array");
            yield return new TestCaseData(new Variant((ArrayOf<long>)new long[] { 1, 2 })).SetName("Int64Array");
            yield return new TestCaseData(new Variant((ArrayOf<ulong>)new ulong[] { 1, 2 })).SetName("UInt64Array");
            yield return new TestCaseData(new Variant((ArrayOf<float>)new[] { 1f, 2f })).SetName("FloatArray");
            yield return new TestCaseData(new Variant((ArrayOf<double>)new[] { 1d, 2d })).SetName("DoubleArray");
            yield return new TestCaseData(new Variant((ArrayOf<DateTimeUtc>)new[] { DateTimeUtc.Now })).SetName("DateTimeArray");
            yield return new TestCaseData(new Variant((ArrayOf<Uuid>)new[] { Uuid.NewUuid() })).SetName("GuidArray");
            yield return new TestCaseData(new Variant((ArrayOf<StatusCode>)new StatusCode[] { StatusCodes.Good })).SetName("StatusCodeArray");
            yield return new TestCaseData(new Variant((ArrayOf<string>)new[] { "a", null!, "ccc" })).SetName("StringArray");
            yield return new TestCaseData(new Variant((ArrayOf<ByteString>)new[] { new ByteString(new byte[10]), default })).SetName("ByteStringArray");
            yield return new TestCaseData(new Variant((ArrayOf<Variant>)new[] { new Variant(1), new Variant("x") })).SetName("VariantArray");
            yield return new TestCaseData(new Variant((ArrayOf<NodeId>)new[] { new NodeId(1u), new NodeId("n", 3) })).SetName("NodeIdArray");
            yield return new TestCaseData(new Variant((ArrayOf<QualifiedName>)new[] { new QualifiedName("q") })).SetName("QualifiedNameArray");
        }
#pragma warning restore CA1861

        [TestCaseSource(nameof(Values))]
        public void VariantEstimateIsAtMostTheBinaryEncoding(Variant value)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(context);
            encoder.WriteVariant(null, value);
            int encoded = encoder.Close();

            long estimate = ResponseSizeEstimator.Estimate(value);

            Assert.That(estimate, Is.GreaterThan(0));
            Assert.That(estimate, Is.LessThanOrEqualTo(encoded));
        }

        [Test]
        public void CallResultEstimateIsAtMostTheBinaryEncoding()
        {
            ArrayOf<Variant> outputs = new[] { new Variant((ArrayOf<uint>)new uint[64]), new Variant("text") };
            var result = new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = outputs };

            Assert.That(ResponseSizeEstimator.Estimate(outputs), Is.LessThanOrEqualTo(Encode(result)));
        }

        [Test]
        public void BrowseResultEstimateIsAtMostTheBinaryEncoding()
        {
            ArrayOf<ReferenceDescription> references = new[]
            {
                new ReferenceDescription
                {
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IsForward = true,
                    NodeId = new NodeId("Child", 2),
                    BrowseName = new QualifiedName("Child", 2),
                    DisplayName = new LocalizedText("en", "Child node"),
                    NodeClass = NodeClass.Object,
                    TypeDefinition = ObjectTypeIds.BaseObjectType
                },
                new ReferenceDescription()
            };
            var result = new BrowseResult { StatusCode = StatusCodes.Good, References = references };

            Assert.That(ResponseSizeEstimator.Estimate(references), Is.LessThanOrEqualTo(Encode(result)));
            Assert.That(ResponseSizeEstimator.Estimate((ReferenceDescription?)null),
                Is.EqualTo(ResponseSizeEstimator.MinReferenceDescriptionSize));
        }

        [Test]
        public void FileOpenQuotaCountsPerSessionAndInTotal()
        {
            var quota = new FileOpenQuota { MaxOpenFilesPerSession = 2, MaxOpenFiles = 3 };
            var first = new NodeId(1u);
            var second = new NodeId(2u);

            Assert.That(quota.TryAcquire(first, out _), Is.True);
            Assert.That(quota.TryAcquire(first, out _), Is.True);
            Assert.That(quota.TryAcquire(first, out ServiceResult perSession), Is.False);
            Assert.That(quota.TryAcquire(second, out _), Is.True);
            Assert.That(quota.TryAcquire(second, out ServiceResult total), Is.False);
            Assert.That(quota.OpenCount, Is.EqualTo(3));
            Assert.That(perSession.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(total.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));

            quota.Release(first);
            quota.Release(new NodeId(3u));
            Assert.That(quota.OpenCount, Is.EqualTo(2));
            Assert.That(quota.TryAcquire(second, out _), Is.True);

            quota.MaxOpenFiles = 0;
            quota.MaxOpenFilesPerSession = 0;
            Assert.That(quota.TryAcquire(second, out _), Is.True);
            Assert.That(() => quota.MaxOpenFiles = -1, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => quota.MaxOpenFilesPerSession = -1, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static int Encode(IEncodeable value)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(context);
            encoder.WriteEncodeable(null, value, value.TypeId);
            return encoder.Close();
        }
    }
}
