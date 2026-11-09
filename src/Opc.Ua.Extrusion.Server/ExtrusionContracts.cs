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

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Supplies the required identity of a real extrusion device.
    /// </summary>
    /// <param name="Manufacturer">Manufacturer, also used in the required instance BrowseName.</param>
    /// <param name="SerialNumber">Stable manufacturer-assigned serial number.</param>
    /// <param name="Model">Human-readable model designation.</param>
    /// <param name="ProductInstanceUri">Globally unique, stable identity used by Machinery and AMB.</param>
    /// <param name="ControllerName">The controller designation required by OPC 40084-1.</param>
    public sealed record ExtrusionIdentity(
        string Manufacturer,
        string SerialNumber,
        LocalizedText Model,
        string ProductInstanceUri,
        string ControllerName);

    /// <summary>
    /// Family-specific commands; common OPC 40083 commands use the common runtime.
    /// </summary>
    public enum ExtrusionCommandKind
    {
        /// <summary>
        /// Writes a declared writable extrusion value.
        /// </summary>
        Write,

        /// <summary>
        /// Requests opening a haul-off or corrugator clamp.
        /// </summary>
        OpenClamp,

        /// <summary>
        /// Requests closing a haul-off or corrugator clamp.
        /// </summary>
        CloseClamp,

        /// <summary>
        /// Requests an immediate cutter operation.
        /// </summary>
        ManualCut,

        /// <summary>
        /// Requests a sample after the next regular cut.
        /// </summary>
        SampleCut,

        /// <summary>
        /// Requests resetting the selected cutting product's output.
        /// </summary>
        ResetOutput
    }

    /// <summary>
    /// Carries the actual end-user request through authorization and hardware execution.
    /// </summary>
    /// <param name="Context">The originating UA request, never the service's startup identity.</param>
    /// <param name="Device">The real device receiving the command.</param>
    /// <param name="Target">The method or variable addressed by the caller.</param>
    /// <param name="Kind">The exact extrusion operation.</param>
    /// <param name="Value">The proposed value for a write; null Variant for a method.</param>
    public sealed record ExtrusionCommand(
        ISystemContext Context,
        ExtrusionDeviceState Device,
        BaseInstanceState Target,
        ExtrusionCommandKind Kind,
        Variant Value);

    /// <summary>
    /// Authorizes and performs hardware-specific extrusion actions asynchronously.
    /// </summary>
    /// <remarks>
    /// A forwarding adapter must preserve the originating user's authority. The runtime never
    /// substitutes a service identity, performs a physical action, or advances production itself.
    /// </remarks>
    public interface IExtrusionCommandProvider
    {
        /// <summary>
        /// Authorizes a command for its original caller before hardware is touched.
        /// </summary>
        ValueTask<StatusCode> AuthorizeAsync(ExtrusionCommand command, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes an authorized command and optionally returns already committed state.
        /// </summary>
        ValueTask<ExtrusionCommandResult> ExecuteAsync(
            ExtrusionCommand command,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reports hardware acceptance without inventing successful physical state.
    /// </summary>
    /// <param name="StatusCode">The hardware result, including denied or invalid-state outcomes.</param>
    /// <param name="CommittedSnapshot">An absolute authoritative snapshot, when already available.</param>
    public sealed record ExtrusionCommandResult(StatusCode StatusCode, ExtrusionSnapshot? CommittedSnapshot = null);

    /// <summary>
    /// One absolute value in a committed snapshot.
    /// </summary>
    /// <param name="NodeId">An instance variable owned by this device.</param>
    /// <param name="Value">The value together with its actual quality and source time.</param>
    public readonly record struct ExtrusionValue(NodeId NodeId, DataValue Value);

    /// <summary>
    /// An immutable committed update; counters are absolute, never increments.
    /// </summary>
    /// <param name="Revision">Monotonically increasing provider commit sequence.</param>
    /// <param name="Values">Atomic value updates from that commit.</param>
    public sealed record ExtrusionSnapshot(ulong Revision, ArrayOf<ExtrusionValue> Values);

    /// <summary>
    /// Configures one concrete device before its address space is published.
    /// </summary>
    public sealed class ExtrusionDeviceOptions
    {
        /// <summary>
        /// Creates options with a required stable real-device identity.
        /// </summary>
        public ExtrusionDeviceOptions(ExtrusionIdentity identity)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        }

        /// <summary>
        /// Gets the device identity.
        /// </summary>
        public ExtrusionIdentity Identity { get; }

        /// <summary>
        /// Gets the optional feature and cardinality selections.
        /// </summary>
        public ExtrusionFeatures Features { get; } = new();

        /// <summary>
        /// Gets or sets the line identity, which remains writable through the authorized provider.
        /// </summary>
        public string LineId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the hardware-command provider. Without one, family commands and writes are denied.
        /// </summary>
        public IExtrusionCommandProvider? Commands { get; set; }

        /// <summary>
        /// Gets or sets the authoritative job adapter for an extrusion line.
        /// </summary>
        public IExtrusionJobProvider? Jobs { get; set; }

        /// <summary>
        /// Gets the common OPC 40083 provider selections. Implementations remain in the common package.
        /// </summary>
        public ExtrusionCommonProviders Common { get; } = new();

        /// <summary>
        /// Gets or sets the maximum materialized nodes in one device, including optional descendants.
        /// </summary>
        public int MaximumNodes { get; set; } = 20000;

        /// <summary>
        /// Validates the identity before allocating or registering any nodes.
        /// </summary>
        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Identity.Manufacturer) ||
                string.IsNullOrWhiteSpace(Identity.SerialNumber) ||
                string.IsNullOrWhiteSpace(Identity.ControllerName) || Identity.Model.IsNull)
            {
                throw new ArgumentException("Manufacturer, serial number, model and controller name are required.");
            }
            if (!Uri.TryCreate(Identity.ProductInstanceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("The product instance identity must be an absolute URI.");
            }
            if (MaximumNodes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumNodes));
            }
        }
    }
}
