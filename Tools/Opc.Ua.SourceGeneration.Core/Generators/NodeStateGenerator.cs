/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Schema.Model;
using Opc.Ua.Types;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates data types and node state classes.
    /// </summary>
    internal sealed class NodeStateGenerator : IGenerator
    {
        /// <summary>
        /// Create node state generator
        /// </summary>
        public NodeStateGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_messageContext = new ServiceMessageContext(context.Telemetry);
            m_systemContext = new SystemContext(context.Telemetry)
            {
                NamespaceUris = context.ModelDesign.NamespaceUris
            };
            m_logger = context.Telemetry.CreateLogger<NodeStateGenerator>();
            CollectNodesToGenerate();
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            m_initializers.Clear();
            if (m_instances.Count + m_nodes.Count == 0)
            {
                return [];
            }
            List<Resource> resources =
            [
                EmitNodeStateClasses(),
                EmitExtensions()
            ];
            Resource initializers = EmbedInitializers();
            if (initializers != null)
            {
                resources.Add(initializers);
            }
            return resources;
        }

        /// <summary>
        /// Create the nodestates classes
        /// </summary>
        private TextFileResource EmitNodeStateClasses()
        {
            string nsPrefix = m_context.ModelDesign.TargetNamespace.Prefix;
            string fileName = Path.Combine(m_context.OutputFolder, CoreUtils.Format(
                "{0}.NodeStates.g.cs",
                nsPrefix));
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, NodeStateTemplates.File);
            template.AddReplacement(Tokens.NamespacePrefix, nsPrefix);
            template.AddReplacement(
                Tokens.Namespace,
                nsPrefix.Replace(".", string.Empty, StringComparison.Ordinal));
            template.AddReplacement(
                Tokens.NamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    m_context.ModelDesign.TargetNamespace.Value));

            // TODO: Switch to "instance" list and get "type" from entry.IsInstanceOfType
            template.AddReplacement(
                Tokens.ListOfTypes,
                m_nodes.Values,
                LoadTemplate_ListOfNodeStateClasses,
                WriteTemplate_ListOfNodeStateClasses);
            template.AddReplacement(
                Tokens.ListOfTypeActivators,
                NodeStateTemplates.ActivatorClass,
                m_nodes.Values,
                LoadTemplate_ListOfNodeStateActivators,
                WriteTemplate_ListOfNodeStateActivators);

            template.Render();
            return fileName.AsTextFileResource();
        }

        /// <summary>
        /// Create extensions
        /// </summary>
        private TextFileResource EmitExtensions()
        {
            string nsPrefix = m_context.ModelDesign.TargetNamespace.Prefix;
            string fileName = Path.Combine(m_context.OutputFolder, CoreUtils.Format(
                "{0}.NodeStates.ex.g.cs",
                nsPrefix));
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(
                templateWriter,
                NodeStateTemplates.Extensions_File);

            template.AddReplacement(Tokens.NamespacePrefix, nsPrefix);
            template.AddReplacement(
                Tokens.Namespace,
                nsPrefix.Replace(".", string.Empty, StringComparison.Ordinal));
            template.AddReplacement(
                Tokens.NamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    m_context.ModelDesign.TargetNamespace.Value));

            template.AddReplacement(
                Tokens.ListOfNodeStateInitializers,
                m_nodes.Values,
                LoadTemplate_ListOfNodeStateInitializers,
                WriteTemplate_ListOfNodeStateInitializers);
            template.AddReplacement(
                Tokens.ListOfNodeStateTypeFactories,
                m_nodes.Values,
                LoadTemplate_ListOfNodeStateFactories,
                WriteTemplate_ListOfNodeStateFactories);

            template.AddReplacement(
                Tokens.ListOfNodeStateInstanceFactories,
                m_instances.Values,
                LoadTemplate_ListOfNodeStateFactories,
                WriteTemplate_ListOfNodeStateFactories);

            // TODO: Adopt to "instance" list and get "type" from entry.IsInstanceOfType
            template.AddReplacement(
                Tokens.ListOfActivatorRegistrations,
                NodeStateTemplates.ActivatorRegistration,
                m_nodes.Values,
                LoadTemplate_ListOfNodeStateActivators,
                WriteTemplate_ListOfNodeStateActivators);

            template.Render();
            return fileName.AsTextFileResource();
        }

        private TemplateString LoadTemplate_ListOfNodeStateActivators(ILoadContext context)
        {
            if (context.Target is not NodeToGenerate node)
            {
                return null;
            }
            if (ExcludeNodeStateClassGeneration(node))
            {
                return null;
            }
            switch (node.Design)
            {
                case ObjectTypeDesign:
                case VariableTypeDesign:
                case MethodDesign method
                    when method.HasArguments && method.IsMethodTypeDesign():
                    return context.TemplateString;
                default:
                    return null;
            }
        }

        private bool WriteTemplate_ListOfNodeStateActivators(IWriteContext context)
        {
            if (context.Target is not NodeToGenerate node)
            {
                return false;
            }
            string nodeClass = node.Design.GetNodeClassAsString();
            context.Template.AddReplacement(Tokens.NodeClass, nodeClass);
            context.Template.AddReplacement(Tokens.BrowseName, node.Design.SymbolicName.Name);
            context.Template.AddReplacement(Tokens.SymbolicId, node.Design.SymbolicId.Name);
            if (node.Design is TypeDesign type)
            {
                context.Template.AddReplacement(
                    Tokens.StateClassName,
                    CoreUtils.Format("{0}State", type.ClassName));
            }
            else if (node.Design is MethodDesign method)
            {
                context.Template.AddReplacement(
                    Tokens.StateClassName,
                    method.GetNodeStateClassName(
                        m_context.ModelDesign.TargetNamespace.Value, []));
            }
            return context.Template.Render();
        }

        private TemplateString LoadTemplate_ListOfNodeStateClasses(ILoadContext context)
        {
            if (context.Target is not NodeToGenerate node)
            {
                return null;
            }

            if (ExcludeNodeStateClassGeneration(node))
            {
                return null;
            }

            return node.Design switch
            {
                ObjectTypeDesign
                    => NodeStateTemplates.ObjectType_Class,
                VariableTypeDesign
                    => NodeStateTemplates.VariableType_Class,
                MethodDesign method when method.HasArguments
                    => NodeStateTemplates.MethodType_Class,
                _ => null
            };
        }

        private bool WriteTemplate_ListOfNodeStateClasses(IWriteContext context)
        {
            if (context.Target is not NodeToGenerate node)
            {
                return false;
            }

            NodeDesign root = node.Design;
            context.Template.AddReplacement(Tokens.NodeClass, root.GetNodeClassAsString());
            context.Template.AddReplacement(Tokens.TypeName, root.SymbolicName.Name);
            context.Template.AddReplacement(Tokens.SymbolicId, root.SymbolicId.Name);
            context.Template.AddReplacement(Tokens.BrowseName, root.SymbolicName.Name);
            context.Template.AddReplacement(
                Tokens.Description,
                root.Description != null ? root.Description.Value : string.Empty);
            context.Template.AddReplacement(
                Tokens.NamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    root.SymbolicName.Namespace));
            context.Template.AddReplacement(
                Tokens.NamespacePrefix,
                m_context.ModelDesign.Namespaces.GetNamespacePrefix(
                    root.SymbolicId.Namespace));
            context.Template.AddReplacement(
                Tokens.XmlNamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantForXmlNamespace(
                    root.SymbolicId.Namespace));
            context.Template.AddReplacement(
                Tokens.BrowseNameNamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    root.SymbolicName.Namespace));

            switch (root)
            {
                case MethodDesign method:
                    AddNodeStateClassMethodTypeReplacements(context, method);
                    break;
                case ObjectTypeDesign objectType:
                    AddNodeStateClassObjectTypeReplacements(context, objectType);
                    break;
                case VariableTypeDesign variableType:
                    AddNodeStateClassVariableTypeReplacements(context, variableType);
                    break;
                default:
                    return false;
            }

            context.Template.AddReplacement(
                Tokens.InitializeOptionalChildren,
                NodeStateTemplates.InitializeOptionalChild,
                node.Children.Values,
                LoadTemplate_InitializeOptionalChildren,
                WriteTemplate_InitializeOptionalChildren);
            context.Template.AddReplacement(
                Tokens.ListOfNonMandatoryChildren,
                node.Children.Values,
                LoadTemplate_ListOfNonMandatoryChildren,
                WriteTemplate_ListOfNonMandatoryChildren);
            context.Template.AddReplacement(
                Tokens.ListOfFields,
                node.Children.Values,
                LoadTemplate_ListOfFields);
            context.Template.AddReplacement(
                Tokens.ListOfProperties,
                node.Children.Values,
                LoadTemplate_ListOfProperties,
                WriteTemplate_ListOfProperties);
            context.Template.AddReplacement(
                Tokens.ListOfChildOperations,
                NodeStateTemplates.ChildOperations,
                [node],
                LoadTemplate_FindChildMethods,
                WriteTemplate_FindChildMethods);

            context.Template.AddReplacement(
                Tokens.ListOfChildCopies,
                NodeStateTemplates.CloneChild,
                node.Children.Values,
                WriteTemplate_ListOfChildren);
            context.Template.AddReplacement(
                Tokens.ListOfChildHashes,
                NodeStateTemplates.HashChild,
                node.Children.Values,
                WriteTemplate_ListOfChildren);
            context.Template.AddReplacement(
                Tokens.ListOfEqualityComparers,
                NodeStateTemplates.CompareChild,
                node.Children.Values,
                WriteTemplate_ListOfChildren);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_ListOfNonMandatoryChildren(ILoadContext context)
        {
            if (context.Target is not NodeToGenerate node ||
                node.Design is not InstanceDesign instance)
            {
                return null;
            }

            if (node.IsNotExplicitlyDefined)
            {
                return null;
            }

            if (instance.IsOverriddenWithSameClass(
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces))
            {
                return null;
            }

            switch (instance.ModellingRule)
            {
                case ModellingRule.Optional:
                    return NodeStateTemplates.OptionalMethod;
                case ModellingRule.OptionalPlaceholder:
                case ModellingRule.MandatoryPlaceholder:
                    return NodeStateTemplates.PlaceHolderMethod;
                case ModellingRule.ExposesItsArray:
                case ModellingRule.CardinalityRestriction:
                case ModellingRule.MandatoryShared:
                    // TODO?
                    break;
            }

            return null;
        }

        private bool WriteTemplate_ListOfNonMandatoryChildren(IWriteContext context)
        {
            if (context.Target is not NodeToGenerate node ||
                node.Design is not InstanceDesign instance)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.AccessorSymbol, "public new");
            if (!instance.IsOverridden())
            {
                if (!s_builtInMethodNames.Contains(instance.SymbolicName.Name))
                {
                    context.Template.AddReplacement(Tokens.AccessorSymbol, "public");
                }
            }
            else
            {
                instance = instance.GetMergedInstance();
            }

            context.Template.AddReplacement(Tokens.Description,
                instance.Description != null ? instance.Description.Value : string.Empty);
            context.Template.AddReplacement(Tokens.ClassName, instance.GetNodeStateClassName(
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces));
            context.Template.AddReplacement(Tokens.ChildName, instance.SymbolicName.Name);
            context.Template.AddReplacement(Tokens.SymbolicId, instance.SymbolicId.Name);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_TypedVariableType(ILoadContext context)
        {
            if (context.Target is not VariableTypeDesign variableType)
            {
                return null;
            }

            if (variableType.DataTypeNode.IsTemplateParameterRequired(variableType.ValueRank))
            {
                return null;
            }

            return context.TemplateString;
        }

        private bool WriteTemplate_TypedVariableType(IWriteContext context)
        {
            if (context.Target is not VariableTypeDesign type)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.NodeClass, type.GetNodeClassAsString());
            context.Template.AddReplacement(Tokens.ClassName, type.ClassName);
            context.Template.AddReplacement(Tokens.TypeName, type.SymbolicName.Name);
            context.Template.AddReplacement(Tokens.BrowseName, type.SymbolicName.Name);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_VariableTypeValue(ILoadContext context)
        {
            if (context.Target is not VariableTypeDesign variableType)
            {
                return null;
            }

            Dictionary<string, Parameter> fields = [];
            CollectMatchingFields(variableType, fields);

            if (fields.Count == 0)
            {
                return null;
            }

            return context.TemplateString;
        }

        private bool WriteTemplate_VariableTypeValue(IWriteContext context)
        {
            if (context.Target is not VariableTypeDesign type)
            {
                return false;
            }

            Dictionary<string, Parameter> fields = [];
            CollectMatchingFields(type, fields);

            if (fields.Count == 0)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.ClassName, type.ClassName);
            context.Template.AddReplacement(Tokens.DataType, type.DataTypeNode.GetDotNetTypeName(
                ValueRank.Scalar,
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces,
                nullable: NullableAnnotation.NonNullable));

            context.Template.AddReplacement(
                Tokens.ListOfChildInitializers,
                fields,
                LoadTemplate_VariableTypeValueInitializers);

            context.Template.AddReplacement(
                Tokens.ListOfUpdateChildrenChangeMasks,
                fields,
                LoadTemplate_VariableTypeValueChangeMasks);

            context.Template.AddReplacement(
                Tokens.ListOfChildMethods,
                NodeStateTemplates.VariableType_ValueMethods,
                fields,
                WriteTemplate_VariableTypeValueField);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_VariableTypeValueInitializers(ILoadContext context)
        {
            if (context.Target is not KeyValuePair<string, Parameter> field ||
                field.Value == null)
            {
                return null;
            }

            string name = field.Key;
            string path = field.Key;

            context.Out.WriteLine("instance = m_variable.{0};", path);
            context.Out.WriteLine("if (instance != null)");
            context.Out.WriteLine("{");
            context.Out.WriteLine("    instance.OnReadValue = OnRead_{0};", name);
            context.Out.WriteLine("    instance.OnWriteValue = OnWrite_{0};", name);
            context.Out.WriteLine("    updateList.Add(instance);");
            context.Out.WriteLine("}");

            return null;
        }

        private TemplateString LoadTemplate_VariableTypeValueChangeMasks(ILoadContext context)
        {
            if (context.Target is not KeyValuePair<string, Parameter> field ||
                field.Value == null)
            {
                return null;
            }

            var dataType = field.Value.Parent as DataTypeDesign;
            string path = field.Key;

            if (dataType.IsDotNetEqualityComparable(field.Value.ValueRank))
            {
                context.Out.WriteLine("if (m_value.{0} != newValue.{0})", path);
            }
            else
            {
                context.Out.WriteLine(
                    "if (!global::Opc.Ua.CoreUtils.IsEqual(m_value.{0}, newValue.{0}))",
                    path);
            }
            context.Out.WriteLine("{");
            context.Out.WriteLine(
                "    UpdateChildVariableStatus(m_variable.{0}, ref statusCode, ref timestamp);",
                path);
            context.Out.WriteLine("}");

            return null;
        }

        private bool WriteTemplate_VariableTypeValueField(IWriteContext context)
        {
            if (context.Target is not KeyValuePair<string, Parameter> field ||
                field.Value == null)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.ChildName, field.Key);
            context.Template.AddReplacement(Tokens.ChildPath, field.Key);
            context.Template.AddReplacement(Tokens.ChildDataType,
                field.Value.DataTypeNode.GetDotNetTypeName(
                    field.Value.ValueRank,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces,
                    nullable: NullableAnnotation.NonNullable));

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_ListOfFields(ILoadContext context)
        {
            if (context.Target is not NodeToGenerate node ||
                node.Design is not InstanceDesign instance)
            {
                return null;
            }

            if (node.IsNotExplicitlyDefined || instance.IsOverridden())
            {
                return null;
            }

            if (instance.ModellingRule
                is ModellingRule.ExposesItsArray
                or ModellingRule.MandatoryPlaceholder
                or ModellingRule.OptionalPlaceholder)
            {
                return null;
            }

            if (instance.ModellingRule == ModellingRule.None)
            {
                return null;
            }

            if (instance is MethodDesign method &&
                method.ModellingRule != ModellingRule.Mandatory &&
                method.ModellingRule != ModellingRule.Optional)
            {
                return null;
            }

            if (IsBuiltInProperty(node))
            {
                return null;
            }

            context.Out.WriteLine(
                "private {0}? {1};",
                instance.GetNodeStateClassName(
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces),
                instance.GetChildFieldName());

            return null;
        }

        private TemplateString LoadTemplate_ListOfInputArguments(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }

            string fieldName = field.GetChildFieldName()[2..];
            string typeName = field.DataTypeNode.GetMethodArgumentTypeAsCode(
                field.ValueRank,
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces,
                false);

            switch (field.DataTypeNode.BasicDataType)
            {
                case BasicDataType.UserDefined:
                    context.Out.WriteLine(
                        "_inputArguments[{2}].TryGetStructure(out {1} {0});",
                        fieldName,
                        typeName,
                        context.Index);
                    break;
                case BasicDataType.BaseDataType when field.ValueRank == ValueRank.Scalar:
                    context.Out.WriteLine(
                        "{1} {0} = _inputArguments[{2}];",
                        fieldName,
                        typeName,
                        context.Index);
                    break;
                default:
                    context.Out.WriteLine(
                        "_inputArguments[{2}].TryGet(out {1} {0});",
                        fieldName,
                        typeName,
                        context.Index);
                    break;
            }
            return null;
        }

        private TemplateString LoadTemplate_ListOfOutputDeclarations(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }

            string fieldName = field.GetChildFieldName()[2..];
            string typeName = field.DataTypeNode.GetMethodArgumentTypeAsCode(
                field.ValueRank,
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces,
                field.IsOptional);

            switch (field.DataTypeNode.BasicDataType)
            {
                case BasicDataType.UserDefined:
                    context.Out.WriteLine(
                        "_outputArguments[{2}].TryGetStructure(out {1} {0});",
                        fieldName,
                        typeName,
                        context.Index);
                    break;
                case BasicDataType.BaseDataType when field.ValueRank == ValueRank.Scalar:
                    context.Out.WriteLine(
                        "{1} {0} = _outputArguments[{2}];",
                        fieldName,
                        typeName,
                        context.Index);
                    break;
                default:
                    context.Out.WriteLine(
                        "_outputArguments[{2}].TryGet(out {1} {0});",
                        fieldName,
                        typeName,
                        context.Index);
                    break;
            }
            return null;
        }

        private TemplateString LoadTemplate_ListOfOutputArguments(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }

            string fieldName = field.GetChildFieldName()[2..];
            switch (field.DataTypeNode.BasicDataType)
            {
                case BasicDataType.UserDefined:
                    context.Out.WriteLine(
                        "_outputArguments[{1}] = global::Opc.Ua.Variant.FromStructure({0});",
                        fieldName,
                        context.Index);
                    break;
                case BasicDataType.BaseDataType:
                    context.Out.WriteLine(
                        "_outputArguments[{1}] = {0};",
                        fieldName,
                        context.Index);
                    break;
                default:
                    context.Out.WriteLine(
                        "_outputArguments[{1}] = global::Opc.Ua.Variant.From({0});",
                        fieldName,
                        context.Index);
                    break;
            }
            return null;
        }

        private TemplateString LoadTemplate_OnCallDeclaration(ILoadContext context)
        {
            if (context.Target is not MethodDesign method)
            {
                return null;
            }

            context.Out.WriteLine("global::Opc.Ua.ISystemContext _context,");
            context.Out.WriteLine("global::Opc.Ua.MethodState _method,");
            context.Out.Write("global::Opc.Ua.NodeId _objectId");

            if (method.InputArguments != null)
            {
                for (int ii = 0; ii < method.InputArguments.Length; ii++)
                {
                    Parameter argument = method.InputArguments[ii];

                    context.Out.WriteLine(",");
                    context.Out.Write("{1} {0}", argument.GetChildFieldName()[2..],
                        argument.DataTypeNode.GetMethodArgumentTypeAsCode(
                            argument.ValueRank,
                            m_context.ModelDesign.TargetNamespace.Value,
                            m_context.ModelDesign.Namespaces,
                            argument.IsOptional));
                }
            }

            if (method.OutputArguments != null)
            {
                for (int ii = 0; ii < method.OutputArguments.Length; ii++)
                {
                    Parameter argument = method.OutputArguments[ii];

                    context.Out.WriteLine(",");
                    context.Out.Write("ref {1} {0}", argument.GetChildFieldName()[2..],
                        argument.DataTypeNode.GetMethodArgumentTypeAsCode(
                            argument.ValueRank,
                            m_context.ModelDesign.TargetNamespace.Value,
                            m_context.ModelDesign.Namespaces,
                            argument.IsOptional));
                }
            }

            context.Out.WriteLine(");");

            return null;
        }

        private TemplateString LoadTemplate_OnCallAsyncDeclaration(ILoadContext context)
        {
            if (context.Target is not MethodDesign method)
            {
                return null;
            }

            context.Out.WriteLine("global::Opc.Ua.ISystemContext _context,");
            context.Out.WriteLine("global::Opc.Ua.MethodState _method,");
            context.Out.Write("global::Opc.Ua.NodeId _objectId");

            if (method.InputArguments != null)
            {
                for (int ii = 0; ii < method.InputArguments.Length; ii++)
                {
                    Parameter argument = method.InputArguments[ii];

                    context.Out.WriteLine(",");
                    context.Out.Write("{1} {0}", argument.GetChildFieldName()[2..],
                        argument.DataTypeNode.GetMethodArgumentTypeAsCode(
                            argument.ValueRank,
                            m_context.ModelDesign.TargetNamespace.Value,
                            m_context.ModelDesign.Namespaces,
                            argument.IsOptional));
                }
            }

            context.Out.WriteLine(",");
            context.Out.Write(
                "global::System.Threading.CancellationToken cancellationToken");
            context.Out.WriteLine(");");

            return null;
        }

        private TemplateString LoadTemplate_ListOfOutputArgumentsFromResult(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }

            string fieldName = field.GetChildFieldName()[2..].ToUpperCamelCase();
            switch (field.DataTypeNode.BasicDataType)
            {
                case BasicDataType.UserDefined:
                    context.Out.WriteLine(
                        "_outputArguments[{1}] = global::Opc.Ua.Variant.FromStructure(_result.{0});",
                        fieldName,
                        context.Index);
                    break;
                case BasicDataType.BaseDataType when field.ValueRank == ValueRank.Scalar:
                    context.Out.WriteLine(
                        "_outputArguments[{1}] = _result.{0};",
                        fieldName,
                        context.Index);
                    break;
                default:
                    context.Out.WriteLine(
                        "_outputArguments[{1}] = global::Opc.Ua.Variant.From(_result.{0});",
                        fieldName,
                        context.Index);
                    break;
            }
            return null;
        }

        private TemplateString LoadTemplate_ListOfResultProperties(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }

            string fieldName = field.GetChildFieldName()[2..].ToUpperCamelCase();
            context.Out.WriteLine(
               "public {1} {0} {{ get; set; }}",
               fieldName,
               field.DataTypeNode.GetMethodArgumentTypeAsCode(
                   field.ValueRank,
                   m_context.ModelDesign.TargetNamespace.Value,
                   m_context.ModelDesign.Namespaces,
                   field.IsOptional));

            return null;
        }

        private TemplateString LoadTemplate_OnCallImplementation(ILoadContext context)
        {
            if (context.Target is not MethodDesign method)
            {
                return null;
            }

            context.Out.WriteLine("_result = OnCall(");
            context.Out.WriteLine("    _context,");
            context.Out.WriteLine("    this,");
            context.Out.Write("    _objectId");

            if (method.InputArguments != null)
            {
                for (int ii = 0; ii < method.InputArguments.Length; ii++)
                {
                    context.Out.WriteLine(",");
                    context.Out.Write("    {0}", method.InputArguments[ii].GetChildFieldName()[2..]);
                }
            }

            if (method.OutputArguments != null)
            {
                for (int ii = 0; ii < method.OutputArguments.Length; ii++)
                {
                    context.Out.WriteLine(",");
                    context.Out.Write("    ref {0}", method.OutputArguments[ii].GetChildFieldName()[2..]);
                }
            }

            context.Out.WriteLine(");");

            return null;
        }

        private TemplateString LoadTemplate_OnCallAsyncImplementation(ILoadContext context)
        {
            if (context.Target is not MethodDesign method)
            {
                return null;
            }

            context.Out.WriteLine("_result = await OnCallAsync(");
            context.Out.WriteLine("    _context,");
            context.Out.WriteLine("    this,");
            context.Out.Write("    _objectId");

            if (method.InputArguments != null)
            {
                for (int ii = 0; ii < method.InputArguments.Length; ii++)
                {
                    context.Out.WriteLine(",");
                    context.Out.Write("    {0}", method.InputArguments[ii].GetChildFieldName()[2..]);
                }
            }

            context.Out.WriteLine(",");
            context.Out.WriteLine("    cancellationToken).ConfigureAwait(false);");
            return null;
        }

        private TemplateString LoadTemplate_InitializeOptionalChildren(ILoadContext context)
        {
            if (context.Target is not NodeToGenerate node ||
                node.Design is not InstanceDesign instance)
            {
                return null;
            }

            if (node.IsNotExplicitlyDefined)
            {
                return null;
            }

            if (instance.ModellingRule != ModellingRule.Optional)
            {
                return null;
            }

            if (IsBuiltInProperty(node))
            {
                return null;
            }

            return context.TemplateString;
        }

        private bool WriteTemplate_InitializeOptionalChildren(IWriteContext context)
        {
            if (context.Target is not NodeToGenerate node ||
                node.Design is not InstanceDesign instance)
            {
                return context.Template.Render();
            }

            context.Template.AddReplacement(Tokens.ChildName, instance.SymbolicName.Name);
            context.Template.AddReplacement(Tokens.SymbolicId, instance.SymbolicId.Name);
            context.Template.AddReplacement(Tokens.FieldName, instance.GetChildFieldName());
            if (instance.Parent is MethodDesign method)
            {
                context.Template.AddReplacement(Tokens.ClassName, method.GetNodeStateClassName(
                    m_context.ModelDesign.TargetNamespace.Value,
                    []));
            }
            else if (instance.Parent is TypeDesign type)
            {
                context.Template.AddReplacement(
                    Tokens.ClassName,
                    type.ClassName);
            }

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_ListOfProperties(ILoadContext context)
        {
            if (context.Target is not NodeToGenerate node ||
                node.Design is not InstanceDesign instance)
            {
                return null;
            }

            if (node.IsNotExplicitlyDefined)
            {
                return null;
            }

            if (instance.ModellingRule
                is ModellingRule.ExposesItsArray
                or ModellingRule.MandatoryPlaceholder
                or ModellingRule.OptionalPlaceholder)
            {
                return null;
            }

            if (instance.ModellingRule == ModellingRule.None)
            {
                return null;
            }

            if (instance is MethodDesign method &&
                method.ModellingRule != ModellingRule.Mandatory &&
                method.ModellingRule != ModellingRule.Optional)
            {
                return null;
            }

            if (instance.IsOverridden())
            {
                if (instance.IsOverriddenWithSameClass(
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces))
                {
                    return null;
                }
                return NodeStateTemplates.PropertyOverride;
            }

            if (IsBuiltInProperty(node))
            {
                return null;
            }

            return NodeStateTemplates.Property;
        }

        private bool WriteTemplate_ListOfProperties(IWriteContext context)
        {
            if (context.Target is not NodeToGenerate node ||
                node.Design is not InstanceDesign instance)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.AccessorSymbol, "public new");
            if (!instance.IsOverridden())
            {
                if (!s_builtInPropertyNames.Contains(instance.SymbolicName.Name) ||
                    (instance is VariableDesign && instance.SymbolicName.Name == "Value"))
                {
                    context.Template.AddReplacement(Tokens.AccessorSymbol, "public");
                }
            }
            else
            {
                instance = instance.GetMergedInstance();
            }

            context.Template.AddReplacement(Tokens.Description,
                instance.Description != null ? instance.Description.Value : string.Empty);
            context.Template.AddReplacement(Tokens.ClassName, instance.GetNodeStateClassName(
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces));
            context.Template.AddReplacement(Tokens.ChildName, instance.SymbolicName.Name);
            context.Template.AddReplacement(Tokens.FieldName, instance.GetChildFieldName());

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_FindChildMethods(ILoadContext context)
        {
            if (context.Target is not NodeToGenerate node)
            {
                return null;
            }

            int count = 0;
            foreach (NodeToGenerate child in node.Children.Values)
            {
                if (child.Design is not InstanceDesign instance)
                {
                    continue;
                }

                if (child.IsNotExplicitlyDefined)
                {
                    continue;
                }

                if (instance.ModellingRule is
                    ModellingRule.ExposesItsArray or
                    ModellingRule.MandatoryPlaceholder or
                    ModellingRule.OptionalPlaceholder)
                {
                    continue;
                }

                if (instance.ModellingRule is
                    ModellingRule.None or
                    ModellingRule.OptionalPlaceholder or
                    ModellingRule.MandatoryPlaceholder)
                {
                    continue;
                }

                if (instance.IsOverriddenWithSameClass(
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces))
                {
                    continue;
                }

                count++;
            }

            if (count == 0)
            {
                return null;
            }

            return context.TemplateString;
        }

        private bool WriteTemplate_FindChildMethods(IWriteContext context)
        {
            if (context.Target is not NodeToGenerate node ||
                node.Design is not TypeDesign type)
            {
                return false;
            }

            List<InstanceDesign> childrenWithProperties = [];
            foreach (NodeToGenerate child in node.Children.Values)
            {
                if (child.Design is not InstanceDesign instance)
                {
                    continue;
                }
                if (child.IsNotExplicitlyDefined)
                {
                    continue;
                }
                if (instance.ModellingRule is
                    not ModellingRule.Mandatory and
                    not ModellingRule.Optional)
                {
                    continue;
                }
                if (instance.IsOverriddenWithSameClass(
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces))
                {
                    continue;
                }
                childrenWithProperties.Add(instance);
            }

            context.Template.AddReplacement(
                Tokens.ListOfFindChildCase,
                NodeStateTemplates.FindChildCase,
                childrenWithProperties,
                WriteTemplate_ListOfChildren);

            context.Template.AddReplacement(
                Tokens.ListOfCreateOrReplaceChild,
                NodeStateTemplates.CreateOrReplaceChild,
                childrenWithProperties,
                WriteTemplate_ListOfChildren);

            List<InstanceDesign> additionalChildren = [];
            foreach (NodeToGenerate child in node.Children.Values)
            {
                if (child.Design is not InstanceDesign instance)
                {
                    continue;
                }
                if (child.IsNotExplicitlyDefined)
                {
                    continue;
                }
                if (instance.ModellingRule is
                    not ModellingRule.Mandatory and
                    not ModellingRule.Optional)
                {
                    continue;
                }
                if (instance.IsOverridden())
                {
                    continue;
                }
                additionalChildren.Add(instance);
            }

            context.Template.AddReplacement(
                Tokens.ListOfFindChildren,
                NodeStateTemplates.FindChildren,
                additionalChildren,
                WriteTemplate_ListOfChildren);

            context.Template.AddReplacement(
                Tokens.ListOfRemoveChild,
                NodeStateTemplates.RemoveChild,
                additionalChildren,
                WriteTemplate_ListOfChildren);

            return context.Template.Render();
        }

        private bool WriteTemplate_ListOfChildren(IWriteContext context)
        {
            if (context.Target is not InstanceDesign instance)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.AccessorSymbol, "public new");
            if (!instance.IsOverridden())
            {
                context.Template.AddReplacement(Tokens.AccessorSymbol, "public");
            }

            context.Template.AddReplacement(Tokens.ClassName, instance.GetNodeStateClassName(
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces));
            context.Template.AddReplacement(Tokens.SymbolicId, instance.SymbolicId.Name);
            context.Template.AddReplacement(Tokens.ChildName, instance.SymbolicName.Name);
            context.Template.AddReplacement(Tokens.FieldName, instance.GetChildFieldName());
            context.Template.AddReplacement(Tokens.NodeClass, instance.GetNodeClassAsString());
            context.Template.AddReplacement(Tokens.BrowseName, instance.SymbolicName.Name);
            context.Template.AddReplacement(
                Tokens.BrowseNameNamespacePrefix,
                m_context.ModelDesign.Namespaces.GetNamespacePrefix(
                    instance.SymbolicName.Namespace));
            context.Template.AddReplacement(
                Tokens.BrowseNameNamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    instance.SymbolicName.Namespace));

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_ListOfNodeStateInitializers(ILoadContext context)
        {
            if (context.Target is not NodeToGenerate node)
            {
                return null;
            }
            if (node.Design.IsMethodTypeDesign() ||
                node.InstanceOf != null ||
                !IsInAddressSpace(node) ||
                node.Design.Purpose == DataTypePurpose.Testing)
            {
                return null;
            }
            if (node.Parent != null)
            {
                // No registering of children
                return null;
            }
            return NodeStateTemplates.Add;
        }

        private bool WriteTemplate_ListOfNodeStateInitializers(IWriteContext context)
        {
            if (context.Target is not NodeToGenerate node)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.SymbolicId, node.Design.SymbolicId.Name);
            context.Template.AddReplacement(Tokens.SymbolicName, node.Design.SymbolicName.Name);
            return context.Template.Render();
        }

        private TemplateString LoadTemplate_ListOfNodeStateFactories(ILoadContext context)
        {
            if (context.Target is not NodeToGenerate node)
            {
                return null;
            }

            if (context.Token == Tokens.ListOfNodeStateInstanceFactories)
            {
                if (node.Parent != null)
                {
                    return null;
                }
                return node.Design switch
                {
                    ObjectDesign => NodeStateTemplates.Create_InstanceOfObjectType,
                    VariableDesign => NodeStateTemplates.Create_InstanceOfVariableType,
                    MethodDesign => NodeStateTemplates.Create_InstanceOfMethodType,
                    _ => null
                };
            }

            if (node.Parent != null)
            {
                if (node.Design is not InstanceDesign instance)
                {
                    return null;
                }

                if (instance.ModellingRule is
                    not ModellingRule.MandatoryPlaceholder and
                    not ModellingRule.OptionalPlaceholder)
                {
                    return node.Design switch
                    {
                        ObjectDesign => NodeStateTemplates.Create_ChildObject,
                        VariableDesign => NodeStateTemplates.Create_ChildVariable,
                        MethodDesign => NodeStateTemplates.Create_ChildMethod,
                        _ => null
                    };
                }
                return node.Design switch
                {
                    ObjectDesign => NodeStateTemplates.Create_ChildObject_Placeholder,
                    VariableDesign => NodeStateTemplates.Create_ChildVariable_Placeholder,
                    MethodDesign => NodeStateTemplates.Create_ChildMethod_Placeholder,
                    _ => null
                };
            }

            return node.Design switch
            {
                ObjectTypeDesign => NodeStateTemplates.Create_ObjectType,
                VariableTypeDesign => NodeStateTemplates.Create_VariableType,
                ReferenceTypeDesign => NodeStateTemplates.Create_ReferenceType,
                DataTypeDesign => NodeStateTemplates.Create_DataType,
                ObjectDesign => NodeStateTemplates.Create_Object,
                VariableDesign => NodeStateTemplates.Create_Variable,
                ViewDesign => NodeStateTemplates.Create_View,
                _ => null
            };
        }

        private bool WriteTemplate_ListOfNodeStateFactories(IWriteContext context)
        {
            if (context.Target is not NodeToGenerate node)
            {
                return false;
            }

            NodeDesign root = node.Design;

            // Common replacements for all node types
            context.Template.AddReplacement(
                Tokens.SymbolicId,
                root.SymbolicId.Name);
            context.Template.AddReplacement(
                Tokens.SymbolicName,
                root.SymbolicName.Name);
            context.Template.AddReplacement(Tokens.BrowseName, root.SymbolicName.Name);
            context.Template.AddReplacement(Tokens.NumericIdValue, root.FindNumericIdentifier() ?? 0);
            context.Template.AddReplacement(
                Tokens.TypeName,
                node.InstanceOf?.Design.SymbolicName.Name);
            context.Template.AddReplacement(
                Tokens.NodeIdConstant,
                root.GetNodeIdAsCode(m_context.ModelDesign.Namespaces, kNamespaceTableContextVariable));

            // .net efficiently interns constant string, so usage of the BrowseName constants is not needed.
            string symbolicNameSymbol = root.SymbolicName.Name.AsStringLiteral();
            context.Template.AddReplacement(Tokens.BrowseNameSymbol,
                string.IsNullOrEmpty(root.BrowseName) ? symbolicNameSymbol : root.BrowseName.AsStringLiteral());
            context.Template.AddReplacement(Tokens.SymbolicNameSymbol, symbolicNameSymbol);
            context.Template.AddReplacement(Tokens.DisplayName, GetDisplayNameValue(root) ?? symbolicNameSymbol);

            context.Template.AddReplacement(
                Tokens.BrowseNameNamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    root.SymbolicName.Namespace));
            context.Template.AddReplacement(
                Tokens.DescriptionValue,
                GetDescriptionValue(root));
            context.Template.AddReplacement(
                Tokens.WriteMaskValue,
                "global::Opc.Ua.AttributeWriteMask.None");
            context.Template.AddReplacement(
                Tokens.UserWriteMaskValue,
                "global::Opc.Ua.AttributeWriteMask.None");

            // Add Children
            context.Template.AddReplacement(
                Tokens.ListOfChildNodeStates,
                node.AllChildren,
                LoadTemplate_ReplaceChild);
            context.Template.AddReplacement(
                Tokens.ListOfOptionalChildNodeStates,
                node.AllChildren,
                LoadTemplate_ReplaceChild);

            // Add References
            HashSet<ReferenceToGenerate> references = GetReferences(node);
            context.Template.AddReplacement(
                Tokens.ListOfReferences,
                references,
                LoadTemplate_AddReference);

            // Node type-specific replacements
            switch (root)
            {
                case ObjectTypeDesign objectType:
                    AddObjectTypeStateFactoryReplacements(context, objectType);
                    break;
                case VariableTypeDesign variableType:
                    AddVariableTypeStateFactoryReplacements(context, variableType);
                    break;
                case ReferenceTypeDesign referenceType:
                    AddReferenceTypeStateFactoryReplacements(context, referenceType);
                    break;
                case DataTypeDesign dataType:
                    AddDataTypeStateFactoryReplacements(context, dataType);
                    break;
                case ObjectDesign objectDesign:
                    AddObjectReplacements(context, objectDesign);
                    break;
                case VariableDesign variableDesign:
                    AddVariableStateFactoryReplacements(context, variableDesign, references);
                    break;
                case MethodDesign methodDesign:
                    AddMethodStateFactoryReplacements(context, methodDesign);
                    break;
                case ViewDesign viewDesign:
                    AddViewStateFactoryReplacements(context, viewDesign);
                    break;
            }

            // Release status
            Export.ReleaseStatus releaseStatus =
                root.ReleaseStatus.ToNodeSetReleaseStatus();
            context.Template.AddReplacement(
                Tokens.ReleaseStatusValue,
                releaseStatus != Export.ReleaseStatus.Released
                    ? CoreUtils.Format(
                        "state.ReleaseStatus = global::Opc.Ua.Export.ReleaseStatus.{0};",
                        releaseStatus)
                    : null);
            // Categories
            context.Template.AddReplacement(
                Tokens.CategoriesValue,
                !string.IsNullOrEmpty(root.Category)
                    ? CoreUtils.Format(
                        "state.Categories = new string[] {{ {0} }};",
                        string.Join(
                            ", ",
                            root.Category
                                .Split([','])
                                .Select(c => CoreUtils.Format("\"{0}\"", c.Trim()))))
                    : null);
            // Specification
            context.Template.AddReplacement(
                Tokens.SpecificationValue,
                root.PartNo != 0
                    ? CoreUtils.Format("state.Specification = \"Part{0}\";", root.PartNo)
                    : null);

            // Access restrictions
            string accessRestrictions =
                root.AccessRestrictions.GetAccessRestrictionsAsCode(
                    root.AccessRestrictionsSpecified) ??
                root.DefaultAccessRestrictions.GetAccessRestrictionsAsCode(
                    root.DefaultAccessRestrictionsSpecified);
            context.Template.AddReplacement(
                Tokens.AccessRestrictionsValue,
                accessRestrictions != null
                    ? CoreUtils.Format("state.AccessRestrictions = {0};", accessRestrictions)
                    : null);

            // Role permissions
            HashSet<RolePermission> rolePermissions = GetRolePermissions(root);
            context.Template.AddReplacement(
                Tokens.ListOfRolePermissions,
                NodeStateTemplates.ListOfRolePermissions,
                [rolePermissions],
                WriteTemplate_RolePermissions);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_AddReference(ILoadContext context)
        {
            if (context.Target is not ReferenceToGenerate reference)
            {
                return null;
            }
            context.Out.WriteLine(
                "state.AddReference({0}, {1}, {2});",
                m_context.ModelDesign.GetNodeIdConstant(
                    reference.ReferenceTypeId,
                    "<ReferenceType>",
                    kNamespaceTableContextVariable),
                reference.IsInverse ? "true" : "false",
                reference.TargetNode.GetNodeIdAsCode(
                    m_context.ModelDesign.Namespaces,
                    kNamespaceTableContextVariable));
            return null;
        }

        private TemplateString LoadTemplate_ReplaceChild(ILoadContext context)
        {
            if (context.Target is not NodeToGenerate node ||
                node.Design is not InstanceDesign instance)
            {
                return null;
            }

            if (context.Token == Tokens.ListOfOptionalChildNodeStates)
            {
                if (instance.ModellingRule == ModellingRule.Mandatory)
                {
                    return null;
                }
            }

            // Otherwise only add mandatory children - all others are created on demand
            else if (instance.ModellingRule != ModellingRule.Mandatory)
            {
                return null;
            }

            string forInstanceVariableValue = node.Parent?.InstanceOf != null ? "true" : "forInstance";
            if (node.Parent != null && IsInAddressSpace(node.Parent))
            {
                switch (node.Parent.Design)
                {
                    case TypeDesign parentType:
                        context.Out.WriteLine(
                            "state.AddChild(Create{0}(context, state, forInstance: {1}));",
                            instance.SymbolicId.Name,
                            forInstanceVariableValue);
                        break;
                    case InstanceDesign parentInstance:
                        if (HasChildDefined(parentInstance.TypeDefinitionNode, instance.SymbolicName.Name) ||
                            IsBuiltInProperty(node))
                        {
                            switch (instance.ModellingRule)
                            {
                                case ModellingRule.Mandatory:
                                case ModellingRule.Optional:
                                    context.Out.WriteLine(
                                        "state.CreateOrReplace{0}(context, Create{1}(context, state, forInstance: {2}));",
                                        instance.SymbolicName.Name,
                                        instance.SymbolicId.Name,
                                        forInstanceVariableValue);
                                    return null;
                                case ModellingRule.OptionalPlaceholder:
                                case ModellingRule.MandatoryPlaceholder:
                                    // TODO
                                    break;
                                case ModellingRule.ExposesItsArray:
                                case ModellingRule.None:
                                case ModellingRule.CardinalityRestriction:
                                case ModellingRule.MandatoryShared:
                                    break;
                            }
                        }
                        break;
                }
            }

            context.Out.WriteLine(
                "state.AddChild(Create{0}(context, state, forInstance: {1}));",
                instance.SymbolicId.Name,
                forInstanceVariableValue);
            return null;
        }

        private bool WriteTemplate_RolePermissions(IWriteContext context)
        {
            if (context.Target is not HashSet<RolePermission> rolePermissions ||
                rolePermissions.Count == 0)
            {
                return false;
            }

            context.Template.AddReplacement(
                Tokens.ListOfRolePermissions,
                NodeStateTemplates.RolePermission,
                rolePermissions,
                LoadTemplate_RolePermissionEntry,
                WriteTemplate_RolePermissionEntry);
            return context.Template.Render();
        }

        private bool WriteTemplate_ArgumentCollection(IWriteContext context)
        {
            switch (context.Target)
            {
                case IList<Argument> arguments:
                    // The parameter that is packed into the Argument value
                    context.Template.AddReplacement(
                        Tokens.ListOfValues,
                        NodeStateTemplates.ArgumentValue,
                        arguments,
                        WriteTemplate_Argument);
                    break;
                case Parameter[] parameters:
                    context.Template.AddReplacement(
                        Tokens.ListOfValues,
                        NodeStateTemplates.ArgumentValue,
                        parameters,
                        WriteTemplate_Argument);
                    break;
                default:
                    return false;
            }
            context.Template.AddReplacement(Tokens.DataType, "global::Opc.Ua.Argument");
            return context.Template.Render();
        }

        private bool WriteTemplate_Argument(IWriteContext context)
        {
            Argument argument = null;
            Parameter parameter;
            switch (context.Target)
            {
                case Argument arg:
                    argument = arg;
                    parameter = argument.Value as Parameter;
                    break;
                case Parameter parm:
                    parameter = parm;
                    break;
                default:
                    return false;
            }

            context.Template.AddReplacement(
                Tokens.Name,
                parameter == null ?
                    argument.Name.AsStringLiteral() :
                    parameter.Name.AsStringLiteral());
            context.Template.AddReplacement(
                Tokens.DataType,
                m_context.ModelDesign.GetNodeIdConstant(
                    parameter?.DataType,
                    "<DataType>",
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(
                Tokens.ValueRank,
                parameter?.ValueRank.GetValueRankAsCode(parameter.ArrayDimensions));
            context.Template.AddReplacement(
                Tokens.ArrayDimensions,
                parameter?.ValueRank.GetArrayDimensionsAsCode(parameter.ArrayDimensions) ?? "default");
            context.Template.AddReplacement(
                Tokens.Description,
                (parameter?.Description).GetLocalizedTextAsCode(true));

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_RolePermissionEntry(ILoadContext context)
        {
            if (context.Target is not RolePermission)
            {
                return null;
            }

            return NodeStateTemplates.RolePermission;
        }

        private bool WriteTemplate_RolePermissionEntry(IWriteContext context)
        {
            if (context.Target is not RolePermission rolePermission)
            {
                return false;
            }

            if (!m_context.ModelDesign.TryFindNode(
                rolePermission.Role,
                rolePermission.Role.Name,
                "RoleType",
                out ObjectDesign roleNode))
            {
                return false;
            }

            context.Template.AddReplacement(
                Tokens.RoleIdConstant,
                roleNode.GetNodeIdAsCode(
                    m_context.ModelDesign.Namespaces,
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(
                Tokens.PermissionsValue,
                CoreUtils.Format(
                    "(uint)({0})",
                    rolePermission.Permission.GetPermissionTypeAsCode()));

            return context.Template.Render();
        }

        private void AddNodeStateClassVariableTypeReplacements(
            IWriteContext context,
            VariableTypeDesign variableType)
        {
            AddNodeStateClassTypeReplacements(context, variableType);
            BasicDataType basicType = variableType.DataTypeNode.BasicDataType;

            if (variableType.SymbolicName.Name == "TwoStateDiscreteType")
            {
                variableType.ValueRank = ValueRank.Scalar;
            }

            if (!variableType.DataTypeNode.IsTemplateParameterRequired(variableType.ValueRank))
            {
                context.Template.AddReplacement(Tokens.BaseT, string.Empty);
            }
            else
            {
                string parameter = GetTemplateParameter(variableType);

                if (parameter == "<T>" && variableType.ValueRank != ValueRank.Scalar)
                {
                    parameter = "<global::Opc.Ua.Variant>";
                }

                context.Template.AddReplacement(Tokens.BaseT, parameter);
            }

            string valueRank = variableType.ValueRank.GetValueRankAsCode(
                variableType.ArrayDimensions);

            if (variableType.ValueRank == ValueRank.ScalarOrArray)
            {
                for (TypeDesign baseType = variableType.BaseTypeNode;
                    baseType != null;
                    baseType = baseType.BaseTypeNode)
                {
                    if (baseType.SymbolicId ==
                        new XmlQualifiedName("DataItemType", Namespaces.OpcUa))
                    {
                        valueRank = $"global::Opc.Ua.ValueRanks.{ValueRank.Scalar}";
                    }
                }
            }

            context.Template.AddReplacement(
                Tokens.DefaultValue,
                variableType.DataTypeNode.GetValueAsCode(
                    variableType.ValueRank,
                    variableType.DefaultValue,
                    variableType.DecodedValue,
                    true,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces,
                    m_messageContext,
                    () => AddXmlInitializerForComplexValue(
                        variableType,
                        variableType.DataTypeNode,
                        variableType.DefaultValue)));
            context.Template.AddReplacement(Tokens.ValueRank, valueRank);
            context.Template.AddReplacement(
                Tokens.ArrayDimensions,
                variableType.ValueRank.GetArrayDimensionsAsCode(
                    variableType.ArrayDimensions) ??
                "default");
            context.Template.AddReplacement(Tokens.IsAbstract, variableType.IsAbstract);
            context.Template.AddReplacement(
                Tokens.AccessLevelValue,
                variableType.AccessLevel.GetAccessLevelAsCode());
            context.Template.AddReplacement(
                Tokens.MinimumSamplingIntervalValue,
                variableType.GetMinimumSamplingIntervalAsCode());
            context.Template.AddReplacement(
                Tokens.Historizing,
                variableType.Historizing);
            context.Template.AddReplacement(
                Tokens.DataType,
                variableType.DataTypeNode.SymbolicName.Name);
            context.Template.AddReplacement(
                Tokens.DataTypeNamespacePrefix,
                m_context.ModelDesign.Namespaces.GetNamespacePrefix(
                    variableType.DataTypeNode.SymbolicId.Namespace));
            context.Template.AddReplacement(
                Tokens.DataTypeNamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    variableType.DataTypeNode.SymbolicId.Namespace));
            context.Template.AddReplacement(
                Tokens.TypedVariableType,
                NodeStateTemplates.VariableTypeWithTypedValue_Class,
                [variableType],
                LoadTemplate_TypedVariableType,
                WriteTemplate_TypedVariableType);
            context.Template.AddReplacement(
                Tokens.VariableTypeValue,
                NodeStateTemplates.VariableTypeValue_Class,
                [variableType],
                LoadTemplate_VariableTypeValue,
                WriteTemplate_VariableTypeValue);
        }

        private void AddNodeStateClassObjectTypeReplacements(
            IWriteContext context,
            ObjectTypeDesign objectType)
        {
            AddNodeStateClassTypeReplacements(context, objectType);
            context.Template.AddReplacement(Tokens.BaseT, string.Empty);
            context.Template.AddReplacement(Tokens.IsAbstract, objectType.IsAbstract);
            context.Template.AddReplacement(Tokens.EventNotifier,
                objectType.GetEventNotifierAsCode());
        }

        private void AddNodeStateClassMethodTypeReplacements(
            IWriteContext context,
            MethodDesign method)
        {
            context.Template.AddReplacement(
                Tokens.ClassName,
                method.GetNodeStateClassName(
                    m_context.ModelDesign.TargetNamespace.Value,
                    []));
            context.Template.AddReplacement(
                Tokens.ListOfInputArguments,
                method.InputArguments,
                LoadTemplate_ListOfInputArguments);
            context.Template.AddReplacement(
                Tokens.OnCallDeclaration,
                [method],
                LoadTemplate_OnCallDeclaration);
            context.Template.AddReplacement(
                Tokens.OnCallAsyncDeclaration,
                [method],
                LoadTemplate_OnCallAsyncDeclaration);
            context.Template.AddReplacement(
                Tokens.OnCallImplementation,
                [method],
                LoadTemplate_OnCallImplementation);
            context.Template.AddReplacement(
                Tokens.OnCallAsyncImplementation,
                [method],
                LoadTemplate_OnCallAsyncImplementation);
            context.Template.AddReplacement(
                Tokens.ListOfOutputDeclarations,
                method.OutputArguments,
                LoadTemplate_ListOfOutputDeclarations);
            context.Template.AddReplacement(
                Tokens.ListOfOutputArgumentsFromResult,
                method.OutputArguments,
                LoadTemplate_ListOfOutputArgumentsFromResult);
            context.Template.AddReplacement(
                Tokens.ListOfOutputArguments,
                method.OutputArguments,
                LoadTemplate_ListOfOutputArguments);
            context.Template.AddReplacement(
                Tokens.ListOfResultProperties,
                method.OutputArguments,
                LoadTemplate_ListOfResultProperties);
        }

        private void AddNodeStateClassTypeReplacements(
            IWriteContext context,
            TypeDesign type)
        {
            context.Template.AddReplacement(Tokens.ClassName, type.ClassName);
            context.Template.AddReplacement(
                Tokens.BaseClassName,
                type.BaseTypeNode.GetClassName(m_context.ModelDesign.Namespaces));
            context.Template.AddReplacement(
                Tokens.BaseTypeNamespacePrefix,
                m_context.ModelDesign.Namespaces.GetNamespacePrefix(
                    type.BaseTypeNode.SymbolicId.Namespace));
            context.Template.AddReplacement(
                Tokens.BaseTypeNamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    type.BaseTypeNode.SymbolicId.Namespace));
        }

        private void AddObjectTypeStateFactoryReplacements(
            IWriteContext context,
            ObjectTypeDesign node)
        {
            context.Template.AddReplacement(
                Tokens.SuperTypeId,
                node.BaseTypeNode.GetNodeIdAsCode(
                    m_context.ModelDesign.Namespaces,
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(Tokens.IsAbstract, node.IsAbstract);
            context.Template.AddReplacement(
                Tokens.StateClassName,
                "global::Opc.Ua.BaseObjectTypeState");
        }

        private void AddVariableTypeStateFactoryReplacements(
            IWriteContext context,
            VariableTypeDesign node)
        {
            context.Template.AddReplacement(Tokens.SuperTypeId,
                node.BaseTypeNode.GetNodeIdAsCode(
                    m_context.ModelDesign.Namespaces,
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(Tokens.IsAbstract, node.IsAbstract);
            context.Template.AddReplacement(
                Tokens.StateClassName,
                "global::Opc.Ua.BaseDataVariableTypeState");

            context.Template.AddReplacement(Tokens.ValueCode, CoreUtils.Format(
                "state.WrappedValue = {0};",
                node.DataTypeNode.GetValueAsCode(
                    node.ValueRank,
                    node.DefaultValue,
                    node.DecodedValue,
                    true,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces,
                    m_messageContext,
                    () => AddXmlInitializerForComplexValue(
                        node,
                        node.DataTypeNode,
                        node.DefaultValue))));
            string dataTypeId =
                GetNodeIdConstantForDataType(node, m_context.ModelDesign.Namespaces);
            context.Template.AddReplacement(
                Tokens.DataTypeIdConstant,
                dataTypeId ?? "global::Opc.Ua.NodeId.Null");
            string valueRank = node.ValueRank.GetValueRankAsCode(
                node.ArrayDimensions);
            context.Template.AddReplacement(Tokens.ValueRank, valueRank);
            string arrayDims = node.ValueRank.GetArrayDimensionsAsCode(
                node.ArrayDimensions);
            context.Template.AddReplacement(
                Tokens.ArrayDimensions,
                !string.IsNullOrEmpty(arrayDims)
                    ? CoreUtils.Format("state.ArrayDimensions = {0};", arrayDims)
                    : null);
        }

        private void AddVariableStateFactoryReplacements(
            IWriteContext context,
            VariableDesign node,
            HashSet<ReferenceToGenerate> references)
        {
            context.Template.AddReplacement(
                Tokens.StateClassName,
                node.GetNodeStateClassName(
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces));
            context.Template.AddReplacement(
                Tokens.TypeDefinitionId,
                node.TypeDefinitionNode.GetNodeIdAsCode(
                    m_context.ModelDesign.Namespaces,
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(
                Tokens.ReferenceTypeId,
                m_context.ModelDesign.GetNodeIdConstant(
                    node.ReferenceType,
                    "<ReferenceType>",
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(
                Tokens.ModellingRuleId,
                GetModellingRuleReplacement(node.ModellingRule));
            context.Template.AddReplacement(
                Tokens.DataTypeIdConstant,
                GetNodeIdConstantForDataType(node, m_context.ModelDesign.Namespaces));
            context.Template.AddReplacement(
                Tokens.ValueRank,
                node.ValueRank.GetValueRankAsCode(node.ArrayDimensions));

            string arrayDims = node.ValueRank.GetArrayDimensionsAsCode(node.ArrayDimensions);
            context.Template.AddReplacement(
                Tokens.ArrayDimensions,
                !string.IsNullOrEmpty(arrayDims)
                    ? CoreUtils.Format("state.ArrayDimensions = {0};", arrayDims)
                    : null);

            context.Template.AddReplacement(
                Tokens.AccessLevelValue,
                node.AccessLevel.GetAccessLevelAsCode());
            context.Template.AddReplacement(
                Tokens.UserAccessLevelValue,
                node.AccessLevel.GetAccessLevelAsCode());
            context.Template.AddReplacement(
                Tokens.MinimumSamplingIntervalValue,
                node.MinimumSamplingInterval.ToString(CultureInfo.InvariantCulture));
            context.Template.AddReplacement(
                Tokens.HistorizingValue,
                node.Historizing);

            // set dictionary to point to embedded schemas
            if (node.TypeDefinitionNode.SymbolicId ==
                    new XmlQualifiedName("DataTypeDictionaryType", Namespaces.OpcUa))
            {
                NodeDesign typeSystemReference = references.FirstOrDefault()?.TargetNode;
                switch (typeSystemReference?.SymbolicId.Name)
                {
                    case "XmlSchema_TypeSystem":
                        context.Template.AddReplacement(
                            Tokens.ValueCode,
                            "state.WrappedValue = global::Opc.Ua.Variant.From(XmlSchemas.TypesXsd.ToArray());");
                        return;
                    case "OPCBinarySchema_TypeSystem":
                        context.Template.AddReplacement(
                            Tokens.ValueCode,
                            "state.WrappedValue = global::Opc.Ua.Variant.From(XmlSchemas.TypesBsd.ToArray());");
                        return;
                }
                // unknown type system
                context.Template.AddReplacement(
                    Tokens.ValueCode,
                    "state.WrappedValue = global::Opc.Ua.Variant.Null;");
                return;
            }

            if (node.DecodedValue is IList<Argument> args)
            {
                context.Template.AddReplacement(
                    Tokens.ValueCode,
                    NodeStateTemplates.VariantArrayValue,
                    [args],
                    WriteTemplate_ArgumentCollection);
            }
            else
            {
                context.Template.AddReplacement(Tokens.ValueCode, CoreUtils.Format(
                    "state.WrappedValue = {0};",
                    node.DataTypeNode.GetValueAsCode(
                        node.ValueRank,
                        node.DefaultValue,
                        node.DecodedValue,
                        true,
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces,
                        m_messageContext,
                        () => AddXmlInitializerForComplexValue(
                            node,
                            node.DataTypeNode,
                            node.DefaultValue))));
            }
        }

        private void AddReferenceTypeStateFactoryReplacements(
            IWriteContext context,
            ReferenceTypeDesign node)
        {
            context.Template.AddReplacement(Tokens.SuperTypeId,
                node.BaseTypeNode.GetNodeIdAsCode(
                    m_context.ModelDesign.Namespaces,
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(Tokens.IsAbstract, node.IsAbstract);
            context.Template.AddReplacement(Tokens.SymmetricValue, node.Symmetric);
            context.Template.AddReplacement(Tokens.InverseNameValue, GetInverseNameValue(node));
        }

        private void AddDataTypeStateFactoryReplacements(
            IWriteContext context,
            DataTypeDesign node)
        {
            context.Template.AddReplacement(Tokens.SuperTypeId,
                node.BaseTypeNode.GetNodeIdAsCode(
                    m_context.ModelDesign.Namespaces,
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(Tokens.IsAbstract, node.IsAbstract);
            context.Template.AddReplacement(Tokens.Purpose,
                (Export.DataTypePurpose)(int)(node.Purpose == DataTypePurpose.Testing ?
                    DataTypePurpose.CodeGenerator :
                    node.Purpose));

            if (node.BasicDataType is not BasicDataType.Enumeration and not BasicDataType.UserDefined)
            {
                context.Template.AddReplacement(
                    Tokens.DataTypeDefinition,
                    "global::Opc.Ua.ExtensionObject.Null");
            }
            else
            {
                context.Template.AddReplacement(
                    Tokens.DataTypeDefinition,
                    CoreUtils.Format(
                        "new global::Opc.Ua.ExtensionObject({0}.DataTypeDefinitions.Create{1}({2}))",
                        m_context.ModelDesign.Namespaces.GetNamespacePrefix(node.SymbolicName.Namespace),
                        node.SymbolicName.Name,
                        kNamespaceTableContextVariable));
            }
        }

        private void AddObjectReplacements(
            IWriteContext context,
            ObjectDesign node)
        {
            context.Template.AddReplacement(
                Tokens.StateClassName,
                node.GetNodeStateClassName(
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces));
            context.Template.AddReplacement(
                Tokens.TypeDefinitionId,
                node.TypeDefinitionNode.GetNodeIdAsCode(
                    m_context.ModelDesign.Namespaces,
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(
                Tokens.ReferenceTypeId,
                m_context.ModelDesign.GetNodeIdConstant(
                    node.ReferenceType,
                    "<ReferenceType>",
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(
                Tokens.ModellingRuleId,
                GetModellingRuleReplacement(node.ModellingRule));
            context.Template.AddReplacement(
                Tokens.EventNotifier,
                node.SupportsEvents
                    ? "global::Opc.Ua.EventNotifiers.SubscribeToEvents"
                    : "global::Opc.Ua.EventNotifiers.None");
        }

        private void AddMethodStateFactoryReplacements(
            IWriteContext context,
            MethodDesign node)
        {
            context.Template.AddReplacement(
                Tokens.ReferenceTypeId,
                m_context.ModelDesign.GetNodeIdConstant(
                    node.ReferenceType,
                    "<ReferenceType>",
                    kNamespaceTableContextVariable));
            context.Template.AddReplacement(
                Tokens.StateClassName,
                node.GetNodeStateClassName(
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces));
            context.Template.AddReplacement(
                Tokens.ModellingRuleId,
                GetModellingRuleReplacement(node.ModellingRule));
            bool executable = !node.NonExecutable;
            context.Template.AddReplacement(Tokens.ExecutableValue, executable);

            context.Template.AddReplacement(
                Tokens.MethodDeclarationId,
                node.MethodDeclarationNode != null
                    ? CoreUtils.Format(
                        "state.MethodDeclarationId = {0};",
                        node.MethodDeclarationNode.GetNodeIdAsCode(
                            m_context.ModelDesign.Namespaces,
                            kNamespaceTableContextVariable))
                    : null);
        }

        private static void AddViewStateFactoryReplacements(
            IWriteContext context,
            ViewDesign node)
        {
            context.Template.AddReplacement(
                Tokens.EventNotifier,
                node.SupportsEvents
                    ? "global::Opc.Ua.EventNotifiers.SubscribeToEvents"
                    : "global::Opc.Ua.EventNotifiers.None");
            context.Template.AddReplacement(Tokens.ContainsNoLoopsValue, node.ContainsNoLoops);
        }

        private void CollectNodesToGenerate()
        {
            foreach (NodeDesign node in m_context.ModelDesign.Nodes)
            {
                if (m_context.ModelDesign.IsExcluded(node))
                {
                    continue;
                }

                NodeToGenerate entry = null;
                if (node is not InstanceDesign)
                {
                    entry = new NodeToGenerate(
                        Parent: null,
                        Path: string.Empty,
                        Hierarchy: node.Hierarchy,
                        Design: node,
                        IsNotExplicitlyDefined: false,
                        RootIsTypeDefinition: true,
                        InstanceOf: null);
                }
                else
                {
                    entry = new NodeToGenerate(
                        Parent: null,
                        Path: string.Empty,
                        Hierarchy: node.Hierarchy,
                        Design: node.Hierarchy.NodeList[0].Instance,
                        IsNotExplicitlyDefined: false,
                        RootIsTypeDefinition: false,
                        InstanceOf: null);
                }
                if (!m_nodes.TryAdd(entry.Design.SymbolicId, entry))
                {
                    m_logger.LogDebug(
                        "Removing duplicate entry for {Node}.",
                        entry.Design.SymbolicId.Name);
                }
                GetChildren(entry, m_nodes, false);

                if ((node is not ObjectTypeDesign and not VariableTypeDesign) &&
                    !node.IsMethodTypeDesign())
                {
                    continue;
                }

                // Add instances to generate
                if (node.Hierarchy == null ||
                    !node.Hierarchy.Nodes.TryGetValue(
                        string.Empty,
                        out HierarchyNode hierarchyNode))
                {
                    continue;
                }
                if (hierarchyNode.Identifier != null)
                {
                    if (hierarchyNode.Identifier is uint numericId)
                    {
                        hierarchyNode.Instance.NumericId = numericId;
                        hierarchyNode.Instance.NumericIdSpecified = true;
                    }
                    else if (hierarchyNode.Identifier is string stringId)
                    {
                        hierarchyNode.Instance.StringId = stringId;
                    }
                    else
                    {
                        throw new InvalidOperationException(CoreUtils.Format(
                            "Invalid identifier {0}",
                            hierarchyNode.Identifier));
                    }
                }

                var instanceToGenerate = new NodeToGenerate(
                    Parent: null,
                    Path: string.Empty,
                    Hierarchy: node.Hierarchy,
                    Design: hierarchyNode.Instance,
                    IsNotExplicitlyDefined: false,
                    RootIsTypeDefinition: false,
                    InstanceOf: entry); // Mark as instance of a type design
                entry.Instance = instanceToGenerate;
                if (!m_instances.TryAdd(instanceToGenerate.Design.SymbolicId, instanceToGenerate))
                {
                    m_logger.LogDebug(
                        "Removing duplicate entry for instance {Node}.",
                        instanceToGenerate.Design.SymbolicId.Name);
                }
                GetChildren(instanceToGenerate, m_instances, true);
            }
        }

        private bool ExcludeNodeStateClassGeneration(NodeToGenerate node)
        {
            // Filter unncessary nodes
            if (!IsInAddressSpace(node))
            {
                m_logger.LogDebug(
                    "Excluded node {Node} as it is marked NotInAddressSpace.",
                    node.Design.SymbolicId.Name);
                return true;
            }

            // Only process type designs and method types for definitions
            if (node.Design is not VariableTypeDesign and not ObjectTypeDesign &&
                !node.Design.IsMethodTypeDesign())
            {
                return true;
            }

            if (node.Design.SymbolicName.Namespace == Namespaces.OpcUa &&
                node.Design.NumericId < 256)
            {
                switch (node.Design.SymbolicName.Name)
                {
                    case "DataTypeDictionaryType":
                    case "DataTypeDescriptionType":
                    case "DataTypeSystemType":
                    case "DataTypeEncodingType":
                    case "ModellingRuleType":
                        break;
                    default:
                        m_logger.LogDebug(
                            "Skipping built-in node state class generation for {Node}.",
                            node.Design.SymbolicId.Name);
                        return true;
                }
            }
            return false;
        }

        private bool IsInAddressSpace(NodeToGenerate node)
        {
            bool isInAddressSpace = !node.Design.NotInAddressSpace;
            if (node.Design is InstanceDesign instanceDesign &&
                instanceDesign.TypeDefinition != null &&
                instanceDesign.TypeDefinition.Name == "DataTypeEncodingType")
            {
                isInAddressSpace =
                    instanceDesign.Parent == null ||
                    !instanceDesign.Parent.NotInAddressSpace;
            }
            return isInAddressSpace;
        }

        /// <summary>
        /// Checks if the instance is a built in property of a base node state
        /// implementation (variable/method) that should not be generated because
        /// it is already defined on the root nodestate class.
        /// </summary>
        private static bool IsBuiltInProperty(NodeToGenerate node)
        {
            switch (node.Parent?.Design)
            {
                case MethodDesign:
                    if (node.Design.SymbolicName ==
                        new XmlQualifiedName("InputArguments", Namespaces.OpcUa))
                    {
                        return true;
                    }

                    if (node.Design.SymbolicName ==
                        new XmlQualifiedName("OutputArguments", Namespaces.OpcUa))
                    {
                        return true;
                    }
                    break;
                case VariableDesign:
                case VariableTypeDesign:
                    if (node.Design.SymbolicName ==
                        new XmlQualifiedName("EnumStrings", Namespaces.OpcUa))
                    {
                        return true;
                    }
                    break;
            }
            return false;
        }

        private bool HasChildDefined(TypeDesign typeDefinitionNode, string symbolicName)
        {
            if (typeDefinitionNode == null)
            {
                return false;
            }
            if (typeDefinitionNode.Children?.Items != null)
            {
                foreach (InstanceDesign child in typeDefinitionNode.Children.Items)
                {
                    if (child.SymbolicName.Name == symbolicName)
                    {
                        return true;
                    }
                }
            }
            return HasChildDefined(typeDefinitionNode.BaseTypeNode, symbolicName);
        }

        private void GetChildren(
            NodeToGenerate node,
            Dictionary<XmlQualifiedName, NodeToGenerate> children,
            bool forInstance)
        {
            if (!forInstance)
            {
                if (node.Design.Children?.Items != null)
                {
                    foreach (InstanceDesign child in node.Design.Children.Items)
                    {
                        if (m_context.ModelDesign.IsExcluded(child))
                        {
                            continue;
                        }
                        string childPath = child.SymbolicName.Name;
                        if (!string.IsNullOrEmpty(node.Path))
                        {
                            childPath = CoreUtils.Format(
                                "{0}{1}{2}",
                                node.Path,
                                NodeDesign.PathChar,
                                childPath);
                        }
                        node.Children[child.SymbolicName.Name] = new NodeToGenerate(
                            node,
                            childPath,
                            node.Hierarchy,
                            child,
                            IsNotExplicitlyDefined: false,
                            RootIsTypeDefinition: node.RootIsTypeDefinition,
                            InstanceOf: null);
                    }
                }
            }

            if (node.Hierarchy == null)
            {
                return;
            }

            foreach (HierarchyNode current in node.Hierarchy.NodeList)
            {
                string childPath = current.RelativePath;

                // only looking for nodes in the current tree.
                if (!childPath.StartsWith(node.Path, StringComparison.Ordinal))
                {
                    continue;
                }

                // ignore reference to the current base node.
                if (childPath == node.Path)
                {
                    continue;
                }

                // relative should always end in the name of the current instance.
                if (!childPath.EndsWith(
                    current.Instance.SymbolicName.Name,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                // get the parent path.
                if (childPath.Length <= current.Instance.SymbolicName.Name.Length)
                {
                    if (!string.IsNullOrEmpty(node.Path))
                    {
                        continue;
                    }
                }
                else
                {
                    int idx = childPath.Length - current.Instance.SymbolicName.Name.Length - 1;
                    string parentPath = current.RelativePath[..idx];

                    if (parentPath != node.Path)
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(node.Path))
                {
                    childPath = childPath[(node.Path.Length + 1)..];
                    childPath = CoreUtils.Format(
                        "{0}{1}{2}",
                        node.Path,
                        NodeDesign.PathChar,
                        childPath);
                }

                if (current.Instance is not InstanceDesign child)
                {
                    continue;
                }

                if (!node.RootIsTypeDefinition &&
                    !current.ExplicitlyDefined &&
                    child.ModellingRule != ModellingRule.Mandatory &&
                    child.ModellingRule != ModellingRule.Optional)
                {
                    continue;
                }

                if (!current.ExplicitlyDefined &&
                    child.ModellingRule != ModellingRule.Mandatory &&
                    child.ModellingRule != ModellingRule.None &&
                    child.ModellingRule != ModellingRule.ExposesItsArray &&
                    child.ModellingRule != ModellingRule.OptionalPlaceholder &&
                    child.ModellingRule != ModellingRule.MandatoryPlaceholder)
                {
                    continue;
                }

                if (node.RootIsTypeDefinition &&
                    !current.ExplicitlyDefined &&
                    current.Inherited &&
                    current.AdHocInstance)
                {
                    // this assumes that ad-hoc instances are not more than one level deep.
                    // i.e. a type defines folder and adds a few instances but does not
                    // defined subfolders.
                    // need a better way to identify when to suppress inherited adhoc instances.
                    if (!node.Path.Contains(NodeDesign.PathChar, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                bool add = false;
                if (node.Design is DataTypeDesign or ViewDesign or ReferenceTypeDesign)
                {
                    add = true;
                }
                else if (node.RootIsTypeDefinition)
                {
                    if (child.ModellingRule == ModellingRule.Mandatory)
                    {
                        add = true;
                    }
                    else if (current.ExplicitlyDefined &&
                        child.ModellingRule == ModellingRule.Optional)
                    {
                        add = true;
                    }
                    else if (current.ExplicitlyDefined &&
                        (child.ModellingRule is
                            ModellingRule.ExposesItsArray or
                            ModellingRule.OptionalPlaceholder or
                            ModellingRule.MandatoryPlaceholder))
                    {
                        add = true;
                    }
                    else if (current.StaticValue && !current.Inherited)
                    {
                        add = true;
                    }
                    else if (child.ModellingRule is not ModellingRule.None)
                    {
                        m_logger.LogDebug(
                            "Excluding child node {Node} from generation.",
                            current.Instance.SymbolicId.Name);
                    }
                }
                else if (child.ModellingRule == ModellingRule.Mandatory)
                {
                    add = true;
                }
                else if (current.ExplicitlyDefined)
                {
                    add = true;
                }
                else
                {
                    m_logger.LogInformation(
                        "Excluding child node {Node} from generation.",
                        current.Instance.SymbolicId.Name);
                }

                if (add)
                {
                    XmlQualifiedName symbolicId = current.Instance.SymbolicId;
                    var childNodeToGenerate = new NodeToGenerate(
                        node,
                        childPath,
                        node.Hierarchy,
                        child,
                        IsNotExplicitlyDefined: !current.ExplicitlyDefined,
                        RootIsTypeDefinition: node.RootIsTypeDefinition,
                        InstanceOf: null);
                    if (!children.TryAdd(symbolicId, childNodeToGenerate))
                    {
                        m_logger.LogInformation(
                            "Removing duplicate entry for child {Node}.",
                            symbolicId.Name);
                    }
                    node.AllChildren.Add(childNodeToGenerate);
                    GetChildren(childNodeToGenerate, children, forInstance);
                }
            }
        }

        private HashSet<ReferenceToGenerate> GetReferences(NodeToGenerate node)
        {
            if (node.Hierarchy == null)
            {
                return [];
            }

            Hierarchy hierarchy = node.Hierarchy;
            NodeDesign root = node.Design;
            HashSet<ReferenceToGenerate> references = [];
            foreach (HierarchyReference reference in hierarchy.References)
            {
                if (reference.SourcePath != node.Path &&
                    reference.TargetPath != node.Path)
                {
                    continue;
                }

                bool isInverse = reference.IsInverse;

                if (reference.TargetId != null)
                {
                    if (!m_context.ModelDesign.TryFindNode(
                        reference.TargetId,
                        root.SymbolicId.Name,
                        reference.ReferenceType.Name,
                        out NodeDesign targetNode))
                    {
                        continue;
                    }

                    if (!node.RootIsTypeDefinition &&
                        targetNode is InstanceDesign instance &&
                        (instance.ModellingRule is
                            ModellingRule.MandatoryPlaceholder or
                            ModellingRule.OptionalPlaceholder))
                    {
                        continue;
                    }

                    references.Add(new ReferenceToGenerate(
                        targetNode,
                        reference.ReferenceType,
                        isInverse));
                    continue;
                }

                if (reference.TargetPath != null &&
                    reference.TargetPath.Length == 0 &&
                    node.Parent?.Design != null)
                {
                    references.Add(new ReferenceToGenerate(
                        node.Parent.Design,
                        reference.ReferenceType,
                        isInverse));
                    continue;
                }
                if (reference.SourcePath == node.Path)
                {
                    if (!hierarchy.Nodes.TryGetValue(
                        reference.TargetPath,
                        out HierarchyNode target))
                    {
                        continue;
                    }

                    if (!target.ExplicitlyDefined && node.RootIsTypeDefinition)
                    {
                        continue;
                    }
                    references.Add(new ReferenceToGenerate(
                        target.Instance,
                        reference.ReferenceType,
                        isInverse));
                    continue;
                }
                if (!hierarchy.Nodes.TryGetValue(reference.SourcePath, out HierarchyNode source))
                {
                    continue;
                }
                if (!source.ExplicitlyDefined && node.RootIsTypeDefinition)
                {
                    continue;
                }
                references.Add(new ReferenceToGenerate(
                    source.Instance,
                    reference.ReferenceType,
                    !isInverse));
            }
            return references;
        }

        private HashSet<RolePermission> GetRolePermissions(NodeDesign node)
        {
            var rolePermissions = new HashSet<RolePermission>();
            RolePermission[] nodeRolePermissions =
                node.RolePermissions?.RolePermission ??
                node.DefaultRolePermissions?.RolePermission;
            if (nodeRolePermissions != null)
            {
                foreach (RolePermission rp in nodeRolePermissions)
                {
                    ObjectDesign roleNode = m_context.ModelDesign.FindNode<ObjectDesign>(
                        rp.Role,
                        rp.Role.Name,
                        "RoleType");

                    if (roleNode != null)
                    {
                        rolePermissions.Add(rp);
                    }
                }
            }
            return rolePermissions;
        }

        private static string GetDisplayNameValue(NodeDesign node)
        {
            if (node.DisplayName != null)
            {
                return CoreUtils.Format(
                    "{0}, string.Empty, {1}",
                    node.DisplayName.Key.AsStringLiteral(),
                    (node.DisplayName.Value?.Trim()).AsStringLiteral());
            }
            return null;
        }

        private static string GetDescriptionValue(NodeDesign node)
        {
            if (node.Description != null && !node.Description.IsAutogenerated)
            {
                return CoreUtils.Format(
                    "state.Description = {0};",
                    node.Description.GetLocalizedTextAsCode());
            }
            return null;
        }

        private static string GetInverseNameValue(ReferenceTypeDesign node)
        {
            if (!node.Symmetric && node.InverseName != null)
            {
                return CoreUtils.Format(
                    "state.InverseName = {0};",
                    node.InverseName.GetLocalizedTextAsCode());
            }
            if (node.Symmetric)
            {
                return "state.InverseName = global::Opc.Ua.LocalizedText.Null;";
            }
            return null;
        }

        private static string GetModellingRuleReplacement(ModellingRule modellingRule)
        {
            string constant = modellingRule switch
            {
                ModellingRule.Mandatory => "global::Opc.Ua.Objects.ModellingRule_Mandatory",
                ModellingRule.Optional => "global::Opc.Ua.Objects.ModellingRule_Optional",
                ModellingRule.MandatoryPlaceholder => "global::Opc.Ua.Objects.ModellingRule_MandatoryPlaceholder",
                ModellingRule.OptionalPlaceholder => "global::Opc.Ua.Objects.ModellingRule_OptionalPlaceholder",
                ModellingRule.ExposesItsArray => "global::Opc.Ua.Objects.ModellingRule_ExposesItsArray",
                _ => null
            };

            return constant != null
                ? CoreUtils.Format("state.ModellingRuleId = {0};", constant)
                : null;
        }

        private string GetNodeIdConstantForDataType(
            VariableTypeDesign type,
            Namespace[] namespaceUris)
        {
            if (!m_context.ModelDesign.UseAllowSubtypes)
            {
                DataTypeDesign dataType = m_context.ModelDesign.FindNode<DataTypeDesign>(
                    type.DataType,
                    type.SymbolicId.Name,
                    "DataType");
                return dataType.GetNodeIdAsCode(namespaceUris, kNamespaceTableContextVariable);
            }
            return type.DataTypeNode.GetNodeIdAsCode(namespaceUris, kNamespaceTableContextVariable);
        }

        private string GetNodeIdConstantForDataType(
            VariableDesign instance,
            Namespace[] namespaceUris)
        {
            if (!m_context.ModelDesign.UseAllowSubtypes)
            {
                DataTypeDesign dataType = m_context.ModelDesign.FindNode<DataTypeDesign>(
                    instance.DataType,
                    instance.SymbolicId.Name,
                    "DataType");
                return dataType.GetNodeIdAsCode(namespaceUris, kNamespaceTableContextVariable);
            }
            return instance.DataTypeNode.GetNodeIdAsCode(namespaceUris, kNamespaceTableContextVariable);
        }

        private string GetTemplateParameter(TypeDesign type)
        {
            if (type is not VariableTypeDesign variableType)
            {
                return string.Empty;
            }

            if (type.BaseTypeNode == null)
            {
                return CoreUtils.Format("<T>");
            }

            if (GetTemplateParameter(type.BaseTypeNode) != "<T>")
            {
                return string.Empty;
            }

            BasicDataType basicType = variableType.DataTypeNode.BasicDataType;

            if (basicType == BasicDataType.BaseDataType)
            {
                return "<T>";
            }

            string scalarName;
            switch (basicType)
            {
                case BasicDataType.UserDefined:
                    scalarName = variableType.DataTypeNode.GetClassName(
                        m_context.ModelDesign.Namespaces);
                    break;
                default:
                    scalarName = variableType.DataTypeNode.GetDotNetTypeName(
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces,
                        nullable: NullableAnnotation.NonNullable,
                        true);
                    break;
            }

            if (variableType.ValueRank != ValueRank.Scalar)
            {
                return variableType.ValueRank == ValueRank.Array ?
                    $"<{scalarName}[]>" :
                    "<global::Opc.Ua.Variant>";
            }

            return $"<{scalarName}>";
        }

        private static void CollectMatchingFields(
            VariableTypeDesign variableType,
            Dictionary<string, Parameter> fields)
        {
            CollectFields(
                variableType.DataTypeNode,
                variableType.ValueRank,
                string.Empty,
                fields);

            List<string> availablePaths = [.. fields.Keys];

            for (int ii = 0; ii < availablePaths.Count; ii++)
            {
                if (!variableType.Hierarchy.Nodes.ContainsKey(availablePaths[ii]))
                {
                    fields.Remove(availablePaths[ii]);
                }
            }
        }

        private static void CollectFields(
            DataTypeDesign dataType,
            ValueRank valueRank,
            string basePath,
            Dictionary<string, Parameter> fields)
        {
            if (dataType.BasicDataType != BasicDataType.UserDefined ||
                valueRank != ValueRank.Scalar)
            {
                return;
            }

            for (DataTypeDesign parent = dataType;
                parent != null;
                parent = parent.BaseTypeNode as DataTypeDesign)
            {
                if (parent.Fields != null)
                {
                    for (int ii = 0; ii < parent.Fields.Length; ii++)
                    {
                        Parameter parameter = parent.Fields[ii];
                        string fieldPath = parameter.Name;

                        if (!string.IsNullOrEmpty(basePath))
                        {
                            fieldPath = CoreUtils.Format("{0}_{1}", basePath, parameter.Name);
                        }

                        fields[fieldPath] = parameter;
                    }
                }
            }
        }

        private string AddXmlInitializerForComplexValue(
            NodeDesign node,
            DataTypeDesign dataType,
            XmlElement element)
        {
            string xml = element?.OuterXml;
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }
            string resourceName = CoreUtils.Format("Values.{0}", node.SymbolicId.Name);
            string uniqueName = resourceName;
            for (int i = 0; i < 1000; i++)
            {
                if (m_initializers.TryAdd(uniqueName, new TextResource(uniqueName, xml)))
                {
                    // Get code to create the variant from the XML resource reference
                    return CoreUtils.Format(
                        "global::Opc.Ua.Variant.FromXml({0}AsStream, context)",
                        uniqueName);
                }
                uniqueName = resourceName + i;
            }
            throw new InvalidOperationException("Unexpected duplicate resource names");
        }

        /// <summary>
        /// Embed all initializers as source code
        /// </summary>
        private Resource EmbedInitializers()
        {
            if (m_initializers.Count == 0)
            {
                return null;
            }
            var initializers = new ResourceGenerator(m_context);
            return initializers.Embed(
                m_context.ModelDesign.TargetNamespace.Prefix,
                "NodeStates.i",
                internalAccess: true,
                [.. m_initializers.Values]);
        }

        /// <summary>
        /// Node state generation template
        /// </summary>
        private record class NodeToGenerate(
            NodeToGenerate Parent = null,
            string Path = null,
            Hierarchy Hierarchy = null,
            NodeDesign Design = null,
            bool IsNotExplicitlyDefined = false,
            bool RootIsTypeDefinition = false,
            NodeToGenerate InstanceOf = null) // Therefore an instance of a type
        {
            /// <summary> Full inherited list of children </summary>
            public List<NodeToGenerate> AllChildren { get; } = [];

            /// <summary> Direclty defined </summary>
            public Dictionary<string, NodeToGenerate> Children { get; } = [];

            public NodeToGenerate Instance { get; set; }

            /// <inheritdoc/>
            public override string ToString()
            {
                return CoreUtils.Format("{0} (Parent: {1})", Design, Parent);
            }
        }

        private record class ReferenceToGenerate(
            NodeDesign TargetNode,
            XmlQualifiedName ReferenceTypeId,
            bool IsInverse);

        private const string kNamespaceTableContextVariable = "context.NamespaceUris";

        private static readonly string[] s_builtInPropertyNames =
        [
            "Description",
            "Save",
            "Handle",
            "Specification",
            "Update",
            "Delete"
        ];

        private static readonly string[] s_builtInMethodNames =
        [
            "Child"
        ];

        private readonly Dictionary<string, Resource> m_initializers = [];
        private readonly Dictionary<XmlQualifiedName, NodeToGenerate> m_nodes = [];
        private readonly Dictionary<XmlQualifiedName, NodeToGenerate> m_instances = [];
        private readonly SystemContext m_systemContext;
        private readonly ILogger m_logger;
        private readonly IServiceMessageContext m_messageContext;
        private readonly IGeneratorContext m_context;
    }
}
