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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Deterministic, offline unit tests for the guard clauses and state helpers
    /// of <see cref="StandardServer"/> that are reachable without starting the server.
    /// </summary>
    [TestFixture]
    [Category("StandardServer")]
    [Parallelizable(ParallelScope.All)]
    public class StandardServerCoverageTests
    {
        private sealed class TestableStandardServer : StandardServer
        {
            public TestableStandardServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public TestableStandardServer(ITelemetryContext telemetry, TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
            }

            public TimeProvider TimeProviderAccessor => TimeProvider;

            public void ValidateRequestPublic(RequestHeader requestHeader)
            {
                ValidateRequest(requestHeader);
            }

            public void SetServerStatePublic(ServerState state)
            {
                SetServerState(state);
            }

            public void SetServerErrorPublic(ServiceResult error)
            {
                SetServerError(error);
            }

            public void OnApplicationCertificateErrorPublic(ByteString clientCertificate, ServiceResult result)
            {
                OnApplicationCertificateError(clientCertificate, result);
            }
        }

        private sealed class TestTimeProvider : TimeProvider;

        private static TestableStandardServer CreateServer()
        {
            return new TestableStandardServer(NUnitTelemetryContext.Create());
        }

        [Test]
        public void ConstructorUsesSystemTimeProviderByDefault()
        {
            using TestableStandardServer server = CreateServer();

            Assert.That(server.TimeProviderAccessor, Is.SameAs(TimeProvider.System));
        }

        [Test]
        public void ConstructorUsesProvidedTimeProvider()
        {
            var custom = new TestTimeProvider();
            using var server = new TestableStandardServer(NUnitTelemetryContext.Create(), custom);

            Assert.That(server.TimeProviderAccessor, Is.SameAs(custom));
        }

        [Test]
        public void LoadComplexTypesDefaultsToTrue()
        {
            using TestableStandardServer server = CreateServer();

            Assert.That(server.LoadComplexTypes, Is.True);
        }

        [Test]
        public void LoadComplexTypesCanBeDisabled()
        {
            using TestableStandardServer server = CreateServer();

            server.LoadComplexTypes = false;

            Assert.That(server.LoadComplexTypes, Is.False);
        }

        [Test]
        public void CurrentInstanceThrowsWhenNotStarted()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = server.CurrentInstance);
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public void CurrentStateThrowsWhenNotStarted()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = server.CurrentState);
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public void GetStatusThrowsWhenNotStarted()
        {
            using TestableStandardServer server = CreateServer();

#pragma warning disable CS0618 // GetStatus is obsolete but still exercised for coverage.
            Assert.That(() => server.GetStatus(), Throws.Exception);
#pragma warning restore CS0618
        }

        [Test]
        public void ValidateRequestThrowsWhenNotStarted()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.ValidateRequestPublic(new RequestHeader()));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public void SetServerStateThrowsWhenNotStarted()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.SetServerStatePublic(ServerState.Running));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public void SetServerErrorUpdatesServerErrorProperty()
        {
            using TestableStandardServer server = CreateServer();
            var error = new ServiceResult(StatusCodes.BadInternalError);

            server.SetServerErrorPublic(error);

            Assert.That(server.ServerError, Is.EqualTo(error));
        }

        [Test]
        public void ValidateRequestThrowsServerErrorWhenSet()
        {
            using TestableStandardServer server = CreateServer();
            server.SetServerErrorPublic(new ServiceResult(StatusCodes.BadInternalError));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.ValidateRequestPublic(new RequestHeader()));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInternalError));
        }

        [Test]
        public void SetServerStateThrowsServerErrorWhenSet()
        {
            using TestableStandardServer server = CreateServer();
            server.SetServerErrorPublic(new ServiceResult(StatusCodes.BadInternalError));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.SetServerStatePublic(ServerState.Running));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInternalError));
        }

        [Test]
        public void OnApplicationCertificateErrorMapsToSecurityChecksFailed()
        {
            StatusCode[] certStatusCodes =
            [
                StatusCodes.BadCertificateInvalid,
                StatusCodes.BadCertificateRevoked,
                StatusCodes.BadCertificateUntrusted,
                StatusCodes.BadCertificateIssuerRevoked,
                StatusCodes.BadCertificateRevocationUnknown,
                StatusCodes.BadCertificateChainIncomplete,
                StatusCodes.BadCertificateIssuerRevocationUnknown
            ];
            using TestableStandardServer server = CreateServer();

            foreach (StatusCode certStatusCode in certStatusCodes)
            {
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => server.OnApplicationCertificateErrorPublic(
                        ByteString.Empty,
                        new ServiceResult(certStatusCode)));
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadSecurityChecksFailed));
            }
        }

        [Test]
        public void OnApplicationCertificateErrorPassesThroughUnmappedCode()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.OnApplicationCertificateErrorPublic(
                    ByteString.Empty,
                    new ServiceResult(StatusCodes.BadCertificateTimeInvalid)));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadCertificateTimeInvalid));
        }
    }
}
