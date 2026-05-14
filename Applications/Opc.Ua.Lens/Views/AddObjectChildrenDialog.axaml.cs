/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Discriminated decision returned by <see cref="AddObjectChildrenDialog"/>.
/// Used by the Add-button handler in <c>MainWindow.axaml.cs</c> to decide
/// which subscription items to create for the selected Object.
/// </summary>
internal enum ObjectAddMode
{
    None,
    EventsOnly,
    VariablesOnly,
    Both
}

internal readonly record struct ObjectAddDecision(ObjectAddMode Mode, uint SamplingIntervalMs);

/// <summary>
/// Dialog shown when the address-space-selected Object has both
/// <see cref="Opc.Ua.EventNotifiers.SubscribeToEvents"/> set AND at least one
/// HasComponent child Variable.  Lets the user choose Events / Variables /
/// Both, with Variables disabled when no child variables exist (the dialog
/// is then bypassed in favour of the simpler <see cref="AddItemDialog"/>).
/// </summary>
internal sealed partial class AddObjectChildrenDialog : Window
{
    public ObjectAddDecision? Result { get; private set; }

    public AddObjectChildrenDialog(NodeViewModel node, byte? eventNotifier,
        int childVariableCount)
    {
        InitializeComponent();

        this.RequiredControl<TextBlock>("NodeIdLabel").Text = node.NodeId.ToString() ?? string.Empty;
        this.RequiredControl<TextBlock>("EventInfoLabel").Text = eventNotifier.HasValue
            ? $"0x{eventNotifier.Value:X2} (SubscribeToEvents={(eventNotifier.Value & Opc.Ua.EventNotifiers.SubscribeToEvents) != 0})"
            : "(unread)";
        this.RequiredControl<TextBlock>("VariablesInfoLabel").Text = childVariableCount.ToString(CultureInfo.InvariantCulture)
            + " HasComponent variable(s) (HasProperty children excluded)";

        bool hasEvents = eventNotifier.HasValue
            && (eventNotifier.Value & Opc.Ua.EventNotifiers.SubscribeToEvents) != 0;
        bool hasVars = childVariableCount > 0;

        var eventsRadio = this.RequiredControl<RadioButton>("EventsRadio");
        var varsRadio = this.RequiredControl<RadioButton>("VariablesRadio");
        var bothRadio = this.RequiredControl<RadioButton>("BothRadio");
        var samplingMs = this.RequiredControl<TextBox>("SamplingMs");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        eventsRadio.IsEnabled = hasEvents;
        varsRadio.IsEnabled = hasVars;
        bothRadio.IsEnabled = hasEvents && hasVars;

        // Default selection: Both when both available, else whichever is.
        if (hasEvents && hasVars)
        { bothRadio.IsChecked = true; }
        else if (hasEvents)
        { eventsRadio.IsChecked = true; }
        else if (hasVars)
        { varsRadio.IsChecked = true; }

        ok.Click += (_, _) =>
        {
            if (!uint.TryParse(samplingMs.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint smp))
            {
                return;
            }
            ObjectAddMode mode = ObjectAddMode.None;
            if (eventsRadio.IsChecked == true)
            {
                mode = ObjectAddMode.EventsOnly;
            }
            else if (varsRadio.IsChecked == true)
            {
                mode = ObjectAddMode.VariablesOnly;
            }
            else if (bothRadio.IsChecked == true)
            {
                mode = ObjectAddMode.Both;
            }

            if (mode == ObjectAddMode.None)
            {
                return;
            }
            Result = new ObjectAddDecision(mode, smp);
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
