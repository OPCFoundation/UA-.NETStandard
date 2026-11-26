// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using UaLens.Tests.Observe;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class CellProviderRestoreTests
{
    [TestCase("robotics")]
    [TestCase("vision")]
    [TestCase("openusd")]
    public void AbsentCompanionNamespacesFailWithoutAnyBrowseOrMutation(string providerId)
    {
        var session = new CellProviderTestSession("urn:unrelated");
        ICompanionProvider provider = providerId switch
        {
            "robotics" => new RoboticsCompanionProvider(),
            "vision" => new VisionCompanionProvider(),
            _ => new OpenUsdCompanionProvider()
        };

        Assert.That(
            async () => await provider.DiscoverAsync(session.Context, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadNotSupported));
        session.Session.Verify(item => item.BrowseAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
            It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
        session.VerifyNoMutationOrSessionOwnership();
    }

    [TestCase("robotics")]
    [TestCase("vision")]
    [TestCase("openusd")]
    public async Task RestoringARealCellProviderNeverOpensItsReaderOrRetainsArmingAsync(string providerId)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            int reads = 0;
            var robotics = new RoboticsCompanionProvider(_ =>
            {
                reads++;
                throw new InvalidOperationException("A restored document must remain passive.");
            });
            var vision = new VisionCompanionProvider(_ =>
            {
                reads++;
                throw new InvalidOperationException("A restored document must remain passive.");
            });
            var openUsd = new OpenUsdCompanionProvider((_, _) =>
            {
                reads++;
                throw new InvalidOperationException("A restored document must remain passive.");
            }, new OpenUsdCompanionExporter());
            var workspace = new CompanionWorkspace([robotics, vision, openUsd], host.Telemetry);
            var document = new CompanionPlugin(host.Host, workspace)
            {
                ConfirmLocalSample = true,
                OperationInput = "do-not-restore-this-path"
            };
            await using (document.ConfigureAwait(false))
            {
                JsonElement state = JsonSerializer.SerializeToElement(
                    new CompanionDocumentState(1, providerId, "nsu=urn:cell;i=123"),
                    CompanionJsonContext.Default.CompanionDocumentState);
                await document.RestoreStateAsync(state).ConfigureAwait(false);

                Assert.That(reads, Is.Zero);
                Assert.That(document.SelectedProvider?.Id, Is.EqualTo(providerId));
                Assert.That(document.Targets, Is.Empty);
                Assert.That(document.Operations, Is.Empty);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.OperationInput, Is.Empty);
                Assert.That(document.CaptureState().GetRawText(), Does.Not.Contain("do-not-restore-this-path"));
            }
        }
    }
}
