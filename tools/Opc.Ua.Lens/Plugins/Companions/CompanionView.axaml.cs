/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.Companions;

internal sealed partial class CompanionView : UserControl
{
    public CompanionView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
