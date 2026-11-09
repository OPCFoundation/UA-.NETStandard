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
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Plugins.PubSub
{
    internal sealed partial class PubSubPlugin
    {
        /// <summary>
        /// Gets the active shared value editor for this document's local sample, never a network task.
        /// </summary>
        internal ComplexValueElementDialog? SampleEditor { get; private set; }

        [RelayCommand(CanExecute = nameof(CanStart), FlowExceptionsToTaskScheduler = true)]
        private Task EditSampleAsync(CancellationToken cancellationToken)
        {
            return PresentAsync(async () =>
            {
                PubSubFieldDraft selected = SelectedField ??
                    throw new InvalidOperationException("Select a field before editing its local sample.");
                if (TopLevel.GetTopLevel(m_view) is not Window owner)
                {
                    throw new InvalidOperationException(
                        "Open the PubSub document in a window before editing a sample.");
                }
                int revision = Volatile.Read(ref m_draftVersion);
                void RequireCurrent()
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (m_closing ||
                        revision != Volatile.Read(ref m_draftVersion) ||
                        !ReferenceEquals(SelectedField, selected) ||
                        !FieldDrafts.Contains(selected))
                    {
                        throw new InvalidOperationException("The selected field or schema changed during editing.");
                    }
                }
                PubSubConfiguration configuration = CreateDraft();
                var service = new PubSubConfiguredValueService(configuration, m_host.Telemetry, RequireCurrent);
                PubSubFieldConfiguration field = selected.ToConfiguration();
                NodeId type = PubSubStructureSchema.FieldType(field, service.MessageContext.NamespaceUris);
                DataTypeDefinition? definition = await service.ResolveAsync(type, cancellationToken)
                    .ConfigureAwait(true);
                Variant initial = field.SampleValue.Length == 0 ? Variant.Null : service.ReadSample(field);
                var dialog = new ComplexValueElementDialog(type, definition, service, initial, field.ValueRank,
                    field.ArrayDimensions, cancellationToken)
                { Title = "Edit local PubSub sample (no network operation)" };
                SampleEditor = dialog;
                await using (dialog.ConfigureAwait(true))
                {
                    using CancellationTokenRegistration canceled = cancellationToken.Register(() =>
                        Dispatcher.UIThread.Post(dialog.Close));
                    try
                    {
                        await dialog.ShowDialog(owner).ConfigureAwait(true);
                        RequireCurrent();
                        if (dialog.WasCommitted)
                        {
                            PubSubFieldConfiguration captured = service.CaptureSample(field, dialog.Result);
                            selected.SetSample(captured);
                            ClearAuthorizations();
                        }
                    }
                    finally
                    {
                        SampleEditor = null;
                        service.Refresh();
                    }
                }
            });
        }

        [RelayCommand(CanExecute = nameof(CanStart))]
        private void ClearSample()
        {
            if (SelectedField is not { } selected || !FieldDrafts.Contains(selected))
            {
                OperationError = "Select a field before clearing its local sample.";
                return;
            }
            selected.SetSample(selected.ToConfiguration() with
            {
                SampleValue = string.Empty, SampleSchema = string.Empty
            });
            ClearAuthorizations();
        }

        private async Task StopSampleEditorAsync()
        {
            EditSampleCommand.Cancel();
            if (EditSampleCommand.ExecutionTask is { } editing)
            {
                await editing.ConfigureAwait(true);
            }
        }
    }
}
