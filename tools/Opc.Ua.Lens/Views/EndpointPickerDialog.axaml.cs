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
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Views;

/// <summary>
/// Dialog returning the user's choice of <c>(EndpointDescription, UserTokenPolicy?)</c>:
/// </summary>
/// <remarks>
/// Endpoint roots are selectable only when Anonymous is advertised. Primary
/// provider-aware callers enable configured certificate/issued-token selection;
/// the next dialog checks the actual key/provider against that exact policy.
/// </remarks>
internal sealed partial class EndpointPickerDialog : Window
{
    public EndpointPickerDialog(
        ArrayOf<EndpointDescription> endpoints,
        bool allowConfiguredIdentities = false)
    {
        InitializeComponent();

        var tree = this.RequiredControl<TreeView>("Tree");
        var status = this.RequiredControl<TextBlock>("StatusLabel");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        ArrayOf<PickerNode> roots = CreateNodes(endpoints, allowConfiguredIdentities);
        tree.ItemsSource = roots.ToArray();

        // Default selection: first endpoint with SecurityMode=None / SecurityPolicy=None,
        // else the very first endpoint root.
        PickerNode? defaultRoot = roots.Count > 0 ? roots[0] : null;
        foreach (PickerNode root in roots)
        {
            if (root.IsNoneNone)
            {
                defaultRoot = root;
                break;
            }
        }
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
                    status.Text = node.TokenPolicy.TokenType switch
                    {
                        UserTokenType.Certificate => "Select an existing user certificate and configured password/PIN source.",
                        UserTokenType.IssuedToken => "Select a configured authority/broker; no bearer tokens are entered here.",
                        _ => $"{node.TokenPolicy.TokenType} policy."
                    };
                }
            }
            else
            {
                ok.IsEnabled = false;
                status.Text = tree.SelectedItem is PickerNode n
                    ? n.UnavailableReason ?? (n.TokenPolicy is null
                        ? "This endpoint requires an explicit user-token policy. Expand it and select one."
                        : "This caller has no configured identity-provider flow for the selected token type.")
                    : "Pick an endpoint or expand and pick a user-token policy.";
            }
        }
    }

    public EndpointDescription? SelectedEndpoint { get; private set; }

    public UserTokenPolicy? SelectedTokenPolicy { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    internal static ArrayOf<PickerNode> CreateNodes(
        ArrayOf<EndpointDescription> endpoints,
        bool allowConfiguredIdentities)
    {
        var roots = new List<PickerNode>();
        foreach (EndpointDescription ep in endpoints)
        {
            string? unavailable = allowConfiguredIdentities
                ? ConnectionTransportCatalog.GetSessionProfileUnavailableReason(ep)
                : null;
            UserTokenPolicy? anonymous = null;
            foreach (UserTokenPolicy policy in ep.UserIdentityTokens)
            {
                if (policy.TokenType == UserTokenType.Anonymous)
                {
                    anonymous = policy;
                    break;
                }
            }
            var rootNode = new PickerNode
            {
                Endpoint = ep,
                TokenPolicy = anonymous,
                Display = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0,-26}  {1,-18}  ({2}, level {3})  {4}",
                    ShortPolicyName(ep.SecurityPolicyUri ?? string.Empty),
                    ep.SecurityMode,
                    ShortTransportName(ep.TransportProfileUri ?? string.Empty),
                    ep.SecurityLevel,
                    ep.EndpointUrl),
                IsExpanded = true,
                IsSelectable = anonymous is not null && unavailable is null,
                UnavailableReason = unavailable
            };
            rootNode.IsNoneNone = ep.SecurityMode == MessageSecurityMode.None
                && (ep.SecurityPolicyUri ?? string.Empty).EndsWith("#None", StringComparison.Ordinal);

            foreach (UserTokenPolicy pol in ep.UserIdentityTokens)
            {
                bool selectable = unavailable is null &&
                    (pol.TokenType is UserTokenType.Anonymous or UserTokenType.UserName ||
                        (allowConfiguredIdentities &&
                            pol.TokenType is UserTokenType.Certificate or UserTokenType.IssuedToken));
                string display = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0,-12}  policyId={1}  {2}{3}",
                    pol.TokenType,
                    pol.PolicyId ?? string.Empty,
                    ShortPolicyName(pol.SecurityPolicyUri ?? string.Empty),
                    selectable ? string.Empty : unavailable is not null
                        ? "  (requires the binary transport profile)"
                        : "  (requires a configured identity-provider caller)");
                rootNode.Children.Add(new PickerNode
                {
                    Endpoint = ep,
                    TokenPolicy = pol,
                    Display = display,
                    IsExpanded = false,
                    IsSelectable = selectable,
                    UnavailableReason = unavailable
                });
            }

            roots.Add(rootNode);
        }
        return [.. roots];
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

/// <summary>
/// One row in the endpoint picker TreeView.
/// </summary>
internal sealed class PickerNode
{
    public required EndpointDescription Endpoint { get; init; }
    public UserTokenPolicy? TokenPolicy { get; init; }
    public required string Display { get; init; }
    public bool IsExpanded { get; set; }
    public bool IsSelectable { get; set; }
    public bool IsNoneNone { get; set; }
    public ObservableCollection<PickerNode> Children { get; } = new();
    public bool CanInteract => IsSelectable || Children.Count > 0;
    public string? UnavailableReason { get; init; }
}
