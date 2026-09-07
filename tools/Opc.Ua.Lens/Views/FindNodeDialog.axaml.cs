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
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Resolves one or more <c>RelativePath</c> strings against a starting
/// node via <c>TranslateBrowsePathsToNodeIds</c> and dumps the results
/// into the multi-line text box.  Port of
/// <c>UA-.NETStandard-Samples/Samples/Controls.Net4/Common/FindNodeDlg.cs</c>
/// adapted for the UaLens browse-by-id workflow.
/// </summary>
internal sealed partial class FindNodeDialog : Window
{
    private readonly BrowserViewModel m_browser;

    public FindNodeDialog(BrowserViewModel browser, NodeId? defaultStart = null)
    {
        m_browser = browser ?? throw new ArgumentNullException(nameof(browser));
        InitializeComponent();

        var startBox = this.RequiredControl<TextBox>("StartNodeBox");
        var pathsBox = this.RequiredControl<TextBox>("PathsBox");
        var resolve = this.RequiredControl<Button>("ResolveButton");
        var close = this.RequiredControl<Button>("CloseButton");

        if (defaultStart is { } start && !start.IsNull)
        {
            startBox.Text = start.ToString();
        }

        resolve.Click += async (_, _) =>
        {
            string startText = (startBox.Text ?? string.Empty).Trim();
            NodeId anchor = NodeId.Null;
            if (startText.Length > 0)
            {
                try
                {
                    anchor = NodeId.Parse(startText);
                }
                catch
                {
                    pathsBox.Text = $"// failed to parse starting NodeId '{startText}'.";
                    return;
                }
            }
            var paths = new List<string>();
            foreach (string line in (pathsBox.Text ?? string.Empty)
                .Split('\n', StringSplitOptions.None))
            {
                string trimmed = line.TrimEnd('\r').Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    paths.Add(trimmed);
                }
            }
            if (paths.Count == 0)
            {
                pathsBox.Text = "// enter at least one relative path";
                return;
            }
            IReadOnlyList<(string Path, StatusCode Status, IReadOnlyList<NodeId> Matches)> results;
            try
            {
                results = await m_browser
                    .ResolveBrowsePathsAsync(anchor, paths, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                pathsBox.Text = $"// resolve failed: {ex.Message}";
                return;
            }
            var sb = new StringBuilder();
            foreach ((string p, StatusCode s, IReadOnlyList<NodeId> matches) in results)
            {
                sb.Append(p);
                sb.Append("    // ");
                if (matches.Count > 0)
                {
                    for (int i = 0; i < matches.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(", ");
                        }
                        sb.Append(matches[i]);
                    }
                }
                else
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "no match ({0})",
                        StatusCode.IsBad(s) ? s.ToString() : "Good");
                }
                sb.Append('\n');
            }
            pathsBox.Text = sb.ToString();
        };
        close.Click += (_, _) => Close();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
