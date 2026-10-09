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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AMB;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.StateMachines;

namespace AmbSample
{
    /// <summary>
    /// Builds the hydraulic press with the Machinery builders and registers
    /// it as an asset through <see cref="IMachineHandle{TState}.AsNode"/>.
    /// </summary>
    /// <remarks>
    /// While the servicing of the press executes, the press is put into the
    /// Machinery operation mode <c>Maintenance</c>. OPC 10000-110 does not
    /// require that coupling; it shows how an application can tie the two
    /// state machines together.
    /// </remarks>
    internal sealed class PressConfigurator : IMachineryConfigurator
    {
        public PressConfigurator(Plant plant, IAssetManagement assets, ILogger<PressConfigurator> logger)
        {
            m_plant = plant;
            m_assets = assets;
            m_logger = logger;
        }

        /// <inheritdoc/>
        public async ValueTask ConfigureAsync(IMachineryBuildContext context, CancellationToken cancellationToken)
        {
            IMachineHandle<BaseObjectState> press = await context
                .AddMachine(new QualifiedName("HydraulicPress"))
                .WithIdentification(identification =>
                {
                    identification.Manufacturer = new LocalizedText("en", "Acme Presses");
                    identification.Model = new LocalizedText("en", "HydraPress 500");
                    identification.SerialNumber = "HP500-0007";
                    identification.ProductInstanceUri = "urn:acme-presses:hp500:HP500-0007";
                })
                .WithMonitoring(monitoring => monitoring.WithOperationMode(MachineryOperationModeValue.Processing))
                .BuildAsync(cancellationToken)
                .ConfigureAwait(false);
            m_press = press;

            m_plant.Press = await m_assets.RegisterAssetAsync(
                press.AsNode(),
                asset => asset
                    .WithConfigurableAssetId("PRESS-01")
                    .WithMaintenance(
                        "Servicing",
                        AmbConditionClass.Servicing,
                        details =>
                        {
                            details.Description = new LocalizedText("en", "Replace the hydraulic oil and filters.");
                            details.PlannedDate = DateTime.UtcNow.AddMinutes(3);
                            details.EstimatedDowntime = TimeSpan.FromHours(4);
                        },
                        OnServicingChangedAsync)
                    .WithDocumentationLinks(links => links
                        .Add("OperatingManual", "https://acme-presses.example/hp500/manual.pdf")
                        .AllowUserLinks())
                    .LocatedIn(AssetLocationKind.Hierarchical, Plant.Line),
                cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask OnServicingChangedAsync(MaintenanceStateKind state, CancellationToken cancellationToken)
        {
            IMachineryOperationModeController? mode = m_press?.OperationMode;
            if (mode == null)
            {
                return;
            }
            MachineryOperationModeValue target = state == MaintenanceStateKind.Executing
                ? MachineryOperationModeValue.Maintenance
                : MachineryOperationModeValue.Processing;
            if (mode.CurrentMode != target &&
                await mode.SetModeAsync(target, cancellationToken).ConfigureAwait(false))
            {
                m_logger.OperationModeChanged(target);
            }
        }

        private readonly Plant m_plant;
        private readonly IAssetManagement m_assets;
        private readonly ILogger m_logger;
        private IMachineHandle<BaseObjectState>? m_press;
    }

    internal static partial class PressConfiguratorLog
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "The press is in operation mode {Mode} now.")]
        public static partial void OperationModeChanged(this ILogger logger, MachineryOperationModeValue mode);
    }
}
