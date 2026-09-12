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
using Opc.Ua.Client;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class CompanionWorkspaceTests
{
    [Test]
    public void RejectsDuplicateProviderIdentities()
    {
        Mock<ICompanionProvider> provider = CreateProvider();
        Assert.That(
            () => new CompanionWorkspace([provider.Object, provider.Object], DefaultTelemetry.Create(static _ => { })),
            Throws.ArgumentException);
    }

    [Test]
    public async Task DisconnectedDiscoveryDoesNotInvokeAProviderAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider();
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await Assert.ThatAsync(
                () => workspace.DiscoverAsync("sample"),
                Throws.InvalidOperationException.With.Message.Contains("Connect")).ConfigureAwait(false);
            provider.Verify(
                item => item.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task DiscoveryAndInspectionPreserveTypedValuesAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider();
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(CreateSession().Object).ConfigureAwait(false);
            ArrayOf<CompanionTarget> targets = await workspace.DiscoverAsync("sample").ConfigureAwait(false);
            CompanionInspection inspection = await workspace.InspectAsync(targets[0]).ConfigureAwait(false);

            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0], Is.EqualTo(s_target));
            Assert.That(inspection.Values[0].Name, Is.EqualTo("Temperature"));
            Assert.That(inspection.Values[0].Value.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(42.5));
            Assert.That(inspection.Operations[0].Safety, Is.EqualTo(CompanionOperationSafety.ReadOnly));
        }
    }

    [Test]
    public async Task TargetMustBeDiscoveredOnTheCurrentConnectionAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider();
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(CreateSession().Object).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => workspace.InspectAsync(s_target),
                Throws.InvalidOperationException.With.Message.Contains("Discover")).ConfigureAwait(false);

            await workspace.DiscoverAsync("sample").ConfigureAwait(false);
            await workspace.BindAsync(CreateSession().Object).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => workspace.InspectAsync(s_target),
                Throws.InvalidOperationException).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task OperationMustComeFromTheCurrentInspectionAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider();
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(CreateSession().Object).ConfigureAwait(false);
            await workspace.DiscoverAsync("sample").ConfigureAwait(false);
            await Assert.ThatAsync(
                () => workspace.ExecuteAsync(s_target, "read", null, false),
                Throws.InvalidOperationException).ConfigureAwait(false);
            await workspace.InspectAsync(s_target).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => workspace.ExecuteAsync(s_target, "unoffered", null, false),
                Throws.InvalidOperationException).ConfigureAwait(false);
            provider.Verify(
                item => item.ExecuteAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SelectedReadOnlyOperationReachesTheProviderAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider();
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(CreateSession("opc.tcp://remote.example:4840/Server").Object)
                .ConfigureAwait(false);
            await workspace.DiscoverAsync("sample").ConfigureAwait(false);
            await workspace.InspectAsync(s_target).ConfigureAwait(false);
            CompanionOperationResult result = await workspace.ExecuteAsync(s_target, "read", null, false)
                .ConfigureAwait(false);

            Assert.That(result.Summary, Is.EqualTo("Read complete."));
            provider.Verify(
                item => item.ExecuteAsync(
                    It.IsAny<CompanionContext>(), s_target, "read", null, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase("opc.tcp://localhost:4840/Sample", false)]
    [TestCase("opc.tcp://remote.example:4840/Server", true)]
    [TestCase("opc.tcp://user@localhost:4840/Sample", true)]
    [TestCase("opc.tcp://localhost:4840/Sample#other", true)]
    public async Task SampleMutationRequiresBothLocalTargetAndConfirmationAsync(string endpoint, bool confirmed)
    {
        Mock<ICompanionProvider> provider = CreateProvider(CompanionOperationSafety.SampleMutation);
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(CreateSession(endpoint).Object).ConfigureAwait(false);
            await workspace.DiscoverAsync("sample").ConfigureAwait(false);
            await workspace.InspectAsync(s_target).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => workspace.ExecuteAsync(s_target, "read", null, confirmed),
                Throws.InvalidOperationException.With.Message.Contains("Sample operations")).ConfigureAwait(false);
            provider.Verify(
                item => item.ExecuteAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExplicitConfirmedLocalSampleMutationReachesTheProviderAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider(CompanionOperationSafety.SampleMutation);
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(CreateSession().Object).ConfigureAwait(false);
            await workspace.DiscoverAsync("sample").ConfigureAwait(false);
            await workspace.InspectAsync(s_target).ConfigureAwait(false);
            CompanionOperationResult result = await workspace.ExecuteAsync(s_target, "read", null, true)
                .ConfigureAwait(false);
            Assert.That(result.Summary, Is.EqualTo("Read complete."));
        }
    }

    [Test]
    public async Task LocalFileOperationRequiresADestinationAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider(CompanionOperationSafety.LocalFile);
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(CreateSession().Object).ConfigureAwait(false);
            await workspace.DiscoverAsync("sample").ConfigureAwait(false);
            await workspace.InspectAsync(s_target).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => workspace.ExecuteAsync(s_target, "read", null, false),
                Throws.ArgumentException.With.Message.Contains("destination")).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task FailedDiscoveryCanBeRetriedWithoutCachingAnEmptyResultAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider();
        provider.SetupSequence(item => item.DiscoverAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
            .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied))
            .Returns(() => ValueTask.FromResult<ArrayOf<CompanionTarget>>([s_target]));
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(CreateSession().Object).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => workspace.DiscoverAsync("sample"),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            ArrayOf<CompanionTarget> targets = await workspace.DiscoverAsync("sample").ConfigureAwait(false);
            Assert.That(targets[0], Is.EqualTo(s_target));
        }
    }

    [Test]
    public async Task RebindingCancelsInFlightDiscoveryBeforeReleasingTheOldSessionAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Setup(item => item.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (CompanionContext _, CancellationToken token) =>
            {
                started.SetResult();
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return ArrayOf<CompanionTarget>.Empty;
            });
        var oldSession = CreateSession();
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(oldSession.Object).ConfigureAwait(false);
            Task<ArrayOf<CompanionTarget>> discovery = workspace.DiscoverAsync("sample");
            await started.Task.ConfigureAwait(false);
            await workspace.BindAsync(CreateSession().Object).ConfigureAwait(false);

            await Assert.ThatAsync(
                () => discovery,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            oldSession.Verify(session => session.Dispose(), Times.Never);
        }
    }

    [Test]
    public async Task DuplicateTargetsAreRejectedInsteadOfOverwritingStateAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider();
        provider.Setup(item => item.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ArrayOf<CompanionTarget>>([s_target, s_target]));
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.BindAsync(CreateSession().Object).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => workspace.DiscoverAsync("sample"),
                Throws.InvalidOperationException.With.Message.Contains("duplicate")).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task DisposingPreventsNewOperationsAndDoesNotDisposeTheBorrowedSessionAsync()
    {
        Mock<ICompanionProvider> provider = CreateProvider();
        Mock<ISession> session = CreateSession();
        var workspace = new CompanionWorkspace([provider.Object], DefaultTelemetry.Create(static _ => { }));
        await workspace.BindAsync(session.Object).ConfigureAwait(false);
        await workspace.DisposeAsync().ConfigureAwait(false);
        await workspace.DisposeAsync().ConfigureAwait(false);

        await Assert.ThatAsync(() => workspace.DiscoverAsync("sample"), Throws.TypeOf<ObjectDisposedException>())
            .ConfigureAwait(false);
        session.Verify(item => item.Dispose(), Times.Never);
    }

    private static Mock<ISession> CreateSession(string endpoint = "opc.tcp://localhost:4840/Sample")
    {
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.SetupGet(item => item.Endpoint).Returns(new EndpointDescription { EndpointUrl = endpoint });
        return session;
    }

    private static Mock<ICompanionProvider> CreateProvider(
        CompanionOperationSafety safety = CompanionOperationSafety.ReadOnly)
    {
        var provider = new Mock<ICompanionProvider>(MockBehavior.Strict);
        provider.SetupGet(item => item.Descriptor)
            .Returns(new CompanionDescriptor("sample", "Sample", "urn:ualens:test", "Test model"));
        provider.Setup(item => item.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ArrayOf<CompanionTarget>>([s_target]));
        provider.Setup(item => item.InspectAsync(
                It.IsAny<CompanionContext>(), s_target, It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new CompanionInspection(
                [new CompanionValue("Temperature", Variant.From(42.5))],
                [new CompanionOperation("read", "Read", safety)],
                "Read-only inspection.")));
        provider.Setup(item => item.ExecuteAsync(
                It.IsAny<CompanionContext>(), s_target, "read", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new CompanionOperationResult("Read complete.", [])));
        return provider;
    }

    private static readonly CompanionTarget s_target = new("sample", new NodeId(1234u, 2), "Sample device", "Device");
}
