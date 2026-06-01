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
using System.Globalization;
using System.IO;
using System.Text;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Emits strongly-typed identifier classes for every concrete
    /// <c>FiniteStateMachineType</c> subtype in a model. For each FSM
    /// type the generator emits a nested static class containing
    /// <c>StateIds</c> / <c>StateNumbers</c> / <c>TransitionIds</c> /
    /// <c>TransitionNumbers</c> constants so application code can
    /// reference Part-16 state identifiers symbolically rather than by
    /// magic numeric NodeId.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Example output (excerpt) for <c>InstallationStateMachineType</c>
    /// in the DI model:
    /// </para>
    /// <code language="csharp">
    /// public static partial class InstallationStateMachineIds
    /// {
    ///     public static partial class StateIds
    ///     {
    ///         public const uint Idle      = Objects.InstallationStateMachineType_Idle;
    ///         public const uint Installing = Objects.InstallationStateMachineType_Installing;
    ///         public const uint Error      = Objects.InstallationStateMachineType_Error;
    ///     }
    ///     public static partial class StateNumbers
    ///     {
    ///         public const uint Idle = 1u; public const uint Installing = 2u; public const uint Error = 3u;
    ///     }
    ///     // ... TransitionIds, TransitionNumbers
    /// }
    /// </code>
    /// <para>
    /// The generator runs by default for every model that defines at
    /// least one FSM subtype; consumers can suppress it with
    /// <see cref="GeneratorOptions.OmitStateMachineIds"/>.
    /// </para>
    /// </remarks>
    internal sealed class StateMachineIdsGenerator : IGenerator
    {
        private const string kFiniteStateMachineTypeName = "FiniteStateMachineType";
        private const string kStateType = "StateType";
        private const string kInitialStateType = "InitialStateType";
        private const string kTransitionType = "TransitionType";
        private const string kStateNumberProperty = "StateNumber";
        private const string kTransitionNumberProperty = "TransitionNumber";
        private const string kStandardUaNamespaceUri = "http://opcfoundation.org/UA/";

        private readonly IGeneratorContext m_context;

        public StateMachineIdsGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            List<FsmTypeInfo> machines = CollectFiniteStateMachineTypes();
            if (machines.Count == 0)
            {
                return [];
            }

            string namespacePrefix = m_context.ModelDesign.TargetNamespace.Prefix;
            string fileName = Path.Combine(m_context.OutputFolder,
                CoreUtils.Format("{0}.StateMachineIds.g.cs", namespacePrefix));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            writer.WriteLine(CodeTemplates.CodeHeader);
            writer.WriteLine();
            writer.WriteLine("namespace {0}", namespacePrefix);
            writer.WriteLine("{");

            for (int ii = 0; ii < machines.Count; ii++)
            {
                if (ii > 0)
                {
                    writer.WriteLine();
                }
                WriteMachine(writer, machines[ii]);
            }

            writer.WriteLine("}");

            return [fileName.AsTextFileResource()];
        }

        // ------------------------------------------------------------------
        // discovery
        // ------------------------------------------------------------------

        private List<FsmTypeInfo> CollectFiniteStateMachineTypes()
        {
            var result = new List<FsmTypeInfo>();
            foreach (NodeDesign node in m_context.ModelDesign.GetNodeDesigns())
            {
                if (node is not ObjectTypeDesign objectType ||
                    m_context.ModelDesign.IsExcluded(objectType) ||
                    !IsFiniteStateMachineSubtype(objectType))
                {
                    continue;
                }
                FsmTypeInfo info = BuildMachineInfo(objectType);
                if (info != null)
                {
                    result.Add(info);
                }
            }
            result.Sort(static (a, b) => string.CompareOrdinal(a.TypeName, b.TypeName));
            return result;
        }

        private static bool IsFiniteStateMachineSubtype(ObjectTypeDesign type)
        {
            TypeDesign current = type;
            while (current != null)
            {
                if (string.Equals(current.SymbolicName?.Name, kFiniteStateMachineTypeName,
                        StringComparison.Ordinal) &&
                    string.Equals(current.SymbolicName?.Namespace, kStandardUaNamespaceUri,
                        StringComparison.Ordinal))
                {
                    return true;
                }
                current = current.BaseTypeNode;
            }
            return false;
        }

        private FsmTypeInfo BuildMachineInfo(ObjectTypeDesign objectType)
        {
            // FiniteStateMachineType itself is abstract / has no child
            // state objects — skip it; only concrete subtypes get IDs.
            if (string.Equals(objectType.SymbolicName?.Namespace, kStandardUaNamespaceUri,
                    StringComparison.Ordinal) &&
                string.Equals(objectType.SymbolicName?.Name, kFiniteStateMachineTypeName,
                    StringComparison.Ordinal))
            {
                return null;
            }

            InstanceDesign[] children = objectType.Children?.Items;
            if (children == null || children.Length == 0)
            {
                return null;
            }

            var info = new FsmTypeInfo(objectType.SymbolicName?.Name);

            foreach (InstanceDesign child in children)
            {
                if (child is not ObjectDesign childObject ||
                    childObject.TypeDefinition == null)
                {
                    continue;
                }
                string typeDefName = childObject.TypeDefinition.Name;
                string typeDefNs = childObject.TypeDefinition.Namespace;
                if (!string.Equals(typeDefNs, kStandardUaNamespaceUri, StringComparison.Ordinal))
                {
                    continue;
                }
                if (string.Equals(typeDefName, kStateType, StringComparison.Ordinal) ||
                    string.Equals(typeDefName, kInitialStateType, StringComparison.Ordinal))
                {
                    info.States.Add(BuildStateEntry(objectType.SymbolicName?.Name, childObject,
                        kStateNumberProperty));
                }
                else if (string.Equals(typeDefName, kTransitionType, StringComparison.Ordinal))
                {
                    info.Transitions.Add(BuildStateEntry(objectType.SymbolicName?.Name, childObject,
                        kTransitionNumberProperty));
                }
            }

            if (info.States.Count == 0 && info.Transitions.Count == 0)
            {
                return null;
            }

            info.States.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
            info.Transitions.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
            return info;
        }

        private static FsmEntry BuildStateEntry(
            string parentTypeName, ObjectDesign child, string numberPropertyName)
        {
            string name = child.SymbolicName?.Name ?? string.Empty;
            uint? number = ExtractNumberProperty(child, numberPropertyName);
            string objectsConstantName = string.IsNullOrEmpty(parentTypeName)
                ? name
                : CoreUtils.Format("{0}_{1}", parentTypeName, name);
            return new FsmEntry(name, number, objectsConstantName);
        }

        private static uint? ExtractNumberProperty(
            ObjectDesign parent, string propertyName)
        {
            InstanceDesign[] grandchildren = parent.Children?.Items;
            if (grandchildren == null)
            {
                return null;
            }
            foreach (InstanceDesign grandchild in grandchildren)
            {
                if (grandchild is not PropertyDesign property ||
                    !string.Equals(property.SymbolicName?.Name, propertyName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                System.Xml.XmlElement element = property.DefaultValue;
                if (element != null && !string.IsNullOrWhiteSpace(element.InnerText) &&
                    uint.TryParse(element.InnerText.Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out uint value))
                {
                    return value;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // emission
        // ------------------------------------------------------------------

        private void WriteMachine(TextWriter writer, FsmTypeInfo machine)
        {
            string idsClassName = CoreUtils.Format("{0}Ids", machine.TypeName);
            string namespacePrefix = m_context.ModelDesign.TargetNamespace.Prefix;

            writer.WriteLine("    /// <summary>");
            writer.WriteLine("    /// Strongly-typed state and transition identifiers for the");
            writer.WriteLine("    /// <c>{0}</c> ObjectType.", machine.TypeName);
            writer.WriteLine("    /// </summary>");
            writer.WriteLine("    [global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Opc.Ua.SourceGeneration.Core\", \"\")]");
            writer.WriteLine("    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]");
            writer.WriteLine("    public static partial class {0}", idsClassName);
            writer.WriteLine("    {");
            WriteNested(writer, "StateIds",
                "Numeric NodeId portion of each state in the model's namespace.",
                machine.States, namespacePrefix, asReference: true);
            writer.WriteLine();
            WriteNested(writer, "StateNumbers",
                "OPC UA <c>StateNumber</c> property value per state.",
                machine.States, namespacePrefix, asReference: false);
            writer.WriteLine();
            WriteNested(writer, "TransitionIds",
                "Numeric NodeId portion of each transition in the model's namespace.",
                machine.Transitions, namespacePrefix, asReference: true);
            writer.WriteLine();
            WriteNested(writer, "TransitionNumbers",
                "OPC UA <c>TransitionNumber</c> property value per transition.",
                machine.Transitions, namespacePrefix, asReference: false);
            writer.WriteLine("    }");
        }

        private static void WriteNested(
            TextWriter writer, string className, string summary,
            List<FsmEntry> entries, string namespacePrefix, bool asReference)
        {
            writer.WriteLine("        /// <summary>");
            writer.WriteLine("        /// {0}", summary);
            writer.WriteLine("        /// </summary>");
            writer.WriteLine("        public static partial class {0}", className);
            writer.WriteLine("        {");
            if (entries.Count == 0)
            {
                writer.WriteLine("            // (none defined in the model)");
            }
            else
            {
                foreach (FsmEntry entry in entries)
                {
                    if (asReference)
                    {
                        writer.WriteLine(
                            "            public const uint {0} = global::{1}.Objects.{2};",
                            entry.Name,
                            namespacePrefix,
                            entry.ObjectsConstantName);
                    }
                    else if (entry.Number.HasValue)
                    {
                        writer.WriteLine(
                            "            public const uint {0} = {1}u;",
                            entry.Name, entry.Number.Value);
                    }
                    else
                    {
                        writer.WriteLine(
                            "            // {0}: StateNumber/TransitionNumber not declared in the model.",
                            entry.Name);
                    }
                }
            }
            writer.WriteLine("        }");
        }

        // ------------------------------------------------------------------
        // helper types
        // ------------------------------------------------------------------

        private sealed class FsmTypeInfo
        {
            public FsmTypeInfo(string typeName)
            {
                TypeName = typeName ?? string.Empty;
            }

            public string TypeName { get; }
            public List<FsmEntry> States { get; } = [];
            public List<FsmEntry> Transitions { get; } = [];
        }

        private sealed class FsmEntry
        {
            public FsmEntry(string name, uint? number, string objectsConstantName)
            {
                Name = name ?? string.Empty;
                Number = number;
                ObjectsConstantName = objectsConstantName ?? string.Empty;
            }

            public string Name { get; }
            public uint? Number { get; }
            public string ObjectsConstantName { get; }
        }
    }
}
