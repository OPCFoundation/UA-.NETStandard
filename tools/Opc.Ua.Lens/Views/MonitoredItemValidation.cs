// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Opc.Ua;

namespace UaLens.Views;

/// <summary>
/// Parameter rules shared by new-item and batch-settings dialogs.
/// </summary>
internal static class MonitoredItemValidation
{
    public static bool TrySampling(string? text, out TimeSpan interval, out string error)
    {
        interval = default;
        error = "Sampling interval (ms): enter -1 (inherit publishing), 0 (fastest), " +
            "or a positive number up to 3600000 (one hour), using the current decimal separator.";
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
            !double.IsFinite(value) ||
            (value < 0 && value != -1) ||
            value > 3_600_000)
        {
            return false;
        }
        interval = TimeSpan.FromMilliseconds(value);
        error = string.Empty;
        return true;
    }

    public static bool TryDeadband(string? text, DeadbandType type, out double value, out string error)
    {
        value = 0;
        error = string.Empty;
        if (type == DeadbandType.None)
        {
            return true;
        }
        error = type == DeadbandType.Percent
            ? "Deadband value (%): enter a finite number from 0 to 100, using the current decimal separator."
            : "Deadband value (absolute): enter a finite, non-negative number, using the current decimal separator.";
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
            !double.IsFinite(value) ||
            value < 0 ||
            (type == DeadbandType.Percent && value > 100))
        {
            return false;
        }
        error = string.Empty;
        return true;
    }

    public static void ShowError(TextBlock summary, Control field, string message)
    {
        summary.Text = message;
        AutomationProperties.SetName(summary, message);
        AutomationProperties.SetHelpText(field, message);
        DataValidationErrors.SetErrors(field, new[] { message });
        field.Focus();
    }

    public static void ClearError(TextBlock summary, Control field)
    {
        if (string.Equals(summary.Text, AutomationProperties.GetHelpText(field), StringComparison.Ordinal))
        {
            summary.Text = string.Empty;
            AutomationProperties.SetName(summary, string.Empty);
        }
        AutomationProperties.SetHelpText(field, string.Empty);
        DataValidationErrors.ClearErrors(field);
    }

    public static void SetDeadbandName(Control field, int selectedIndex)
    {
        AutomationProperties.SetName(field, selectedIndex == (int)DeadbandType.Percent
            ? "Deadband value (%)" : "Deadband value (absolute)");
    }
}
