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
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.StructuredValues;

namespace UaLens.Views;

/// <summary>
/// Modal editor for a 1-D array field of a structured value.  Hosts a
/// <see cref="ListBox"/> of element renderings with Add / Edit / Remove
/// / reorder buttons; primitive element edits use the same one-line
/// TextBox prompt as the inline editor, structure elements open a child
/// <see cref="ComplexValueEditor"/> hosted in a dialog so the user can
/// drill into nested fields.  Operates on a working
/// typed element copy and returns it to the caller on OK.
/// </summary>
internal sealed partial class EditArrayDialog : Window
{
    public EditArrayDialog(
        string header,
        NodeId elementDataType,
        DataTypeDefinition? elementDefinition,
        ArrayOf<Variant> initial,
        IStructuredValueService service,
        BuiltInType elementType)
    {
        m_elementDataType = elementDataType;
        m_elementDefinition = elementDefinition;
        m_service = service ?? throw new ArgumentNullException(nameof(service));
        m_elementType = elementType;
        InitializeComponent();

        this.RequiredControl<TextBlock>("HeaderLabel").Text = header;
        this.RequiredControl<TextBlock>("HintLabel").Text =
            BuildHint(elementDataType, elementDefinition);

        ListBox list = this.RequiredControl<ListBox>("ItemsList");
        list.ItemsSource = m_rows;

        this.RequiredControl<Button>("AddButton").Click += async (_, _) =>
        {
            m_editTask = PresentEditAsync(OnAddAsync);
            await m_editTask.ConfigureAwait(true);
        };
        this.RequiredControl<Button>("EditButton").Click += async (_, _) =>
        {
            m_editTask = PresentEditAsync(OnEditAsync);
            await m_editTask.ConfigureAwait(true);
        };
        this.RequiredControl<Button>("RemoveButton").Click += (_, _) => OnRemove();
        this.RequiredControl<Button>("MoveUpButton").Click += (_, _) => OnMove(-1);
        this.RequiredControl<Button>("MoveDownButton").Click += (_, _) => OnMove(+1);
        this.RequiredControl<Button>("OkButton").Click += (_, _) =>
        {
            m_ok = true;
            Result = SnapshotRows();
            Close();
        };
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close();
        Closed += async (_, _) => await StopAsync().ConfigureAwait(true);

        foreach (Variant value in initial)
        {
            m_rows.Add(new ArrayRow(value.Copy()));
        }
    }

    /// <summary>
    /// The accepted elements; consult WasCommitted to distinguish cancel from an empty array.
    /// </summary>
    public ArrayOf<Variant> Result { get; private set; }

    public bool WasCommitted => m_ok;

    public async Task StopAsync()
    {
        m_child?.Close();
        await m_editTask.ConfigureAwait(true);
    }

    /// <summary>
    /// Builds a friendly type hint shown above the listbox.  Falls back
    /// to the bare NodeId when no definition was resolved — the user
    /// still gets a working editor (primitive elements only).
    /// </summary>
    private static string BuildHint(NodeId dataType, DataTypeDefinition? def)
    {
        if (def is StructureDefinition)
        {
            return $"Element type: structure  ({dataType})";
        }
        if (def is EnumDefinition)
        {
            return $"Element type: enum  ({dataType})";
        }
        BuiltInType bi = TypeInfo.GetBuiltInType(dataType);
        return bi == BuiltInType.Null
            ? $"Element type: {dataType}  (opaque)"
            : $"Element type: {bi}  ({dataType})";
    }

    private async Task OnAddAsync()
    {
        Variant fresh = ComplexValueIO.DefaultScalar(m_elementType);
        if (m_elementDefinition is StructureDefinition or EnumDefinition)
        {
            var dialog = new ComplexValueElementDialog(
                m_elementDataType, m_elementDefinition, m_service, Variant.Null);
            await ShowChildAsync(dialog).ConfigureAwait(true);
            if (!dialog.WasCommitted)
            {
                return;
            }
            fresh = dialog.Result;
        }
        m_rows.Add(new ArrayRow(fresh.Copy()));
        this.RequiredControl<ListBox>("ItemsList").SelectedIndex = m_rows.Count - 1;
    }

    private async Task OnEditAsync()
    {
        ListBox list = this.RequiredControl<ListBox>("ItemsList");
        int idx = list.SelectedIndex;
        if (idx < 0 || idx >= m_rows.Count)
        {
            return;
        }
        if (m_elementDefinition is StructureDefinition or EnumDefinition)
        {
            var dialog = new ComplexValueElementDialog(
                m_elementDataType, m_elementDefinition, m_service, m_rows[idx].Value);
            await ShowChildAsync(dialog).ConfigureAwait(true);
            if (!dialog.WasCommitted)
            {
                return;
            }
            m_rows[idx] = new ArrayRow(dialog.Result);
        }
        else
        {
            var dialog = new PrimitiveValuePromptDialog(m_elementType, m_rows[idx].Value);
            await ShowChildAsync(dialog).ConfigureAwait(true);
            if (!dialog.WasCommitted)
            {
                return;
            }
            m_rows[idx] = new ArrayRow(dialog.Result);
        }
        list.SelectedIndex = idx;
    }

    private void OnRemove()
    {
        ListBox list = this.RequiredControl<ListBox>("ItemsList");
        int idx = list.SelectedIndex;
        if (idx < 0 || idx >= m_rows.Count)
        {
            return;
        }
        m_rows.RemoveAt(idx);
        if (m_rows.Count > 0)
        {
            list.SelectedIndex = Math.Min(idx, m_rows.Count - 1);
        }
    }

    private void OnMove(int delta)
    {
        ListBox list = this.RequiredControl<ListBox>("ItemsList");
        int idx = list.SelectedIndex;
        int target = idx + delta;
        if (idx < 0 || target < 0 || target >= m_rows.Count)
        {
            return;
        }
        (m_rows[idx], m_rows[target]) = (m_rows[target], m_rows[idx]);
        list.SelectedIndex = target;
    }

    private ArrayOf<Variant> SnapshotRows()
    {
        var arr = new Variant[m_rows.Count];
        for (int i = 0; i < m_rows.Count; i++)
        {
            arr[i] = m_rows[i].Value.Copy();
        }
        return arr;
    }

    private async Task ShowChildAsync(Window dialog)
    {
        m_child = dialog;
        try
        {
            await dialog.ShowDialog(this).ConfigureAwait(true);
        }
        finally
        {
            if (dialog is ComplexValueElementDialog complex)
            {
                await complex.StopAsync().ConfigureAwait(true);
            }
            m_child = null;
        }
    }

    private async Task PresentEditAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            this.RequiredControl<TextBlock>("HintLabel").Text = $"Edit failed: {ex.Message}";
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private readonly NodeId m_elementDataType;
    private readonly DataTypeDefinition? m_elementDefinition;
    private readonly IStructuredValueService m_service;
    private readonly BuiltInType m_elementType;
    private readonly ObservableCollection<ArrayRow> m_rows = [];
    private bool m_ok;
    private Task m_editTask = Task.CompletedTask;
    private Window? m_child;
}

/// <summary>
/// Single row backing the array editor's <see cref="ListBox"/>.  The
/// <see cref="ToString"/> override drives the default item template.
/// </summary>
internal sealed class ArrayRow
{
    public ArrayRow(Variant value)
    {
        Value = value;
    }

    public Variant Value { get; }

    public override string ToString()
    {
        if (Value.IsNull)
        {
            return "(null)";
        }
        if (Value.TryGetValue(out ExtensionObject extension))
        {
            return $"(struct  {extension.TypeId})";
        }
        return StructuredScalarValue.Format(Value);
    }
}
