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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Security.Certificates;
using UaLens.Connection;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class ConnectionTrustScopeTests
{
    [Test]
    public async Task RealValidatorAcceptsOnlyWhileTheConnectionScopeIsAlive()
    {
        ITelemetryContext telemetry = CreateTelemetry();
        var manager = new CertificateManager(telemetry);
        await using (manager.ConfigureAwait(false))
        {
            using Certificate certificate = CertificateBuilder
                .Create("CN=Scoped untrusted peer")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            (EndpointDescription endpoint, ConnectionProfile profile) = CreateProfile(certificate);
            using (var scope = new ConnectionTrustScope(profile, endpoint, manager, telemetry))
            {
                Opc.Ua.CertificateValidationResult rejected = await manager
                    .ValidateAsync(certificate)
                    .ConfigureAwait(false);
                Assert.That(rejected.IsValid, Is.False);
                CertificateTrustRequest? request = scope.TakeRequest();
                Assert.That(request, Is.Not.Null);
                scope.AcceptOnce(request!);

                Opc.Ua.CertificateValidationResult accepted = await manager
                    .ValidateAsync(certificate)
                    .ConfigureAwait(false);
                Assert.That(accepted.IsValid, Is.True);
            }

            Opc.Ua.CertificateValidationResult afterDisconnect = await manager
                .ValidateAsync(certificate)
                .ConfigureAwait(false);
            Assert.That(afterDisconnect.IsValid, Is.False);
            Assert.That(manager.AcceptError, Is.Null);
        }
    }

    [Test]
    public async Task RealValidatorCannotTurnAnExpiredUntrustedCertificateIntoATrustPrompt()
    {
        ITelemetryContext telemetry = CreateTelemetry();
        var manager = new CertificateManager(telemetry);
        await using (manager.ConfigureAwait(false))
        {
            using Certificate expired = CertificateBuilder
                .Create("CN=Expired untrusted peer")
                .SetNotBefore(DateTime.UtcNow.AddDays(-10))
                .SetNotAfter(DateTime.UtcNow.AddDays(-1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            (EndpointDescription endpoint, ConnectionProfile profile) = CreateProfile(expired);
            using var scope = new ConnectionTrustScope(profile, endpoint, manager, telemetry);

            Opc.Ua.CertificateValidationResult result = await manager.ValidateAsync(expired).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(scope.TakeRequest(), Is.Null);
            Assert.That(
                manager.AcceptError!(expired, new ServiceResult(StatusCodes.BadCertificateTimeInvalid)),
                Is.False);
        }
    }

    [Test]
    public void AcceptOnceCannotBeMovedToAnotherEndpoint()
    {
        ITelemetryContext telemetry = CreateTelemetry();
        using Certificate certificate = CertificateBuilder
            .Create("CN=Endpoint-scoped peer")
            .SetRSAKeySize(2048)
            .CreateForRSA();
        (EndpointDescription endpoint, ConnectionProfile profile) = CreateProfile(certificate);
        var validator = new Mock<ICertificateValidatorEx>();
        validator.SetupProperty(value => value.AcceptError);
        using var scope = new ConnectionTrustScope(profile, endpoint, validator.Object, telemetry);
        var request = new CertificateTrustRequest(
            profile with { EndpointUrl = "opc.tcp://localhost:4901/different" },
            ByteString.From(certificate.RawData),
            new ServiceResult(StatusCodes.BadCertificateUntrusted));

        Assert.That(() => scope.AcceptOnce(request), Throws.InvalidOperationException);
        Assert.That(validator.Object.AcceptError!(
            certificate, new ServiceResult(StatusCodes.BadCertificateUntrusted)), Is.False);
    }

    private static ITelemetryContext CreateTelemetry()
    {
        var telemetry = new Mock<ITelemetryContext>();
        telemetry.SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
        return telemetry.Object;
    }

    private static (EndpointDescription Endpoint, ConnectionProfile Profile) CreateProfile(Certificate certificate)
    {
        var endpoint = new EndpointDescription("opc.tcp://localhost:4900/primary")
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            ServerCertificate = ByteString.From(certificate.RawData),
            UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" }]
        };
        ConnectionProfile profile = ConnectionProfile.Create(
            endpoint, endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2);
        return (endpoint, profile);
    }
}
