/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;

namespace UaLens.Views;

/// <summary>
/// Pre-export dialog that lets the user choose which non-base server
/// namespaces to include in a NodeSet2 export.  Returns the selected
/// URIs via <see cref="ShowDialog{IReadOnlyList{string}}(Window)"/>
/// (returns <c>null</c> on cancel).
/// </summary>
internal sealed partial class NodeSetExportDialog : Window
{
    public ObservableCollection<NsRow> Namespaces { get; } = new();

    public NodeSetExportDialog()
    {
        InitializeComponent();
    }

    public NodeSetExportDialog(NamespaceTable namespaceTable)
    {
        InitializeComponent();
        // Skip ns=0 (OPC UA base) — its schema ships with the SDK.
        for (ushort i = 1; i < namespaceTable.Count; i++)
        {
            string uri = namespaceTable.GetString(i);
            if (string.IsNullOrEmpty(uri))
            {
                continue;
            }

            Namespaces.Add(new NsRow { Selected = true, Uri = uri, Display = $"ns={i}  {uri}" });
        }
        this.RequiredControl<ItemsControl>("NamespaceList").ItemsSource = Namespaces;
        this.RequiredControl<Button>("SelectAllBtn").Click += (_, _) =>
        {
            foreach (NsRow r in Namespaces)
            {
                r.Selected = true;
            }
        };
        this.RequiredControl<Button>("SelectNoneBtn").Click += (_, _) =>
        {
            foreach (NsRow r in Namespaces)
            {
                r.Selected = false;
            }
        };
        this.RequiredControl<Button>("OkButton").Click += (_, _) =>
        {
            var picked = new List<string>();
            foreach (NsRow r in Namespaces)
            {
                if (r.Selected)
                {
                    picked.Add(r.Uri);
                }
            }
            Close(picked);
        };
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>One row in the namespace picker.</summary>
internal sealed partial class NsRow : ObservableObject
{
    [ObservableProperty]
    private bool m_selected = true;

    public required string Uri { get; init; }
    public required string Display { get; init; }
}
