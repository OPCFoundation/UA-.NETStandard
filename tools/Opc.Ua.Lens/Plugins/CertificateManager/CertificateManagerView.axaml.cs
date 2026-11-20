/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.CertificateManager;

/// <summary>
/// Avalonia view for <see cref="CertificateManagerPlugin"/>.  Layout:
/// toolbar on top, left-hand TreeView listing the well-known cert
/// stores, right-hand ListBox of certificates in the selected store.
/// The right-pane context menu is declared in XAML and binds to the
/// plug-in's per-cert <see cref="RelayCommand"/> properties so commands
/// honour their <c>CanExecute</c> based on the current selection.
/// </summary>
internal sealed partial class CertificateManagerView : UserControl
{
    public CertificateManagerView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Pointer-press on a certificate row sets the plug-in's selected
    /// certificate to the clicked row before the context menu opens, so
    /// right-click-to-act always targets the row under the cursor.
    /// </summary>
    private void OnCertRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not CertificateManagerPlugin vm)
        {
            return;
        }
        if (sender is not Control row || row.DataContext is not CertItemRow item)
        {
            return;
        }
        PointerPointProperties props = e.GetCurrentPoint(row).Properties;
        if (props.IsRightButtonPressed || props.IsLeftButtonPressed)
        {
            vm.SelectedCertificate = item;
        }
    }

    /// <summary>
    /// Double-tap on a certificate row opens the details popup (the same
    /// action as the ContextMenu / toolbar "Show details" button) — the
    /// natural shortcut once a row is selected.
    /// </summary>
    private void OnCertRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not CertificateManagerPlugin vm
            || vm.SelectedCertificate is null)
        {
            return;
        }
        if (vm.ViewDetailsCommand.CanExecute(null))
        {
            vm.ViewDetailsCommand.Execute(null);
        }
    }
}
