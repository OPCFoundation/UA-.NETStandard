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
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Identity;
using UaLens.Connection;
using UaLens.Plugins.Continuity;
using UaLens.Telemetry;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class ContinuitySessionFactoryTests
{
    [Test]
    public void DefaultSameEndpointSetupIsAvailableButDoesNotAssertDurabilityOrRedundancy()
    {
        var factory = CreateFactory(Profile());

        Assert.That(factory.CheckSetup(ContinuityScenario.TransferOnLoad).CanStart, Is.True);
        Assert.That(factory.CheckSetup(ContinuityScenario.TransferOnLoad).Description, Does.Contain("unproven"));
        Assert.That(factory.CheckSetup(ContinuityScenario.GracefulDurableRestore).Availability,
            Is.EqualTo(ContinuityAvailability.RequiresConfiguration));
        Assert.That(factory.CheckSetup(ContinuityScenario.ConfiguredFailover).Availability,
            Is.EqualTo(ContinuityAvailability.RequiresConfiguration));
    }

    [Test]
    public void InteractivePasswordsAndUnconfiguredApplicationIdentityCannotBeCopiedIntoLabSessions()
    {
        var username = CreateFactory(Profile() with
        {
            IdentityType = UserTokenType.UserName,
            IdentityName = "operator"
        });
        var application = CreateFactory(Profile() with { ApplicationIdentityId = "configured-application" });

        Assert.That(username.CheckSetup(ContinuityScenario.TransferOnLoad).CanStart, Is.False);
        Assert.That(username.CheckSetup(ContinuityScenario.TransferOnLoad).Description,
            Does.Contain("not copied"));
        Assert.That(application.CheckSetup(ContinuityScenario.TransferOnLoad).CanStart, Is.False);
    }

    [Test]
    public async Task RedundantOpenIsRejectedBeforeAnyCredentialsOrBackendWork()
    {
        var backend = new Mock<IConnectionBackend>(MockBehavior.Strict);
        var factory = new WorkspaceContinuitySessionFactory(
            () => Profile(),
            (_, _) => throw new InvalidOperationException("Credentials must not be requested."),
            Telemetry(),
            backend.Object);

        await Assert.ThatAsync(
            () => factory.OpenAsync(ContinuitySessionPurpose.RedundantTarget, CancellationToken.None),
            Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
        backend.VerifyNoOtherCalls();
    }

    [Test]
    public async Task EndpointMismatchFailsClosedAndDisposesTheAcquiredProviderNotTheSharedBackend()
    {
        var backend = new Mock<IConnectionBackend>();
        Mock<IAsyncDisposable> backendLifetime = backend.As<IAsyncDisposable>();
        var provider = new Mock<IClientIdentityProvider>();
        Mock<IAsyncDisposable> providerLifetime = provider.As<IAsyncDisposable>();
        providerLifetime.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        backend.Setup(value => value.CreateConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationConfiguration());
        backend.Setup(value => value.DiscoverAsync(
            It.IsAny<ApplicationConfiguration>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArrayOf<EndpointDescription>(new[]
            {
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://unselected.test:4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            }));
        ConnectionProfile? resolvedProfile = null;
        ConnectionProfile selected = Profile();
        var factory = new WorkspaceContinuitySessionFactory(
            () => selected,
            (profile, _) =>
            {
                resolvedProfile = profile;
                return ValueTask.FromResult(provider.Object);
            },
            Telemetry(),
            backend.Object);

        await Assert.ThatAsync(() => factory.OpenAsync(ContinuitySessionPurpose.Source, CancellationToken.None),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(resolvedProfile, Is.SameAs(selected));
        providerLifetime.Verify(value => value.DisposeAsync(), Times.Once);
        backendLifetime.Verify(value => value.DisposeAsync(), Times.Never);
        backend.Verify(value => value.ConnectAsync(
            It.IsAny<ApplicationConfiguration>(), It.IsAny<EndpointDescription>(), It.IsAny<ConnectionProfile>(),
            It.IsAny<IClientIdentityProvider>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task CanceledAuxiliaryOpenDoesNotRequestCredentialsOrConstructAConnection()
    {
        var backend = new Mock<IConnectionBackend>(MockBehavior.Strict);
        var factory = new WorkspaceContinuitySessionFactory(
            () => Profile(),
            (_, _) => throw new InvalidOperationException("Canceled work must not resolve credentials."),
            Telemetry(),
            backend.Object);

        await Assert.ThatAsync(
            () => factory.OpenAsync(ContinuitySessionPurpose.Source, new CancellationToken(true)),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        backend.VerifyNoOtherCalls();
    }

    private static WorkspaceContinuitySessionFactory CreateFactory(ConnectionProfile profile)
    {
        return new WorkspaceContinuitySessionFactory(
            () => profile,
            (_, _) => throw new InvalidOperationException("This test only checks setup."),
            Telemetry());
    }

    private static AppTelemetryContext Telemetry() => new(new LogRingBuffer(16));

    private static ConnectionProfile Profile()
    {
        return new ConnectionProfile
        {
            EndpointUrl = "opc.tcp://continuity.test:4840",
            SecurityMode = MessageSecurityMode.None,
            SecurityPolicyUri = SecurityPolicies.None,
            IdentityType = UserTokenType.Anonymous,
            UserTokenPolicyId = "anonymous"
        };
    }
}
