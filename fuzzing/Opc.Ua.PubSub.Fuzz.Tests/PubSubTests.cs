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
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Retained PubSub callbacks participate in the standard corpus replay harness.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class PubSubTests : FuzzTargetTestsBase
    {
        [DatapointSource]
        public static readonly FuzzTargetFunction[] FuzzableFunctions =
            CreateFuzzTargetFunctions(typeof(FuzzableCode));

        protected override Type FuzzableCodeType => typeof(FuzzableCode);

        [Test]
        public void PublicSurfaceContainsExactlySixCallbacksAndFuzzInfo()
        {
            string[] streamNames =
            [
                "AflfuzzPubSubJsonDecode",
                "AflfuzzUadpNetworkMessageDecode",
                "AflfuzzUadpChunkReassembly"
            ];
            string[] spanNames =
            [
                "LibfuzzPubSubJsonDecode",
                "LibfuzzUadpNetworkMessageDecode",
                "LibfuzzUadpChunkReassembly"
            ];
            string[] callbackNames = [.. streamNames, .. spanNames];
            string[] publicNames = ["FuzzInfo", .. callbackNames];
            MethodInfo[] methods = typeof(FuzzableCode).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

            Assert.That(
                methods.Select(static method => method.Name),
                Is.EquivalentTo(publicNames));
            foreach (MethodInfo method in methods)
            {
                Assert.That(method.ReturnType, Is.EqualTo(typeof(void)), method.Name);
                Assert.That(method.ContainsGenericParameters, Is.False, method.Name);
                Type[] parameters = [.. method.GetParameters().Select(static parameter => parameter.ParameterType)];
                if (method.Name == "FuzzInfo")
                {
                    Assert.That(parameters, Is.Empty);
                }
                else
                {
                    Type parameterType = streamNames.Contains(method.Name)
                        ? typeof(Stream) : typeof(ReadOnlySpan<byte>);
                    Assert.That(parameters, Is.EqualTo(new[] { parameterType }), method.Name);
                }
            }

            Assert.That(FuzzableFunctions, Has.Length.EqualTo(6));
            Assert.That(
                FuzzableFunctions.Select(static target => target.MethodInfo.Name),
                Is.EquivalentTo(callbackNames));
            Assert.That(
                FuzzMethods.FindFuzzMethods(typeof(FuzzMethods.AflFuzzStream))
                    .Select(static callback => callback.Method.Name),
                Is.EquivalentTo(streamNames));
            Assert.That(
                FuzzMethods.FindFuzzMethods(typeof(FuzzMethods.LibFuzzSpan))
                    .Select(static callback => callback.Method.Name),
                Is.EquivalentTo(spanNames));
            Assert.That(FuzzMethods.FindFuzzMethods(typeof(FuzzMethods.AflFuzzString)), Is.Empty);
        }

        [Test]
        public void ContextUsesFixedClockAndRegistersBothWireIdentities()
        {
            var expectedTime = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
            PubSubNetworkMessageContext context = FuzzableCode.NewContext();

            Assert.That(context.TimeProvider.GetUtcNow(), Is.EqualTo(expectedTime));
            Assert.That(context.Diagnostics.Level, Is.EqualTo(PubSubDiagnosticsLevel.High));
            Assert.That(context.MetaDataRegistry.Keys.Count, Is.EqualTo(2));
            string[] names = ["Running", "Count", "Temperature", "Label"];
            BuiltInType[] types = [BuiltInType.Boolean, BuiltInType.Int32, BuiltInType.Double, BuiltInType.String];
            NodeId[] dataTypes = [DataTypeIds.Boolean, DataTypeIds.Int32, DataTypeIds.Double, DataTypeIds.String];
            foreach (ushort writerGroupId in new ushort[] { 0, 1 })
            {
                var key = new DataSetMetaDataKey(
                    PublisherId.FromUInt16(300),
                    writerGroupId,
                    1,
                    new Uuid("aabbccdd-1122-3344-5566-778899aabbcc"),
                    1);
                Assert.That(
                    context.MetaDataRegistry.TryGet(key, out DataSetMetaDataType metadata),
                    Is.EqualTo(MetaDataMatchResult.Match));
                Assert.That(metadata.Name, Is.EqualTo("RetainedFuzzDataSet"));
                Assert.That(metadata.DataSetClassId, Is.EqualTo(key.DataSetClassId));
                Assert.That(metadata.ConfigurationVersion.MajorVersion, Is.EqualTo(1));
                Assert.That(metadata.ConfigurationVersion.MinorVersion, Is.EqualTo(2));
                Assert.That(metadata.Fields.Count, Is.EqualTo(4));
                for (int i = 0; i < metadata.Fields.Count; i++)
                {
                    Assert.That(metadata.Fields[i].Name, Is.EqualTo(names[i]));
                    Assert.That(metadata.Fields[i].BuiltInType, Is.EqualTo((byte)types[i]));
                    Assert.That(metadata.Fields[i].DataType, Is.EqualTo(dataTypes[i]));
                    Assert.That(metadata.Fields[i].ValueRank, Is.EqualTo(ValueRanks.Scalar));
                }
            }

            var clock = new FakeTimeProvider(expectedTime);
            PubSubNetworkMessageContext other = FuzzableCode.NewContext(clock);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(other.TimeProvider, Is.SameAs(clock));
            Assert.That(other.TimeProvider.GetUtcNow(), Is.EqualTo(expectedTime.AddSeconds(1)));
            Assert.That(context.TimeProvider.GetUtcNow(), Is.EqualTo(expectedTime));
            other.Diagnostics.Increment(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
            Assert.That(context.Diagnostics.Read(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages), Is.Zero);
        }

        [TestCase(0)]
        [TestCase(4097)]
        [TestCase(1048576)]
        [TestCase(1048577)]
        public void SpanAndShortReadStreamKeepOnlyTheOneMiBPrefix(int inputLength)
        {
            byte[] input = new byte[inputLength];
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = (byte)((i % 251) + 1);
            }
            int expectedLength = Math.Min(inputLength, 1024 * 1024);
            byte[] expected = input.AsSpan(0, expectedLength).ToArray();
            using var stream = new ShortReadStream(input);

            byte[] copied = FuzzableCode.CopyCapped(input);
            byte[] read = FuzzableCode.ReadCapped(stream);

            Assert.That(copied, Is.EqualTo(expected));
            Assert.That(read, Is.EqualTo(expected));
            Assert.That(stream.Position, Is.EqualTo(expectedLength));
            Assert.That(stream.CanRead, Is.True, "The caller owns the input stream.");
            if (inputLength > 0)
            {
                Assert.That(copied, Is.Not.SameAs(input));
                Assert.That(stream.ReadCalls, Is.GreaterThan(1));
            }
            if (inputLength > expectedLength)
            {
                Assert.That(stream.ReadByte(), Is.EqualTo(input[expectedLength]));
            }
        }

        private sealed class ShortReadStream(byte[] input) : MemoryStream(input, writable: false)
        {
            public int ReadCalls { get; private set; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ReadCalls++;
                return base.Read(buffer, offset, Math.Min(count, 67));
            }
        }
    }
}
