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
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;

namespace UaLens.Views;

/// <summary>
/// Dialog returning the user's choice of <c>(EndpointDescription, UserTokenPolicy?)</c>:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>If the user picks an endpoint root → <see cref="SelectedTokenPolicy"/> is
///   <c>null</c> and the caller defaults to Anonymous regardless of what the
///   endpoint actually advertises.</item>
/// <item>If the user picks a child policy → that policy is returned.
///   Certificate / IssuedToken policies are rendered but their tree nodes have
///   <c>IsEnabled=false</c>, which prevents the Avalonia TreeView from selecting
///   them and keeps the OK button disabled.</item>
/// </list>
/// </remarks>
internal sealed partial class EndpointPickerDialog : Window
{
    public EndpointDescription? SelectedEndpoint { get; private set; }
    public UserTokenPolicy? SelectedTokenPolicy { get; private set; }

    public EndpointPickerDialog(ArrayOf<EndpointDescription> endpoints)
    {
        InitializeComponent();

        var tree = this.RequiredControl<TreeView>("Tree");
        var status = this.RequiredControl<TextBlock>("StatusLabel");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        var roots = new ObservableCollection<PickerNode>();
        EndpointPickerDialog.PopulateRoots(endpoints, roots);
        tree.ItemsSource = roots;

        // Default selection: first endpoint with SecurityMode=None / SecurityPolicy=None,
        // else the very first endpoint root.
        PickerNode? defaultRoot =
            roots.FirstOrDefault(r => r.IsNoneNone) ?? roots.FirstOrDefault();
        if (defaultRoot is not null)
        {
            tree.SelectedItem = defaultRoot;
        }

        UpdateState();
        tree.SelectionChanged += (_, _) => UpdateState();

        ok.Click += (_, _) =>
        {
            if (tree.SelectedItem is not PickerNode node || !node.IsSelectable)
            {
                return;
            }
            SelectedEndpoint = node.Endpoint;
            SelectedTokenPolicy = node.TokenPolicy;
            Close((SelectedEndpoint, SelectedTokenPolicy));
        };
        cancel.Click += (_, _) => Close(null);

        void UpdateState()
        {
            if (tree.SelectedItem is PickerNode node && node.IsSelectable)
            {
                ok.IsEnabled = true;
                if (node.TokenPolicy is null)
                {
                    status.Text = "Endpoint root selected → connect with Anonymous identity.";
                }
                else if (node.TokenPolicy.TokenType == UserTokenType.UserName)
                {
                    status.Text = "UserName policy → you will be prompted for credentials.";
                }
                else
                {
                    status.Text = $"{node.TokenPolicy.TokenType} policy.";
                }
            }
            else
            {
                ok.IsEnabled = false;
                status.Text = tree.SelectedItem is PickerNode n
                    ? $"{n.Display} — not yet supported in this preview."
                    : "Pick an endpoint or expand and pick a user-token policy.";
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static void PopulateRoots(ArrayOf<EndpointDescription> endpoints, ObservableCollection<PickerNode> roots)
    {
        foreach (EndpointDescription ep in endpoints)
        {
            var rootNode = new PickerNode
            {
                Endpoint = ep,
                TokenPolicy = null,
                Display = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0,-26}  {1,-18}  ({2}, level {3})  {4}",
                    ShortPolicyName(ep.SecurityPolicyUri ?? string.Empty),
                    ep.SecurityMode,
                    ShortTransportName(ep.TransportProfileUri ?? string.Empty),
                    ep.SecurityLevel,
                    ep.EndpointUrl),
                IsExpanded = true,
                IsSelectable = true
            };
            rootNode.IsNoneNone = ep.SecurityMode == MessageSecurityMode.None
                && (ep.SecurityPolicyUri ?? string.Empty).EndsWith("#None", StringComparison.Ordinal);

            foreach (UserTokenPolicy pol in ep.UserIdentityTokens)
            {
                bool selectable = pol.TokenType is UserTokenType.Anonymous or UserTokenType.UserName;
                string display = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0,-12}  policyId={1}  {2}{3}",
                    pol.TokenType,
                    pol.PolicyId ?? string.Empty,
                    ShortPolicyName(pol.SecurityPolicyUri ?? string.Empty),
                    selectable ? string.Empty : "  (not supported in this preview)");
                rootNode.Children.Add(new PickerNode
                {
                    Endpoint = ep,
                    TokenPolicy = pol,
                    Display = display,
                    IsExpanded = false,
                    IsSelectable = selectable
                });
            }

            roots.Add(rootNode);
        }
    }

    private static string ShortPolicyName(string uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return "(none)";
        }
        int hash = uri.IndexOf('#', StringComparison.Ordinal);
        return hash >= 0 ? uri.Substring(hash + 1) : uri;
    }

    private static string ShortTransportName(string uri)
    {
        if (uri.EndsWith("/uatcp-uasc-uabinary", StringComparison.Ordinal))
        {
            return "UA-TCP";
        }

        if (uri.EndsWith("/uahttps-uabinary", StringComparison.Ordinal))
        {
            return "UA-HTTPS";
        }

        return uri;
    }
}

/// <summary>One row in the endpoint picker TreeView.</summary>
internal sealed class PickerNode
{
    public required EndpointDescription Endpoint { get; init; }
    public UserTokenPolicy? TokenPolicy { get; init; }
    public required string Display { get; init; }
    public bool IsExpanded { get; set; }
    public bool IsSelectable { get; set; }
    public bool IsNoneNone { get; set; }
    public ObservableCollection<PickerNode> Children { get; } = new();
}
