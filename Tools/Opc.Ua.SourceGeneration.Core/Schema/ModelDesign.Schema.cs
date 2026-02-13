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
#nullable enable

using System.Linq;
using System.Xml;

namespace Opc.Ua.Schema.Model
{
    /// <summary>
    /// Serializable record for ModelDesign
    /// </summary>
    internal sealed record ModelDesignJson(
        Namespace[]? Namespaces,
        RolePermissionSet[]? PermissionSets,
        NodeDesignJson[]? Items,
        XmlElement[]? Extensions,
        string? TargetNamespace,
        string? TargetVersion,
        System.DateTime? TargetPublicationDate,
        string? TargetXmlNamespace,
        string? DefaultLocale = "en")
    {
        /// <summary>
        /// Converts this record to a <see cref="ModelDesign"/>.
        /// </summary>
        public ModelDesign ToModelDesign()
        {
            var model = new ModelDesign
            {
                TargetNamespace = TargetNamespace,
                TargetVersion = TargetVersion,
                TargetXmlNamespace = TargetXmlNamespace,
                DefaultLocale = DefaultLocale,
                PermissionSets = PermissionSets,
                Extensions = Extensions
            };

            if (TargetPublicationDate.HasValue)
            {
                model.TargetPublicationDate = TargetPublicationDate.Value;
                model.TargetPublicationDateSpecified = true;
            }

            if (Namespaces != null)
            {
                model.Namespaces = Namespaces;
            }

            if (Items != null)
            {
                model.Items = [.. Items.Select(i => i.ToNodeDesign())];
            }

            return model;
        }
    }

    /// <summary>
    /// Serializable record for Parameter
    /// </summary>
    internal sealed record ParameterJson(
        LocalizedText? Description,
        XmlElement? DefaultValue,
        LocalizedText? DisplayName,
        string? Name,
        decimal? Identifier,
        string? BitMask,
        XmlQualifiedName? DataType,
        ValueRank ValueRank,
        string? ArrayDimensions,
        bool AllowSubTypes,
        bool IsOptional,
        ReleaseStatus ReleaseStatus)
    {
        /// <summary>
        /// Converts this record to a <see cref="Parameter"/>.
        /// </summary>
        public Parameter ToParameter()
        {
            var param = new Parameter
            {
                Name = Name,
                Description = Description,
                DefaultValue = DefaultValue,
                DisplayName = DisplayName,
                BitMask = BitMask,
                DataType = DataType,
                ArrayDimensions = ArrayDimensions
            };

            if (Identifier.HasValue)
            {
                param.Identifier = Identifier.Value;
                param.IdentifierSpecified = true;
            }

            param.ValueRank = ValueRank;
            param.AllowSubTypes = AllowSubTypes;
            param.IsOptional = IsOptional;
            param.ReleaseStatus = ReleaseStatus;

            return param;
        }
    }

    /// <summary>
    /// Serializable record for NodeDesign with nullable Specified properties.
    /// </summary>
    internal record NodeDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType)
    {
        /// <summary>
        /// Converts this record to a <see cref="NodeDesign"/>.
        /// </summary>
        public virtual NodeDesign ToNodeDesign()
        {
            var node = new NodeDesign();
            ApplyTo(node);
            return node;
        }

        /// <summary>
        /// Applies the properties of this record to the given node.
        /// </summary>
        protected void ApplyTo(NodeDesign node)
        {
            node.BrowseName = BrowseName;
            node.DisplayName = DisplayName;
            node.Description = Description;
            node.Children = Children?.ToListOfChildren();
            node.References = References;
            node.RolePermissions = RolePermissions;
            node.DefaultRolePermissions = DefaultRolePermissions;
            node.Extensions = Extensions;
            node.SymbolicName = SymbolicName;
            node.SymbolicId = SymbolicId;
            node.StringId = StringId;
            node.Category = Category;

            if (AccessRestrictions.HasValue)
            {
                node.AccessRestrictions = AccessRestrictions.Value;
                node.AccessRestrictionsSpecified = true;
            }

            if (DefaultAccessRestrictions.HasValue)
            {
                node.DefaultAccessRestrictions = DefaultAccessRestrictions.Value;
                node.DefaultAccessRestrictionsSpecified = true;
            }

            node.IsDeclaration = IsDeclaration;

            if (NumericId.HasValue)
            {
                node.NumericId = NumericId.Value;
                node.NumericIdSpecified = true;
            }

            node.WriteAccess = WriteAccess;
            node.PartNo = PartNo;
            node.NotInAddressSpace = NotInAddressSpace;
            node.ReleaseStatus = ReleaseStatus;
            node.Purpose = Purpose;
            node.IsDynamic = IsDynamic;
        }
    }

    /// <summary>
    /// Serializable record for TypeDesign.
    /// </summary>
    internal record TypeDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        string? ClassName,
        XmlQualifiedName? BaseType,
        bool IsAbstract,
        bool NoClassGeneration
    ) : NodeDesignJson(
        BrowseName, DisplayName, Description, Children, References, RolePermissions,
        DefaultRolePermissions, AccessRestrictions, DefaultAccessRestrictions, Extensions,
        SymbolicName, SymbolicId, IsDeclaration, NumericId, StringId, WriteAccess, PartNo,
        Category, NotInAddressSpace, ReleaseStatus, Purpose, IsDynamic, NodeType
    )
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var type = new TypeDesign();
            ApplyTo(type);
            return type;
        }

        /// <summary>
        /// Applies the properties of this record to the given type design.
        /// </summary>
        protected void ApplyTo(TypeDesign type)
        {
            base.ApplyTo(type);
            type.ClassName = ClassName;
            type.BaseType = BaseType;
            type.IsAbstract = IsAbstract;
            type.NoClassGeneration = NoClassGeneration;
        }
    }

    /// <summary>
    /// Serializable record for InstanceDesign with nullable ModellingRule.
    /// </summary>
    internal record InstanceDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        XmlQualifiedName? ReferenceType,
        XmlQualifiedName? Declaration,
        XmlQualifiedName? TypeDefinition,
        ModellingRule? ModellingRule,
        uint MinCardinality,
        uint MaxCardinality,
        bool PreserveDefaultAttributes,
        bool DesignToolOnly) :
        NodeDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var instance = new InstanceDesign();
            ApplyTo(instance);
            return instance;
        }

        /// <summary>
        /// Converts this record to an <see cref="InstanceDesign"/>.
        /// </summary>
        public virtual InstanceDesign ToInstanceDesign()
        {
            var instance = new InstanceDesign();
            ApplyTo(instance);
            return instance;
        }

        /// <summary>
        /// Applies the properties of this record to the given instance design.
        /// </summary>
        protected void ApplyTo(InstanceDesign instance)
        {
            base.ApplyTo(instance);
            instance.ReferenceType = ReferenceType;
            instance.Declaration = Declaration;
            instance.TypeDefinition = TypeDefinition;

            if (ModellingRule.HasValue)
            {
                instance.ModellingRule = ModellingRule.Value;
                instance.ModellingRuleSpecified = true;
            }

            instance.MinCardinality = MinCardinality;
            instance.MaxCardinality = MaxCardinality;
            instance.PreserveDefaultAttributes = PreserveDefaultAttributes;
            instance.DesignToolOnly = DesignToolOnly;
        }
    }

    /// <summary>
    /// Serializable record for ObjectTypeDesign with nullable SupportsEvents.
    /// </summary>
    internal sealed record ObjectTypeDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        string? ClassName,
        XmlQualifiedName? BaseType,
        bool IsAbstract,
        bool NoClassGeneration,
        bool? SupportsEvents) :
        TypeDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ClassName,
            BaseType,
            IsAbstract,
            NoClassGeneration)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var objectType = new ObjectTypeDesign();
            ApplyTo(objectType);

            if (SupportsEvents.HasValue)
            {
                objectType.SupportsEvents = SupportsEvents.Value;
                objectType.SupportsEventsSpecified = true;
            }

            return objectType;
        }
    }

    /// <summary>
    /// Serializable record for VariableTypeDesign with nullable Specified properties.
    /// </summary>
    internal sealed record VariableTypeDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        string? ClassName,
        XmlQualifiedName? BaseType,
        bool IsAbstract,
        bool NoClassGeneration,
        XmlElement? DefaultValue,
        XmlQualifiedName? DataType,
        ValueRank? ValueRank,
        string? ArrayDimensions,
        AccessLevel? AccessLevel,
        int? MinimumSamplingInterval,
        bool? Historizing,
        bool ExposesItsChildren) :
        TypeDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ClassName,
            BaseType,
            IsAbstract,
            NoClassGeneration)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var variableType = new VariableTypeDesign();
            ApplyTo(variableType);

            variableType.DefaultValue = DefaultValue;
            variableType.DataType = DataType;
            variableType.ArrayDimensions = ArrayDimensions;

            if (ValueRank.HasValue)
            {
                variableType.ValueRank = ValueRank.Value;
                variableType.ValueRankSpecified = true;
            }

            if (AccessLevel.HasValue)
            {
                variableType.AccessLevel = AccessLevel.Value;
                variableType.AccessLevelSpecified = true;
            }

            if (MinimumSamplingInterval.HasValue)
            {
                variableType.MinimumSamplingInterval = MinimumSamplingInterval.Value;
                variableType.MinimumSamplingIntervalSpecified = true;
            }

            if (Historizing.HasValue)
            {
                variableType.Historizing = Historizing.Value;
                variableType.HistorizingSpecified = true;
            }

            variableType.ExposesItsChildren = ExposesItsChildren;

            return variableType;
        }
    }

    /// <summary>
    /// Serializable record for ReferenceTypeDesign with nullable Symmetric.
    /// </summary>
    internal sealed record ReferenceTypeDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        string? ClassName,
        XmlQualifiedName? BaseType,
        bool IsAbstract,
        bool NoClassGeneration,
        LocalizedText? InverseName,
        bool? Symmetric) :
        TypeDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ClassName,
            BaseType,
            IsAbstract,
            NoClassGeneration)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var referenceType = new ReferenceTypeDesign();
            ApplyTo(referenceType);

            referenceType.InverseName = InverseName;

            if (Symmetric.HasValue)
            {
                referenceType.Symmetric = Symmetric.Value;
                referenceType.SymmetricSpecified = true;
            }

            return referenceType;
        }
    }

    /// <summary>
    /// Serializable record for DataTypeDesign.
    /// </summary>
    internal sealed record DataTypeDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        string? ClassName,
        XmlQualifiedName? BaseType,
        bool IsAbstract,
        bool NoClassGeneration,
        ParameterJson[]? Fields,
        EncodingDesignJson[]? Encodings,
        bool IsOptionSet,
        bool IsUnion,
        bool NoArraysAllowed,
        bool ForceEnumValues,
        bool NoEncodings) :
        TypeDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ClassName,
            BaseType,
            IsAbstract,
            NoClassGeneration)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var dataType = new DataTypeDesign();
            ApplyTo(dataType);

            if (Fields != null)
            {
                dataType.Fields = [.. Fields
                    .Select(f => f.ToParameter())];
            }

            if (Encodings != null)
            {
                dataType.Encodings = [.. Encodings
                    .Select(e => (EncodingDesign)e.ToNodeDesign())];
            }

            dataType.IsOptionSet = IsOptionSet;
            dataType.IsUnion = IsUnion;
            dataType.NoArraysAllowed = NoArraysAllowed;
            dataType.ForceEnumValues = ForceEnumValues;
            dataType.NoEncodings = NoEncodings;

            return dataType;
        }
    }

    /// <summary>
    /// Serializable record for ObjectDesign with nullable SupportsEvents.
    /// </summary>
    internal record ObjectDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        XmlQualifiedName? ReferenceType,
        XmlQualifiedName? Declaration,
        XmlQualifiedName? TypeDefinition,
        ModellingRule? ModellingRule,
        uint MinCardinality,
        uint MaxCardinality,
        bool PreserveDefaultAttributes,
        bool DesignToolOnly,
        bool? SupportsEvents) :
        InstanceDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ReferenceType,
            Declaration,
            TypeDefinition,
            ModellingRule,
            MinCardinality,
            MaxCardinality,
            PreserveDefaultAttributes,
            DesignToolOnly)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var obj = new ObjectDesign();
            ApplyTo(obj);
            return obj;
        }

        /// <inheritdoc/>
        public override InstanceDesign ToInstanceDesign()
        {
            return (ObjectDesign)ToNodeDesign();
        }

        /// <summary>
        /// Applies the properties of this record to the given object design.
        /// </summary>
        protected void ApplyTo(ObjectDesign obj)
        {
            base.ApplyTo(obj);

            if (SupportsEvents.HasValue)
            {
                obj.SupportsEvents = SupportsEvents.Value;
                obj.SupportsEventsSpecified = true;
            }
        }
    }

    /// <summary>
    /// Serializable record for EncodingDesign.
    /// </summary>
    internal sealed record EncodingDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        XmlQualifiedName? ReferenceType,
        XmlQualifiedName? Declaration,
        XmlQualifiedName? TypeDefinition,
        ModellingRule? ModellingRule,
        uint MinCardinality,
        uint MaxCardinality,
        bool PreserveDefaultAttributes,
        bool DesignToolOnly,
        bool? SupportsEvents) :
        ObjectDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ReferenceType,
            Declaration,
            TypeDefinition,
            ModellingRule,
            MinCardinality,
            MaxCardinality,
            PreserveDefaultAttributes,
            DesignToolOnly,
            SupportsEvents)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var encoding = new EncodingDesign();
            ApplyTo(encoding);
            return encoding;
        }
    }

    /// <summary>
    /// Serializable record for VariableDesign
    /// </summary>
    internal record VariableDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        XmlQualifiedName? ReferenceType,
        XmlQualifiedName? Declaration,
        XmlQualifiedName? TypeDefinition,
        ModellingRule? ModellingRule,
        uint MinCardinality,
        uint MaxCardinality,
        bool PreserveDefaultAttributes,
        bool DesignToolOnly,
        XmlElement? DefaultValue,
        XmlQualifiedName? DataType,
        ValueRank? ValueRank,
        string? ArrayDimensions,
        AccessLevel? AccessLevel,
        AccessLevel? InstanceAccessLevel,
        int? MinimumSamplingInterval,
        bool? Historizing) :
        InstanceDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ReferenceType,
            Declaration,
            TypeDefinition,
            ModellingRule,
            MinCardinality,
            MaxCardinality,
            PreserveDefaultAttributes,
            DesignToolOnly)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var variable = new VariableDesign();
            ApplyTo(variable);
            return variable;
        }

        /// <inheritdoc/>
        public override InstanceDesign ToInstanceDesign()
        {
            return (VariableDesign)ToNodeDesign();
        }

        /// <summary>
        /// Applies the properties of this record to the given variable design.
        /// </summary>
        protected void ApplyTo(VariableDesign variable)
        {
            base.ApplyTo(variable);

            variable.DefaultValue = DefaultValue;
            variable.DataType = DataType;
            variable.ArrayDimensions = ArrayDimensions;

            if (ValueRank.HasValue)
            {
                variable.ValueRank = ValueRank.Value;
                variable.ValueRankSpecified = true;
            }

            if (AccessLevel.HasValue)
            {
                variable.AccessLevel = AccessLevel.Value;
                variable.AccessLevelSpecified = true;
            }

            if (InstanceAccessLevel.HasValue)
            {
                variable.InstanceAccessLevel = InstanceAccessLevel.Value;
                variable.InstanceAccessLevelSpecified = true;
            }

            if (MinimumSamplingInterval.HasValue)
            {
                variable.MinimumSamplingInterval = MinimumSamplingInterval.Value;
                variable.MinimumSamplingIntervalSpecified = true;
            }

            if (Historizing.HasValue)
            {
                variable.Historizing = Historizing.Value;
                variable.HistorizingSpecified = true;
            }
        }
    }

    /// <summary>
    /// Serializable record for PropertyDesign.
    /// </summary>
    internal sealed record PropertyDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        XmlQualifiedName? ReferenceType,
        XmlQualifiedName? Declaration,
        XmlQualifiedName? TypeDefinition,
        ModellingRule? ModellingRule,
        uint MinCardinality,
        uint MaxCardinality,
        bool PreserveDefaultAttributes,
        bool DesignToolOnly,
        XmlElement? DefaultValue,
        XmlQualifiedName? DataType,
        ValueRank? ValueRank,
        string? ArrayDimensions,
        AccessLevel? AccessLevel,
        AccessLevel? InstanceAccessLevel,
        int? MinimumSamplingInterval,
        bool? Historizing) :
        VariableDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ReferenceType,
            Declaration,
            TypeDefinition,
            ModellingRule,
            MinCardinality,
            MaxCardinality,
            PreserveDefaultAttributes,
            DesignToolOnly,
            DefaultValue,
            DataType,
            ValueRank,
            ArrayDimensions,
            AccessLevel,
            InstanceAccessLevel,
            MinimumSamplingInterval,
            Historizing)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var property = new PropertyDesign();
            ApplyTo(property);
            return property;
        }
    }

    /// <summary>
    /// Serializable record for DictionaryDesign.
    /// </summary>
    internal sealed record DictionaryDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        XmlQualifiedName? ReferenceType,
        XmlQualifiedName? Declaration,
        XmlQualifiedName? TypeDefinition,
        ModellingRule? ModellingRule,
        uint MinCardinality,
        uint MaxCardinality,
        bool PreserveDefaultAttributes,
        bool DesignToolOnly,
        XmlElement? DefaultValue,
        XmlQualifiedName? DataType,
        ValueRank? ValueRank,
        string? ArrayDimensions,
        AccessLevel? AccessLevel,
        AccessLevel? InstanceAccessLevel,
        int? MinimumSamplingInterval,
        bool? Historizing,
        XmlQualifiedName? EncodingName) :
        VariableDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ReferenceType,
            Declaration,
            TypeDefinition,
            ModellingRule,
            MinCardinality,
            MaxCardinality,
            PreserveDefaultAttributes,
            DesignToolOnly,
            DefaultValue,
            DataType,
            ValueRank,
            ArrayDimensions,
            AccessLevel,
            InstanceAccessLevel,
            MinimumSamplingInterval,
            Historizing)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var dictionary = new DictionaryDesign();
            ApplyTo(dictionary);
            dictionary.EncodingName = EncodingName;
            return dictionary;
        }
    }

    /// <summary>
    /// Serializable record for MethodDesign with nullable NonExecutable.
    /// </summary>
    internal sealed record MethodDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        XmlQualifiedName? ReferenceType,
        XmlQualifiedName? Declaration,
        XmlQualifiedName? TypeDefinition,
        ModellingRule? ModellingRule,
        uint MinCardinality,
        uint MaxCardinality,
        bool PreserveDefaultAttributes,
        bool DesignToolOnly,
        ParameterJson[]? InputArguments,
        ParameterJson[]? OutputArguments,
        bool? NonExecutable) :
        InstanceDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ReferenceType,
            Declaration,
            TypeDefinition,
            ModellingRule,
            MinCardinality,
            MaxCardinality,
            PreserveDefaultAttributes,
            DesignToolOnly)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var method = new MethodDesign();
            ApplyTo(method);

            if (InputArguments != null)
            {
                method.InputArguments = [.. InputArguments
                    .Select(a => a.ToParameter())];
            }

            if (OutputArguments != null)
            {
                method.OutputArguments = [.. OutputArguments
                    .Select(a => a.ToParameter())];
            }

            if (NonExecutable.HasValue)
            {
                method.NonExecutable = NonExecutable.Value;
                method.NonExecutableSpecified = true;
            }

            return method;
        }

        /// <inheritdoc/>
        public override InstanceDesign ToInstanceDesign()
        {
            return (MethodDesign)ToNodeDesign();
        }
    }

    /// <summary>
    /// Serializable record for ViewDesign.
    /// </summary>
    internal sealed record ViewDesignJson(
        string? BrowseName,
        LocalizedText? DisplayName,
        LocalizedText? Description,
        ListOfChildrenJson? Children,
        Reference[]? References,
        RolePermissionSet? RolePermissions,
        RolePermissionSet? DefaultRolePermissions,
        AccessRestrictions? AccessRestrictions,
        AccessRestrictions? DefaultAccessRestrictions,
        XmlElement[]? Extensions,
        XmlQualifiedName? SymbolicName,
        XmlQualifiedName? SymbolicId,
        bool IsDeclaration,
        uint? NumericId,
        string? StringId,
        uint WriteAccess,
        uint PartNo,
        string? Category,
        bool NotInAddressSpace,
        ReleaseStatus ReleaseStatus,
        DataTypePurpose Purpose,
        bool IsDynamic,
        string? NodeType,
        XmlQualifiedName? ReferenceType,
        XmlQualifiedName? Declaration,
        XmlQualifiedName? TypeDefinition,
        ModellingRule? ModellingRule,
        uint MinCardinality,
        uint MaxCardinality,
        bool PreserveDefaultAttributes,
        bool DesignToolOnly,
        bool SupportsEvents,
        bool ContainsNoLoops) :
        InstanceDesignJson(
            BrowseName,
            DisplayName,
            Description,
            Children,
            References,
            RolePermissions,
            DefaultRolePermissions,
            AccessRestrictions,
            DefaultAccessRestrictions,
            Extensions,
            SymbolicName,
            SymbolicId,
            IsDeclaration,
            NumericId,
            StringId,
            WriteAccess,
            PartNo,
            Category,
            NotInAddressSpace,
            ReleaseStatus,
            Purpose,
            IsDynamic,
            NodeType,
            ReferenceType,
            Declaration,
            TypeDefinition,
            ModellingRule,
            MinCardinality,
            MaxCardinality,
            PreserveDefaultAttributes,
            DesignToolOnly)
    {
        /// <inheritdoc/>
        public override NodeDesign ToNodeDesign()
        {
            var view = new ViewDesign();
            ApplyTo(view);

            view.SupportsEvents = SupportsEvents;
            view.ContainsNoLoops = ContainsNoLoops;

            return view;
        }

        /// <inheritdoc/>
        public override InstanceDesign ToInstanceDesign()
        {
            return (ViewDesign)ToNodeDesign();
        }
    }

    /// <summary>
    /// Serializable record for ListOfChildren.
    /// </summary>
    internal sealed record ListOfChildrenJson(InstanceDesignJson[]? Items)
    {
        /// <summary>
        /// Converts this record to a <see cref="ListOfChildren"/>.
        /// </summary>
        public ListOfChildren? ToListOfChildren()
        {
            if (Items == null)
            {
                return null;
            }

            return new ListOfChildren
            {
                Items = [.. Items.Select(i => i.ToInstanceDesign())]
            };
        }
    }
}
