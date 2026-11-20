/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
