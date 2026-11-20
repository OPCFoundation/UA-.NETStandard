// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.Companions.Machinery
{
    /// <summary>
    /// Compact single-section Machinery workspace.
    /// </summary>
    internal sealed partial class MachineryView : UserControl
    {
        /// <summary>
        /// Loads the typed view without starting a domain read.
        /// </summary>
        public MachineryView()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Returns focus to the document before starting a cooperating tool or transient task.
        /// </summary>
        internal void HideActions()
        {
            this.FindControl<Button>("SelectedActionsButton")?.Flyout?.Hide();
        }
    }
}
