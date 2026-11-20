// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Themes
{
    internal sealed partial class LightTheme : ResourceDictionary
    {
        public LightTheme()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }

    internal sealed partial class DarkStandardTheme : ResourceDictionary
    {
        public DarkStandardTheme()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }

    internal sealed partial class DarkNavyTheme : ResourceDictionary
    {
        public DarkNavyTheme()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
