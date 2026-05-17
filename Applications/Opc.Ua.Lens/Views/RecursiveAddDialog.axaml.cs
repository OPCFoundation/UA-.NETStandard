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

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Views;

/// <summary>
/// User options for the "Add recursively…" flow.
/// </summary>
internal sealed record RecursiveAddOptions(
    bool IncludeVariables,
    bool IncludeEvents,
    TimeSpan SamplingInterval,
    int MaxItems,
    int MaxDepth);

internal sealed partial class RecursiveAddDialog : Window
{
    public RecursiveAddDialog()
    {
        InitializeComponent();
    }

    public RecursiveAddDialog(string startingNodeDescription)
    {
        InitializeComponent();
        this.RequiredControl<TextBlock>("StartingNodeLabel").Text = startingNodeDescription;
        this.RequiredControl<Button>("OkButton").Click += (_, _) =>
        {
            var err = this.RequiredControl<TextBlock>("ErrorLabel");
            err.IsVisible = false;
            bool inclVars = this.RequiredControl<CheckBox>("IncludeVariablesCheck").IsChecked == true;
            bool inclEvts = this.RequiredControl<CheckBox>("IncludeEventsCheck").IsChecked == true;
            if (!inclVars && !inclEvts)
            {
                err.Text = "Pick at least one of Variables or Events.";
                err.IsVisible = true;
                return;
            }
            if (!int.TryParse(this.RequiredControl<TextBox>("SamplingMs").Text,
                              NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out int samplingMs) || samplingMs <= 0)
            {
                err.Text = "Sampling interval must be a positive integer (ms).";
                err.IsVisible = true;
                return;
            }
            if (!int.TryParse(this.RequiredControl<TextBox>("MaxItems").Text,
                              NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out int maxItems) || maxItems <= 0)
            {
                err.Text = "Max items must be a positive integer.";
                err.IsVisible = true;
                return;
            }
            if (!int.TryParse(this.RequiredControl<TextBox>("MaxDepth").Text,
                              NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out int maxDepth) || maxDepth <= 0)
            {
                err.Text = "Max depth must be a positive integer.";
                err.IsVisible = true;
                return;
            }
            Close(new RecursiveAddOptions(
                IncludeVariables: inclVars,
                IncludeEvents: inclEvts,
                SamplingInterval: TimeSpan.FromMilliseconds(samplingMs),
                MaxItems: maxItems,
                MaxDepth: maxDepth));
        };
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
