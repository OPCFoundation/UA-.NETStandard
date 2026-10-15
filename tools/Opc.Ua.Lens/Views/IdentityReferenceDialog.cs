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
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Opc.Ua;
using Opc.Ua.Identity;
using UaLens.Connection;

namespace UaLens.Views
{
    /// <summary>
    /// Selects named, configured identity capabilities and checks the actual private
    /// key/algorithm or authority metadata before returning a reconnectable owner.
    /// There are no fields for raw tokens, private keys, module paths or PINs.
    /// </summary>
    internal sealed class IdentityReferenceDialog : Window, IAsyncDisposable
    {
        public IdentityReferenceDialog(
            EndpointDescription endpoint,
            UserTokenPolicy policy,
            SubscriptionEngineKind engine,
            ConnectionIdentityConfiguration identities,
            ApplicationConfiguration application,
            ConnectionProfile? restored = null,
            Func<ConnectionProfile, CancellationToken, ValueTask<IClientIdentityProvider>>? resolver = null)
        {
            m_endpoint = endpoint;
            m_policy = policy;
            m_engine = engine;
            m_resolver = resolver ?? ((profile, _) => ValueTask.FromResult(identities.Resolve(profile)));
            m_application = application;
            m_restored = restored;
            Title = policy.TokenType == UserTokenType.Certificate
                ? "User certificate identity"
                : "Configured token identity";
            Width = 650;
            Height = 530;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var content = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
            content.Children.Add(new TextBlock
            {
                Text = $"{policy.TokenType} · {policy.PolicyId}\n{endpoint.EndpointUrl}",
                TextWrapping = TextWrapping.Wrap
            });
            m_source = new ComboBox { Name = "IdentitySourceBox", HorizontalAlignment = HorizontalAlignment.Stretch };
            m_password = new ComboBox { Name = "PasswordSourceBox", HorizontalAlignment = HorizontalAlignment.Stretch };
            m_thumbprint = new TextBox { Name = "CertificateThumbprintBox", MaxLength = 128 };
            m_subject = new TextBox { Name = "CertificateSubjectBox", MaxLength = 512 };
            m_status = new TextBlock { Name = "IdentityStatus", TextWrapping = TextWrapping.Wrap };
            m_use = new Button { Name = "UseIdentityButton", Content = "Check policy & use", IsDefault = true };
            m_setup = new Button { Name = "ConfigureIdentityButton", Content = "Configure selected identity..." };
            var cancel = new Button { Name = "CancelButton", Content = "Cancel", IsCancel = true };
            if (policy.TokenType == UserTokenType.Certificate)
            {
                var sources = new List<ConfiguredCertificateSource>();
                foreach (ConfiguredCertificateSource source in identities.CertificateSources)
                {
                    if (source.Purpose == CryptoPurpose.UserIdentityKey)
                    {
                        sources.Add(source);
                    }
                }
                m_source.ItemsSource = sources;
                m_source.SelectionChanged += (_, _) => UpdatePasswordSources();
                m_source.SelectedIndex = sources.Count > 0 ? 0 : -1;
                AddField(content, "Configured user certificate provider / store", m_source);
                AddField(content, "Configured password / PIN source", m_password);
                AddField(content, "Certificate thumbprint (hex), or use subject below", m_thumbprint);
                AddField(content, "Certificate subject (for example CN=Operator)", m_subject);
                content.Children.Add(new TextBlock
                {
                    Text = "Purpose: UserIdentityKey. This is not UaLens's application/secure-channel certificate. " +
                        "Device/module provisioning and PIN/secret configuration are external. " +
                        "Access and key policy are checked now.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12
                });
                if (restored?.CertificateIdentity is { } reference)
                {
                    foreach (ConfiguredCertificateSource source in sources)
                    {
                        if (string.Equals(source.Id, reference.SourceId, StringComparison.Ordinal))
                        {
                            m_source.SelectedItem = source;
                            foreach (ConfiguredCertificatePasswordSource password in source.PasswordSources)
                            {
                                if (string.Equals(password.Id, reference.PasswordSourceId, StringComparison.Ordinal))
                                {
                                    m_password.SelectedItem = password;
                                }
                            }
                            break;
                        }
                    }
                    m_thumbprint.Text = reference.Thumbprint;
                    m_subject.Text = reference.SubjectName;
                }
                m_use.IsEnabled = sources.Count > 0;
                m_status.Text = sources.Count == 0
                    ? "No user-certificate provider/store is configured. " +
                        "Register an existing store/provider and password/PIN source."
                    : "The private key must be present, currently valid, accessible " +
                        "and compatible with this token policy.";
            }
            else
            {
                m_source.ItemsSource = identities.AccessTokenSources.ToArray();
                m_source.SelectedIndex = identities.AccessTokenSources.Count > 0 ? 0 : -1;
                AddField(content, "Configured authority / broker adapter", m_source);
                content.Children.Add(new TextBlock
                {
                    Text = "Only the configured JWT profile is supported. " +
                        "The adapter acquires and refreshes credentials. Authority registration, consent, " +
                        "device flow and firewall access are external setup, not installed here.",
                    TextWrapping = TextWrapping.Wrap
                });
                if (restored?.IssuedIdentity is { } reference)
                {
                    foreach (ConfiguredAccessTokenSource source in identities.AccessTokenSources)
                    {
                        if (string.Equals(source.Id, reference.ProviderId, StringComparison.Ordinal))
                        {
                            m_source.SelectedItem = source;
                            break;
                        }
                    }
                }
                m_use.IsEnabled = identities.AccessTokenSources.Count > 0;
                m_status.Text = identities.AccessTokenSources.Count == 0
                    ? "No access-token provider is configured. " +
                        "Register an IAccessTokenProvider for the server's authority."
                    : "Authority/profile metadata is checked before any token is requested. " +
                        "Tokens are never saved in a workspace.";
            }
            content.Children.Add(m_status);
            content.Children.Add(m_setup);
            content.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { m_use, cancel }
            });
            Content = new ScrollViewer { Content = content };
            m_use.Click += async (_, _) =>
            {
                if (!m_validation.IsCompleted)
                {
                    return;
                }
                m_validation = ValidateSelectionAsync();
                await m_validation.ConfigureAwait(true);
            };
            m_setup.Click += async (_, _) =>
            {
                if (!m_validation.IsCompleted)
                {
                    return;
                }
                m_validation = ConfigureIdentityAsync();
                await m_validation.ConfigureAwait(true);
            };
            m_source.SelectionChanged += (_, _) => RefreshSetup();
            RefreshSetup();
            cancel.Click += (_, _) => Close(false);
        }

        public async Task<ConnectionSelection?> PromptAsync(Window owner, CancellationToken ct)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            m_lifetimeToken = lifetime.Token;
            RefreshSetup();
            CancellationTokenRegistration registration = ct.Register(() =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsVisible)
                    {
                        Close(false);
                    }
                }));
            await using (registration.ConfigureAwait(true))
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    bool accepted = await ShowDialog<bool>(owner).ConfigureAwait(true);
                    ct.ThrowIfCancellationRequested();
                    if (!accepted)
                    {
                        return null;
                    }
                    ConnectionSelection? result = m_selection;
                    m_selection = null;
                    return result;
                }
                finally
                {
                    await lifetime.CancelAsync().ConfigureAwait(true);
                    try
                    {
                        await m_validation.ConfigureAwait(true);
                    }
                    finally
                    {
                        m_lifetimeToken = null;
                        await DisposeAsync().ConfigureAwait(true);
                    }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (m_selection is not null)
            {
                await m_selection.DisposeAsync().ConfigureAwait(true);
                m_selection = null;
            }
        }

        private async Task ValidateSelectionAsync()
        {
            if (m_lifetimeToken is not { } token)
            {
                return;
            }
            m_use.IsEnabled = false;
            try
            {
                CertificateIdentityReference? certificate = null;
                IssuedIdentityReference? issued = null;
                if (m_policy.TokenType == UserTokenType.Certificate)
                {
                    if (m_source.SelectedItem is not ConfiguredCertificateSource source ||
                        m_password.SelectedItem is not ConfiguredCertificatePasswordSource password)
                    {
                        throw new InvalidOperationException("Select a configured certificate and password/PIN source.");
                    }
                    certificate = new CertificateIdentityReference
                    {
                        SourceId = source.Id,
                        PasswordSourceId = password.Id,
                        Thumbprint = EmptyToNull(m_thumbprint.Text),
                        SubjectName = EmptyToNull(m_subject.Text)
                    };
                }
                else
                {
                    if (m_source.SelectedItem is not ConfiguredAccessTokenSource source)
                    {
                        throw new InvalidOperationException("Select a configured authority/broker.");
                    }
                    issued = new IssuedIdentityReference { ProviderId = source.Id, AuthorityUri = source.AuthorityUri };
                }
                var profile = ConnectionProfile.Create(
                    m_endpoint, m_policy, m_engine, certificateIdentity: certificate, issuedIdentity: issued);
                if (m_restored is not null &&
                    (m_restored.CertificateIdentity != certificate || m_restored.IssuedIdentity != issued))
                {
                    throw new InvalidOperationException(
                        "The saved reference must be reacquired exactly. " +
                        "Use a new connection to select another identity.");
                }
                IClientIdentityProvider provider = await m_resolver(profile, token).ConfigureAwait(true);
                var pinned = new ProfileIdentityProvider(profile, provider);
                m_status.Text = "Checking configured provider and exact policy… Cancel remains available.";
                await pinned.SelectUserTokenPolicyAsync(new IdentitySelectionContext(
                    m_endpoint,
                    m_endpoint.UserIdentityTokens,
                    m_application.CreateMessageContext(),
                    m_application.SecurityConfiguration.SupportedSecurityPolicies),
                    token).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                m_selection = new ConnectionSelection(m_endpoint, profile, provider);
                if (IsVisible)
                {
                    Close(true);
                }
            }
            catch (OperationCanceledException)
            {
                m_status.Text = "Identity acquisition canceled. No alternate identity was selected.";
            }
            catch (UnauthorizedAccessException)
            {
                m_status.Text = "The configured identity source denied access. Check its permissions externally.";
            }
            catch (Exception error) when (
                error is ServiceResultException or ArgumentException or InvalidOperationException)
            {
                m_status.Text = error.Message;
            }
            finally
            {
                m_use.IsEnabled = true;
            }
        }

        private void RefreshSetup()
        {
            m_setup.IsEnabled = m_restored is null &&
                m_lifetimeToken is not null &&
                (m_source.SelectedItem is ConfiguredCertificateSource { CreateEnrollment: not null } ||
                    m_source.SelectedItem is ConfiguredAccessTokenSource { Interaction: not null });
            m_setup.Content = m_policy.TokenType == UserTokenType.Certificate
                ? "Enroll / renew configured user certificate..."
                : "Authorize configured token provider...";
        }

        private async Task ConfigureIdentityAsync()
        {
            if (m_lifetimeToken is not { } token || m_restored is not null)
            {
                return;
            }
            m_setup.IsEnabled = false;
            m_use.IsEnabled = false;
            m_source.IsEnabled = false;
            m_password.IsEnabled = false;
            m_thumbprint.IsEnabled = false;
            m_subject.IsEnabled = false;
            bool reportProgress = true;
            try
            {
                if (m_source.SelectedItem is ConfiguredAccessTokenSource source)
                {
                    var progress = new Progress<IdentityInteractionStage>(stage =>
                    {
                        if (reportProgress && !token.IsCancellationRequested && IsVisible)
                        {
                            m_status.Text = stage switch
                            {
                                IdentityInteractionStage.ContactingAuthority => "Contacting the configured authority.",
                                IdentityInteractionStage.WaitingForUser =>
                                    "Complete the configured provider's user interaction.",
                                _ => "Completing configured provider authorization."
                            };
                        }
                    });
                    await IdentityTokenInteraction.AuthorizeAsync(source, m_policy, progress, token)
                        .ConfigureAwait(true);
                    m_status.Text = "Provider authorization completed. " +
                        "Check the policy before connecting with fresh credentials.";
                }
                else if (m_source.SelectedItem is
                    ConfiguredCertificateSource { CreateEnrollment: { } create } certificates &&
                    m_password.SelectedItem is ConfiguredCertificatePasswordSource password)
                {
                    var reference = new CertificateIdentityReference
                    {
                        SourceId = certificates.Id,
                        PasswordSourceId = password.Id,
                        Thumbprint = EmptyToNull(m_thumbprint.Text),
                        SubjectName = EmptyToNull(m_subject.Text)
                    };
                    UserTokenPolicy policy = CoreUtils.Clone(m_policy)!;
                    policy.SecurityPolicyUri ??= m_endpoint.SecurityPolicyUri;
                    var dialog = new IdentityEnrollmentDialog(create(), reference, policy);
                    await using (dialog.ConfigureAwait(true))
                    {
                        CertificateIdentityReference? updated = await dialog.PromptAsync(this, token)
                            .ConfigureAwait(true);
                        token.ThrowIfCancellationRequested();
                        if (updated is not null)
                        {
                            if (updated.SourceId != reference.SourceId ||
                                updated.PasswordSourceId != reference.PasswordSourceId)
                            {
                                throw new InvalidOperationException(
                                    "Enrollment attempted to change the configured source.");
                            }
                            m_thumbprint.Text = updated.Thumbprint;
                            m_subject.Text = updated.SubjectName;
                            m_status.Text = "Certificate added to its configured source. " +
                                "Check the token policy before use.";
                        }
                    }
                }
                else
                {
                    m_status.Text = "This provider needs external setup; " +
                        "no enrollment or interaction adapter is configured.";
                }
            }
            catch (OperationCanceledException)
            {
                m_status.Text = "Identity setup canceled. No alternate identity was selected.";
            }
            catch (Exception error) when (error is ServiceResultException or InvalidOperationException or
                UnauthorizedAccessException or ArgumentException or NotSupportedException or System.IO.IOException)
            {
                m_status.Text = $"Identity setup failed ({error.GetType().Name}). Check the configured provider.";
            }
            finally
            {
                reportProgress = false;
                m_source.IsEnabled = true;
                m_password.IsEnabled = true;
                m_thumbprint.IsEnabled = true;
                m_subject.IsEnabled = true;
                m_use.IsEnabled = m_source.SelectedItem is not null;
                RefreshSetup();
            }
        }

        private void UpdatePasswordSources()
        {
            if (m_source.SelectedItem is ConfiguredCertificateSource source)
            {
                m_password.ItemsSource = source.PasswordSources.ToArray();
                m_password.SelectedIndex = source.PasswordSources.Count > 0 ? 0 : -1;
            }
        }

        private static string? EmptyToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void AddField(StackPanel panel, string label, Control control)
        {
            panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
            panel.Children.Add(control);
        }

        private readonly EndpointDescription m_endpoint;
        private readonly UserTokenPolicy m_policy;
        private readonly SubscriptionEngineKind m_engine;
        private readonly Func<ConnectionProfile, CancellationToken, ValueTask<IClientIdentityProvider>> m_resolver;
        private readonly ApplicationConfiguration m_application;
        private readonly ConnectionProfile? m_restored;
        private readonly ComboBox m_source;
        private readonly ComboBox m_password;
        private readonly TextBox m_thumbprint;
        private readonly TextBox m_subject;
        private readonly TextBlock m_status;
        private readonly Button m_use;
        private readonly Button m_setup;
        private CancellationToken? m_lifetimeToken;
        private ConnectionSelection? m_selection;
        private Task m_validation = Task.CompletedTask;
    }
}
