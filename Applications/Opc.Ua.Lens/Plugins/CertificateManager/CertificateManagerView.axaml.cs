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

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.CertificateManager;

/// <summary>
/// Avalonia view for <see cref="CertificateManagerPlugin"/>.  Layout:
/// toolbar on top, left-hand TreeView listing the well-known cert
/// stores, right-hand ListBox of certificates in the selected store.
/// The right-pane context menu is built in code-behind so the actions
/// (Trust / Reject / Delete / Export / Import / View details) always
/// target the row that was actually clicked.
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
    /// Right-click on a certificate row sets the plug-in's selected
    /// certificate to the clicked row before showing the context menu,
    /// so subsequent commands always act on the row under the cursor.
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
        if (props.IsRightButtonPressed)
        {
            row.ContextMenu = BuildContextMenu(vm);
        }
    }

    private static ContextMenu BuildContextMenu(CertificateManagerPlugin vm)
    {
        var menu = new ContextMenu();

        var details = new MenuItem { Header = "_View details…" };
        details.Click += async (_, _) => await vm.ViewDetailsAsync().ConfigureAwait(true);
        menu.Items.Add(details);

        menu.Items.Add(new Separator());

        var trustPeer = new MenuItem { Header = "Trust → _Peer" };
        trustPeer.Click += async (_, _) => await vm.TrustToPeerAsync().ConfigureAwait(true);
        menu.Items.Add(trustPeer);

        var trustIssuer = new MenuItem { Header = "Trust → _Issuer" };
        trustIssuer.Click += async (_, _) => await vm.TrustToIssuerAsync().ConfigureAwait(true);
        menu.Items.Add(trustIssuer);

        var reject = new MenuItem { Header = "_Reject" };
        reject.Click += async (_, _) => await vm.RejectAsync().ConfigureAwait(true);
        menu.Items.Add(reject);

        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = "_Delete" };
        delete.Click += async (_, _) => await vm.DeleteAsync().ConfigureAwait(true);
        menu.Items.Add(delete);

        menu.Items.Add(new Separator());

        var export = new MenuItem { Header = "_Export…" };
        export.Click += async (_, _) => await vm.ExportAsync().ConfigureAwait(true);
        menu.Items.Add(export);

        var import = new MenuItem { Header = "I_mport…" };
        import.Click += async (_, _) => await vm.ImportAsync().ConfigureAwait(true);
        menu.Items.Add(import);

        return menu;
    }
}
