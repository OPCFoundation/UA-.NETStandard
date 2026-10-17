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

using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.PubSub;
using UaLens.Tests.Desktop;
using UaLens.Tests.Observe;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.PubSub
{
    [TestFixture]
    [Platform("Win,Linux")]
    [Category("LensDesktopMainline")]
    [NonParallelizable]
    public sealed class PubSubStructuredValueWorkflowTests
    {
        [Test]
        public async Task LocalStructureEditingUsesTheSharedEditorWithoutStartingTrafficAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var host = new ObserveTestHost();
                await using (host.ConfigureAwait(true))
                {
                    var plugin = new PubSubPlugin(host.Host);
                    await using (plugin.ConfigureAwait(true))
                    {
                        PubSubConfiguration configuration = Configuration();
                        await plugin.RestoreStateAsync(PubSubStateCodec.Capture(configuration)).ConfigureAwait(true);
                        DesktopInteraction.Owner.Content = ((IPlugin)plugin).View;
                        Task editing = Task.CompletedTask;
                        ComplexValueElementDialog dialog =
                            await DesktopInteraction.OpenedAsync<ComplexValueElementDialog>(
                                () => editing = plugin.EditSampleCommand.ExecuteAsync(null)).ConfigureAwait(true);
                        Button accept = DesktopInteraction.Control<Button>(dialog, "OkButton");
                        await DesktopInteraction.ChangedAsync(accept, () => accept.IsEnabled, () => Task.CompletedTask)
                            .ConfigureAwait(true);
                        ComplexValueEditor editor = DesktopInteraction.Control<ComplexValueEditor>(dialog, "Editor");
                        TextBox value = StructuredEditorActions.Body(editor).GetLogicalDescendants()
                            .OfType<TextBox>().Single();
                        value.Text = "37.5";
                        DesktopInteraction.Click(accept);
                        await editing.ConfigureAwait(true);
                        Assert.That(plugin.OperationError, Is.Empty);
                        Assert.That(plugin.IsRunning, Is.False);
                        Assert.That(plugin.SampleEditor, Is.Null);
                        Assert.That(plugin.AllowPublication, Is.False);
                        Assert.That(plugin.AllowWriteBack, Is.False);
                        await plugin.ApplyConfigurationCommand.ExecuteAsync(null).ConfigureAwait(true);
                        PubSubConfiguration captured = PubSubStateCodec.Restore(plugin.CaptureState());
                        var service = new PubSubConfiguredValueService(captured, host.Telemetry);
                        Variant sample = service.ReadSample(captured.Fields[0]);
                        Assert.That(
                            sample.TryGetValue<IEncodeable>(out IEncodeable? body, service.MessageContext), Is.True);
                        Assert.That(((IStructure)body!)["Temperature"], Is.EqualTo(Variant.From(37.5)));
                        Assert.That(host.Host.Session, Is.Null);
                    }
                }
            }).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StoppingOrClosingDrainsTheLocalSampleEditorWithoutSavingValuesAsync(bool close)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var host = new ObserveTestHost();
                await using (host.ConfigureAwait(true))
                {
                    var plugin = new PubSubPlugin(host.Host);
                    await using (plugin.ConfigureAwait(true))
                    {
                        await plugin.RestoreStateAsync(PubSubStateCodec.Capture(Configuration())).ConfigureAwait(true);
                        DesktopInteraction.Owner.Content = ((IPlugin)plugin).View;
                        Task editing = Task.CompletedTask;
                        ComplexValueElementDialog dialog =
                            await DesktopInteraction.OpenedAsync<ComplexValueElementDialog>(
                                () => editing = plugin.EditSampleCommand.ExecuteAsync(null)).ConfigureAwait(true);
                        if (close)
                        {
                            await plugin.DisposeAsync().ConfigureAwait(true);
                        }
                        else
                        {
                            await plugin.StopCommand.ExecuteAsync(null).ConfigureAwait(true);
                        }
                        await editing.ConfigureAwait(true);
                        Assert.That(dialog.IsVisible, Is.False);
                        Assert.That(dialog.WasCommitted, Is.False);
                        Assert.That(plugin.SampleEditor, Is.Null);
                        Assert.That(plugin.FieldDrafts[0].SampleValue, Is.Empty);
                        Assert.That(plugin.IsRunning, Is.False);
                    }
                }
            }).ConfigureAwait(false);
        }

        private static PubSubConfiguration Configuration()
        {
            const string model = "urn:ualens:test:offline-pubsub";
            return new PubSubConfiguration
            {
                Namespaces = [Namespaces.OpcUa, model],
                Fields = [new() { Name = "Reading", Type = BuiltInType.ExtensionObject,
                    DataTypeId = "nsu=" + model + ";i=5001" }],
                Structures = [new()
                {
                    Name = "ReadingType", DataTypeId = "nsu=" + model + ";i=5001",
                    BinaryEncodingId = "nsu=" + model + ";i=5002",
                    Fields = [new() { Name = "Temperature", DataTypeId = "i=11" }]
                }]
            };
        }
    }
}
