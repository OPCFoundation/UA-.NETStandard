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
using System.Reflection;
using Opc.Ua.Extrusion;
using Opc.Ua.Extrusion.Calender;
using Opc.Ua.Extrusion.Calibrator;
using Opc.Ua.Extrusion.Corrugator;
using Opc.Ua.Extrusion.Cutter;
using Opc.Ua.Extrusion.Die;
using Opc.Ua.Extrusion.Extruder;
using Opc.Ua.Extrusion.Filter;
using Opc.Ua.Extrusion.HaulOff;
using Opc.Ua.Extrusion.Line;
using Opc.Ua.Extrusion.MeltPump;
using Opc.Ua.Extrusion.Pelletizer;
using Opc.Ua.GranulateDrying;

namespace Opc.Ua.PlasticsRubber.Tests
{
    /// <summary>
    /// Explicit compiled registrations for the fourteen models in two producing assemblies.
    /// </summary>
    internal static class CompanionModels
    {
        /// <summary>
        /// Gets the model contracts in dependency order.
        /// </summary>
        internal static ArrayOf<CompanionModel> All { get; } =
        [
            new("Opc.Ua.PlasticsRubber", Namespaces.PlasticsRubber, "PlasticsRubber", 961,
                typeof(MonitoredParameterState),
                (nodes, context) => nodes.AddOpcUaPlasticsRubber(context),
                builder => builder.AddOpcUaPlasticsRubber()),
            new("Opc.Ua.Extrusion", Extrusion.Namespaces.Extrusion, "Extrusion", 1218,
                typeof(ExtrusionDeviceState),
                (nodes, context) => nodes.AddOpcUaExtrusion(context),
                builder => builder.AddOpcUaExtrusion()),
            new("Opc.Ua.Extrusion.Line", Extrusion.Line.Namespaces.ExtrusionLine, "ExtrusionLine", 265,
                typeof(ExtrusionLine_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionLine(context),
                builder => builder.AddOpcUaExtrusionLine()),
            new("Opc.Ua.Extrusion.Extruder", Extrusion.Extruder.Namespaces.ExtrusionExtruder, "ExtrusionExtruder", 567,
                typeof(Extruder_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionExtruder(context),
                builder => builder.AddOpcUaExtrusionExtruder()),
            new("Opc.Ua.Extrusion.HaulOff", Extrusion.HaulOff.Namespaces.ExtrusionHaulOff, "ExtrusionHaulOff", 73,
                typeof(HaulOff_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionHaulOff(context),
                null),
            new("Opc.Ua.Extrusion.MeltPump", Extrusion.MeltPump.Namespaces.ExtrusionMeltPump, "ExtrusionMeltPump", 140,
                typeof(MeltPump_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionMeltPump(context),
                null),
            new("Opc.Ua.Extrusion.Filter", Extrusion.Filter.Namespaces.ExtrusionFilter, "ExtrusionFilter", 326,
                typeof(Filter_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionFilter(context),
                builder => builder.AddOpcUaExtrusionFilter()),
            new("Opc.Ua.Extrusion.Die", Extrusion.Die.Namespaces.ExtrusionDie, "ExtrusionDie", 33,
                typeof(Die_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionDie(context),
                null),
            new("Opc.Ua.Extrusion.Pelletizer", Extrusion.Pelletizer.Namespaces.ExtrusionPelletizer,
                "ExtrusionPelletizer", 156,
                typeof(Pelletizer_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionPelletizer(context),
                null),
            new("Opc.Ua.Extrusion.Cutter", Extrusion.Cutter.Namespaces.ExtrusionCutter, "ExtrusionCutter", 116,
                typeof(Cutter_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionCutter(context),
                builder => builder.AddOpcUaExtrusionCutter()),
            new("Opc.Ua.Extrusion.Calibrator", Extrusion.Calibrator.Namespaces.ExtrusionCalibrator,
                "ExtrusionCalibrator", 1331,
                typeof(Calibrator_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionCalibrator(context),
                null),
            new("Opc.Ua.Extrusion.Corrugator", Extrusion.Corrugator.Namespaces.ExtrusionCorrugator,
                "ExtrusionCorrugator", 11,
                typeof(Corrugator_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionCorrugator(context),
                null),
            new("Opc.Ua.Extrusion.Calender", Extrusion.Calender.Namespaces.ExtrusionCalender, "ExtrusionCalender", 210,
                typeof(Calender_InterfaceState),
                (nodes, context) => nodes.AddOpcUaExtrusionCalender(context),
                null),
            new("Opc.Ua.GranulateDrying", GranulateDrying.Namespaces.GranulateDrying, "GranulateDrying", 4304,
                typeof(GranulateDryingDeviceState),
                (nodes, context) => nodes.AddOpcUaGranulateDrying(context),
                builder => builder.AddOpcUaGranulateDrying())
        ];

        /// <summary>
        /// Creates a context with shifted namespace indexes and generated registrations.
        /// </summary>
        internal static ServiceMessageContext CreateMessageContext()
        {
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend("urn:companion-model-tests:application");
            context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            context.NamespaceUris.GetIndexOrAppend(IA.Namespaces.IA);
            context.NamespaceUris.GetIndexOrAppend(Machinery.Namespaces.Machinery);
            IEncodeableFactoryBuilder builder = context.Factory.Builder;
            foreach (CompanionModel model in All)
            {
                context.NamespaceUris.GetIndexOrAppend(model.Uri);
                model.Register?.Invoke(builder);
            }
            builder.Commit();
            return context;
        }

        /// <summary>
        /// Creates an address-space context sharing the generated encodeable registry.
        /// </summary>
        internal static SystemContext CreateSystemContext()
        {
            ServiceMessageContext messageContext = CreateMessageContext();
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
        }
    }

    /// <summary>
    /// Binds an expected model identity to directly compiled loader and registration calls.
    /// </summary>
    internal sealed class CompanionModel
    {
        /// <summary>
        /// Initializes one model contract without reflective activation.
        /// </summary>
        internal CompanionModel(
            string clrNamespace,
            string uri,
            string name,
            int nodeCount,
            Type emittedType,
            Action<NodeStateCollection, ISystemContext> load,
            Action<IEncodeableFactoryBuilder>? register)
        {
            ClrNamespace = clrNamespace;
            Uri = uri;
            Name = name;
            NodeCount = nodeCount;
            Assembly = emittedType.Assembly;
            Load = load;
            Register = register;
        }

        /// <summary>
        /// Gets the generated CLR namespace, independently of its producing assembly.
        /// </summary>
        internal string ClrNamespace { get; }

        /// <summary>
        /// Gets the wire model URI.
        /// </summary>
        internal string Uri { get; }

        /// <summary>
        /// Gets the pinned generator model name.
        /// </summary>
        internal string Name { get; }

        /// <summary>
        /// Gets the number of published nodes.
        /// </summary>
        internal int NodeCount { get; }

        /// <summary>
        /// Gets the sole producing assembly.
        /// </summary>
        internal Assembly Assembly { get; }

        /// <summary>
        /// Gets the compiled address-space loader.
        /// </summary>
        internal Action<NodeStateCollection, ISystemContext> Load { get; }

        /// <summary>
        /// Gets the compiled data-type factory registration.
        /// </summary>
        internal Action<IEncodeableFactoryBuilder>? Register { get; }
    }
}
