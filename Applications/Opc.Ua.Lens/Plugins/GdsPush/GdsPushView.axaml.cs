/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.GdsPush;

/// <summary>
/// Avalonia <see cref="UserControl"/> hosted in a GDS Push tab.  The
/// DataContext is the <see cref="GdsPushPlugin"/>; the code-behind
/// hooks the tab-control's <c>SelectionChanged</c> event so the active
/// <see cref="TrustListBucket"/> on the view model stays in sync with the
/// visible list (drives Add / Remove destination semantics).  Selection
/// from the three trust-list <c>ListBox</c> instances is mirrored into the
/// view model's <see cref="GdsPushPlugin.SelectedCertificate"/>.
/// </summary>
internal sealed partial class GdsPushView : UserControl
{
    public GdsPushView()
    {
        InitializeComponent();
        WireUp();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireUp()
    {
        var trustTabs = this.FindControl<TabControl>("TrustTabs");
        if (trustTabs is not null)
        {
            trustTabs.SelectionChanged += (_, _) => SyncBucket(trustTabs);
        }
        WireList("TrustedList", TrustListBucket.Trusted);
        WireList("IssuerList", TrustListBucket.Issuer);
        WireList("RejectedList", TrustListBucket.Rejected);
    }

    private void WireList(string name, TrustListBucket bucket)
    {
        var lb = this.FindControl<ListBox>(name);
        if (lb is null)
        {
            return;
        }

        lb.SelectionChanged += (_, _) =>
        {
            if (DataContext is not GdsPushPlugin vm)
            {
                return;
            }

            if (vm.ActiveBucket != bucket)
            {
                return;
            }

            vm.SelectedCertificate = lb.SelectedItem as GdsCertItem;
        };
    }

    private void SyncBucket(SelectingItemsControl tabs)
    {
        if (DataContext is not GdsPushPlugin vm)
        {
            return;
        }

        int idx = tabs.SelectedIndex;
        vm.ActiveBucket = idx switch
        {
            0 => TrustListBucket.Trusted,
            1 => TrustListBucket.Issuer,
            2 => TrustListBucket.Rejected,
            _ => TrustListBucket.Trusted
        };
        vm.SelectedCertificate = null;
    }
}
