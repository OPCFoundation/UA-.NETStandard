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

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di;

namespace Opc.Ua.AMB.Server.Health
{
    /// <summary>
    /// The health status of a registered asset (OPC 10000-110 §9): its
    /// overall <c>DeviceHealth</c> and the alarms that detail it.
    /// </summary>
    public interface IAssetHealth
    {
        /// <summary>
        /// Gets the current <c>DeviceHealth</c>, or <see langword="null"/>
        /// when the asset was registered without one.
        /// </summary>
        DeviceHealthEnumeration? DeviceHealth { get; }

        /// <summary>
        /// Gets whether <c>DeviceHealth</c> follows the active alarms.
        /// </summary>
        bool DerivesFromAlarms { get; }

        /// <summary>
        /// Gets the health alarms of the asset, in registration order.
        /// </summary>
        ArrayOf<IAssetHealthAlarm> Alarms { get; }

        /// <summary>
        /// Finds a health alarm by the name it was registered with.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="alarm">The alarm, when found.</param>
        /// <returns><see langword="true"/> when the asset has the alarm.</returns>
        bool TryGetAlarm(string name, [NotNullWhen(true)] out IAssetHealthAlarm? alarm);

        /// <summary>
        /// Sets <c>DeviceHealth</c>. Its source timestamp marks when the asset
        /// entered the state (§9.2), so it changes only with the value.
        /// </summary>
        /// <param name="health">The new health.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when the asset has no
        /// <c>DeviceHealth</c> or derives it from its alarms.
        /// </exception>
        ValueTask SetDeviceHealthAsync(DeviceHealthEnumeration health, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A health alarm of an asset (OPC 10000-110 §9.3): an OPC 10000-100
    /// alarm with the asset as its source, an AMB condition class and the
    /// potential root causes of §9.4.
    /// </summary>
    public interface IAssetHealthAlarm
    {
        /// <summary>
        /// Gets the name the alarm was registered with, which is also its
        /// browse name and <c>ConditionName</c>.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the NodeId of the alarm, which is also its <c>ConditionId</c>.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// Gets the alarm type the alarm is an instance of.
        /// </summary>
        AssetHealthAlarmKind Kind { get; }

        /// <summary>
        /// Gets the condition class the alarm reports.
        /// </summary>
        AmbConditionClass ConditionClass { get; }

        /// <summary>
        /// Gets whether the alarm is active.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Gets the current <c>Severity</c> of the alarm.
        /// </summary>
        ushort Severity { get; }

        /// <summary>
        /// Gets the current <c>PotentialRootCauses</c> of the alarm.
        /// </summary>
        ArrayOf<RootCauseDataType> PotentialRootCauses { get; }

        /// <summary>
        /// Activates the alarm, or updates an active one, and reports the
        /// event.
        /// </summary>
        /// <param name="severity">
        /// The severity, within one of the active bands of OPC 10000-110
        /// Table 15 (201 to 1000); see <see cref="AssetFaultSeverities"/>.
        /// </param>
        /// <param name="message">The message of the event.</param>
        /// <param name="potentialRootCauses">
        /// The potential root causes. Leave it out when none has been
        /// identified - the alarm then lists one "unknown" entry - and pass
        /// <see cref="AssetRootCauses.Self"/> when the alarm itself is the
        /// root cause.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// <paramref name="severity"/> is outside 201 to 1000.
        /// </exception>
        ValueTask RaiseAsync(
            ushort severity,
            LocalizedText message,
            ArrayOf<RootCauseDataType> potentialRootCauses = default,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deactivates the alarm and reports the event. The severity drops
        /// into the inactive band of Table 15; the alarm stays retained until
        /// it is acknowledged.
        /// </summary>
        /// <param name="message">The message of the event; a default when empty.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask ClearAsync(LocalizedText message = default, CancellationToken cancellationToken = default);
    }
}
