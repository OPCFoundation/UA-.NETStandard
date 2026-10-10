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
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Plugins.RoleManagement;

/// <summary>
/// Modal dialog that collects the inputs for
/// <see cref="Opc.Ua.Client.Roles.IRoleManagementClient.AddIdentityAsync"/>.
/// Returns a fully-built <see cref="IdentityMappingRuleType"/> on OK or
/// <c>null</c> on Cancel.
/// </summary>
internal sealed partial class AddIdentityDialog : Window
{
    public AddIdentityDialog()
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
        var typeBox = this.RequiredControl<ComboBox>("CriteriaTypeBox");
        var criteriaBox = this.RequiredControl<TextBox>("CriteriaBox");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var err = this.RequiredControl<TextBlock>("ErrorLabel");

        // Pre-populate the combo from the IdentityCriteriaType enum so the
        // list stays in sync with the SDK without per-value duplication
        // in XAML.
        IdentityCriteriaType[] items = Enum.GetValues<IdentityCriteriaType>();
        typeBox.ItemsSource = items;
        typeBox.SelectedIndex = 0;

        ok.Click += (_, _) =>
        {
            if (typeBox.SelectedItem is not IdentityCriteriaType type)
            {
                err.Text = "Pick a criteria type.";
                err.IsVisible = true;
                return;
            }
            string criteria = (criteriaBox.Text ?? string.Empty).Trim();
            // Anonymous / AuthenticatedUser don't need a criteria string
            // (Part 18 §4.4.4); all other types do.
            bool criteriaRequired =
                type != IdentityCriteriaType.Anonymous
                && type != IdentityCriteriaType.AuthenticatedUser;
            if (criteriaRequired && criteria.Length == 0)
            {
                err.Text = $"Criteria is required for {type}.";
                err.IsVisible = true;
                return;
            }
            var rule = new IdentityMappingRuleType
            {
                CriteriaType = type,
                Criteria = criteria
            };
            Close(rule);
        };

        cancel.Click += (_, _) => Close(null);
    }
}
