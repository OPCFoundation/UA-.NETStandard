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
