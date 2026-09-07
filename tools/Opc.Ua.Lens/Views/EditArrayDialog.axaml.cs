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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Subscriptions;

namespace UaLens.Views;

/// <summary>
/// Modal editor for a 1-D array field of a structured value.  Hosts a
/// <see cref="ListBox"/> of element renderings with Add / Edit / Remove
/// / reorder buttons; primitive element edits use the same one-line
/// TextBox prompt as the inline editor, structure elements open a child
/// <see cref="ComplexValueEditor"/> hosted in a dialog so the user can
/// drill into nested fields.  Operates on a working
/// <see cref="List{Variant}"/> copy and returns it to the caller on OK.
/// </summary>
internal sealed partial class EditArrayDialog : Window
{
    private readonly NodeId m_elementDataType;
    private readonly DataTypeDefinition? m_elementDefinition;
    private readonly ManagedSession? m_session;
    private readonly ObservableCollection<ArrayRow> m_rows = [];
    private bool m_ok;

    /// <summary>
    /// Snapshot of the elements at the time OK was clicked.  Null when
    /// the dialog was cancelled; the caller treats that as "no change".
    /// </summary>
    public IReadOnlyList<Variant>? Result { get; private set; }

    public EditArrayDialog(
        string header,
        NodeId elementDataType,
        DataTypeDefinition? elementDefinition,
        IEnumerable<Variant>? initial,
        ManagedSession? session)
    {
        m_elementDataType = elementDataType;
        m_elementDefinition = elementDefinition;
        m_session = session;
        InitializeComponent();

        this.RequiredControl<TextBlock>("HeaderLabel").Text = header;
        this.RequiredControl<TextBlock>("HintLabel").Text =
            BuildHint(elementDataType, elementDefinition);

        ListBox list = this.RequiredControl<ListBox>("ItemsList");
        list.ItemsSource = m_rows;

        this.RequiredControl<Button>("AddButton").Click += async (_, _) => await OnAddAsync().ConfigureAwait(true);
        this.RequiredControl<Button>("EditButton").Click += async (_, _) => await OnEditAsync().ConfigureAwait(true);
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

        if (initial is not null)
        {
            foreach (Variant v in initial)
            {
                m_rows.Add(new ArrayRow(v));
            }
        }
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
        Variant fresh = await CreateDefaultElementAsync().ConfigureAwait(true);
        m_rows.Add(new ArrayRow(fresh));
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
        Variant edited = await EditElementAsync(m_rows[idx].Value).ConfigureAwait(true);
        m_rows[idx] = new ArrayRow(edited);
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

    private async Task<Variant> CreateDefaultElementAsync()
    {
        if (m_elementDefinition is StructureDefinition && m_session is not null)
        {
            return await EditStructureAsync(Variant.Null).ConfigureAwait(true);
        }
        if (m_elementDefinition is EnumDefinition)
        {
            return Variant.From(0);
        }
        BuiltInType bi = TypeInfo.GetBuiltInType(m_elementDataType);
        return ComplexValueIO.DefaultScalar(bi);
    }

    private Task<Variant> EditElementAsync(Variant current)
    {
        if (m_elementDefinition is StructureDefinition && m_session is not null)
        {
            return EditStructureAsync(current);
        }
        // For primitive / enum elements, edit via a one-line text prompt;
        // arrays-of-arrays aren't supported by this dialog (callers gate on
        // ValueRank == 1).
        return EditPrimitiveAsync(current);
    }

    private async Task<Variant> EditStructureAsync(Variant current)
    {
        if (m_session is null || m_elementDefinition is not StructureDefinition)
        {
            return current;
        }
        var dlg = new ComplexValueElementDialog(
            m_elementDataType, m_elementDefinition, m_session, current);
        Variant? committed = await dlg.ShowDialog<Variant?>(this).ConfigureAwait(true);
        return committed ?? current;
    }

    private async Task<Variant> EditPrimitiveAsync(Variant current)
    {
        var dlg = new PrimitiveValuePromptDialog(m_elementDataType, current);
        Variant? committed = await dlg.ShowDialog<Variant?>(this).ConfigureAwait(true);
        return committed ?? current;
    }

    private Variant[] SnapshotRows()
    {
        var arr = new Variant[m_rows.Count];
        for (int i = 0; i < m_rows.Count; i++)
        {
            arr[i] = m_rows[i].Value;
        }
        return arr;
    }

    /// <summary>
    /// Returns <c>true</c> only when the user clicked OK — used by
    /// callers to distinguish a real "save empty array" intent from a
    /// cancel.
    /// </summary>
    public bool WasCommitted => m_ok;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>
/// Single row backing the array editor's <see cref="ListBox"/>.  The
/// <see cref="ToString"/> override drives the default item template.
/// </summary>
internal sealed class ArrayRow
{
    public Variant Value { get; }

    public ArrayRow(Variant value)
    {
        Value = value;
    }

    public override string ToString()
    {
        if (Value.IsNull)
        {
            return "(null)";
        }
        object? boxed = Value.AsBoxedObject();
        if (boxed is ExtensionObject eo)
        {
            return $"(struct  {eo.TypeId})";
        }
        return boxed switch
        {
            null => "(null)",
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => boxed.ToString() ?? string.Empty
        };
    }
}
