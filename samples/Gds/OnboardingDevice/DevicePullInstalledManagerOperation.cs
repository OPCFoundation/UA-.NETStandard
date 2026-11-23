// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Onboarding;
using Opc.Ua.Server;

namespace OnboardingDevice
{
    /// <summary>
    /// Executes the installed DCA's manager request for one captured device deployment.
    /// </summary>
    internal sealed class DevicePullInstalledManagerOperation : IPullDcaInstalledManagerOperation
    {
        /// <summary>
        /// Borrows an already captured deployment operation and the device's pull journal.
        /// </summary>
        public DevicePullInstalledManagerOperation(
            IApplicationConfigurationPushOperations operations,
            DevicePullJournal journal)
        {
            m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
            m_journal = journal ?? throw new ArgumentNullException(nameof(journal));
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<ManagerDescription>> ExecuteAsync(
            PullDcaSigningBinding binding,
            ByteString installedCertificate,
            Func<CancellationToken, ValueTask<ArrayOf<ManagerDescription>>> getManagers,
            CancellationToken cancellationToken = default)
        {
            if (getManagers == null)
            {
                throw new ArgumentNullException(nameof(getManagers));
            }
            binding = m_journal.SnapshotBinding(binding);
            installedCertificate = installedCertificate.IsNull
                ? default
                : ByteString.From(installedCertificate.Span);
            return m_operations.ExecuteAsync(async () =>
            {
                await m_journal.RequireInstalledManagerAsync(binding, installedCertificate, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await getManagers(cancellationToken).ConfigureAwait(false);
            }, cancellationToken);
        }

        /// <summary>The operation bound to the deployment captured before admission.</summary>
        private readonly IApplicationConfigurationPushOperations m_operations;

        /// <summary>The durable journal for that deployment.</summary>
        private readonly DevicePullJournal m_journal;
    }
}
