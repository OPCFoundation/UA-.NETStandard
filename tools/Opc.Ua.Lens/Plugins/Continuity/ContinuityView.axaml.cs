// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.Continuity;

internal sealed partial class ContinuityView : UserControl
{
    public ContinuityView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
