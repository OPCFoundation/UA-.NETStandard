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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using UaLens.Plugins.PubSub;
using UaLens.Tests.Observe;

namespace UaLens.Tests.PubSub;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class PubSubCommissioningWorkflowTests
{
    [Test]
    public void CatalogAndPresetsUseOnlyInstalledProvidersWithoutAcquiringResources()
    {
        var transport = new Mock<IPubSubTransportProvider>(MockBehavior.Strict);
        transport.SetupGet(value => value.Id).Returns("dtls-configured");
        transport.SetupGet(value => value.Profiles).Returns((ArrayOf<PubSubProfile>)[PubSubProfile.DtlsUadp]);
        var factory = new PubSubRuntimeFactory(
            DefaultTelemetry.Create(static _ => { }), transportProviders: [transport.Object]);
        PubSubProviderCatalog catalog = factory.Catalog;
        Assert.That(catalog.Transports.Contains(choice =>
            choice.Profile == PubSubProfile.DtlsUadp && choice.ProviderId == "dtls-configured"), Is.True);
        Assert.That(catalog.Transports.Contains(choice => choice.Profile == PubSubProfile.EthernetUadp), Is.False);
        ArrayOf<PubSubPreset> presets = PubSubPresets.Create(catalog);
        Assert.That(presets.Count, Is.EqualTo(catalog.Transports.Count * 2));
        Assert.That(PubSubPresets.Create(PubSubProviderCatalog.Empty).IsEmpty, Is.True);
        foreach (PubSubPreset preset in presets)
        {
            Assert.That(preset.Configuration.Endpoint, Is.Empty);
            Assert.That(preset.Configuration.NetworkInterface, Is.Empty);
            Assert.That(preset.Configuration.CredentialReference, Is.Empty);
            Assert.That(preset.Configuration.AdapterProviderId, Is.Empty);
            Assert.That(preset.Configuration.WriteBackEnabled, Is.False);
            Assert.That(preset.Configuration.ActionResponderEnabled, Is.False);
            Assert.That(PubSubConfigurationValidation.Inspect(preset.Configuration, requireEndpoint: false).IsEmpty,
                Is.True, preset.Name);
            if (preset.Configuration.IsBroker || preset.Configuration.SecurityMode == MessageSecurityMode.None ||
                preset.Configuration.Publication != PubSubPublication.Disabled)
            {
                Assert.That(() => PubSubConfigurationValidation.RequireAuthorization(preset.Configuration, new()),
                    Throws.InvalidOperationException);
            }
            else
            {
                PubSubConfigurationValidation.RequireAuthorization(preset.Configuration, new());
            }
        }
        transport.Verify(value => value.AcquireAsync(
            It.IsAny<PubSubConfiguration>(), It.IsAny<PubSubProviderContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        transport.Verify(value => value.Inspect(It.IsAny<PubSubConfiguration>()), Times.Never);
    }

    [Test]
    public void FieldAndActionEditorsPreserveTypesIdsAndInputOrder()
    {
        var id = new Guid("0018e36c-5e5c-4e9f-8fd8-6a2aa635ff5e");
        var original = new PubSubFieldConfiguration
        {
            Name = "Temperature", Type = BuiltInType.Double, FieldId = id, SourceNodeId = "i=2258"
        };
        var draft = new PubSubFieldDraft(original);
        Assert.That(draft.ToConfiguration(), Is.EqualTo(original));
        draft.Name = "Edited";
        draft.FieldId = "not-a-guid";
        Assert.That(() => draft.ToConfiguration(), Throws.ArgumentException);
        Assert.That(original.Name, Is.EqualTo("Temperature"));
        draft.FieldId = id.ToString("D");
        Assert.That(draft.ToConfiguration().Name, Is.EqualTo("Edited"));
        var input = new PubSubActionInputEditor("Count") { Type = BuiltInType.UInt32, Text = "17" };
        ArrayOf<DataSetField> fields = PubSubActionInputs.Create([input.ToDraft(),
            new PubSubActionInputDraft("Enabled", BuiltInType.Boolean, "true")]);
        Assert.That(fields.Count, Is.EqualTo(2));
        Assert.That(fields[0].Name, Is.EqualTo("Count"));
        Assert.That(fields[0].Value.TryGetValue(out uint count), Is.True);
        Assert.That(count, Is.EqualTo(17u));
        Assert.That(fields[1].Name, Is.EqualTo("Enabled"));
        Assert.That(fields[1].Value.TryGetValue(out bool enabled), Is.True);
        Assert.That(enabled, Is.True);
        input.Text = "-1";
        Assert.That(() => PubSubActionInputs.Create([input.ToDraft()]), Throws.TypeOf<JsonException>());
        Assert.That(fields[0].Value.TryGetValue(out count), Is.True);
        Assert.That(count, Is.EqualTo(17u));
    }

    [TestCase("name")]
    [TestCase("type")]
    [TestCase("mapping")]
    [TestCase("mask")]
    [TestCase("identity")]
    [TestCase("action")]
    public async Task EditingCommissioningDraftsRevokesConsentWithoutMutatingAppliedConfigurationAsync(string field)
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var runtime = new PubSubTestRuntime();
            var plugin = new PubSubPlugin(host.Host, runtime.Factory.Object);
            await using (plugin.ConfigureAwait(false))
            {
                await plugin.RestoreStateAsync(PubSubStateCodec.Capture(PubSubTestRuntime.Configuration))
                    .ConfigureAwait(false);
                string applied = plugin.CaptureState().GetRawText();
                plugin.AllowUnsecured = true;
                plugin.AllowPublication = true;
                plugin.AllowWriteBack = true;
                plugin.AllowResponder = true;
                plugin.AllowAction = true;
                switch (field)
                {
                    case "name":
                        plugin.FieldDrafts[0].Name = "Edited";
                        break;
                    case "type":
                        plugin.FieldDrafts[0].Type = BuiltInType.Double;
                        break;
                    case "mapping":
                        plugin.FieldDrafts[0].TargetNodeId = "i=2258";
                        break;
                    case "mask":
                        plugin.NetworkMaskOptions.First().IsSelected = !plugin.NetworkMaskOptions.First().IsSelected;
                        break;
                    case "identity":
                        plugin.LocalPublisherId = 42;
                        break;
                    case "action":
                        plugin.ActionName = "ChangedAction";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(field));
                }
                Assert.That(plugin.AllowUnsecured, Is.False);
                Assert.That(plugin.AllowPublication, Is.False);
                Assert.That(plugin.AllowWriteBack, Is.False);
                Assert.That(plugin.AllowResponder, Is.False);
                Assert.That(plugin.AllowAction, Is.False);
                Assert.That(plugin.CaptureState().GetRawText(), Is.EqualTo(applied));
                Assert.That(plugin.IsRunning, Is.False);
                runtime.Factory.Verify(value => value.CreateAsync(
                    It.IsAny<PubSubConfiguration>(), It.IsAny<PubSubStartAuthorization>(),
                    It.IsAny<Opc.Ua.Client.ISession>(),
                    It.IsAny<PubSubObservationStore>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }
    }

    [Test]
    public async Task StructuredApplyNeedsFreshConsentAndTransientActionFieldsNeverPersistAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var runtime = new PubSubTestRuntime();
            var plugin = new PubSubPlugin(host.Host, runtime.Factory.Object);
            await using (plugin.ConfigureAwait(false))
            {
                await plugin.RestoreStateAsync(PubSubStateCodec.Capture(PubSubTestRuntime.Configuration))
                    .ConfigureAwait(false);
                plugin.FieldDrafts[0].Name = "Edited";
                plugin.AllowUnsecured = true;
                await plugin.StartCommand.ExecuteAsync(null).ConfigureAwait(false);
                Assert.That(plugin.OperationError, Does.Contain("Prerequisites"));
                Assert.That(runtime.Started.Task.IsCompleted, Is.False);
                await plugin.ApplyConfigurationCommand.ExecuteAsync(null).ConfigureAwait(false);
                Assert.That(PubSubStateCodec.Restore(plugin.CaptureState()).Fields[0].Name, Is.EqualTo("Edited"));
                Assert.That(plugin.AllowUnsecured, Is.False);
                plugin.AddActionInputCommand.Execute(null);
                plugin.ActionInputDrafts[0].Text = "13579";
                Assert.That(plugin.CaptureState().GetRawText(), Does.Not.Contain("13579"));
                plugin.AllowAction = true;
                plugin.ActionInputDrafts[0].Text = "24680";
                Assert.That(plugin.AllowAction, Is.False);
                plugin.AllowUnsecured = true;
                await plugin.StartCommand.ExecuteAsync(null).ConfigureAwait(false);
                Assert.That(plugin.IsRunning, Is.True, plugin.OperationError);
                Assert.That(runtime.Started.Task.IsCompletedSuccessfully, Is.True);
                Assert.That(plugin.AllowUnsecured, Is.False);
                await plugin.StopCommand.ExecuteAsync(null).ConfigureAwait(false);
                Assert.That(runtime.Disposed.Task.IsCompletedSuccessfully, Is.True);
            }
        }
    }

    [Test]
    public async Task ImportIsAtomicOfflineAndLeavesDraftAndConsentUntouchedWhenParsingFailsAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var runtime = new PubSubTestRuntime();
            var plugin = new PubSubPlugin(host.Host, runtime.Factory.Object);
            await using (plugin.ConfigureAwait(false))
            {
                await plugin.RestoreStateAsync(PubSubStateCodec.Capture(PubSubTestRuntime.Configuration))
                    .ConfigureAwait(false);
                plugin.AllowUnsecured = true;
                using var invalid = new MemoryStream(Encoding.UTF8.GetBytes("{\"fields\":[]}"));
                string before = plugin.CaptureState().GetRawText();
                await Assert.ThatAsync(() => plugin.ImportConfigurationAsync(invalid), Throws.TypeOf<JsonException>())
                    .ConfigureAwait(false);
                Assert.That(plugin.CaptureState().GetRawText(), Is.EqualTo(before));
                Assert.That(plugin.AllowUnsecured, Is.True);
                PubSubConfiguration imported = PubSubTestRuntime.Configuration with { DataSetWriterId = 456 };
                using var valid = new MemoryStream(Encoding.UTF8.GetBytes(PubSubStateCodec.Format(imported)));
                await plugin.ImportConfigurationAsync(valid).ConfigureAwait(false);
                Assert.That(plugin.DataSetWriterId, Is.EqualTo(456));
                Assert.That(plugin.AllowUnsecured, Is.False);
                Assert.That(plugin.IsRunning, Is.False);
                Assert.That(plugin.ActionInputDrafts, Is.Empty);
                runtime.Factory.Verify(value => value.CreateAsync(
                    It.IsAny<PubSubConfiguration>(), It.IsAny<PubSubStartAuthorization>(),
                    It.IsAny<Opc.Ua.Client.ISession>(),
                    It.IsAny<PubSubObservationStore>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }

    }

    [Test]
    public async Task DelayedConfigurationImportCannotOverwriteNewerLocalEditsAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var runtime = new PubSubTestRuntime();
            var plugin = new PubSubPlugin(host.Host, runtime.Factory.Object);
            await using (plugin.ConfigureAwait(false))
            {
                await plugin.RestoreStateAsync(PubSubStateCodec.Capture(PubSubTestRuntime.Configuration))
                    .ConfigureAwait(false);
                using var stream = new DelayedReadStream(
                    Encoding.UTF8.GetBytes(PubSubStateCodec.Format(
                        PubSubTestRuntime.Configuration with { DataSetWriterId = 555 })),
                    () => plugin.FieldDrafts[0].Name = "Newer local edit");
                await Assert.ThatAsync(() => plugin.ImportConfigurationAsync(stream), Throws.InvalidOperationException)
                    .ConfigureAwait(false);
                Assert.That(plugin.FieldDrafts[0].Name, Is.EqualTo("Newer local edit"));
                Assert.That(PubSubStateCodec.Restore(plugin.CaptureState()).DataSetWriterId, Is.EqualTo(1));
                Assert.That(plugin.IsRunning, Is.False);
            }
        }
    }

    private sealed class DelayedReadStream(byte[] bytes, Action editDuringRead) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_edited)
            {
                m_edited = true;
                editDuringRead();
            }
            return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        private bool m_edited;
    }
}
