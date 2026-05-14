/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Subscriptions;

namespace UaLens.Views;

internal sealed partial class SubscriptionSettingsDialog : Window
{
    public SubscriptionConfig? Result { get; private set; }

    public SubscriptionSettingsDialog(SubscriptionConfig current, bool engineHasWorkerPool)
    {
        InitializeComponent();

        var pubMs = this.RequiredControl<TextBox>("PubMs");
        var keepAlive = this.RequiredControl<TextBox>("KeepAlive");
        var lifetime = this.RequiredControl<TextBox>("Lifetime");
        var maxNotifs = this.RequiredControl<TextBox>("MaxNotifs");
        var priority = this.RequiredControl<TextBox>("Priority");
        var pubEna = this.RequiredControl<CheckBox>("PublishingEnabled");
        var minReq = this.RequiredControl<TextBox>("MinReq");
        var maxReq = this.RequiredControl<TextBox>("MaxReq");
        var engineHint = this.RequiredControl<TextBlock>("EngineHint");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        pubMs.Text = ((int)current.PublishingInterval.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        keepAlive.Text = current.KeepAliveCount.ToString(CultureInfo.InvariantCulture);
        lifetime.Text = current.LifetimeCount.ToString(CultureInfo.InvariantCulture);
        maxNotifs.Text = current.MaxNotificationsPerPublish.ToString(CultureInfo.InvariantCulture);
        priority.Text = current.Priority.ToString(CultureInfo.InvariantCulture);
        pubEna.IsChecked = current.PublishingEnabled;
        minReq.Text = current.MinPublishRequestCount.ToString(CultureInfo.InvariantCulture);
        maxReq.Text = current.MaxPublishRequestCount.ToString(CultureInfo.InvariantCulture);

        engineHint.Text = engineHasWorkerPool
            ? "V2 engine — these values size both the publish-worker pool AND the publish-request pipeline (they are aliased internally)."
            : "Classic engine — drives the publish-request pipeline depth.  No separate worker pool concept in this engine.";

        ok.Click += (_, _) =>
        {
            if (!int.TryParse(pubMs.Text, out int pub) || pub <= 0
             || !uint.TryParse(keepAlive.Text, out uint ka)
             || !uint.TryParse(lifetime.Text, out uint life)
             || !uint.TryParse(maxNotifs.Text, out uint maxN)
             || !byte.TryParse(priority.Text, out byte pri)
             || !int.TryParse(minReq.Text, out int minR) || minR < 1
             || !int.TryParse(maxReq.Text, out int maxR) || maxR < 1)
            {
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
                MinPublishRequestCount = minR,
                MaxPublishRequestCount = Math.Max(minR, maxR)
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
