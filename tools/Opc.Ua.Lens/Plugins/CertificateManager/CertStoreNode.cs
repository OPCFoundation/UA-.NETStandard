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
using System.Security.Cryptography.X509Certificates;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;

namespace UaLens.Plugins.CertificateManager;

/// <summary>
/// Logical bucket for each well-known store the Certificate Manager
/// exposes in its left-hand tree.  <see cref="Custom"/> covers any
/// additional <c>DirectoryStore</c> roots the user attaches via the
/// "Add store…" toolbar button.
/// </summary>
internal enum CertStoreRole
{
    Application,
    TrustedPeer,
    TrustedIssuer,
    Rejected,
    Custom
}

/// <summary>
/// Tree-bound row for one certificate store in the Certificate Manager
/// plug-in.  Wraps a <see cref="CertificateStoreIdentifier"/> plus a
/// human-friendly display label and a glyph picked from
/// <see cref="Role"/>.  Adding/deleting certs in the underlying store
/// is performed by <see cref="CertificateManagerPlugin"/>; this row is
/// pure view-state.
/// </summary>
internal sealed partial class CertStoreNode : ObservableObject
{
    public CertStoreNode(CertStoreRole role, string displayName, CertificateStoreIdentifier identifier)
    {
        Role = role;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
    }

    /// <summary>Which well-known store this row represents.</summary>
    public CertStoreRole Role { get; }

    /// <summary>The label shown in the tree row.</summary>
    public string DisplayName { get; }

    /// <summary>Underlying SDK identifier used to <see cref="CertificateStoreIdentifier.OpenStore"/>.</summary>
    public CertificateStoreIdentifier Identifier { get; }

    /// <summary>Small leading glyph for the tree row.</summary>
    public string Glyph => Role switch
    {
        CertStoreRole.Application => "🛡",
        CertStoreRole.TrustedPeer => "✓",
        CertStoreRole.TrustedIssuer => "🏛",
        CertStoreRole.Rejected => "✗",
        _ => "📁"
    };

    /// <summary>Tooltip-friendly description: <c>[StoreType]StorePath</c>.</summary>
    public string Description
    {
        get
        {
            string st = Identifier.StoreType ?? string.Empty;
            string sp = Identifier.StorePath ?? string.Empty;
            if (string.IsNullOrEmpty(st))
            {
                return sp;
            }
            return string.Format(CultureInfo.InvariantCulture, "[{0}]{1}", st, sp);
        }
    }
}

/// <summary>
/// Right-pane row for a single <see cref="X509Certificate2"/> inside
/// the currently-selected store.  All values are pre-formatted strings
/// so the ListBox template stays free of converters.
/// </summary>
internal sealed record CertItemRow(
    string Thumbprint,
    string Subject,
    string Issuer,
    string NotBefore,
    string NotAfter,
    string Status,
    X509Certificate2 Certificate)
{
    public static CertItemRow From(X509Certificate2 cert)
    {
        if (cert is null)
        {
            throw new ArgumentNullException(nameof(cert));
        }

        DateTime now = DateTime.UtcNow;
        DateTime nb = cert.NotBefore.ToUniversalTime();
        DateTime na = cert.NotAfter.ToUniversalTime();
        string status =
            na < now ? "EXPIRED"
            : nb > now ? "NOT YET VALID"
            : "OK";
        return new CertItemRow(
            cert.Thumbprint,
            ShortName(cert.Subject),
            ShortName(cert.Issuer),
            nb.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            na.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            status,
            cert);
    }

    private static string ShortName(string distinguished)
    {
        foreach (string rdn in distinguished.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rdn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return rdn[3..];
            }
        }
        return distinguished;
    }
}
