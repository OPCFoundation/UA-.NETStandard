/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
