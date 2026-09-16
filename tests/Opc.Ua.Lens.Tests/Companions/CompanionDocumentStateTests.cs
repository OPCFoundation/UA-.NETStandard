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
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using UaLens.Plugins.Companions;
using UaLens.Tests.Observe;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class CompanionDocumentStateTests
{
    [Test]
    public async Task RestoringConfigurationDoesNotDiscoverOrArmSampleOperationsAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            Mock<ICompanionProvider> provider = CreateProvider();
            var workspace = new CompanionWorkspace([provider.Object], host.Telemetry);
            var document = new CompanionPlugin(host.Host, workspace)
            {
                ConfirmLocalSample = true,
                OperationInput = "ephemeral task input"
            };
            await using (document.ConfigureAwait(false))
            {
                await document.RestoreStateAsync(State(new CompanionDocumentState(
                    1, "sample", "nsu=urn:ualens:test;i=1234"))).ConfigureAwait(false);

                Assert.That(document.SelectedProvider?.Id, Is.EqualTo("sample"));
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.OperationInput, Is.Empty);
                Assert.That(document.Targets, Is.Empty);
                Assert.That(document.Values, Is.Empty);
                Assert.That(document.Operations, Is.Empty);
                Assert.That(document.Status, Does.Contain("no task or workload"));
                Assert.That(document.CaptureState().GetProperty("TargetId").GetString(),
                    Is.EqualTo("nsu=urn:ualens:test;i=1234"));
                provider.Verify(
                    item => item.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()),
                    Times.Never);
            }
        }
    }

    [Test]
    public async Task CapturedStateExcludesOperationInputAndSampleConfirmationAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            Mock<ICompanionProvider> provider = CreateProvider();
            var workspace = new CompanionWorkspace([provider.Object], host.Telemetry);
            var document = new CompanionPlugin(host.Host, workspace)
            {
                ConfirmLocalSample = true,
                OperationInput = "marker-that-must-not-be-persisted"
            };
            await using (document.ConfigureAwait(false))
            {
                JsonElement state = document.CaptureState();
                Assert.That(state.GetProperty("ProviderId").GetString(), Is.EqualTo("sample"));
                Assert.That(state.GetRawText(), Does.Not.Contain("marker-that-must-not-be-persisted"));
                Assert.That(state.TryGetProperty("ConfirmLocalSample", out _), Is.False);
                Assert.That(state.TryGetProperty("OperationInput", out _), Is.False);
            }
        }
    }

    [Test]
    public async Task CapturingAPreparedDocumentPersistsOnlyConfigurationWithoutDisarmingItAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var context = new CompanionPreparedOperationTestContext(
                CompanionOperationSafety.SampleMutation, telemetry: host.Telemetry);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await PrepareDocumentAsync(document, context).ConfigureAwait(false);
                CompanionOperationDraft draft = document.PreparedOperation ??
                    throw new InvalidOperationException("Expected a prepared task before capturing state.");
                Assert.That(draft.Input, Is.EqualTo("prepared-input-must-not-be-persisted"));
                Assert.That(document.ConfirmLocalSample, Is.True);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);

                JsonElement state = document.CaptureState();

                string[] expectedProperties = ["Version", "ProviderId", "TargetId"];
                Assert.That(state.EnumerateObject().Select(property => property.Name),
                    Is.EquivalentTo(expectedProperties));
                Assert.That(state.GetProperty("Version").GetInt32(), Is.EqualTo(1));
                Assert.That(state.GetProperty("ProviderId").GetString(), Is.EqualTo("sample"));
                Assert.That(state.GetRawText(), Does.Not.Contain("prepared-input-must-not-be-persisted"));
                Assert.That(state.GetRawText(), Does.Not.Contain("Apply recipe"));
                Assert.That(document.PreparedOperation, Is.SameAs(draft));
                Assert.That(document.ConfirmLocalSample, Is.True);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);
                context.VerifyInspectionCount(2);
                context.VerifyExecutionCount(0);
            }
        }
    }

    [Test]
    public async Task RestoreDiscardsPreparedAndInjectedAuthorizationStateWithoutProviderWorkAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var context = new CompanionPreparedOperationTestContext(
                CompanionOperationSafety.SampleMutation, telemetry: host.Telemetry);
            var document = new CompanionPlugin(host.Host, context.Workspace);
            await using (document.ConfigureAwait(false))
            {
                await PrepareDocumentAsync(document, context).ConfigureAwait(false);
                CompanionOperationDraft draft = document.PreparedOperation ??
                    throw new InvalidOperationException("Expected a prepared task before restoring state.");
                Assert.That(document.ConfirmLocalSample, Is.True);
                Assert.That(document.Targets, Has.Count.EqualTo(1));
                Assert.That(document.Values[0].Name, Is.EqualTo("Temperature"));
                using JsonDocument saved = JsonDocument.Parse("""
                    {
                      "Version": 1,
                      "ProviderId": "sample",
                      "TargetId": "nsu=urn:ualens:test;i=1234",
                      "OperationInput": "restored-input-must-not-arm-a-task",
                      "ConfirmLocalSample": true,
                      "PreparedOperation": { "OperationId": "apply", "Input": "restored recipe" }
                    }
                    """);

                await document.RestoreStateAsync(saved.RootElement).ConfigureAwait(false);

                Assert.That(document.SelectedProvider?.Id, Is.EqualTo("sample"));
                Assert.That(document.PreparedOperation, Is.Null);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.OperationInput, Is.Empty);
                Assert.That(document.SelectedTarget, Is.Null);
                Assert.That(document.SelectedOperation, Is.Null);
                Assert.That(document.Targets, Is.Empty);
                Assert.That(document.Values, Is.Empty);
                Assert.That(document.Operations, Is.Empty);
                Assert.That(document.PrepareTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                Assert.That(document.PreparationSummary, Does.StartWith("Prepare the selected task"));
                Assert.That(document.Status, Does.Contain("no task or workload"));
                Assert.That(document.CaptureState().GetProperty("TargetId").GetString(),
                    Is.EqualTo("nsu=urn:ualens:test;i=1234"));
                await Assert.ThatAsync(
                    () => context.Workspace.ExecuteAsync(draft, true),
                    Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
                context.Provider.Verify(
                    item => item.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()),
                    Times.Once);
                context.VerifyInspectionCount(2);
                context.VerifyExecutionCount(0);
            }
        }
    }

    [TestCase(2, "sample", null)]
    [TestCase(1, "unregistered", null)]
    [TestCase(1, "sample", "not a node id")]
    public async Task InvalidStateIsRejectedBeforeChangingConfigurationAsync(
        int version,
        string providerId,
        string? targetId)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            Mock<ICompanionProvider> provider = CreateProvider();
            var workspace = new CompanionWorkspace([provider.Object], host.Telemetry);
            var document = new CompanionPlugin(host.Host, workspace);
            await using (document.ConfigureAwait(false))
            {
                JsonElement before = document.CaptureState();
                Assert.That(
                    async () => await document.RestoreStateAsync(State(new CompanionDocumentState(
                        version, providerId, targetId))).ConfigureAwait(false),
                    Throws.TypeOf<JsonException>());
                Assert.That(document.CaptureState().GetRawText(), Is.EqualTo(before.GetRawText()));
            }
        }
    }

    [Test]
    public async Task DocumentClosureOwnsAndDisposesTheTaskWorkspaceAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            Mock<ICompanionProvider> provider = CreateProvider();
            var workspace = new CompanionWorkspace([provider.Object], host.Telemetry);
            var document = new CompanionPlugin(host.Host, workspace);

            await document.DisposeAsync().ConfigureAwait(false);
            await document.DisposeAsync().ConfigureAwait(false);

            Assert.That(() => workspace.DiscoverAsync("sample"), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(host.Connection.CurrentSession, Is.Null);
        }
    }

    private static JsonElement State(CompanionDocumentState state)
    {
        return JsonSerializer.SerializeToElement(state, CompanionJsonContext.Default.CompanionDocumentState);
    }

    private static async Task PrepareDocumentAsync(
        CompanionPlugin document,
        CompanionPreparedOperationTestContext context)
    {
        await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
        document.IsOffline = false;
        await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(false);
        await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(false);
        document.OperationInput = "prepared-input-must-not-be-persisted";
        await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(false);
        document.ConfirmLocalSample = true;
    }

    private static Mock<ICompanionProvider> CreateProvider()
    {
        var provider = new Mock<ICompanionProvider>(MockBehavior.Strict);
        provider.SetupGet(item => item.Descriptor)
            .Returns(new CompanionDescriptor("sample", "Sample model", "urn:ualens:test", "Test model"));
        return provider;
    }
}
