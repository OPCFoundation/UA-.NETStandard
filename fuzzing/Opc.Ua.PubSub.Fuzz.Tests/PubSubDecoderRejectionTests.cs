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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using PubSubJsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Soft rejections are observed at the production core and both retained adapters.
    /// No test catches unexpected decoder or oracle failures.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class PubSubDecoderRejectionTests
    {
        [TestCase("", 0)]
        [TestCase("{", 0)]
        [TestCase("null", 0)]
        [TestCase("42", 0)]
        [TestCase("[42,true]", 1)]
        [TestCase("{\"MessageId\":\"missing-type\",\"PublisherId\":\"300\"}", 0)]
        [TestCase("{\"MessageType\":42,\"Messages\":[]}", 0)]
        [TestCase("{\"MessageType\":\"unknown\"}", 1)]
        public async Task MalformedJsonAndInvalidRootsAreSoftRejectedAsync(string text, int received)
        {
            await AssertJsonRejectedAsync(Encoding.UTF8.GetBytes(text), received).ConfigureAwait(false);
        }

        [TestCase("PublisherId", "301")]
        [TestCase("DataSetClassId", "aabbccdd-1122-3344-5566-778899aabbcd")]
        public async Task NestedIdentityConflictRejectsTheEntireStoredJsonMessageAsync(
            string propertyName,
            string conflictingValue)
        {
            byte[] seed = PubSubSeedAssertions.LoadSeed("Json", "metadata-keyframe-verbose.json");
            JsonObject envelope = JsonNode.Parse(seed).AsObject();
            JsonObject dataSet = envelope["Messages"].AsArray()[0].AsObject();
            dataSet[propertyName] = conflictingValue;
            byte[] conflicting = JsonSerializer.SerializeToUtf8Bytes(envelope);

            await AssertJsonRejectedAsync(conflicting, received: 1).ConfigureAwait(false);

            // Removing only the conflicting nested identity restores all four valid fields.
            Assert.That(dataSet.Remove(propertyName), Is.True);
            PubSubNetworkMessageContext context = FuzzableCode.NewContext();
            PubSubNetworkMessage recovered = FuzzableCode.DecodePubSubJson(
                JsonSerializer.SerializeToUtf8Bytes(envelope), context);
            PubSubSeedAssertions.AssertJsonSeed(recovered, "retained-Verbose-Variant", PubSubFieldEncoding.Variant);
            PubSubSeedAssertions.AssertDiagnostics(context, received: 1, dataSets: 1);
        }

        [TestCase("metadata-keyframe-variant.uadp", PubSubFieldEncoding.Variant)]
        [TestCase("metadata-keyframe-datavalue.uadp", PubSubFieldEncoding.DataValue)]
        [TestCase("metadata-keyframe-raw.uadp", PubSubFieldEncoding.RawData)]
        public void EveryTruncatedSeedHeaderAndFinalFieldAreRejected(string name, PubSubFieldEncoding encoding)
        {
            byte[] seed = PubSubSeedAssertions.LoadSeed("Uadp", name);
            // The fixed seed has a 36-byte network prefix and a 20-byte DataSetMessage header.
            const int headerSize = 56;
            Assert.That(seed.Length, Is.GreaterThan(headerSize));
            for (int length = 0; length < headerSize; length++)
            {
                byte[] truncated = seed.AsSpan(0, length).ToArray();
                PubSubNetworkMessageContext context = FuzzableCode.NewContext();

                Assert.That(FuzzableCode.DecodeUadp(truncated, context), Is.Null, $"Header prefix length: {length}");
                PubSubSeedAssertions.AssertDiagnostics(context, invalid: length == 0 ? 0 : 1);
                PubSubSeedAssertions.ReplayUadpAdapters(truncated);
            }

            PubSubNetworkMessageContext tailContext = FuzzableCode.NewContext();
            byte[] missingFinalByte = seed[..^1];
            Assert.That(FuzzableCode.DecodeUadp(missingFinalByte, tailContext), Is.Null);
            PubSubSeedAssertions.AssertDiagnostics(tailContext, invalid: 1);
            PubSubSeedAssertions.ReplayUadpAdapters(missingFinalByte);

            PubSubNetworkMessageContext completeContext = FuzzableCode.NewContext();
            PubSubSeedAssertions.AssertUadpSeed(FuzzableCode.DecodeUadp(seed, completeContext), encoding);
            PubSubSeedAssertions.AssertDiagnostics(completeContext, received: 1, dataSets: 1);
        }

        [Test]
        public void StreamAdapterReadFailuresAreNotSwallowedOrConvertedIntoSoftRejections()
        {
            Action<Stream>[] callbacks =
            [
                FuzzableCode.AflfuzzPubSubJsonDecode,
                FuzzableCode.AflfuzzUadpNetworkMessageDecode,
                FuzzableCode.AflfuzzUadpChunkReassembly
            ];
            foreach (Action<Stream> callback in callbacks)
            {
                var failure = new InvalidOperationException("Injected input-reader failure.");
                using var stream = new FailingReadStream(failure);

                Assert.That(
                    () => callback(stream),
                    Throws.TypeOf<InvalidOperationException>().And.SameAs(failure), callback.Method.Name);
                Assert.That(stream.CanRead, Is.True, "A failed adapter must not dispose the caller's stream.");
            }
        }

        private static async Task AssertJsonRejectedAsync(byte[] frame, int received)
        {
            PubSubNetworkMessageContext coreContext = FuzzableCode.NewContext();
            PubSubNetworkMessageContext publicContext = FuzzableCode.NewContext();
            PubSubNetworkMessage coreResult = FuzzableCode.DecodePubSubJson(frame, coreContext);
            PubSubNetworkMessage publicResult = await new PubSubJsonDecoder()
                .TryDecodeAsync(frame, publicContext).ConfigureAwait(false);

            Assert.That(coreResult, Is.Null);
            Assert.That(publicResult, Is.Null);
            PubSubSeedAssertions.AssertDiagnostics(coreContext, received: received, invalid: 1);
            PubSubSeedAssertions.AssertDiagnostics(publicContext, received: received, invalid: 1);
            PubSubSeedAssertions.ReplayJsonAdapters(frame);
        }

        private sealed class FailingReadStream(InvalidOperationException failure) : MemoryStream
        {
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw failure;
            }
        }
    }
}
