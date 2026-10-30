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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.TestModels.AsyncExecution
{
    /// <summary>
    /// Hosts synthetic Read, Write and delayed Call paths using the normal generated model and fluent callbacks.
    /// </summary>
    [NodeManager(NamespaceUri = "urn:opcfoundation:tests:async-services")]
    public sealed partial class AsyncExecutionNodeManager
    {
        /// <summary>
        /// Gets or sets the scenario used by this independently hosted fixture.
        /// </summary>
        internal AsyncExecutionScenario Scenario { get; set; } = new();

        partial void Configure(IAsyncExecutionNodeManagerBuilder builder)
        {
            builder.Probe.Counter
                .OnRead(_ => new ValueTask<int>(Scenario.Counter))
                .OnWrite((value, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    Scenario.Counter = value;
                    return default;
                });
            builder.Probe.Blob.OnRead(Scenario.ReadBlobAsync);
            builder.Probe.DelayedAdd.OnCall(Scenario.AddAsync);
            builder.Probe.DeclaredAdd.OnCall(Scenario.AddAsync);
            Scenario.DeclaredMethod = builder.Probe.DeclaredAdd.Node;
            if (Scenario.RecoverableHint is { } allowed)
            {
                MethodState method = builder.Probe.DeclaredAdd.Node;
                var property = PropertyState<bool>.With<VariantBuilder>(method, allowed);
                property.BrowseName = new QualifiedName(
                    "SupportsRecoverableExecution", SystemContext.NamespaceUris.GetIndexOrAppend(RseNamespaces.Rse));
                property.DisplayName = new LocalizedText("SupportsRecoverableExecution");
                property.TypeDefinitionId = VariableTypeIds.PropertyType;
                property.DataType = DataTypeIds.Boolean;
                property.ValueRank = ValueRanks.Scalar;
                property.AccessLevel = AccessLevels.CurrentRead;
                property.UserAccessLevel = AccessLevels.CurrentRead;
                property.NodeId = New(SystemContext, property);
                method.AddChild(property);
                AddNode(property);
                Scenario.RecoveryProperty = property;
            }
            builder.DeviceManagement.Node.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = Ua.ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Call)
                }
            ];
            builder.DeviceManagement.Node.AccessRestrictions =
                AccessRestrictionType.SessionRequired |
                AccessRestrictionType.EncryptionRequired |
                AccessRestrictionType.SigningRequired;
            builder.DeviceManagement.CreateSigningRequest.OnCall(
                (group, type, subject, regenerate, nonce, ct) =>
                    Scenario.RequireCertificates().CreateSigningRequestAsync(
                        group, type, subject, regenerate, nonce, ct));
            builder.DeviceManagement.UpdateCertificate.OnCall(
                (group, type, certificate, issuers,
                    keyFormat, key, ct) =>
                    Scenario.RequireCertificates().UpdateCertificateAsync(
                        group, type, certificate, issuers, keyFormat, key, ct));
            builder.DeviceManagement.ApplyChanges.OnCall(ct => Scenario.RequireCertificates().ApplyChangesAsync(ct));
            builder.DeviceManagement.GetCertificates.OnCall(
                (group, ct) => Scenario.RequireCertificates().GetCertificatesAsync(group, ct));
        }
    }

    /// <summary>
    /// Supplies deterministic gates and a visible mutation count without simulating a Service handler.
    /// </summary>
    internal sealed class AsyncExecutionScenario
    {
        /// <summary>
        /// Gets or sets the synthetic process value.
        /// </summary>
        public int Counter
        {
            get => Volatile.Read(ref m_counter);
            set => Volatile.Write(ref m_counter, value);
        }

        /// <summary>
        /// Gets or sets the payload used for actual encoding-boundary checks.
        /// </summary>
        public ByteString Blob { get; set; }

        /// <summary>
        /// Gets how often the ordinary Blob read callback ran.
        /// </summary>
        public int BlobReads => Volatile.Read(ref m_blobReads);

        /// <summary>
        /// Gets or sets the optional current eligibility declaration installed on DeclaredAdd.
        /// </summary>
        public bool? RecoverableHint { get; set; }

        /// <summary>
        /// Gets or sets the installed declaration, allowing deterministic current-policy changes in tests.
        /// </summary>
        public PropertyState<bool>? RecoveryProperty { get; set; }

        /// <summary>
        /// Gets or sets the generated Method whose current permissions and eligibility are exercised.
        /// </summary>
        public MethodState? DeclaredMethod { get; set; }

        /// <summary>
        /// Gets or sets whether the Method waits for Release.
        /// </summary>
        public bool Delay { get; set; }

        /// <summary>
        /// Gets or sets whether the Blob read waits for Release.
        /// </summary>
        public bool DelayBlob { get; set; }

        /// <summary>
        /// Gets or sets whether the Method deliberately ignores cancellation until released.
        /// </summary>
        public bool IgnoreCancellation { get; set; }

        /// <summary>
        /// Gets or sets the real downstream management adapter when this fixture is the gateway.
        /// </summary>
        public AsyncCertificateForwarder? Certificates { get; set; }

        /// <summary>
        /// Returns an explicitly configured downstream adapter, never a simulated certificate result.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public AsyncCertificateForwarder RequireCertificates()
        {
            return Certificates ?? throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Gets the number of actual Method invocations.
        /// </summary>
        public int Invocations => Volatile.Read(ref m_invocations);

        /// <summary>
        /// Gets the signal raised by the actual Method handler.
        /// </summary>
        public Task Entered => m_entered.Task;

        /// <summary>
        /// Releases the actual Method handler without a sleep or a fixed network port.
        /// </summary>
        public void Release()
        {
            m_release.TrySetResult(true);
        }

        /// <summary>
        /// Rearms the gates after the preceding invocation has completed and been acknowledged.
        /// </summary>
        public void ResetGate()
        {
            m_entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            m_release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Returns the actual configured variable value while counting ordinary Service execution.
        /// </summary>
        public async ValueTask<ByteString> ReadBlobAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref m_blobReads);
            if (DelayBlob)
            {
                m_entered.TrySetResult(true);
                await m_release.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            return Blob;
        }

        /// <summary>
        /// Mutates the value once after any requested wait.
        /// </summary>
        public async ValueTask<int> AddAsync(int amount, CancellationToken ct)
        {
            Interlocked.Increment(ref m_invocations);
            m_entered.TrySetResult(true);
            if (Delay)
            {
                await m_release.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : ct).ConfigureAwait(false);
            }
            return Interlocked.Add(ref m_counter, amount);
        }

        private TaskCompletionSource<bool> m_entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<bool> m_release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int m_counter = 7;
        private int m_invocations;
        private int m_blobReads;
    }

    /// <summary>
    /// Creates one generated node manager for a scenario through the existing asynchronous factory seam.
    /// </summary>
    internal sealed class AsyncExecutionScenarioFactory(AsyncExecutionScenario scenario) : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => ["urn:opcfoundation:tests:async-services"];

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server, ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IAsyncNodeManager>(new AsyncExecutionNodeManager(server, configuration)
            {
                Scenario = scenario
            });
        }
    }
}
