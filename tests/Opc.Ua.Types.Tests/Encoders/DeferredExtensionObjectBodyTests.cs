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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Notification bodies that the binary decoder keeps as bytes until first use.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [Parallelizable(ParallelScope.All)]
    public class DeferredExtensionObjectBodyTests
    {
        [Test]
        public void EventsAreDeferredAndDecodeToTheSameValue()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            byte[] encoded = Encode(context, CreateEvents(3));

            ExtensionObject decoded = Decode(context, encoded);

            Assert.That(decoded.IsDeferred, Is.True);
            Assert.That(decoded.TryGetValue(out EventNotificationList? events), Is.True);
            Assert.That(events!.Events.Count, Is.EqualTo(3));
            Assert.That(events.Events[2].ClientHandle, Is.EqualTo(2u));
            Assert.That(events.Events[1].EventFields[0].TryGetValue(out string? text), Is.True);
            Assert.That(text, Is.EqualTo("event 1"));
        }

        [Test]
        public void DataChangesAreDeferred()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            var notification = new DataChangeNotification
            {
                MonitoredItems =
                [
                    new MonitoredItemNotification { ClientHandle = 7, Value = new DataValue(Variant.From(42)) }
                ]
            };

            ExtensionObject decoded = Decode(context, Encode(context, notification));

            Assert.That(decoded.IsDeferred, Is.True);
            Assert.That(decoded.TryGetValue(out DataChangeNotification? data), Is.True);
            Assert.That(data!.MonitoredItems[0].ClientHandle, Is.EqualTo(7u));
        }

        [Test]
        public void BodiesAreDecodedEagerlyByDefault()
        {
            ServiceMessageContext context = CreateContext(defer: false);

            ExtensionObject decoded = Decode(context, Encode(context, CreateEvents(1)));

            Assert.That(decoded.IsDeferred, Is.False);
            Assert.That(decoded.TryGetValue(out EventNotificationList? _), Is.True);
        }

        [Test]
        public void OtherTypesAreNotDeferred()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            var status = new StatusChangeNotification { Status = StatusCodes.BadDecodingError };

            ExtensionObject decoded = Decode(context, Encode(context, status));

            Assert.That(decoded.IsDeferred, Is.False);
            Assert.That(decoded.TryGetValue(out StatusChangeNotification? value), Is.True);
            Assert.That(value!.Status, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void DecodedValueIsCached()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            ExtensionObject decoded = Decode(context, Encode(context, CreateEvents(1)));

            Assert.That(decoded.TryGetValue(out EventNotificationList? first), Is.True);
            Assert.That(decoded.TryGetValue(out EventNotificationList? second), Is.True);

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public async Task ConcurrentReadersSeeOneValueAsync()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            ExtensionObject decoded = Decode(context, Encode(context, CreateEvents(50)));

            var readers = new Task<EventNotificationList?>[8];
            for (int i = 0; i < readers.Length; i++)
            {
                readers[i] = Task.Run(() =>
                {
                    decoded.TryGetValue(out EventNotificationList? value);
                    return value;
                });
            }
            EventNotificationList?[] values = await Task.WhenAll(readers).ConfigureAwait(false);

            Assert.That(values[0], Is.Not.Null);
            Assert.That(values, Has.All.SameAs(values[0]));
        }

        [Test]
        public void ReleaseDropsTheValue()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            ExtensionObject decoded = Decode(context, Encode(context, CreateEvents(1)));
            Assert.That(decoded.TryGetValue(out EventNotificationList? _), Is.True);

            decoded.ReleaseDeferredBody();

            Assert.That(decoded.TryGetValue(out EventNotificationList? _), Is.False);
            Assert.That(decoded.TryGetAsBinary(out ByteString _), Is.False);
        }

        [Test]
        public void ReleaseBeforeFirstReadIsSafe()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            ExtensionObject decoded = Decode(context, Encode(context, CreateEvents(1)));

            decoded.ReleaseDeferredBody();
            decoded.ReleaseDeferredBody();

            Assert.That(decoded.TryGetValue(out EventNotificationList? _), Is.False);
        }

        [Test]
        public void ReleaseLeavesOtherBodiesAlone()
        {
            var extension = new ExtensionObject(CreateEvents(1));

            extension.ReleaseDeferredBody();

            Assert.That(extension.TryGetValue(out EventNotificationList? _), Is.True);
        }

        [Test]
        public void MalformedBodyDoesNotFailTheDecoderAndStaysAvailableAsBytes()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            byte[] body = [1, 0, 0, 0, 1, 2, 3];
            byte[] encoded = EncodeRaw(body);

            ExtensionObject decoded = Decode(context, encoded);

            Assert.That(decoded.TryGetValue(out EventNotificationList? _), Is.False);
            Assert.That(decoded.TryGetAsBinary(out ByteString bytes), Is.True);
            Assert.That(bytes.ToArray(), Is.EqualTo(body));
        }

        [Test]
        public void TrailingBytesAreRejectedLikeTheEagerDecoderDoes()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            byte[] valid = EncodeBody(context, CreateEvents(1));
            byte[] body = new byte[valid.Length + 2];
            valid.CopyTo(body, 0);

            ExtensionObject decoded = Decode(context, EncodeRaw(body));

            Assert.That(decoded.TryGetValue(out EventNotificationList? _), Is.False);
            Assert.That(decoded.TryGetAsBinary(out ByteString bytes), Is.True);
            Assert.That(bytes.Length, Is.EqualTo(body.Length));
        }

        [Test]
        public void LengthBeyondTheInputIsRejectedBeforeRenting()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            byte[] encoded = EncodeRaw([1, 2, 3], claimedLength: int.MaxValue);

            ServiceResultException? ex = Assert.Throws<ServiceResultException>(() => Decode(context, encoded));

            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void LengthAboveMaxMessageSizeIsRejected()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            context.MaxMessageSize = 16;
            byte[] encoded = EncodeRaw(new byte[32]);

            ServiceResultException? ex = Assert.Throws<ServiceResultException>(() => Decode(context, encoded));

            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NestingLimitIsEnforcedLikeTheEagerDecoderDoes(bool defer)
        {
            ServiceMessageContext context = CreateContext(defer);
            context.MaxEncodingNestingLevels = 4;
            byte[] encoded = Encode(context, new EventNotificationList());

            using var atLimit = new BinaryDecoder(encoded, context);
            atLimit.InheritDecodingState(null, null, 4);
            Assert.That(atLimit.ReadExtensionObject(null).TryGetValue(out EventNotificationList? _), Is.True);

            using var beyondLimit = new BinaryDecoder(encoded, context);
            beyondLimit.InheritDecodingState(null, null, 5);
            ServiceResultException? ex = Assert.Throws<ServiceResultException>(
                () => beyondLimit.ReadExtensionObject(null));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void MalformedBodiesOfOneResponseLogOnce()
        {
            var provider = new CountingLoggerProvider();
            using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(provider));
            ServiceMessageContext context = ServiceMessageContext.Create(new TestTelemetry(factory));
            context.DeferNotificationBodies = true;
            byte[] body = EncodeRaw([0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3]);
            byte[] encoded = [.. body, .. body, .. body];

            using (var response = new BinaryDecoder(encoded, context))
            {
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(response.ReadExtensionObject(null).TryGetValue(out EventNotificationList? _), Is.False);
                }
            }
            Assert.That(provider.Warnings, Is.EqualTo(1));

            using (var next = new BinaryDecoder(body, context))
            {
                Assert.That(next.ReadExtensionObject(null).TryGetValue(out EventNotificationList? _), Is.False);
            }
            Assert.That(provider.Warnings, Is.EqualTo(2));
        }

        [Test]
        public void BodyRejectedByALimitIsNotRetainedForARetry()
        {
            ServiceMessageContext encoding = CreateContext(defer: true);
            ServiceMessageContext context = CreateContext(defer: true);
            context.MaxEncodingNestingLevels = 1;

            ExtensionObject decoded = Decode(context, Encode(encoding, CreateEvents(1)));

            Assert.That(decoded.TryGetValue(out EventNotificationList? _), Is.False);
            Assert.That(decoded.TryGetValue(out EventNotificationList? _, context), Is.False);
            Assert.That(decoded.TryGetAsBinary(out ByteString _), Is.False);
        }

        [Test]
        public void MalformedBodyAboveMaxByteStringLengthIsNotRetained()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            context.MaxByteStringLength = 8;
            byte[] valid = EncodeBody(context, new EventNotificationList());
            byte[] body = new byte[valid.Length + 16];
            valid.CopyTo(body, 0);

            ExtensionObject decoded = Decode(context, EncodeRaw(body));

            Assert.That(decoded.TryGetValue(out EventNotificationList? _), Is.False);
            Assert.That(decoded.TryGetAsBinary(out ByteString _), Is.False);
        }

        [Test]
        public void DeferredBodiesOfOneResponseShareTheRecoveryBudget()
        {
            var provider = new CountingLoggerProvider();
            using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(provider));
            ServiceMessageContext context = CreateVendorContext(factory);
            byte[] body = Encode(context, CreateEventsWithMalformedVendorBody(context));

            using var response = new BinaryDecoder([.. body, .. body], context);
            ExtensionObject first = response.ReadExtensionObject(null);
            ExtensionObject second = response.ReadExtensionObject(null);

            Assert.That(first.TryGetValue(out EventNotificationList? _), Is.True);
            Assert.That(second.TryGetValue(out EventNotificationList? _), Is.False);
            Assert.That(provider.Warnings, Is.EqualTo(1));
        }

        [Test]
        public void EagerRecoveryCountsAgainstTheBudgetOfDeferredBodies()
        {
            var provider = new CountingLoggerProvider();
            using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(provider));
            ServiceMessageContext context = CreateVendorContext(factory);
            byte[] eager = Encode(context, MalformedVendorBody(context));
            byte[] deferred = Encode(context, CreateEventsWithMalformedVendorBody(context));

            using var response = new BinaryDecoder([.. eager, .. deferred], context);
            Assert.That(response.ReadExtensionObject(null).TryGetAsBinary(out ByteString _), Is.True);
            ExtensionObject events = response.ReadExtensionObject(null);

            Assert.That(events.TryGetValue(out EventNotificationList? _), Is.False);
            Assert.That(provider.Warnings, Is.EqualTo(1));
        }

        [Test]
        public async Task ConcurrentReadsOfDistinctBodiesStayWithinTheRecoveryBudgetAsync()
        {
            var provider = new CountingLoggerProvider();
            using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(provider));
            ServiceMessageContext context = CreateVendorContext(factory);
            byte[] body = Encode(context, CreateEventsWithMalformedVendorBody(context));
            const int kBodies = 8;
            var encoded = new byte[body.Length * kBodies];
            for (int i = 0; i < kBodies; i++)
            {
                body.CopyTo(encoded, i * body.Length);
            }

            for (int round = 0; round < 200; round++)
            {
                using var response = new BinaryDecoder(encoded, context);
                var bodies = new ExtensionObject[kBodies];
                for (int i = 0; i < kBodies; i++)
                {
                    bodies[i] = response.ReadExtensionObject(null);
                }

                using var start = new Barrier(kBodies);
                var readers = new Task<bool>[kBodies];
                for (int i = 0; i < kBodies; i++)
                {
                    ExtensionObject deferred = bodies[i];
                    readers[i] = Task.Factory.StartNew(
                        () =>
                        {
                            start.SignalAndWait();
                            return deferred.TryGetValue(out EventNotificationList? _);
                        },
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                }
                bool[] decoded = await Task.WhenAll(readers).ConfigureAwait(false);

                Assert.That(decoded.Count(value => value), Is.EqualTo(1));
            }
        }

        [Test]
        public void CopiedContextKeepsDeferringBodies()
        {
            ServiceMessageContext context = CreateContext(defer: true);

            var copy = new ServiceMessageContext(context, context.Telemetry);

            Assert.That(copy.DeferNotificationBodies, Is.True);
            Assert.That(Decode(copy, Encode(context, CreateEvents(1))).IsDeferred, Is.True);
            ServiceMessageContext eager = CreateContext(defer: false);
            Assert.That(new ServiceMessageContext(eager, eager.Telemetry).DeferNotificationBodies, Is.False);
        }

        [Test]
        public void ForwardOnlyStreamDecodesEagerly()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            byte[] encoded = Encode(context, CreateEvents(2));
            using var stream = new ForwardOnlyStream(encoded);
            using var decoder = new BinaryDecoder(stream, context, leaveOpen: true);

            ExtensionObject decoded = decoder.ReadExtensionObject(null);

            Assert.That(decoded.IsDeferred, Is.False);
            Assert.That(decoded.TryGetValue(out EventNotificationList? events), Is.True);
            Assert.That(events!.Events.Count, Is.EqualTo(2));
        }

        [Test]
        public void SeekableStreamDefers()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            using var stream = new MemoryStream(Encode(context, CreateEvents(2)));
            using var decoder = new BinaryDecoder(stream, context, leaveOpen: true);

            ExtensionObject decoded = decoder.ReadExtensionObject(null);

            Assert.That(decoded.IsDeferred, Is.True);
            Assert.That(decoded.TryGetValue(out EventNotificationList? events), Is.True);
            Assert.That(events!.Events.Count, Is.EqualTo(2));
        }

        [Test]
        public void DeferredBodyEncodesAgain()
        {
            ServiceMessageContext context = CreateContext(defer: true);
            byte[] encoded = Encode(context, CreateEvents(2));
            ExtensionObject decoded = Decode(context, encoded);

            using var encoder = new BinaryEncoder(context);
            encoder.WriteExtensionObject(null, decoded);

            Assert.That(encoder.CloseAndReturnBuffer(), Is.EqualTo(encoded));
        }

        private static ServiceMessageContext CreateContext(bool defer)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.DeferNotificationBodies = defer;
            return context;
        }

        private static EventNotificationList CreateEvents(int count)
        {
            var events = new EventFieldList[count];
            for (int i = 0; i < count; i++)
            {
                events[i] = new EventFieldList
                {
                    ClientHandle = (uint)i,
                    EventFields = [Variant.From($"event {i}"), Variant.From(i)]
                };
            }
            return new EventNotificationList { Events = events };
        }

        private static byte[] Encode(IServiceMessageContext context, IEncodeable value)
        {
            using var encoder = new BinaryEncoder(context);
            encoder.WriteExtensionObject(null, new ExtensionObject(value));
            return encoder.CloseAndReturnBuffer()!;
        }

        private static byte[] Encode(IServiceMessageContext context, ExtensionObject value)
        {
            using var encoder = new BinaryEncoder(context);
            encoder.WriteExtensionObject(null, value);
            return encoder.CloseAndReturnBuffer()!;
        }

        private static ServiceMessageContext CreateVendorContext(ILoggerFactory factory)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(new TestTelemetry(factory));
            context.DeferNotificationBodies = true;
            context.MaxDecoderRecoveries = 1;
            context.NamespaceUris.GetIndexOrAppend(kVendorUri);
            context.Factory.AddEncodeableType(typeof(BinaryDecoderAuditTests.XmlSampleNs1));
            return context;
        }

        /// <summary>
        /// A known vendor type with a body too short for its only field.
        /// </summary>
        private static ExtensionObject MalformedVendorBody(ServiceMessageContext context)
        {
            return new ExtensionObject(
                new ExpandedNodeId(new NodeId(88902u, context.NamespaceUris.GetIndexOrAppend(kVendorUri))),
                ByteString.From(new byte[] { 1, 2 }));
        }

        private static EventNotificationList CreateEventsWithMalformedVendorBody(ServiceMessageContext context)
        {
            return new EventNotificationList
            {
                Events = [new EventFieldList { EventFields = [Variant.From(MalformedVendorBody(context))] }]
            };
        }

        private static byte[] EncodeBody(IServiceMessageContext context, EventNotificationList value)
        {
            using var encoder = new BinaryEncoder(context);
            value.Encode(encoder);
            return encoder.CloseAndReturnBuffer()!;
        }

        /// <summary>
        /// An EventNotificationList with the given body bytes: type id
        /// i=916 as a four byte node id, binary encoding, length, body.
        /// </summary>
        private static byte[] EncodeRaw(byte[] body, int? claimedLength = null)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)1);
            writer.Write((byte)0);
            writer.Write((ushort)916);
            writer.Write((byte)1);
            writer.Write(claimedLength ?? body.Length);
            writer.Write(body);
            writer.Flush();
            return stream.ToArray();
        }

        private static ExtensionObject Decode(IServiceMessageContext context, byte[] encoded)
        {
            using var decoder = new BinaryDecoder(encoded, context);
            return decoder.ReadExtensionObject(null);
        }

        private const string kVendorUri = "urn:test:xmlsample";

        private sealed class TestTelemetry : TelemetryContextBase
        {
            public TestTelemetry(ILoggerFactory factory)
                : base(factory)
            {
            }
        }

        private sealed class CountingLoggerProvider : ILoggerProvider, ILogger
        {
            public int Warnings { get; private set; }

            public ILogger CreateLogger(string categoryName)
            {
                return this;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel == LogLevel.Warning;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    Warnings++;
                }
            }

            public void Dispose()
            {
            }
        }

        private sealed class ForwardOnlyStream : MemoryStream
        {
            public ForwardOnlyStream(byte[] buffer)
                : base(buffer)
            {
            }

            public override bool CanSeek => false;
        }
    }
}