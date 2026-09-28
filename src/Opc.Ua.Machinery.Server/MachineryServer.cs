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
using Opc.Ua.Di;
using Opc.Ua.ECM;
using Opc.Ua.IA;
using Opc.Ua.IRDI;
using Opc.Ua.ISA95.JobControl.V2;
using Opc.Ua.Machinery.Energy;
using Opc.Ua.Machinery.Jobs;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.Machinery.Result;
using Opc.Ua.PADIM;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Reusable server-side helpers for the OPC 40001 Machinery specification
    /// series: loads the type systems of the selected parts into a node
    /// manager's predefined-node collection, in dependency order.
    /// </summary>
    /// <remarks>
    /// Instantiate Machinery-typed objects with the generated
    /// <c>ISystemContext.CreateInstanceOf&lt;Type&gt;</c> factories (for
    /// example <c>CreateInstanceOfMonitoringType</c>) so instances carry the
    /// full companion-type structure rather than only a type-definition
    /// reference — or, better, drive the fluent
    /// <see cref="IMachineryBuildContext"/> surface, which does that and wires
    /// the behaviour as well.
    /// </remarks>
    public static class MachineryServer
    {
        /// <summary>
        /// Loads the type systems of the selected OPC 40001 parts into
        /// <paramref name="nodes"/>, together with every model they depend on.
        /// Call this while building the predefined-node collection of a node
        /// manager.
        /// </summary>
        /// <param name="nodes">The collection to add to.</param>
        /// <param name="context">The system context used for initialisation.</param>
        /// <param name="parts">The parts to load.</param>
        /// <returns>The number of nodes the Machinery models added.</returns>
        /// <remarks>
        /// <para>
        /// The OPC UA Device Integration base model is loaded first whenever a
        /// part needs it, so a caller that has already loaded DI itself must
        /// not call <c>AddOpcUaDi</c> again — the generated loaders are
        /// idempotent per collection, but a node manager that loads DI twice
        /// ends up with duplicate nodes.
        /// </para>
        /// <para>
        /// <see cref="MachineryParts.Result"/> alone needs no Device
        /// Integration: a pure result server can load it without the machine
        /// model. <see cref="MachineryParts.Jobs"/> alone needs only the
        /// ISA-95 Job Control V2 model.
        /// </para>
        /// </remarks>
        public static int AddMachineryTypeSystem(
            this NodeStateCollection nodes,
            ISystemContext context,
            MachineryParts parts = MachineryParts.All)
        {
            if (nodes is null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            MachineryPartsValidation.Validate(parts);

            if (RequiresDeviceIntegration(parts))
            {
                nodes.AddOpcUaDi(context);
            }

            int before = nodes.Count;
            if (parts.HasFlag(MachineryParts.BuildingBlocks))
            {
                // OPC 40001-1 types MonitoringType/Status/Stacklight with IA's
                // BasicStacklightType - the single edge that makes IA a
                // dependency of the machine model.
                nodes.AddOpcUaIA(context);
                nodes.AddOpcUaMachinery(context);
            }
            if (parts.HasFlag(MachineryParts.ProcessValues))
            {
                nodes.AddOpcUaIRDI(context);
                nodes.AddOpcUaPADIM(context);
                nodes.AddOpcUaMachineryProcessValues(context);
            }
            if (parts.HasFlag(MachineryParts.Jobs))
            {
                OpcUaISA95JobControlV2Extensions.AddOpcUaISA95JobControlV2(nodes, context);
                nodes.AddOpcUaMachineryJobs(context);
            }
            if (parts.HasFlag(MachineryParts.Energy))
            {
                nodes.AddOpcUaIA(context);
                nodes.AddOpcUaECM(context);
                nodes.AddOpcUaMachineryEnergy(context);
            }
            if (parts.HasFlag(MachineryParts.Result))
            {
                nodes.AddOpcUaMachineryResult(context);
            }
            return nodes.Count - before;
        }

        /// <summary>
        /// Loads the OPC 40001-101 Result Transfer type system alone. The part
        /// needs UA core only, so a server that publishes nothing but results
        /// can use this without carrying the machine model.
        /// </summary>
        /// <param name="nodes">The collection to add to.</param>
        /// <param name="context">The system context used for initialisation.</param>
        /// <returns>The number of nodes added.</returns>
        public static int AddMachineryResultTypeSystem(
            this NodeStateCollection nodes,
            ISystemContext context)
        {
            return nodes.AddMachineryTypeSystem(context, MachineryParts.Result);
        }

        /// <summary>
        /// Returns the model namespace URIs the selected parts contribute, in
        /// dependency order and without duplicates. The OPC UA Device
        /// Integration namespace is not included: the node manager base class
        /// always registers it.
        /// </summary>
        /// <param name="parts">The parts to enumerate.</param>
        public static ArrayOf<string> GetNamespaceUris(MachineryParts parts)
        {
            MachineryPartsValidation.Validate(parts);
            var namespaceUris = new List<string>();
            if (parts.HasFlag(MachineryParts.BuildingBlocks))
            {
                Add(namespaceUris, Opc.Ua.IA.Namespaces.IA);
                Add(namespaceUris, Opc.Ua.Machinery.Namespaces.Machinery);
            }
            if (parts.HasFlag(MachineryParts.ProcessValues))
            {
                Add(namespaceUris, Opc.Ua.IRDI.Namespaces.IRDI);
                Add(namespaceUris, Opc.Ua.PADIM.Namespaces.PADIM);
                Add(
                    namespaceUris,
                    Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues);
            }
            if (parts.HasFlag(MachineryParts.Jobs))
            {
                Add(namespaceUris, Opc.Ua.ISA95.JobControl.V2.Namespaces.ISA95JobControlV2);
                Add(namespaceUris, Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs);
            }
            if (parts.HasFlag(MachineryParts.Energy))
            {
                Add(namespaceUris, Opc.Ua.IA.Namespaces.IA);
                Add(namespaceUris, Opc.Ua.ECM.Namespaces.ECM);
                Add(namespaceUris, Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy);
            }
            if (parts.HasFlag(MachineryParts.Result))
            {
                Add(namespaceUris, Opc.Ua.Machinery.Result.Namespaces.MachineryResult);
            }
            return namespaceUris.ToArrayOf();
        }

        /// <summary>
        /// Returns <see langword="true"/> when the selected parts need the OPC
        /// UA Device Integration base model. Only
        /// <see cref="MachineryParts.Jobs"/> and
        /// <see cref="MachineryParts.Result"/> do not.
        /// </summary>
        /// <param name="parts">The parts to test.</param>
        public static bool RequiresDeviceIntegration(MachineryParts parts)
        {
            return parts.HasFlag(MachineryParts.BuildingBlocks) ||
                parts.HasFlag(MachineryParts.ProcessValues) ||
                parts.HasFlag(MachineryParts.Energy);
        }

        private static void Add(List<string> namespaceUris, string namespaceUri)
        {
            if (!namespaceUris.Contains(namespaceUri))
            {
                namespaceUris.Add(namespaceUri);
            }
        }
    }

    /// <summary>
    /// Rejects a <see cref="MachineryParts"/> selection that is empty, carries
    /// undefined flags, or picks OPC 40001-2 process values or OPC 40001-4
    /// energy without the OPC 40001-1 building blocks they extend.
    /// </summary>
    internal static class MachineryPartsValidation
    {
        public static void Validate(MachineryParts parts)
        {
            if (parts == MachineryParts.None)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "At least one OPC 40001 part must be selected.");
            }
            if ((parts & ~MachineryParts.All) != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "MachineryParts '{0}' contains undefined flags.",
                    parts);
            }
            if (RequiresBuildingBlocks(parts) &&
                !parts.HasFlag(MachineryParts.BuildingBlocks))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "MachineryParts '{0}' requires MachineryParts.BuildingBlocks: " +
                    "OPC 40001-2 and OPC 40001-4 extend the machine model of " +
                    "OPC 40001-1. Only Jobs and Result stand on their own.",
                    parts);
            }
        }

        private static bool RequiresBuildingBlocks(MachineryParts parts)
        {
            return parts.HasFlag(MachineryParts.ProcessValues) ||
                parts.HasFlag(MachineryParts.Energy);
        }
    }
}
