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
using System.Text;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Views;

namespace UaLens.Plugins.EventView;

/// <summary>
/// Modal editor for an OPC UA <see cref="ContentFilter"/>. Hosts the
/// generic <see cref="ContentFilterEditor"/> control full-screen so the
/// operand list has room to render, and adds a Validate button that
/// runs <see cref="ContentFilter.Validate(IFilterContext)"/> against
/// the live session's TypeTree before the user commits. Returns the
/// authored <see cref="ContentFilter"/> on Apply, or <c>null</c> on
/// Cancel.
/// </summary>
internal sealed partial class WhereClauseDialog : Window
{
    private readonly ISession? m_session;
    private ContentFilterEditor m_editor = null!;
    private TextBlock m_status = null!;

    public ContentFilter? Result { get; private set; }

    /// <summary>
    /// True when the user committed via Apply (even if the result is an
    /// empty filter); false when Cancel was pressed. Lets the caller
    /// distinguish "no change" from "explicitly cleared".
    /// </summary>
    public bool Applied { get; private set; }

    public WhereClauseDialog()
        : this(null, null)
    {
    }

    public WhereClauseDialog(ContentFilter? initial, ISession? session)
    {
        m_session = session;
        InitializeComponent();
        m_editor = this.RequiredControl<ContentFilterEditor>("WhereClauseEditor");
        m_status = this.RequiredControl<TextBlock>("ValidationStatus");
        m_editor.Initialize(initial, session);

        var clear = this.RequiredControl<Button>("ClearButton");
        var validate = this.RequiredControl<Button>("ValidateButton");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        clear.Click += (_, _) =>
        {
            m_editor.Initialize(null, session);
            m_status.Foreground = Brushes.Gray;
            m_status.Text = "Cleared.";
        };
        validate.Click += (_, _) => RunValidation();
        ok.Click += (_, _) =>
        {
            // Apply does NOT auto-validate so the user can store a
            // work-in-progress filter the server will reject — the
            // status badge already shows whatever the last Validate
            // returned. We trim empty filters to null so the caller
            // can render "(none)" in the parent dialog.
            ContentFilter built = m_editor.BuildResult();
            Result = built.Elements.Count == 0 ? null : built;
            Applied = true;
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private void RunValidation()
    {
        ContentFilter built;
        try
        {
            built = m_editor.BuildResult();
        }
        catch (Exception ex)
        {
            m_status.Foreground = Brushes.OrangeRed;
            m_status.Text = $"Build failed: {ex.Message}";
            return;
        }

        if (built.Elements.Count == 0)
        {
            m_status.Foreground = Brushes.Gray;
            m_status.Text = "Filter is empty — the server will accept all events.";
            return;
        }

        if (m_session is null)
        {
            m_status.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            m_status.Text = "No active session — structure looks well-formed but cannot be validated against the server's TypeTree. Connect to validate.";
            return;
        }

        try
        {
            var ctx = new FilterContext(m_session.NamespaceUris, m_session.TypeTree);
            ContentFilter.Result vr = built.Validate(ctx);
            if (ServiceResult.IsGood(vr.Status))
            {
                m_status.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                m_status.Text = "✓ Validates against the session's TypeTree.";
                return;
            }
            var buf = new StringBuilder();
            buf.Append("✗ ").Append(vr.Status.ToString());
            for (int i = 0; i < vr.ElementResults.Count; i++)
            {
                ContentFilter.ElementResult? er = vr.ElementResults[i];
                if (er is null || ServiceResult.IsGood(er.Status))
                {
                    continue;
                }
                buf.AppendLine();
                buf.AppendFormat(CultureInfo.InvariantCulture,
                    "  [{0}] {1}", i, er.Status.ToString());
                if (er.OperandResults is { } ops)
                {
                    for (int j = 0; j < ops.Count; j++)
                    {
                        ServiceResult? or = ops[j];
                        if (or is null || ServiceResult.IsGood(or))
                        {
                            continue;
                        }
                        buf.AppendLine();
                        buf.AppendFormat(CultureInfo.InvariantCulture,
                            "    operand[{0}] {1}", j, or.ToString());
                    }
                }
            }
            m_status.Foreground = Brushes.OrangeRed;
            m_status.Text = buf.ToString();
        }
        catch (Exception ex)
        {
            m_status.Foreground = Brushes.OrangeRed;
            m_status.Text = $"Validate failed: {ex.Message}";
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
