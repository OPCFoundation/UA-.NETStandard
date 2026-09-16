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
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Plugins.Historian;

/// <summary>
/// Modal editor for the <see cref="Opc.Ua.Annotation"/> attached to a
/// single history row.  The user edits only the <c>Message</c> — the
/// <c>UserName</c> is auto-populated from the current OS user and the
/// <c>AnnotationTime</c> is stamped with <see cref="DateTime.UtcNow"/>
/// at Save, per OPC UA Part 11 §5.4.5.  Dialog result is the populated
/// <see cref="Annotation"/> or <c>null</c> on cancel.
/// </summary>
internal sealed partial class AnnotationEditDialog : Window
{
    private readonly DateTime m_sourceTimestamp;
    private readonly Annotation m_seed;

    public Annotation? Result { get; private set; }

    public AnnotationEditDialog()
        : this(DateTime.UtcNow, null)
    {
    }

    public AnnotationEditDialog(DateTime sourceTimestamp, Annotation? existing)
    {
        m_sourceTimestamp = sourceTimestamp.Kind == DateTimeKind.Utc
            ? sourceTimestamp
            : sourceTimestamp.ToUniversalTime();
        m_seed = existing ?? new Annotation
        {
            UserName = CurrentUserName(),
            AnnotationTime = DateTime.UtcNow,
            Message = string.Empty
        };
        InitializeComponent();

        TextBlock tsLabel = this.RequiredControl<TextBlock>("TimestampLabel");
        TextBlock userLabel = this.RequiredControl<TextBlock>("UserNameLabel");
        TextBlock annTimeLabel = this.RequiredControl<TextBlock>("AnnotationTimeLabel");
        TextBox message = this.RequiredControl<TextBox>("MessageText");
        TextBlock resultLabel = this.RequiredControl<TextBlock>("ResultLabel");
        Button ok = this.RequiredControl<Button>("OkButton");
        Button cancel = this.RequiredControl<Button>("CancelButton");

        tsLabel.Text = m_sourceTimestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        userLabel.Text = CurrentUserName();
        annTimeLabel.Text = "(stamped at Save)";
        message.Text = m_seed.Message ?? string.Empty;

        ok.Click += (_, _) =>
        {
            string text = message.Text ?? string.Empty;
            if (text.Length == 0)
            {
                resultLabel.Text = "Message must not be empty.";
                return;
            }
            Result = new Annotation
            {
                Message = text,
                UserName = CurrentUserName(),
                AnnotationTime = DateTime.UtcNow
            };
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private static string CurrentUserName()
    {
        try
        {
            string? n = Environment.UserName;
            return string.IsNullOrEmpty(n) ? "(anonymous)" : n;
        }
        catch (Exception)
        {
            return "(anonymous)";
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
