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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Offline regression tests for GDS client helpers that do not need a
    /// live server: trust-list file transfer, the access-token provider
    /// client cache and AuthorizationService output validation.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Category("GdsClientOffline")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class GdsClientRegressionTests
    {
        /// <summary>
        /// Part 20 4.2.4 allows a server to return less data than requested;
        /// only an empty ByteString ends the file. The reader must keep going.
        /// </summary>
        [Test]
        public async Task TrustListReadContinuesAfterShortReadAsync()
        {
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            byte[] certificate = new byte[700];
            for (int i = 0; i < certificate.Length; i++)
            {
                certificate[i] = (byte)i;
            }
            var expected = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                TrustedCertificates = [certificate.ToByteString()],
                TrustedCrls = [],
                IssuerCertificates = [],
                IssuerCrls = []
            };
            byte[] payload;
            using (var strm = new MemoryStream())
            {
                using (var encoder = new BinaryEncoder(strm, messageContext, true))
                {
                    encoder.WriteEncodeable(null, expected);
                }
                payload = strm.ToArray();
            }

            // The server caps every reply at 100 bytes although 256 are requested.
            int position = 0;
            int reads = 0;
            var session = new Mock<ISessionClient>();
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    reads++;
                    int take = Math.Min(100, payload.Length - position);
                    byte[] chunk = new byte[take];
                    Array.Copy(payload, position, chunk, 0, take);
                    position += take;
                    return new ValueTask<CallResponse>(CreateCallResponse(new Variant(chunk.ToByteString())));
                });
            var file = new FileTypeClient(session.Object, new NodeId(7u), messageContext.Telemetry);

            TrustListDataType actual = await TrustListFileTransferHelper.ReadAsync(
                file, 1, messageContext, 1024 * 1024, 256, CancellationToken.None).ConfigureAwait(false);

            Assert.That(actual.TrustedCertificates.Count, Is.EqualTo(1));
            Assert.That(actual.TrustedCertificates[0].ToArray(), Is.EqualTo(certificate));
            Assert.That(reads, Is.EqualTo(((payload.Length + 99) / 100) + 1));
        }

        /// <summary>
        /// Servers that bound every Read by MaxTrustListSize reject the end-of-file
        /// Read after the last short chunk of a list close to that size; the reader
        /// treats that rejection as end of file.
        /// </summary>
        [Test]
        public async Task TrustListReadToleratesSizeLimitRejectionOfEndOfFileReadAsync()
        {
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var expected = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                TrustedCertificates = [new byte[300].ToByteString()],
                TrustedCrls = [],
                IssuerCertificates = [],
                IssuerCrls = []
            };
            byte[] payload;
            using (var strm = new MemoryStream())
            {
                using (var encoder = new BinaryEncoder(strm, messageContext, true))
                {
                    encoder.WriteEncodeable(null, expected);
                }
                payload = strm.ToArray();
            }

            int position = 0;
            var session = new Mock<ISessionClient>();
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    if (position == payload.Length)
                    {
                        // position + requested length exceeds the server's MaxTrustListSize.
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new CallMethodResult { StatusCode = StatusCodes.BadEncodingLimitsExceeded }],
                            DiagnosticInfos = default
                        });
                    }
                    int take = Math.Min(256, payload.Length - position);
                    byte[] chunk = new byte[take];
                    Array.Copy(payload, position, chunk, 0, take);
                    position += take;
                    return new ValueTask<CallResponse>(CreateCallResponse(new Variant(chunk.ToByteString())));
                });
            var file = new FileTypeClient(session.Object, new NodeId(7u), messageContext.Telemetry);

            TrustListDataType actual = await TrustListFileTransferHelper.ReadAsync(
                file, 1, messageContext, 1024 * 1024, 256, CancellationToken.None).ConfigureAwait(false);

            Assert.That(actual.TrustedCertificates.Count, Is.EqualTo(1));
            Assert.That(actual.TrustedCertificates[0].Length, Is.EqualTo(300));
        }

        /// <summary>
        /// A client factory that fails synchronously must not leave the failed
        /// task cached; the next acquisition has to call the factory again.
        /// </summary>
        [Test]
        public void AccessTokenProviderDoesNotCacheSynchronousFactoryFailure()
        {
            int factoryCalls = 0;
            var provider = new GdsAccessTokenProvider(
                _ =>
                {
                    factoryCalls++;
                    throw new InvalidOperationException("factory failed");
                },
                "urn:authority");
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority", Audience = "urn:target" };

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.AcquireAsync(metadata).ConfigureAwait(false));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.AcquireAsync(metadata).ConfigureAwait(false));

            Assert.That(factoryCalls, Is.EqualTo(2));
        }

        /// <summary>
        /// The client creation is shared by concurrent acquisitions; cancelling
        /// the caller that started it must not cancel the creation the other
        /// callers are waiting for.
        /// </summary>
        [Test]
        public void AccessTokenProviderCancellationOfFirstCallerDoesNotFailOthers()
        {
            int factoryCalls = 0;
            var creation = new TaskCompletionSource<AuthorizationServiceClient>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new GdsAccessTokenProvider(
                ct =>
                {
                    factoryCalls++;
                    // A factory that honours the token it is given.
                    ct.Register(() => creation.TrySetCanceled(ct));
                    return new ValueTask<AuthorizationServiceClient>(creation.Task);
                },
                "urn:authority");
            var metadata = new AuthorizationServerMetadata { AuthorityUri = "urn:authority", Audience = "urn:target" };

            using var firstCallerCts = new CancellationTokenSource();
            Task first = provider.AcquireAsync(metadata, firstCallerCts.Token).AsTask();
            Task second = provider.AcquireAsync(metadata, CancellationToken.None).AsTask();

            firstCallerCts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await first.ConfigureAwait(false));
            Assert.That(second.IsCompleted, Is.False);

            creation.TrySetException(new InvalidOperationException("creation finished"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await second.ConfigureAwait(false));
            Assert.That(factoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// A server returning fewer output arguments than the method defines
        /// must surface as a ServiceResultException, not IndexOutOfRange.
        /// </summary>
        [Test]
        public void AuthorizationServiceShortOutputThrowsServiceResultException()
        {
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var session = new Mock<ISession>();
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session.SetupGet(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            session
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { StatusCode = StatusCodes.Good, References = [] }]
                }));
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(CreateCallResponse(new Variant(ByteString.Empty))));
            var client = new AuthorizationServiceClient(session.Object, new NodeId(1000u, 2));

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.StartRequestTokenAsync("urn:resource", "jwt", ByteString.Empty).ConfigureAwait(false));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        /// <summary>
        /// A wrongly typed ServiceData output is rejected instead of being read as empty.
        /// </summary>
        [Test]
        public void AuthorizationServiceWronglyTypedServiceDataThrowsTypeMismatch()
        {
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var session = new Mock<ISession>();
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session.SetupGet(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            session
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { StatusCode = StatusCodes.Good, References = [] }]
                }));
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(CreateCallResponse(
                    new Variant("not a ByteString"),
                    new Variant(new Uuid(Guid.NewGuid())))));
            var client = new AuthorizationServiceClient(session.Object, new NodeId(1000u, 2));

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.StartRequestTokenAsync("urn:resource", "jwt", ByteString.Empty).ConfigureAwait(false));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        private static CallResponse CreateCallResponse(params Variant[] outputs)
        {
            return new CallResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = outputs.ToArrayOf() }],
                DiagnosticInfos = default
            };
        }
    }
}
