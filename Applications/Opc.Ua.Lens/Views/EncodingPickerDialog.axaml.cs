/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Connection;

namespace UaLens.Views;

/// <summary>
/// Three-radio dialog returning the chosen <see cref="EncodingFormat"/>
/// (or null on cancel).  Used by every encode / decode flow so the user
/// can pick whether the file uses Binary, XML or JSON.
/// </summary>
internal sealed partial class EncodingPickerDialog : Window
{
    public EncodingFormat? Result { get; private set; }

    public EncodingPickerDialog(EncodingFormat? initial = null)
    {
        InitializeComponent();
        var binary = this.RequiredControl<RadioButton>("BinaryRadio");
        var xml = this.RequiredControl<RadioButton>("XmlRadio");
        var json = this.RequiredControl<RadioButton>("JsonRadio");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        EncodingFormat fmt = initial ?? EncodingFormat.Json;
        switch (fmt)
        {
            case EncodingFormat.Binary:
                binary.IsChecked = true;
                break;
            case EncodingFormat.Xml:
                xml.IsChecked = true;
                break;
            default:
                json.IsChecked = true;
                break;
        }

        ok.Click += (_, _) =>
        {
            if (binary.IsChecked == true)
            {
                Result = EncodingFormat.Binary;
            }
            else if (xml.IsChecked == true)
            {
                Result = EncodingFormat.Xml;
            }
            else
            {
                Result = EncodingFormat.Json;
            }
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
