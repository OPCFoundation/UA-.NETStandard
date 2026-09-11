/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Views;

internal interface ICertificateStoreAccess
{
    Task<ArrayOf<X509Certificate2>> ListAsync(CertStoreKind kind, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(CertStoreKind kind, string thumbprint, CancellationToken cancellationToken);

    Task<bool> AddAsync(CertStoreKind kind, X509Certificate2 certificate, CancellationToken cancellationToken);

    Task<bool> TrustRejectedAsync(string thumbprint, CancellationToken cancellationToken);
}

/// <summary>
/// The last explicit mutation, independent of loading state and certificate counts.
/// </summary>
internal sealed record CertificateOperationResult(
    string Operation, CertStoreKind Store, int Attempted, int Succeeded, int Failed, string Detail)
{
    public string Summary =>
        $"{Operation} — {Store}: attempted {Attempted}, succeeded {Succeeded}, failed {Failed}."
        + (string.IsNullOrEmpty(Detail) ? string.Empty : $" {Detail}");
}

/// <summary>
/// Executes certificate mutations and refreshes without overwriting their outcome.
/// The caller owns the presentation context; no window or dispatcher is required.
/// </summary>
internal sealed partial class CertificateStoreOperations : ObservableObject
{
    public CertificateStoreOperations(ICertificateStoreAccess store, TimeProvider? timeProvider = null)
    {
        m_store = store ?? throw new ArgumentNullException(nameof(store));
        m_timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ObservableCollection<CertRow> Trusted { get; } = new();

    public ObservableCollection<CertRow> Issuers { get; } = new();

    public ObservableCollection<CertRow> Rejected { get; } = new();

    public async Task ReloadAsync(CertStoreKind kind, CancellationToken cancellationToken = default)
    {
        LoadingStatus = $"Loading {kind}…";
        try
        {
            ArrayOf<X509Certificate2> certificates = await m_store.ListAsync(kind, cancellationToken)
                .ConfigureAwait(true);
            try
            {
                ObservableCollection<CertRow> target = TargetFor(kind);
                target.Clear();
                foreach (X509Certificate2 certificate in certificates)
                {
                    target.Add(CertRow.From(certificate));
                }
                LoadingStatus = $"{kind}: {certificates.Count} certificate(s).";
            }
            finally
            {
                DisposeCertificates(certificates);
            }
        }
        catch (Exception ex)
        {
            LoadingStatus = $"Load {kind} failed: {ex.Message}";
        }
    }

    public Task DeleteAsync(
        CertStoreKind kind, string thumbprint, CancellationToken cancellationToken = default)
        => MutateAsync(
            "Delete", kind, [thumbprint],
            (item, token) => m_store.DeleteAsync(kind, item, token), false, cancellationToken);

    public Task TrustAsync(string thumbprint, CancellationToken cancellationToken = default)
        => MutateAsync(
            "Trust into Trusted", CertStoreKind.Rejected, [thumbprint],
            m_store.TrustRejectedAsync, true, cancellationToken);

    public Task AddAsync(
        CertStoreKind kind, X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return MutateAsync(
            "Add", kind, [certificate.Thumbprint],
            (_, token) => m_store.AddAsync(kind, certificate, token), false, cancellationToken);
    }

    public async Task DeleteAllAsync(
        CertStoreKind kind, bool expiredOnly, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        LastResult = null;
        string operation = expiredOnly ? "Delete expired" : "Clear all";
        try
        {
            ArrayOf<X509Certificate2> certificates = await m_store.ListAsync(kind, cancellationToken)
                .ConfigureAwait(true);
            var thumbprints = new List<string>();
            try
            {
                DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
                foreach (X509Certificate2 certificate in certificates)
                {
                    if (!expiredOnly || certificate.NotAfter.ToUniversalTime() < now)
                    {
                        thumbprints.Add(certificate.Thumbprint);
                    }
                }
            }
            finally
            {
                DisposeCertificates(certificates);
            }
            await MutateCoreAsync(
                operation, kind, thumbprints.ToArray(),
                (item, token) => m_store.DeleteAsync(kind, item, token), false, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LastResult = new CertificateOperationResult(
                operation, kind, 0, 0, 0, $"Could not enumerate targets; nothing was attempted: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ReportFailure(string operation, CertStoreKind store, string detail)
    {
        LastResult = new CertificateOperationResult(operation, store, 1, 0, 1, detail);
    }

    public void AcknowledgeResult()
    {
        LastResult = null;
    }

    private async Task MutateAsync(
        string operation,
        CertStoreKind kind,
        ArrayOf<string> thumbprints,
        Func<string, CancellationToken, Task<bool>> mutation,
        bool refreshTrusted,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        LastResult = null;
        try
        {
            await MutateCoreAsync(
                operation, kind, thumbprints, mutation, refreshTrusted, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MutateCoreAsync(
        string operation,
        CertStoreKind kind,
        ArrayOf<string> thumbprints,
        Func<string, CancellationToken, Task<bool>> mutation,
        bool refreshTrusted,
        CancellationToken cancellationToken)
    {
        int attempted = 0;
        int succeeded = 0;
        var failures = new List<string>();
        for (int index = 0; index < thumbprints.Count; index++)
        {
            string thumbprint = thumbprints[index];
            attempted++;
            try
            {
                if (await mutation(thumbprint, cancellationToken).ConfigureAwait(true))
                {
                    succeeded++;
                }
                else
                {
                    failures.Add($"{thumbprint}: operation did not complete.");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{thumbprint}: {ex.Message}");
            }
        }
        LastResult = new CertificateOperationResult(
            operation, kind, attempted, succeeded, attempted - succeeded, string.Join(" ", failures));
        await ReloadAsync(kind, cancellationToken).ConfigureAwait(true);
        if (refreshTrusted)
        {
            await ReloadAsync(CertStoreKind.Trusted, cancellationToken).ConfigureAwait(true);
        }
    }

    private ObservableCollection<CertRow> TargetFor(CertStoreKind kind) => kind switch
    {
        CertStoreKind.Trusted => Trusted,
        CertStoreKind.Issuer => Issuers,
        CertStoreKind.Rejected => Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void DisposeCertificates(ArrayOf<X509Certificate2> certificates)
    {
        foreach (X509Certificate2 certificate in certificates)
        {
            certificate.Dispose();
        }
    }

    private readonly ICertificateStoreAccess m_store;
    private readonly TimeProvider m_timeProvider;

    [ObservableProperty]
    private CertificateOperationResult? m_lastResult;

    [ObservableProperty]
    private string m_loadingStatus = string.Empty;

    [ObservableProperty]
    private bool m_isBusy;
}
