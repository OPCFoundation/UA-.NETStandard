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
using NUnit.Framework;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    public sealed class RegistrySnapshotTests
    {
        [Test]
        public void LargeStringReadsStayPinnedAndDoNotSplitUnicodeScalars()
        {
            ServiceMessageContext context = Context();
            using var snapshots = new RegistryNativeSnapshots(context);
            var session = new NodeId("reader", 1);
            const string original = "\U0001F600abc\U0001F600def\U0001F600ghi";
            var document = new RegistryStringValueDataType { Kind = 2, Value = original };
            RegistrySnapshotOpenResultDataType opened = snapshots.Open(session, "view-1",
                new RegistrySnapshotOpenRequestDataType { TargetXid = "/", DocumentKind = "metadata", View = 0 },
                document, 3, 7);
            Assert.That(opened.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(opened.TargetEpoch, Is.EqualTo(3));
            Assert.That(opened.RegistryEpoch, Is.EqualTo(7));
            document.Value = "changed after open";
            var request = new RegistrySnapshotReadRequestDataType
            {
                SnapshotId = opened.SnapshotId,
                Path = [new RegistryPathElementDataType { Kind = 0, Name = "Value" }],
                MaxItems = 3,
                MaxBytes = 256,
                ContinuationPoint = ByteString.Empty
            };
            var recovered = new StringBuilder();
            while (true)
            {
                RegistrySnapshotReadResultDataType result = snapshots.Read(session, "view-1", request);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.Kind, Is.EqualTo(1));
                Assert.That(result.Value.TryGetValue(out string text), Is.True);
                recovered.Append(text);
                if (result.Complete)
                {
                    break;
                }
                request.Offset += 3;
                request.ContinuationPoint = result.ContinuationPoint;
            }
            Assert.That(recovered.ToString(), Is.EqualTo(original));
            Assert.That(snapshots.Close(session, "view-1", opened.SnapshotId).StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(snapshots.Read(session, "view-1", request).StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void ContinuationsAreSingleUseAndWrongViewsCannotReadSnapshots()
        {
            using var snapshots = new RegistryNativeSnapshots(Context());
            var session = new NodeId("reader", 1);
            RegistrySnapshotOpenResultDataType opened = snapshots.Open(session, "view-1",
                new RegistrySnapshotOpenRequestDataType { TargetXid = "/", DocumentKind = "metadata", View = 0 },
                new RegistryArrayValueDataType
                {
                    Kind = 4,
                    Items =
                    [
                        new RegistryNullValueDataType { Kind = 0 },
                        new RegistryBooleanValueDataType { Kind = 1, Value = true },
                        new RegistryStringValueDataType { Kind = 2, Value = "third" }
                    ]
                }, 1, 1);
            var request = new RegistrySnapshotReadRequestDataType
            {
                SnapshotId = opened.SnapshotId,
                Path = [new RegistryPathElementDataType { Kind = 0, Name = "Items" }],
                MaxItems = 1,
                MaxBytes = 512
            };
            RegistrySnapshotReadResultDataType first = snapshots.Read(session, "view-1", request);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(first.Entries.Count, Is.EqualTo(1));
            Assert.That(first.Entries[0].Selector.Index, Is.Zero);
            request.ContinuationPoint = first.ContinuationPoint;
            request.Offset = 1;
            RegistrySnapshotReadResultDataType second = snapshots.Read(session, "view-1", request);
            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(second.Entries[0].Selector.Index, Is.EqualTo(1));
            Assert.That(snapshots.Read(session, "view-1", request).StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            request.ContinuationPoint = second.ContinuationPoint;
            request.Offset = 2;
            Assert.That(snapshots.Read(new NodeId("other", 1), "view-1", request).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(snapshots.Read(session, "revoked-view", request).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void RevokedSelectionReleasesItsSnapshotAndExposesNoNativePart()
        {
            using var snapshots = new RegistryNativeSnapshots(Context());
            var session = new NodeId("reader", 1);
            bool visible = true;
            RegistrySnapshotOpenResultDataType opened = snapshots.Open(session, "view",
                new RegistrySnapshotOpenRequestDataType { TargetXid = "/", DocumentKind = "metadata", View = 0 },
                new RegistryStringValueDataType { Kind = 2, Value = "private" }, 1, 1,
                () => visible ? ServiceResult.Good : StatusCodes.BadUserAccessDenied);
            var request = new RegistrySnapshotReadRequestDataType
            {
                SnapshotId = opened.SnapshotId,
                Path = [new RegistryPathElementDataType { Kind = 0, Name = "Value" }],
                MaxItems = 128,
                MaxBytes = 512
            };
            Assert.That(snapshots.Read(session, "view", request).StatusCode, Is.EqualTo(StatusCodes.Good));
            visible = false;
            RegistrySnapshotReadResultDataType denied = snapshots.Read(session, "view", request);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(denied.Value.IsNull, Is.True);
            Assert.That(denied.Entries.Count, Is.Zero);
            visible = true;
            Assert.That(snapshots.Read(session, "view", request).StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void EncodedSizeHonorsTheExactBinaryBoundaryAndRejectsInvalidInputs()
        {
            ServiceMessageContext context = Context();
            var document = new RegistryStringValueDataType { Kind = 2, Value = "A\U0001F600B" };
            using var wire = new MemoryStream();
            using (var encoder = new BinaryEncoder(wire, context, true))
            {
                document.Encode(encoder);
            }
            Assert.That(RegistryEncodedSize.Measure(document, context, wire.Length), Is.EqualTo(wire.Length));
            ServiceResultException tooSmall = Assert.Throws<ServiceResultException>(
                () => RegistryEncodedSize.Measure(document, context, wire.Length - 1))!;
            Assert.That(tooSmall.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.Throws<ArgumentOutOfRangeException>(() => RegistryEncodedSize.Measure(document, context, 0));
            Assert.Throws<ArgumentNullException>(() => RegistryEncodedSize.Measure(null!, context, 128));
            Assert.Throws<ArgumentNullException>(() => RegistryEncodedSize.Measure(document, null!, 128));
        }

        [Test]
        public void OneOversizedBinaryLeafIsReconstructedWithinTheActualEncodedByteLimit()
        {
            ServiceMessageContext context = Context();
            using var snapshots = new RegistryNativeSnapshots(context);
            var bytes = new byte[8192];
            bytes[0] = 1;
            var session = new NodeId("reader", 1);
            RegistrySnapshotOpenResultDataType opened = snapshots.Open(session, "view",
                new RegistrySnapshotOpenRequestDataType { TargetXid = "/", DocumentKind = "metadata", View = 0 },
                new RegistryNumberValueDataType
                {
                    Kind = 3,
                    Coefficient = ByteString.From(bytes),
                    IsInteger = true
                }, 1, 1);
            var request = new RegistrySnapshotReadRequestDataType
            {
                SnapshotId = opened.SnapshotId,
                Path = [new RegistryPathElementDataType { Kind = 0, Name = "Coefficient" }],
                MaxItems = 256,
                MaxBytes = 256
            };
            using var recovered = new MemoryStream();
            while (true)
            {
                RegistrySnapshotReadResultDataType result = snapshots.Read(session, "view", request);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.Value.TryGetValue(out ByteString chunk), Is.True);
                using var encoded = new MemoryStream();
                using (var encoder = new BinaryEncoder(encoded, context, true))
                {
                    result.Encode(encoder);
                }
                Assert.That(encoded.Length, Is.LessThanOrEqualTo(256));
                Assert.That(chunk.Length, Is.GreaterThan(0).And.LessThan(256));
                byte[] next = chunk.ToArray();
                recovered.Write(next, 0, next.Length);
                if (result.Complete)
                {
                    break;
                }
                request.Offset += (ulong)chunk.Length;
                request.ContinuationPoint = result.ContinuationPoint;
            }
            Assert.That(recovered.ToArray(), Is.EqualTo(bytes));
        }

        [Test]
        public void CapacityAndExpiryDoNotEvictAnUnexpiredSnapshot()
        {
            var clock = new ManualClock();
            var limits = new RegistrySnapshotLimitsDataType
            {
                MaxSnapshotsPerSession = 1,
                MaxContinuationPointsPerSession = 2,
                MaxSnapshotBytes = 16384,
                MaxDepth = 32,
                MaxReadItems = 8,
                MaxReadBytes = 1024,
                SnapshotTimeout = 1000
            };
            using var snapshots = new RegistryNativeSnapshots(Context(), limits, clock);
            var session = new NodeId("reader", 1);
            var selection = new RegistrySnapshotOpenRequestDataType
            {
                TargetXid = "/",
                DocumentKind = "metadata",
                View = 0
            };
            var value = new RegistryBooleanValueDataType { Kind = 1, Value = true };
            RegistrySnapshotOpenResultDataType first = snapshots.Open(session, "view", selection, value, 1, 1);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(snapshots.Open(session, "view", selection, value, 1, 1).StatusCode,
                Is.EqualTo(StatusCodes.BadResourceUnavailable));
            var read = new RegistrySnapshotReadRequestDataType
            {
                SnapshotId = first.SnapshotId,
                Path = [],
                MaxItems = 8,
                MaxBytes = 1024
            };
            Assert.That(snapshots.Read(session, "view", read).StatusCode, Is.EqualTo(StatusCodes.Good));
            clock.Advance(1000);
            Assert.That(snapshots.Read(session, "view", read).StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(snapshots.Open(session, "view", selection, value, 1, 1).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            snapshots.ReleaseSession(session);
            Assert.That(snapshots.Open(session, "view", selection, value, 1, 1).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void EncodedByteLimitIncludesTheReturnedContinuationPoint()
        {
            ServiceMessageContext context = Context();
            using var snapshots = new RegistryNativeSnapshots(context);
            var session = new NodeId("reader", 1);
            RegistrySnapshotOpenResultDataType opened = snapshots.Open(session, "view",
                new RegistrySnapshotOpenRequestDataType { TargetXid = "/", DocumentKind = "metadata" },
                new RegistryStringValueDataType { Kind = 2, Value = new string('x', 4096) }, 1, 1);
            for (uint maximum = 192; maximum <= 320; maximum++)
            {
                RegistrySnapshotReadResultDataType result = snapshots.Read(session, "view",
                    new RegistrySnapshotReadRequestDataType
                    {
                        SnapshotId = opened.SnapshotId,
                        Path = [new RegistryPathElementDataType { Kind = 0, Name = "Value" }],
                        MaxItems = 128,
                        MaxBytes = maximum
                    });
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good), $"MaxBytes={maximum}");
                using var encoded = new MemoryStream();
                using (var encoder = new BinaryEncoder(encoded, context, true))
                {
                    result.Encode(encoder);
                }
                Assert.That(encoded.Length, Is.LessThanOrEqualTo(maximum), $"MaxBytes={maximum}");
                Assert.That(result.ContinuationPoint.Length, Is.GreaterThan(0));
                snapshots.ReleaseSession(session);
                opened = snapshots.Open(session, "view",
                    new RegistrySnapshotOpenRequestDataType { TargetXid = "/", DocumentKind = "metadata" },
                    new RegistryStringValueDataType { Kind = 2, Value = new string('x', 4096) }, 1, 1);
            }
        }

        private static ServiceMessageContext Context()
        {
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            context.Factory.Builder.AddOpcUaXRegistry().Commit();
            return context;
        }

        private sealed class ManualClock : TimeProvider
        {
            public override long TimestampFrequency => 1000;
            public override long GetTimestamp() => m_timestamp;
            public void Advance(long milliseconds)
            {
                m_timestamp += milliseconds;
            }
            private long m_timestamp;
        }
    }
}
