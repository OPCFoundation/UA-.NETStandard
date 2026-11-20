/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using UaLens.ViewModels;

namespace UaLens.Views;

internal sealed partial class ReferencesView : UserControl
{
    public ReferencesView()
    {
        InitializeComponent();
        var list = this.RequiredControl<ListBox>("ReferenceList");
        var menu = this.RequiredControl<ContextMenu>("ReferenceMenu");
        var goToTarget = this.RequiredControl<MenuItem>("MenuGoToTarget");
        var goToReferenceType = this.RequiredControl<MenuItem>("MenuGoToReferenceType");
        DoubleTapped += (_, args) =>
        {
            if (args.Source is Control { DataContext: ReferenceRow row })
            {
                NavigateToTarget(row);
            }
        };
        list.KeyDown += (_, args) =>
        {
            bool control = args.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                args.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (!control || list.SelectedItem is not ReferenceRow row)
            {
                return;
            }
            if (args.Key == Key.R)
            {
                args.Handled = true;
                NavigateToTarget(row);
            }
            else if (args.Key == Key.D)
            {
                args.Handled = true;
                NavigateToReferenceType(row);
            }
        };
        menu.Opening += (_, _) =>
        {
            ReferenceRow? row = list.SelectedItem as ReferenceRow;
            goToTarget.IsEnabled = row is not null && row.TargetNodeId.Length > 0;
            goToReferenceType.IsEnabled = row is not null && row.ReferenceTypeNodeId.Length > 0;
        };
        goToTarget.Click += (_, _) =>
        {
            if (list.SelectedItem is ReferenceRow row)
            {
                NavigateToTarget(row);
            }
        };
        goToReferenceType.Click += (_, _) =>
        {
            if (list.SelectedItem is ReferenceRow row)
            {
                NavigateToReferenceType(row);
            }
        };
    }

    public event Action<ReferenceRow>? NavigateTargetRequested;

    public event Action<ReferenceRow>? NavigateReferenceTypeRequested;

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is Control { DataContext: ReferenceRow row } control &&
            args.GetCurrentPoint(control).Properties.IsRightButtonPressed)
        {
            var list = this.RequiredControl<ListBox>("ReferenceList");
            list.SelectedItem = row;
            list.Focus();
        }
    }

    private void NavigateToTarget(ReferenceRow row)
    {
        if (row.TargetNodeId.Length > 0)
        {
            NavigateTargetRequested?.Invoke(row);
        }
    }

    private void NavigateToReferenceType(ReferenceRow row)
    {
        if (row.ReferenceTypeNodeId.Length > 0)
        {
            NavigateReferenceTypeRequested?.Invoke(row);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
