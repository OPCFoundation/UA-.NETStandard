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
using UaLens.Subscriptions;

namespace UaLens.Views
{
    internal sealed partial class SubscriptionSettingsDialog : Window
    {
        public SubscriptionConfig? Result { get; private set; }

        public SubscriptionSettingsDialog(SubscriptionConfig current, bool engineHasWorkerPool)
        {
            InitializeComponent();

            TextBox pubMs = this.RequiredControl<TextBox>("PubMs");
            TextBox keepAlive = this.RequiredControl<TextBox>("KeepAlive");
            TextBox lifetime = this.RequiredControl<TextBox>("Lifetime");
            TextBox maxNotifs = this.RequiredControl<TextBox>("MaxNotifs");
            TextBox priority = this.RequiredControl<TextBox>("Priority");
            CheckBox pubEna = this.RequiredControl<CheckBox>("PublishingEnabled");
            Button ok = this.RequiredControl<Button>("OkButton");
            Button cancel = this.RequiredControl<Button>("CancelButton");

            pubMs.Text = current.PublishingInterval.TotalMilliseconds.ToString("0.###", CultureInfo.CurrentCulture);
            keepAlive.Text = current.KeepAliveCount.ToString(CultureInfo.InvariantCulture);
            lifetime.Text = current.LifetimeCount.ToString(CultureInfo.InvariantCulture);
            maxNotifs.Text = current.MaxNotificationsPerPublish.ToString(CultureInfo.InvariantCulture);
            priority.Text = current.Priority.ToString(CultureInfo.InvariantCulture);
            pubEna.IsChecked = current.PublishingEnabled;
            _ = engineHasWorkerPool;

            ok.Click += (_, _) =>
            {
                if (!double.TryParse(pubMs.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double pub) ||
                    !double.IsFinite(pub) ||
                    pub < 0 ||
                    pub > 3_600_000 ||
                    !uint.TryParse(keepAlive.Text, out uint ka) ||
                    !uint.TryParse(lifetime.Text, out uint life) ||
                    !uint.TryParse(maxNotifs.Text, out uint maxN) ||
                    !byte.TryParse(priority.Text, out byte pri))
                {
                    this.RequiredControl<TextBlock>("ValidationError").Text =
                        "Enter a publishing interval from 0 to 3,600,000 ms, non-negative counts, " +
                        "and a priority from 0 to 255.";
                    return;
                }
                Result = new SubscriptionConfig
                {
                    PublishingInterval = TimeSpan.FromMilliseconds(pub),
                    KeepAliveCount = ka,
                    LifetimeCount = life,
                    MaxNotificationsPerPublish = maxN,
                    Priority = pri,
                    PublishingEnabled = pubEna.IsChecked == true,
                    MinPublishRequestCount = current.MinPublishRequestCount,
                    MaxPublishRequestCount = current.MaxPublishRequestCount
                };
                Close(Result);
            };
            cancel.Click += (_, _) => Close(null);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
