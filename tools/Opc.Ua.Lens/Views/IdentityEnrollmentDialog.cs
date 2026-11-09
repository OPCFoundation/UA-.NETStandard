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
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Opc.Ua;
using UaLens.Connection;
using Orientation = Avalonia.Layout.Orientation;

namespace UaLens.Views
{
    internal sealed class IdentityEnrollmentDialog : Window, IAsyncDisposable
    {
        public IdentityEnrollmentDialog(
            IIdentityEnrollment enrollment, CertificateIdentityReference reference, UserTokenPolicy policy)
        {
            m_enrollment = enrollment ?? throw new ArgumentNullException(nameof(enrollment));
            m_reference = reference ?? throw new ArgumentNullException(nameof(reference));
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
            Title = "Enroll or renew configured user certificate";
            Width = 690;
            Height = 430;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            m_status = new TextBlock
            {
                Name = "EnrollmentStatus",
                TextWrapping = TextWrapping.Wrap,
                Text = "Prepare reads the existing user key and creates a local CSR. No request is sent yet."
            };
            m_prepare = new Button { Name = "PrepareEnrollmentButton", Content = "Prepare signing request" };
            m_request = new Button { Name = "RequestEnrollmentButton", Content = "Request certificate" };
            m_adopt = new Button { Name = "AdoptEnrollmentButton", Content = "Add reviewed certificate" };
            m_consent = new CheckBox
            {
                Name = "EnrollmentConsent",
                Content = "I authorize the next displayed request or certificate-store addition."
            };
            var cancel = new Button { Name = "CancelEnrollmentButton", Content = "Cancel" };
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Preparation, GDS request and local adoption are separate steps. " +
                        "The old certificate is retained. Cancel cannot undo a GDS request already submitted.",
                        TextWrapping = TextWrapping.Wrap },
                    m_status, m_consent,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                        Children = { m_prepare, m_request, m_adopt, cancel } }
                }
            };
            m_prepare.Click += async (_, _) => await RunAsync(PrepareAsync).ConfigureAwait(true);
            m_request.Click += async (_, _) => await RunAsync(RequestAsync).ConfigureAwait(true);
            m_adopt.Click += async (_, _) => await RunAsync(AdoptAsync).ConfigureAwait(true);
            m_consent.IsCheckedChanged += (_, _) => Refresh();
            cancel.Click += (_, _) => Close();
            Refresh();
        }

        public async Task<CertificateIdentityReference?> PromptAsync(Window owner, CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => Dispatcher.UIThread.Post(Close));
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ShowDialog(owner).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                return m_adopted;
            }
            finally
            {
                await DisposeAsync().ConfigureAwait(true);
            }
        }

        public ValueTask DisposeAsync()
        {
            return new(m_disposal ??= DisposeCoreAsync());
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (!m_work.IsCompleted || m_disposal is not null)
            {
                return;
            }
            m_work = PresentAsync(action);
            await m_work.ConfigureAwait(true);
        }

        private async Task PresentAsync(Func<Task> action)
        {
            m_busy = true;
            Refresh();
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                m_status.Text = "Enrollment canceled or expired. No alternate identity was selected.";
                m_review = null;
                m_result = null;
            }
            catch (Exception error) when (error is ServiceResultException or InvalidOperationException or
                UnauthorizedAccessException or ArgumentException or NotSupportedException or TimeoutException or
                System.IO.IOException or System.Security.Cryptography.CryptographicException)
            {
                m_status.Text = error is ServiceResultException service
                    ? $"Enrollment failed: {service.StatusCode}."
                    : $"Enrollment failed ({error.GetType().Name}). " +
                        "Check the configured provider and certificate policy.";
                m_review = null;
                m_result = null;
            }
            finally
            {
                m_busy = false;
                m_consent.IsChecked = false;
                Refresh();
            }
        }

        private async Task PrepareAsync()
        {
            m_review = null;
            m_result = null;
            m_consent.IsChecked = false;
            m_review = await m_enrollment.PrepareAsync(m_reference, m_policy, m_lifetime.Token).ConfigureAwait(true);
            m_status.Text = $"Prepared for {m_review.Subject} using {m_review.SourceId}; " +
                $"expires {m_review.ExpiresAt:O}.\nGDS: {m_review.GdsEndpoint}\n" +
                $"Application: {m_review.ApplicationUri} ({m_review.ApplicationId}); " +
                $"group: {m_review.CertificateGroupId}; type: {m_review.CertificateTypeId}.\n" +
                "Confirm before sending the CSR; GDS policy may assign the application to this certificate group.";
        }

        private async Task RequestAsync()
        {
            IdentityEnrollmentReview review = m_review ??
                throw new InvalidOperationException("Prepare the signing request first.");
            bool consent = m_consent.IsChecked == true;
            m_review = null;
            m_consent.IsChecked = false;
            if (!consent)
            {
                throw new InvalidOperationException("Confirm the request before sending.");
            }
            var progress = new Progress<string>(message =>
            {
                if (m_busy && m_disposal is null)
                {
                    m_status.Text = message;
                }
            });
            m_result = await m_enrollment.RequestAsync(review, progress, m_lifetime.Token).ConfigureAwait(true);
            m_status.Text = $"Issued certificate: {m_result.Subject}\nThumbprint: {m_result.Thumbprint}\n" +
                $"Valid until: {m_result.NotAfter:O}. Confirm before adding it to the configured store.";
        }

        private async Task AdoptAsync()
        {
            IdentityEnrollmentResult result = m_result ??
                throw new InvalidOperationException("Request a certificate first.");
            bool consent = m_consent.IsChecked == true;
            m_result = null;
            m_consent.IsChecked = false;
            if (!consent)
            {
                throw new InvalidOperationException("Confirm the certificate addition.");
            }
            m_adopted = await m_enrollment.AdoptAsync(result, m_lifetime.Token).ConfigureAwait(true);
            Close();
        }

        private void Refresh()
        {
            m_prepare.IsEnabled = !m_busy && m_disposal is null;
            m_request.IsEnabled = !m_busy && m_review is not null && m_consent.IsChecked == true;
            m_adopt.IsEnabled = !m_busy && m_result is not null && m_consent.IsChecked == true;
            m_consent.IsEnabled = !m_busy;
        }

        private async Task DisposeCoreAsync()
        {
            await m_lifetime.CancelAsync().ConfigureAwait(true);
            try
            {
                await m_work.ConfigureAwait(true);
            }
            finally
            {
                await m_enrollment.DisposeAsync().ConfigureAwait(true);
                m_lifetime.Dispose();
            }
        }

        private readonly IIdentityEnrollment m_enrollment;
        private readonly CertificateIdentityReference m_reference;
        private readonly UserTokenPolicy m_policy;
        private readonly TextBlock m_status;
        private readonly Button m_prepare;
        private readonly Button m_request;
        private readonly Button m_adopt;
        private readonly CheckBox m_consent;
        private readonly CancellationTokenSource m_lifetime = new();
        private Task m_work = Task.CompletedTask;
        private Task? m_disposal;
        private IdentityEnrollmentReview? m_review;
        private IdentityEnrollmentResult? m_result;
        private CertificateIdentityReference? m_adopted;
        private bool m_busy;
    }
}
