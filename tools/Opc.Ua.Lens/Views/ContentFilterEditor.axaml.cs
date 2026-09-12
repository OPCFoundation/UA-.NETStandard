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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Views;

/// <summary>
/// One row in <see cref="ContentFilterEditor"/>'s element list.
/// Owns a mutable copy of an OPC UA <see cref="ContentFilterElement"/>:
/// the operator and the ordered list of operand POCOs (any subclass of
/// <see cref="FilterOperand"/>).  The <see cref="Summary"/> property is
/// refreshed by the host whenever the underlying data changes.
/// </summary>
internal sealed partial class ContentFilterElementVm : ObservableObject
{
    [ObservableProperty]
    private FilterOperator m_filterOperator;

    [ObservableProperty]
    private string m_summary = string.Empty;

    public ObservableCollection<FilterOperand> Operands { get; } = new();

    public ContentFilterElementVm(FilterOperator op)
    {
        m_filterOperator = op;
    }
}

/// <summary>
/// Composite editor for an OPC UA <see cref="ContentFilter"/>.  Renders a
/// left-hand list of <see cref="ContentFilterElement"/>s with Add /
/// Remove / Up / Down buttons, a right-hand "element editor" with an
/// operator combo and operand list, and a bottom preview that
/// pretty-prints the constructed filter using the stack's built-in
/// <c>ContentFilter.ToString</c>.
///
/// <para>The control deliberately stays "edit any operand the spec
/// permits" rather than constraining choices by operator arity — when
/// the resulting filter is sent to the server in a
/// <c>CreateMonitoredItems</c> call the server's <c>EventFilterResult</c>
/// surfaces any semantic problems.  This lets advanced users author
/// non-trivial expressions (nested AND / OR, Between, InList) without
/// the editor refusing them at edit time.</para>
/// </summary>
internal sealed partial class ContentFilterEditor : UserControl
{
    private readonly ObservableCollection<ContentFilterElementVm> m_elements = new();
    private ISession? m_session;

    private ListBox m_elementsList = null!;
    private ListBox m_operandsList = null!;
    private ComboBox m_operatorCombo = null!;
    private TextBlock m_preview = null!;
    private TextBlock m_editorStatus = null!;
    private Button m_addElementBtn = null!;
    private Button m_removeElementBtn = null!;
    private Button m_moveUpBtn = null!;
    private Button m_moveDownBtn = null!;
    private Button m_addOperandBtn = null!;
    private Button m_editOperandBtn = null!;
    private Button m_removeOperandBtn = null!;

    public ContentFilterEditor()
    {
        InitializeComponent();
        WireControls();
    }

    /// <summary>
    /// Replaces the editor's contents with <paramref name="initial"/> and
    /// captures the session used when an operand sub-editor needs a node
    /// picker.  Safe to call multiple times.
    /// </summary>
    public void Initialize(ContentFilter? initial, ISession? session)
    {
        m_session = session;
        m_elements.Clear();
        if (initial is not null)
        {
            for (int i = 0; i < initial.Elements.Count; i++)
            {
                ContentFilterElement el = initial.Elements[i];
                var vm = new ContentFilterElementVm(el.FilterOperator);
                for (int j = 0; j < el.FilterOperands.Count; j++)
                {
                    if (el.FilterOperands[j].Body is FilterOperand op)
                    {
                        vm.Operands.Add(op);
                    }
                }
                RefreshElementSummary(vm);
                m_elements.Add(vm);
            }
        }
        if (m_elements.Count > 0)
        {
            m_elementsList.SelectedIndex = 0;
        }
        RebuildPreview();
        RefreshSelectionDependentUi();
    }

    /// <summary>
    /// Constructs a fresh <see cref="ContentFilter"/> from the editor's
    /// current contents.  Returns an empty (but non-null) filter when no
    /// elements have been authored.
    /// </summary>
    public ContentFilter BuildResult()
    {
        var filter = new ContentFilter();
        var elements = new List<ContentFilterElement>(m_elements.Count);
        foreach (ContentFilterElementVm vm in m_elements)
        {
            var element = new ContentFilterElement
            {
                FilterOperator = vm.FilterOperator
            };
            var operands = new List<ExtensionObject>(vm.Operands.Count);
            foreach (FilterOperand operand in vm.Operands)
            {
                operands.Add(new ExtensionObject(operand));
            }
            element.FilterOperands = operands;
            elements.Add(element);
        }
        filter.Elements = elements;
        return filter;
    }

    private void WireControls()
    {
        m_elementsList = this.RequiredControl<ListBox>("ElementsList");
        m_operandsList = this.RequiredControl<ListBox>("OperandsList");
        m_operatorCombo = this.RequiredControl<ComboBox>("OperatorCombo");
        m_preview = this.RequiredControl<TextBlock>("PreviewText");
        m_editorStatus = this.RequiredControl<TextBlock>("EditorStatus");
        m_addElementBtn = this.RequiredControl<Button>("AddElementBtn");
        m_removeElementBtn = this.RequiredControl<Button>("RemoveElementBtn");
        m_moveUpBtn = this.RequiredControl<Button>("MoveUpBtn");
        m_moveDownBtn = this.RequiredControl<Button>("MoveDownBtn");
        m_addOperandBtn = this.RequiredControl<Button>("AddOperandBtn");
        m_editOperandBtn = this.RequiredControl<Button>("EditOperandBtn");
        m_removeOperandBtn = this.RequiredControl<Button>("RemoveOperandBtn");

        m_elementsList.ItemsSource = m_elements;
        m_elementsList.SelectionChanged += (_, _) => OnElementSelectionChanged();

        m_operatorCombo.ItemsSource = Enum.GetValues<FilterOperator>();
        m_operatorCombo.SelectionChanged += (_, _) => OnOperatorChanged();

        m_addElementBtn.Click += (_, _) => OnAddElement();
        m_removeElementBtn.Click += (_, _) => OnRemoveElement();
        m_moveUpBtn.Click += (_, _) => OnMove(-1);
        m_moveDownBtn.Click += (_, _) => OnMove(+1);

        m_addOperandBtn.Click += async (_, _) => await OnAddOperandAsync().ConfigureAwait(true);
        m_editOperandBtn.Click += async (_, _) => await OnEditOperandAsync().ConfigureAwait(true);
        m_removeOperandBtn.Click += (_, _) => OnRemoveOperand();

        RefreshSelectionDependentUi();
    }

    private void OnElementSelectionChanged()
    {
        ContentFilterElementVm? sel = SelectedElement();
        if (sel is null)
        {
            m_operatorCombo.SelectedItem = FilterOperator.Equals;
            m_operandsList.ItemsSource = null;
        }
        else
        {
            m_operatorCombo.SelectedItem = sel.FilterOperator;
            m_operandsList.ItemsSource = BuildOperandView(sel.Operands);
        }
        RefreshSelectionDependentUi();
    }

    private void OnOperatorChanged()
    {
        ContentFilterElementVm? sel = SelectedElement();
        if (sel is null || m_operatorCombo.SelectedItem is not FilterOperator op)
        {
            return;
        }
        if (sel.FilterOperator == op)
        {
            return;
        }
        sel.FilterOperator = op;
        RefreshElementSummary(sel);
        RebuildPreview();
    }

    private void OnAddElement()
    {
        var vm = new ContentFilterElementVm(FilterOperator.Equals);
        RefreshElementSummary(vm);
        m_elements.Add(vm);
        m_elementsList.SelectedItem = vm;
        RebuildPreview();
    }

    private void OnRemoveElement()
    {
        ContentFilterElementVm? sel = SelectedElement();
        if (sel is null)
        {
            return;
        }
        int idx = m_elements.IndexOf(sel);
        m_elements.RemoveAt(idx);
        if (m_elements.Count > 0)
        {
            m_elementsList.SelectedIndex = Math.Min(idx, m_elements.Count - 1);
        }
        RebuildPreview();
        RefreshSelectionDependentUi();
    }

    private void OnMove(int direction)
    {
        ContentFilterElementVm? sel = SelectedElement();
        if (sel is null)
        {
            return;
        }
        int idx = m_elements.IndexOf(sel);
        int target = idx + direction;
        if (target < 0 || target >= m_elements.Count)
        {
            return;
        }
        m_elements.Move(idx, target);
        m_elementsList.SelectedIndex = target;
        RebuildPreview();
    }

    private async Task OnAddOperandAsync()
    {
        ContentFilterElementVm? sel = SelectedElement();
        if (sel is null)
        {
            return;
        }
        FilterOperand? built = await OpenOperandDialogAsync(initial: null).ConfigureAwait(true);
        if (built is null)
        {
            return;
        }
        sel.Operands.Add(built);
        RefreshOperandView(sel);
        RefreshElementSummary(sel);
        RebuildPreview();
    }

    private async Task OnEditOperandAsync()
    {
        ContentFilterElementVm? sel = SelectedElement();
        if (sel is null)
        {
            return;
        }
        int idx = m_operandsList.SelectedIndex;
        if (idx < 0 || idx >= sel.Operands.Count)
        {
            return;
        }
        FilterOperand existing = sel.Operands[idx];
        FilterOperand? edited = await OpenOperandDialogAsync(existing).ConfigureAwait(true);
        if (edited is null)
        {
            return;
        }
        sel.Operands[idx] = edited;
        RefreshOperandView(sel);
        m_operandsList.SelectedIndex = idx;
        RefreshElementSummary(sel);
        RebuildPreview();
    }

    private void OnRemoveOperand()
    {
        ContentFilterElementVm? sel = SelectedElement();
        if (sel is null)
        {
            return;
        }
        int idx = m_operandsList.SelectedIndex;
        if (idx < 0 || idx >= sel.Operands.Count)
        {
            return;
        }
        sel.Operands.RemoveAt(idx);
        RefreshOperandView(sel);
        RefreshElementSummary(sel);
        RebuildPreview();
    }

    private async Task<FilterOperand?> OpenOperandDialogAsync(FilterOperand? initial)
    {
        Window? owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            m_editorStatus.Text = "Cannot open operand editor outside a window context.";
            return null;
        }
        var dlg = new FilterOperandEditDialog(initial, m_session);
        FilterOperand? result = await dlg.ShowDialog<FilterOperand?>(owner).ConfigureAwait(true);
        return result;
    }

    private void RefreshOperandView(ContentFilterElementVm vm)
    {
        m_operandsList.ItemsSource = BuildOperandView(vm.Operands);
    }

    private static List<string> BuildOperandView(ObservableCollection<FilterOperand> operands)
    {
        var list = new List<string>(operands.Count);
        for (int i = 0; i < operands.Count; i++)
        {
            list.Add(string.Format(CultureInfo.InvariantCulture, "[{0}] {1}", i, FormatOperand(operands[i])));
        }
        return list;
    }

    private static string FormatOperand(FilterOperand op)
    {
        return op switch
        {
            LiteralOperand lit => $"Literal({FormatVariantInline(lit.Value)})",
            ElementOperand el => $"Element[{el.Index}]",
            AttributeOperand ao => $"Attribute({DisplayNodeId(ao.NodeId)}, attr={ao.AttributeId})",
            SimpleAttributeOperand sao => $"SimpleAttribute({DisplayNodeId(sao.TypeDefinitionId)}, {FormatSimplePath(sao.BrowsePath)})",
            _ => op.GetType().Name
        };
    }

    private static string DisplayNodeId(NodeId id)
    {
        return id.IsNull ? "null" : (id.ToString() ?? "null");
    }

    private static string FormatVariantInline(Variant v)
    {
        if (v.IsNull)
        {
            return "null";
        }
        object? raw = v.AsBoxedObject();
        return raw switch
        {
            null => "null",
            string s => $"\"{s}\"",
            System.IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString() ?? "null"
        };
    }

    private static string FormatSimplePath(ArrayOf<QualifiedName> path)
    {
        if (path.IsEmpty)
        {
            return "(empty)";
        }
        var buffer = new StringBuilder();
        for (int i = 0; i < path.Count; i++)
        {
            QualifiedName qn = path[i];
            if (qn.IsNull)
            {
                continue;
            }
            buffer.Append('/').Append(qn.Name);
        }
        return buffer.ToString();
    }

    private void RefreshElementSummary(ContentFilterElementVm vm)
    {
        var buffer = new StringBuilder();
        buffer.Append(vm.FilterOperator);
        buffer.Append('(');
        for (int i = 0; i < vm.Operands.Count; i++)
        {
            if (i > 0)
            {
                buffer.Append(", ");
            }
            buffer.Append(FormatOperand(vm.Operands[i]));
        }
        buffer.Append(')');
        vm.Summary = buffer.ToString();
    }

    private void RebuildPreview()
    {
        try
        {
            ContentFilter f = BuildResult();
            string text = f.ToString();
            m_preview.Text = string.IsNullOrEmpty(text) ? "(empty)" : text;
            m_editorStatus.Text = string.Empty;
        }
        catch (Exception ex)
        {
            m_preview.Text = string.Empty;
            m_editorStatus.Text = $"Preview error: {ex.Message}";
        }
    }

    private void RefreshSelectionDependentUi()
    {
        bool hasElement = SelectedElement() is not null;
        m_operatorCombo.IsEnabled = hasElement;
        m_removeElementBtn.IsEnabled = hasElement;
        m_moveUpBtn.IsEnabled = hasElement;
        m_moveDownBtn.IsEnabled = hasElement;
        m_addOperandBtn.IsEnabled = hasElement;
        m_editOperandBtn.IsEnabled = hasElement;
        m_removeOperandBtn.IsEnabled = hasElement;
    }

    private ContentFilterElementVm? SelectedElement()
    {
        return m_elementsList.SelectedItem as ContentFilterElementVm;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
