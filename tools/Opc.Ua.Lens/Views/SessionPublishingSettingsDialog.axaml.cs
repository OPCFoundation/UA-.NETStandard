/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Subscriptions;

namespace UaLens.Views
{
    internal sealed partial class SessionPublishingSettingsDialog : Window
    {
        public SessionPublishingSettingsDialog(SessionPublishingSettings current)
        {
            ArgumentNullException.ThrowIfNull(current);
            AvaloniaXamlLoader.Load(this);
            NumericUpDown minimum = this.RequiredControl<NumericUpDown>("MinimumRequests");
            NumericUpDown maximum = this.RequiredControl<NumericUpDown>("MaximumRequests");
            minimum.Value = current.MinimumRequests;
            maximum.Value = current.MaximumRequests;
            this.RequiredControl<Button>("ApplyButton").Click += (_, _) =>
            {
                if (minimum.Value is not { } min ||
                    maximum.Value is not { } max ||
                    min < 1 ||
                    max < min ||
                    decimal.Truncate(min) != min ||
                    decimal.Truncate(max) != max)
                {
                    this.RequiredControl<TextBlock>("ValidationError").Text =
                        "Enter whole numbers with 1 <= minimum <= maximum.";
                    return;
                }
                Close(new SessionPublishingSettings((int)min, (int)max));
            };
            this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close(null);
        }
    }
}
