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
