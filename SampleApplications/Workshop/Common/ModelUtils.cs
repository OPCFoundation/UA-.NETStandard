/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts
{
    /// <summary>
    /// Defines functions used to access the type model a server.
    /// </summary>
    public static class ModelUtils
    {
        /// <summary>
        /// Collects the instance declarations for a type.
        /// </summary>
        public static List<InstanceDeclaration> CollectInstanceDeclarationsForType(Session session, NodeId typeId)
        {
            // process the types starting from the top of the tree.
            List<InstanceDeclaration> instances = new List<InstanceDeclaration>();
            Dictionary<string, InstanceDeclaration> map = new Dictionary<string, InstanceDeclaration>();

            // get the supertypes.
            ReferenceDescriptionCollection supertypes = FormUtils.BrowseSuperTypes(session, typeId, false);

            if (supertypes != null)
            {
                for (int ii = supertypes.Count - 1; ii >= 0; ii--)
                {
                    CollectInstanceDeclarations(session, (NodeId)supertypes[ii].NodeId, null, instances, map);
                }
            }

            // collect the fields for the selected type.
            CollectInstanceDeclarations(session, typeId, null, instances, map);

            // return the complete list.
            return instances;
        }

        /// <summary>
        /// Collects the fields for the instance node.
        /// </summary>
        private static void CollectInstanceDeclarations(
            Session session,
            NodeId typeId,
            InstanceDeclaration parent,
            List<InstanceDeclaration> instances,
            IDictionary<string, InstanceDeclaration> map)
        {
            // find the children.
            BrowseDescription nodeToBrowse = new BrowseDescription();

            if (parent == null)
            {
                nodeToBrowse.NodeId = typeId;
            }
            else
            {
                nodeToBrowse.NodeId = parent.NodeId;
            }

            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasChild;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.Method);
            nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            // ignore any browsing errors.
            ReferenceDescriptionCollection references = FormUtils.Browse(session, nodeToBrowse, false);

            if (references == null)
            {
                return;
            }

            // process the children.
            List<NodeId> nodeIds = new List<NodeId>();
            List<InstanceDeclaration> children = new List<InstanceDeclaration>();

            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];

                if (reference.NodeId.IsAbsolute)
                {
                    continue;
                }

                // create a new declaration.
                InstanceDeclaration child = new InstanceDeclaration();

                child.RootTypeId = typeId;
                child.NodeId = (NodeId)reference.NodeId;
                child.BrowseName = reference.BrowseName;
                child.NodeClass = reference.NodeClass;

                if (!LocalizedText.IsNullOrEmpty(reference.DisplayName))
                {
                    child.DisplayName = reference.DisplayName.Text;
                }
                else
                {
                    child.DisplayName = reference.BrowseName.Name;
                }

                if (parent != null)
                {
                    child.BrowsePath = new QualifiedNameCollection(parent.BrowsePath);
                    child.BrowsePathDisplayText = Utils.Format("{0}/{1}", parent.BrowsePathDisplayText, reference.BrowseName);
                    child.DisplayPath = Utils.Format("{0}/{1}", parent.DisplayPath, reference.DisplayName);
                }
                else
                {
                    child.BrowsePath = new QualifiedNameCollection();
                    child.BrowsePathDisplayText = Utils.Format("{0}", reference.BrowseName);
                    child.DisplayPath = Utils.Format("{0}", reference.DisplayName);
                }

                child.BrowsePath.Add(reference.BrowseName);

                // check if reading an overridden declaration.
                InstanceDeclaration overriden = null;

                if (map.TryGetValue(child.BrowsePathDisplayText, out overriden))
                {
                    child.OverriddenDeclaration = overriden;
                }

                map[child.BrowsePathDisplayText] = child;

                // add to list.
                children.Add(child);
                nodeIds.Add(child.NodeId);
            }

            // check if nothing more to do.
            if (children.Count == 0)
            {
                return;
            }

            // find the modelling rules.
            List<NodeId> modellingRules = FindTargetOfReference(session, nodeIds, Opc.Ua.ReferenceTypeIds.HasModellingRule, false);

            if (modellingRules != null)
            {
                for (int ii = 0; ii < nodeIds.Count; ii++)
                {
                    children[ii].ModellingRule = modellingRules[ii];

                    // if the modelling rule is null then the instance is not part of the type declaration.
                    if (NodeId.IsNull(modellingRules[ii]))
                    {
                        map.Remove(children[ii].BrowsePathDisplayText);
                    }
                }
            }

            // update the descriptions.
            UpdateInstanceDescriptions(session, children, false);

            // recusively collect instance declarations for the tree below.
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (!NodeId.IsNull(children[ii].ModellingRule))
                {
                    instances.Add(children[ii]);
                    CollectInstanceDeclarations(session, typeId, children[ii], instances, map);
                }
            }
        }

        /// <summary>
        /// Finds the targets for the specified reference.
        /// </summary>
        private static List<NodeId> FindTargetOfReference(Session session, List<NodeId> nodeIds, NodeId referenceTypeId, bool throwOnError)
        {
            try
            {
                // construct browse request.
                BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();

                for (int ii = 0; ii < nodeIds.Count; ii++)
                {
                    BrowseDescription nodeToBrowse = new BrowseDescription();
                    nodeToBrowse.NodeId = nodeIds[ii];
                    nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
                    nodeToBrowse.ReferenceTypeId = referenceTypeId;
                    nodeToBrowse.IncludeSubtypes = false;
                    nodeToBrowse.NodeClassMask = 0;
                    nodeToBrowse.ResultMask = (uint)BrowseResultMask.None;
                    nodesToBrowse.Add(nodeToBrowse);
                }

                // start the browse operation.
                BrowseResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                session.Browse(
                    null,
                    null,
                    1,
                    nodesToBrowse,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                List<NodeId> targetIds = new List<NodeId>();
                ByteStringCollection continuationPoints = new ByteStringCollection();

                for (int ii = 0; ii < nodeIds.Count; ii++)
                {
                    targetIds.Add(null);

                    // check for error.
                    if (StatusCode.IsBad(results[ii].StatusCode))
                    {
                        continue;
                    }

                    // check for continuation point.
                    if (results[ii].ContinuationPoint != null && results[ii].ContinuationPoint.Length > 0)
                    {
                        continuationPoints.Add(results[ii].ContinuationPoint);
                    }

                    // get the node id.
                    if (results[ii].References.Count > 0)
                    {
                        if (NodeId.IsNull(results[ii].References[0].NodeId) || results[ii].References[0].NodeId.IsAbsolute)
                        {
                            continue;
                        }

                        targetIds[ii] = (NodeId)results[ii].References[0].NodeId;
                    }
                }

                // release continuation points.
                if (continuationPoints.Count > 0)
                {
                    session.BrowseNext(
                        null,
                        true,
                        continuationPoints,
                        out results,
                        out diagnosticInfos);

                    ClientBase.ValidateResponse(results, nodesToBrowse);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);
                }

                //return complete list.
                return targetIds;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        /// <summary>
        /// Finds the targets for the specified reference.
        /// </summary>
        private static void UpdateInstanceDescriptions(Session session, List<InstanceDeclaration> instances, bool throwOnError)
        {
            try
            {
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

                for (int ii = 0; ii < instances.Count; ii++)
                {
                    ReadValueId nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = instances[ii].NodeId;
                    nodeToRead.AttributeId = Attributes.Description;
                    nodesToRead.Add(nodeToRead);

                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = instances[ii].NodeId;
                    nodeToRead.AttributeId = Attributes.DataType;
                    nodesToRead.Add(nodeToRead);

                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = instances[ii].NodeId;
                    nodeToRead.AttributeId = Attributes.ValueRank;
                    nodesToRead.Add(nodeToRead);
                }

                // start the browse operation.
                DataValueCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                // update the instances.
                for (int ii = 0; ii < nodesToRead.Count; ii += 3)
                {
                    InstanceDeclaration instance = instances[ii / 3];

                    instance.Description = results[ii].GetValue<LocalizedText>(LocalizedText.Null).Text;
                    instance.DataType = results[ii + 1].GetValue<NodeId>(NodeId.Null);
                    instance.ValueRank = results[ii + 2].GetValue<int>(ValueRanks.Any);

                    if (!NodeId.IsNull(instance.DataType))
                    {
                        instance.BuiltInType = DataTypes.GetBuiltInType(instance.DataType);
                        instance.DataTypeDisplayText = session.NodeCache.GetDisplayText(instance.DataType);

                        if (instance.ValueRank >= 0)
                        {
                            instance.DataTypeDisplayText += "[]";
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }
            }
        }
    }

    /// <summary>
    /// Stores a type declaration retrieved from a server.
    /// </summary>
    public class TypeDeclaration
    {
        /// <summary>
        /// The node if for the type.
        /// </summary>
        public NodeId NodeId;

        /// <summary>
        /// The fully inhierited list of instance declarations for the type.
        /// </summary>
        public List<InstanceDeclaration> Declarations;
    }

    /// <summary>
    /// Stores an instance declaration fetched from the server.
    /// </summary>
    public class InstanceDeclaration
    {
        /// <summary>
        /// The type that the declaration belongs to.
        /// </summary>
        public NodeId RootTypeId;

        /// <summary>
        /// The browse path to the instance declaration.
        /// </summary>
        public QualifiedNameCollection BrowsePath;

        /// <summary>
        /// The browse path to the instance declaration.
        /// </summary>
        public string BrowsePathDisplayText;

        /// <summary>
        /// A localized path to the instance declaration.
        /// </summary>
        public string DisplayPath;

        /// <summary>
        /// The node id for the instance declaration.
        /// </summary>
        public NodeId NodeId;

        /// <summary>
        /// The node class of the instance declaration.
        /// </summary>
        public NodeClass NodeClass;

        /// <summary>
        /// The browse name for the instance declaration.
        /// </summary>
        public QualifiedName BrowseName;

        /// <summary>
        /// The display name for the instance declaration.
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// The description for the instance declaration.
        /// </summary>
        public string Description;

        /// <summary>
        /// The modelling rule for the instance declaration (i.e. Mandatory or Optional).
        /// </summary>
        public NodeId ModellingRule;

        /// <summary>
        /// The data type for the instance declaration.
        /// </summary>
        public NodeId DataType;

        /// <summary>
        /// The value rank for the instance declaration.
        /// </summary>
        public int ValueRank;

        /// <summary>
        /// The built-in type parent for the data type.
        /// </summary>
        public BuiltInType BuiltInType;

        /// <summary>
        /// A localized name for the data type.
        /// </summary>
        public string DataTypeDisplayText;

        /// <summary>
        /// An instance declaration that has been overridden by the current instance.
        /// </summary>
        public InstanceDeclaration OverriddenDeclaration;
    }

    /// <summary>
    /// A field in a filter declaration.
    /// </summary>
    public class FilterDeclarationField
    {
        /// <summary>
        /// Creates a new instance of a FilterDeclarationField.
        /// </summary>
        public FilterDeclarationField()
        {
            DisplayInList = false;
            FilterEnabled = false;
            FilterOperator = FilterOperator.Equals;
            FilterValue = Variant.Null;
            InstanceDeclaration = null;
        }

        /// <summary>
        /// Creates a new instance of a FilterDeclarationField.
        /// </summary>
        public FilterDeclarationField(InstanceDeclaration instanceDeclaration)
        {
            DisplayInList = false;
            FilterEnabled = false;
            FilterOperator = FilterOperator.Equals;
            FilterValue = Variant.Null;
            InstanceDeclaration = instanceDeclaration;
        }

        /// <summary>
        /// Creates a new instance of a FilterDeclarationField.
        /// </summary>
        public FilterDeclarationField(FilterDeclarationField field)
        {
            DisplayInList = field.DisplayInList;
            FilterEnabled = field.FilterEnabled;
            FilterOperator = field.FilterOperator;
            FilterValue = field.FilterValue;
            InstanceDeclaration = field.InstanceDeclaration;
        }

        /// <summary>
        /// Whether the field is displayed in the list view.
        /// </summary>
        public bool DisplayInList;

        /// <summary>
        /// Whether the filter is enabled.
        /// </summary>
        public bool FilterEnabled;

        /// <summary>
        /// The filter operator to use in the where clause.
        /// </summary>
        public FilterOperator FilterOperator;

        /// <summary>
        /// The filter value to use in the where clause.
        /// </summary>
        public Variant FilterValue;

        /// <summary>
        /// The instance declaration associated with the field.
        /// </summary>
        public InstanceDeclaration InstanceDeclaration;
    }

    /// <summary>
    /// A declararion of an event filter.
    /// </summary>
    public class FilterDeclaration
    {
        /// <summary>
        /// Creates a new instance of a FilterDeclaration.
        /// </summary>
        public FilterDeclaration()
        {
            EventTypeId = Opc.Ua.ObjectTypeIds.BaseEventType;
            Fields = new List<FilterDeclarationField>();
        }

        /// <summary>
        /// Creates a new instance of a FilterDeclaration.
        /// </summary>
        public FilterDeclaration(TypeDeclaration eventType, FilterDeclaration template)
        {
            EventTypeId = eventType.NodeId;
            Fields = new List<FilterDeclarationField>();

            foreach (InstanceDeclaration instanceDeclaration in eventType.Declarations)
            {
                if (instanceDeclaration.NodeClass == NodeClass.Method)
                {
                    continue;
                }

                if (NodeId.IsNull(instanceDeclaration.ModellingRule))
                {
                    continue;
                }

                FilterDeclarationField element = new FilterDeclarationField(instanceDeclaration);
                Fields.Add(element);

                // set reasonable defaults.
                if (template == null)
                {
                    if (instanceDeclaration.RootTypeId == Opc.Ua.ObjectTypeIds.BaseEventType && instanceDeclaration.BrowseName != Opc.Ua.BrowseNames.EventId)
                    {
                        element.DisplayInList = true;
                    }
                }

                // preserve filter settings.
                else
                {
                    foreach (FilterDeclarationField field in template.Fields)
                    {
                        if (field.InstanceDeclaration.BrowsePathDisplayText == element.InstanceDeclaration.BrowsePathDisplayText)
                        {
                            element.DisplayInList = field.DisplayInList;
                            element.FilterEnabled = field.FilterEnabled;
                            element.FilterOperator = field.FilterOperator;
                            element.FilterValue = field.FilterValue;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates a new instance of a FilterDeclaration.
        /// </summary>
        public FilterDeclaration(FilterDeclaration declaration)
        {
            EventTypeId = declaration.EventTypeId;
            Fields = new List<FilterDeclarationField>(declaration.Fields.Count);

            for (int ii = 0; ii < declaration.Fields.Count; ii++)
            {
                Fields.Add(new FilterDeclarationField(declaration.Fields[ii]));
            }
        }

        /// <summary>
        /// Returns the event filter defined by the filter declaration.
        /// </summary>
        public EventFilter GetFilter()
        {
            EventFilter filter = new EventFilter();
            filter.SelectClauses = GetSelectClause();
            filter.WhereClause = GetWhereClause();
            return filter;
        }
        
        /// <summary>
        /// Adds a simple field to the declaration.
        /// </summary>
        public void AddSimpleField(QualifiedName browseName, BuiltInType dataType, bool displayInList)
        {
            AddSimpleField(browseName, dataType, ValueRanks.Scalar, displayInList);
        }

        /// <summary>
        /// Adds a simple field to the declaration.
        /// </summary>
        public void AddSimpleField(QualifiedName browseName, BuiltInType dataType, int valueRank, bool displayInList)
        {
            FilterDeclarationField field = new FilterDeclarationField();

            field.DisplayInList = displayInList;
            field.InstanceDeclaration = new InstanceDeclaration();
            field.InstanceDeclaration.BrowseName = browseName;
            field.InstanceDeclaration.BrowsePath = new QualifiedNameCollection();
            field.InstanceDeclaration.BrowsePath.Add(field.InstanceDeclaration.BrowseName);
            field.InstanceDeclaration.BrowsePathDisplayText = browseName.Name;

            field.InstanceDeclaration.BuiltInType = dataType;
            field.InstanceDeclaration.DataType = (uint)dataType;
            field.InstanceDeclaration.ValueRank = valueRank;
            field.InstanceDeclaration.DataTypeDisplayText = dataType.ToString();

            if (valueRank >= 0)
            {
                field.InstanceDeclaration.DataTypeDisplayText += "[]";
            }

            field.InstanceDeclaration.DisplayName = browseName.Name;
            field.InstanceDeclaration.DisplayPath = browseName.Name;
            field.InstanceDeclaration.RootTypeId = ObjectTypeIds.BaseEventType;
            Fields.Add(field);
        }

        /// <summary>
        /// Returns the select clause defined by the filter declaration.
        /// </summary>
        public SimpleAttributeOperandCollection GetSelectClause()
        {
            SimpleAttributeOperandCollection selectClause = new SimpleAttributeOperandCollection();

            SimpleAttributeOperand operand = new SimpleAttributeOperand();
            operand.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
            operand.AttributeId = Attributes.NodeId;
            selectClause.Add(operand);

            foreach (FilterDeclarationField field in Fields)
            {
                operand = new SimpleAttributeOperand();
                operand.TypeDefinitionId = field.InstanceDeclaration.RootTypeId;
                operand.AttributeId = (field.InstanceDeclaration.NodeClass == NodeClass.Object)?Attributes.NodeId:Attributes.Value;
                operand.BrowsePath = field.InstanceDeclaration.BrowsePath;
                selectClause.Add(operand);
            }

            return selectClause;
        }
        
        /// <summary>
        /// Returns the where clause defined by the filter declaration.
        /// </summary>
        public ContentFilter GetWhereClause()
        {
            ContentFilter whereClause = new ContentFilter();
            ContentFilterElement element1 = whereClause.Push(FilterOperator.OfType, EventTypeId);

            EventFilter filter = new EventFilter();

            foreach (FilterDeclarationField field in Fields)
            {
                if (field.FilterEnabled)
                {
                    SimpleAttributeOperand operand1 = new SimpleAttributeOperand();
                    operand1.TypeDefinitionId = field.InstanceDeclaration.RootTypeId;
                    operand1.AttributeId = (field.InstanceDeclaration.NodeClass == NodeClass.Object) ? Attributes.NodeId : Attributes.Value;
                    operand1.BrowsePath = field.InstanceDeclaration.BrowsePath;

                    LiteralOperand operand2 = new LiteralOperand();
                    operand2.Value = field.FilterValue;

                    ContentFilterElement element2 = whereClause.Push(field.FilterOperator, operand1, operand2);
                    element1 = whereClause.Push(FilterOperator.And, element1, element2);
                }
            }

            return whereClause;
        }

        /// <summary>
        /// Returns the value for the specified browse name.
        /// </summary>
        public T GetValue<T>(QualifiedName browseName, VariantCollection fields, T defaultValue)
        {
            if (fields == null || fields.Count == 0)
            {
                return defaultValue;
            }

            for (int ii = 0; ii < this.Fields.Count; ii++)
            {
                if (this.Fields[ii].InstanceDeclaration.BrowseName == browseName)
                {
                    if (ii >= fields.Count+1)
                    {
                        return defaultValue;
                    }

                    object value = fields[ii+1].Value;

                    if (typeof(T).IsInstanceOfType(value))
                    {
                        return (T)value;
                    }

                    break;
                }
            }

            return defaultValue;
        }

        public NodeId EventTypeId;
        public List<FilterDeclarationField> Fields;
    }
}
