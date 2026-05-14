/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.GdsManagement;

/// <summary>
/// Sub-panel displaying the certificate groups attached to the
/// currently-selected application in the GDS Management view.  Embedded
/// inside <see cref="GdsManagementView"/> via a <c>ContentControl</c>; the
/// DataContext is the parent <see cref="GdsManagementPlugin"/>, exposing
/// <c>CertGroups</c> as the bound collection.
/// </summary>
internal sealed partial class CertGroupView : UserControl
{
    public CertGroupView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
