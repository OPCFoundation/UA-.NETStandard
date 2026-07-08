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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Security.Sks
{
    /// <summary>
    /// Tests for <see cref="OpcUaSecurityKeyServiceClient"/> using a
    /// mocked <see cref="ISession"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3.2")]
    public class OpcUaSecurityKeyServiceClientTests
    {
        private static IPubSubSecurityPolicy Policy =>
            PubSubSecurityPolicyRegistry.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr)!;

        private static (Mock<ISession> session, CallMethodRequest? captured) BuildSessionMock(
            CallResponse response)
        {
            var mock = new Mock<ISession>();
            mock.SetupGet(s => s.Connected).Returns(true);
            CallMethodRequest? captured = null;
            mock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        if (requests.Count > 0)
                        {
                            captured = requests[0];
                        }
                    })
                .Returns(new ValueTask<CallResponse>(response));
            mock.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            return (mock, captured);
        }

        private static CallResponse BuildSuccessfulResponse()
        {
            int total = Policy.SigningKeyLength + Policy.EncryptingKeyLength + Policy.NonceLength;
            byte[] keyBytes = new byte[total];
            for (int i = 0; i < total; i++)
            {
                keyBytes[i] = (byte)i;
            }
            ByteString[] keys = new[] { new ByteString(keyBytes) };
            ArrayOf<Variant> outputs = new Variant[]
            {
                Variant.From(Policy.PolicyUri),
                Variant.From(7U),
                Variant.From((ArrayOf<ByteString>)keys),
                Variant.From(1000.0),
                Variant.From(60000.0)
            };
            var result = new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = outputs
            };
            return new CallResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = new[] { result },
                DiagnosticInfos = ArrayOf<DiagnosticInfo>.Empty
            };
        }

        private static EndpointDescription BuildSksEndpoint(MessageSecurityMode securityMode)
        {
            return new EndpointDescription
            {
                EndpointUrl = "opc.tcp://sks:4840",
                SecurityMode = securityMode,
                SecurityPolicyUri = securityMode == MessageSecurityMode.None
                    ? SecurityPolicies.None
                    : SecurityPolicies.Basic256Sha256,
                UserIdentityTokens = new ArrayOf<UserTokenPolicy>(
                    new[]
                    {
                        new UserTokenPolicy
                        {
                            PolicyId = "username",
                            TokenType = UserTokenType.UserName
                        }
                    })
            };
        }

        [Test]
        public async Task GetSecurityKeysAsync_InvokesCorrectNodeIdsAndArguments()
        {
            CallMethodRequest? captured = null;
            var sessionMock = new Mock<ISession>();
            sessionMock.SetupGet(s => s.Connected).Returns(true);
            sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) => captured = requests[0])
                .Returns(new ValueTask<CallResponse>(BuildSuccessfulResponse()));
            sessionMock.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));

            await using var client = new OpcUaSecurityKeyServiceClient(
                _ => new ValueTask<ISession>(sessionMock.Object),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider());

            SksKeyResponse response = await client.GetSecurityKeysAsync(
                new SksKeyRequest("group-1", 0U, 1U)).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ObjectId, Is.EqualTo(ObjectIds.PublishSubscribe));
            Assert.That(captured.MethodId, Is.EqualTo(MethodIds.PublishSubscribe_GetSecurityKeys));
            Assert.That(captured.InputArguments, Has.Count.EqualTo(3));
            Assert.That(captured.InputArguments[0].TryGetValue(out string? gid), Is.True);
            Assert.That(gid, Is.EqualTo("group-1"));
            Assert.That(captured.InputArguments[1].TryGetValue(out uint startTok), Is.True);
            Assert.That(startTok, Is.Zero);
            Assert.That(captured.InputArguments[2].TryGetValue(out uint reqCount), Is.True);
            Assert.That(reqCount, Is.EqualTo(1U));

            Assert.That(response.SecurityPolicyUri, Is.EqualTo(Policy.PolicyUri));
            Assert.That(response.FirstTokenId, Is.EqualTo(7U));
            Assert.That(((byte[][]?)response.Keys) ?? [], Has.Length.EqualTo(1));
            Assert.That(response.TimeToNextKey, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(response.KeyLifetime, Is.EqualTo(TimeSpan.FromMinutes(1)));
        }

        [Test]
        public void GetSecurityKeysAsync_RejectsEmptySecurityGroupId()
        {
            (Mock<ISession> session, _) = BuildSessionMock(BuildSuccessfulResponse());
            var client = new OpcUaSecurityKeyServiceClient(
                _ => new ValueTask<ISession>(session.Object),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider());
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await client.GetSecurityKeysAsync(
                    new SksKeyRequest(string.Empty, 0U, 1U)).ConfigureAwait(false))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task GetSecurityKeysAsync_RaisesAvailabilityChangedOnFirstSuccess()
        {
            (Mock<ISession> session, _) = BuildSessionMock(BuildSuccessfulResponse());
            await using var client = new OpcUaSecurityKeyServiceClient(
                _ => new ValueTask<ISession>(session.Object),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider());
            int callCount = 0;
            SksAvailabilityChangedEventArgs? lastArgs = null;
            client.AvailabilityChanged += (_, e) =>
            {
                Interlocked.Increment(ref callCount);
                lastArgs = e;
            };
            await client.GetSecurityKeysAsync(new SksKeyRequest("g", 0U, 1U)).ConfigureAwait(false);
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(lastArgs!.IsAvailable, Is.True);
        }

        [Test]
        public async Task GetSecurityKeysAsync_WrapsServiceResultExceptionInOpcUaSksException()
        {
            var sessionMock = new Mock<ISession>();
            sessionMock.SetupGet(s => s.Connected).Returns(true);
            sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied));
            sessionMock.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));

            await using var client = new OpcUaSecurityKeyServiceClient(
                _ => new ValueTask<ISession>(sessionMock.Object),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider());

            int unavailableCount = 0;
            client.AvailabilityChanged += (_, e) =>
            {
                if (!e.IsAvailable)
                {
                    Interlocked.Increment(ref unavailableCount);
                }
            };
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await client.GetSecurityKeysAsync(
                    new SksKeyRequest("g", 0U, 1U)).ConfigureAwait(false))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(unavailableCount, Is.EqualTo(1));
        }

        [Test]
        public async Task GetSecurityKeysAsync_WrapsSessionFactoryFailure()
        {
            await using var client = new OpcUaSecurityKeyServiceClient(
                _ => throw new InvalidOperationException("boom"),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider());
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await client.GetSecurityKeysAsync(
                    new SksKeyRequest("g", 0U, 1U)).ConfigureAwait(false))!;
            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadCommunicationError));
        }

        [Test]
        public async Task DisposeAsync_DisposesSessionAndIsIdempotent()
        {
            var sessionMock = new Mock<ISession>();
            sessionMock.SetupGet(s => s.Connected).Returns(true);
            sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(BuildSuccessfulResponse()));
            sessionMock.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));

            var client = new OpcUaSecurityKeyServiceClient(
                _ => new ValueTask<ISession>(sessionMock.Object),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider());
            await client.GetSecurityKeysAsync(new SksKeyRequest("g", 0U, 1U)).ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
            sessionMock.Verify(s => s.DisposeAsync(), Times.AtLeastOnce);
            Assert.That(
                async () => await client.GetSecurityKeysAsync(new SksKeyRequest("g", 0U, 1U)).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void Constructor_RejectsNullSessionFactory()
        {
            Assert.That(
                () => new OpcUaSecurityKeyServiceClient(
                    null!,
                    NUnitTelemetryContext.Create(),
                    new FakeTimeProvider()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_RejectsNullEndpoint()
        {
            Assert.That(
                () => new OpcUaSecurityKeyServiceClient(
                    null!,
                    new ApplicationConfiguration(NUnitTelemetryContext.Create()),
                    NUnitTelemetryContext.Create(),
                    new FakeTimeProvider()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        [TestSpec("8.3.2", Part = 14, Summary = "SKS client requires encrypted OPC UA channel")]
        public void ConstructorRejectsNoneSksEndpoint()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => new OpcUaSecurityKeyServiceClient(
                    BuildSksEndpoint(MessageSecurityMode.None),
                    new ApplicationConfiguration(NUnitTelemetryContext.Create()),
                    NUnitTelemetryContext.Create(),
                    new FakeTimeProvider()))!;

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
        }

        [Test]
        [TestSpec("8.3.2", Part = 14, Summary = "SKS client requires encrypted OPC UA channel")]
        public void ConstructorRejectsSignOnlySksEndpoint()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => new OpcUaSecurityKeyServiceClient(
                    BuildSksEndpoint(MessageSecurityMode.Sign),
                    new ApplicationConfiguration(NUnitTelemetryContext.Create()),
                    NUnitTelemetryContext.Create(),
                    new FakeTimeProvider()))!;

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
        }

        [Test]
        [TestSpec("8.3.2", Part = 14, Summary = "SKS client allows encrypted OPC UA channel")]
        public async Task ConstructorAcceptsSignAndEncryptSksEndpoint()
        {
            await using var client = new OpcUaSecurityKeyServiceClient(
                BuildSksEndpoint(MessageSecurityMode.SignAndEncrypt),
                new ApplicationConfiguration(NUnitTelemetryContext.Create()),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider());

            Assert.That(client, Is.Not.Null);
        }


        [Test]
        [TestSpec("8.3.2", Part = 14, Summary = "Malformed SKS durations are rejected")]
        public void GetSecurityKeysAsyncRejectsMalformedKeyLifetime()
        {
            CallResponse response = BuildSuccessfulResponse();
            ArrayOf<Variant> original = response.Results[0].OutputArguments;
            response.Results[0].OutputArguments = new Variant[]
            {
                original[0],
                original[1],
                original[2],
                original[3],
                Variant.From(0.0)
            };
            (Mock<ISession> session, _) = BuildSessionMock(response);
            var client = new OpcUaSecurityKeyServiceClient(
                _ => new ValueTask<ISession>(session.Object),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider());

            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await client.GetSecurityKeysAsync(new SksKeyRequest("g", 0U, 1U)).ConfigureAwait(false))!;

            Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("KeyLifetime"));
        }

        [Test]
        public void Constructor_RejectsNullTelemetry()
        {
            Assert.That(
                () => new OpcUaSecurityKeyServiceClient(
                    _ => new ValueTask<ISession>((ISession)null!),
                    null!,
                    new FakeTimeProvider()),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
