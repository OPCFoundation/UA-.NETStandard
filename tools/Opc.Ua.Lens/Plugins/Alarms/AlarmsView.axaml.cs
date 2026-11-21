// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.Alarms;

internal sealed partial class AlarmsView : UserControl
{
    public AlarmsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
