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
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Opc.Ua;
using UaLens.Connection;
using UaLens.StructuredValues;
using UaLens.Views;

namespace UaLens.Plugins.Models;

/// <summary>
/// Presentation only: structured fields use the shared editor, and primitive/array
/// edits use the existing dialogs. No alternative field editor is maintained here.
/// </summary>
internal sealed partial class ModelInspectorView : UserControl
{
    public ModelInspectorView()
    {
        AvaloniaXamlLoader.Load(this);
        this.RequiredControl<ComplexValueEditor>("ValueEditor").IsVisible = false;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ModelInspectorPlugin plugin)
            {
                ContextMenu = new ContextMenu { ItemsSource = plugin.ContributeMenuItems() };
                this.RequiredControl<ComboBox>("ModelsSchemaFormat").ItemsSource = plugin.SchemaFormats.ToArray();
            }
            else
            {
                ContextMenu = null;
                this.RequiredControl<ComboBox>("ModelsSchemaFormat").ItemsSource = null;
            }
        };
    }

    public async Task LoadAsync(
        ModelInspection inspection, IStructuredValueService values, CancellationToken cancellationToken)
    {
        await StopAsync().ConfigureAwait(true);
        m_inspection = inspection;
        m_values = values;
        m_candidate = inspection.Value.WrappedValue.Copy();
        ComplexValueEditor editor = this.RequiredControl<ComplexValueEditor>("ValueEditor");
        editor.IsVisible = false;
        m_structured = inspection.NodeClass == NodeClass.Variable &&
            (inspection.Definition is StructureDefinition or EnumDefinition) &&
            (inspection.ValueRank == ValueRanks.Scalar || m_candidate.TypeInfo.IsScalar);
        if (!m_structured || inspection.Definition is EnumDefinition { IsOptionSet: true })
        {
            return;
        }
        try
        {
            await editor.InitializeAsync(
                inspection.DataType, inspection.Definition, values, m_candidate, cancellationToken)
                .ConfigureAwait(true);
            editor.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (DataContext is ModelInspectorPlugin plugin)
            {
                plugin.SetEditorAvailability(false, $"Value remains read-only: {ex.Message}");
            }
        }
    }

    public async Task EditValueAsync(CancellationToken cancellationToken)
    {
        if (m_inspection is null || m_values is null)
        {
            throw new InvalidOperationException("Read a Variable before editing.");
        }
        if (m_structured)
        {
            return;
        }
        Window owner = RequireOwner();
        if (!m_candidate.IsNull && !m_candidate.TypeInfo.IsScalar)
        {
            StructuredArrayValue array = StructuredArrayValue.Read(m_candidate, m_values.MessageContext);
            var dialog = new EditArrayDialog(
                "Models — typed array", m_inspection.DataType, m_inspection.Definition,
                array.Elements, m_values, array.ElementType);
            await ShowAsync(dialog, owner, cancellationToken).ConfigureAwait(true);
            if (!dialog.WasCommitted)
            {
                throw new OperationCanceledException("Array edit canceled.");
            }
            m_candidate = array.WithElements(dialog.Result, m_values.MessageContext);
        }
        else
        {
            if (m_inspection.ValueRank >= ValueRanks.OneOrMoreDimensions)
            {
                throw new InvalidOperationException("Import a typed array/matrix to establish its shape first.");
            }
            BuiltInType type = m_candidate.IsNull
                ? TypeInfo.GetBuiltInType(m_inspection.DataType)
                : m_candidate.TypeInfo.BuiltInType;
            var dialog = new PrimitiveValuePromptDialog(type, m_candidate);
            await ShowAsync(dialog, owner, cancellationToken).ConfigureAwait(true);
            if (!dialog.WasCommitted)
            {
                throw new OperationCanceledException("Value edit canceled.");
            }
            m_candidate = dialog.Result.Copy();
        }
        UpdatePreview();
    }

    public async Task ImportAsync(CancellationToken cancellationToken)
    {
        if (m_values is null || m_inspection is null)
        {
            throw new InvalidOperationException("Read a Variable before importing.");
        }
        (byte[] bytes, EncodingFormat format, string fileName) =
            await EncodedValueIO.LoadAsync(RequireOwner()).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length == 0)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                throw new OperationCanceledException("Import canceled.");
            }
            throw new ServiceResultException(StatusCodes.BadDecodingError, "The imported value file is empty.");
        }
        Variant imported = DataValueCodec.DecodeDataValue(bytes, format, m_values.MessageContext).WrappedValue;
        if (m_structured)
        {
            await this.RequiredControl<ComplexValueEditor>("ValueEditor").InitializeAsync(
                m_inspection.DataType, m_inspection.Definition, m_values, imported, cancellationToken)
                .ConfigureAwait(true);
        }
        m_candidate = imported.Copy();
        UpdatePreview();
    }

    public bool TryCommit(out Variant value, out string? error)
    {
        if (m_structured)
        {
            return this.RequiredControl<ComplexValueEditor>("ValueEditor").TryCommit(out value, out error);
        }
        value = m_candidate.Copy();
        error = m_inspection is null ? "Read a Variable before committing." : null;
        return error is null;
    }

    public async Task StopAsync()
    {
        m_dialog?.Close();
        await this.RequiredControl<ComplexValueEditor>("ValueEditor").StopAsync().ConfigureAwait(true);
    }

    public void Clear()
    {
        m_inspection = null;
        m_values = null;
        m_candidate = Variant.Null;
        m_structured = false;
        this.RequiredControl<ComplexValueEditor>("ValueEditor").IsVisible = false;
    }

    private async Task ShowAsync(Window dialog, Window owner, CancellationToken cancellationToken)
    {
        m_dialog = dialog;
        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => Dispatcher.UIThread.Post(dialog.Close));
        try
        {
            await dialog.ShowDialog(owner).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            if (dialog is EditArrayDialog array)
            {
                await array.StopAsync().ConfigureAwait(true);
            }
            m_dialog = null;
        }
    }

    private void UpdatePreview()
    {
        if (DataContext is ModelInspectorPlugin plugin && m_values is not null)
        {
            plugin.ValuePreview = StructuredScalarValue.FormatValue(m_candidate, m_values.MessageContext);
        }
    }

    private Window RequireOwner()
    {
        return TopLevel.GetTopLevel(this) as Window ??
            throw new InvalidOperationException("The Models view is not attached to a window.");
    }

    private ModelInspection? m_inspection;
    private IStructuredValueService? m_values;
    private Variant m_candidate;
    private bool m_structured;
    private Window? m_dialog;
}
