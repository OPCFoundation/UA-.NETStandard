/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

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
