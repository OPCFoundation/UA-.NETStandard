/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Reports the OPC 40100-1 conformance units implemented by a configured control runtime.
    /// </summary>
    internal static class MachineVisionConformance
    {
        /// <summary>
        /// Returns the units whose protocol handlers the control runtime binds.
        /// </summary>
        public static ArrayOf<QualifiedName> Control(ISystemContext context, MachineVisionFeatures features)
        {
            ushort ns = (ushort)context.NamespaceUris.GetIndex(MachineVisionModels.ControlNamespaceUri);
            var units = new List<QualifiedName>();
            Add("Basic Vision System");
            Add("Automatic Mode");
            Add("Easy Confirmation");
            Add("Error Events");
            Add("Error Conditions");
            Add("Information Events");
            Add("Measurement ID");
            Add("Part ID");
            Add("Simulation", Has(MachineVisionFeatures.Simulation));
            Add("Safety State Management", Has(MachineVisionFeatures.Safety));
            Add("System State Information", Has(MachineVisionFeatures.Diagnostics));
            Add("Diagnostic Events", Has(MachineVisionFeatures.Diagnostics));
            if (Has(MachineVisionFeatures.Steps))
            {
                foreach (string state in s_stepStates)
                {
                    Add("StepModel " + state);
                }
            }
            if (Has(MachineVisionFeatures.Recipes))
            {
                Add("Basic Recipe Management");
                Add("Recipe ID External");
                Add("Product ID");
                Add("Recipe Versioning");
                Add("Recipe File", Has(MachineVisionFeatures.FileTransfer));
                Add("Recipe Folder", Has(MachineVisionFeatures.Folders));
                Add("Product Folder", Has(MachineVisionFeatures.Folders));
            }
            if (Has(MachineVisionFeatures.Configurations))
            {
                Add("Basic Configuration Management");
                Add("Configuration ID External");
                Add("Configuration Versioning");
                Add("Configuration File", Has(MachineVisionFeatures.FileTransfer));
                Add("Configuration Folder", Has(MachineVisionFeatures.Folders));
            }
            if (Has(MachineVisionFeatures.Results))
            {
                Add("Basic Result Management");
                Add("Event Based Content");
                Add("Method Based Content");
                Add("Result File", Has(MachineVisionFeatures.FileTransfer));
                Add("Result Folder", Has(MachineVisionFeatures.Folders));
            }
            return units.ToArrayOf();

            void Add(string name, bool present = true)
            {
                if (present)
                {
                    units.Add(new QualifiedName(name, ns));
                }
            }

            bool Has(MachineVisionFeatures feature) => (features & feature) != 0;
        }

        private static readonly string[] s_stepStates =
        [
            "Preoperational", "Halted", "Error", "Initialized", "Ready", "SingleExecution", "ContinuousExecution"
        ];
    }
}
