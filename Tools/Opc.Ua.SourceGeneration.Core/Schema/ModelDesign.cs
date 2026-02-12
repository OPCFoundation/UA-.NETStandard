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
using System.Xml;
using System.Xml.Serialization;

namespace Opc.Ua.Schema.Model
{
    public partial class Namespace : IEquatable<Namespace>
    {
        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is Namespace other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(Namespace other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                Name == other.Name &&
                Prefix == other.Prefix &&
                InternalPrefix == other.InternalPrefix &&
                XmlNamespace == other.XmlNamespace &&
                XmlPrefix == other.XmlPrefix &&
                FilePath == other.FilePath &&
                Version == other.Version &&
                PublicationDate == other.PublicationDate &&
                Value == other.Value;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Name);
            hash.Add(Prefix);
            hash.Add(InternalPrefix);
            hash.Add(XmlNamespace);
            hash.Add(XmlPrefix);
            hash.Add(FilePath);
            hash.Add(Version);
            hash.Add(PublicationDate);
            hash.Add(Value);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(Namespace left, Namespace right)
        {
            return EqualityComparer<Namespace>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(Namespace left, Namespace right)
        {
            return !(left == right);
        }
    }

    public partial class ListOfChildren : IEquatable<ListOfChildren>
    {
        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is ListOfChildren other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(ListOfChildren other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            if (other is null)
            {
                return false;
            }
            if (other.Items is null || Items is null)
            {
                return other.Items == Items;
            }
            if (other.Items.Length != Items.Length)
            {
                return false;
            }
            // Only compare symbolicid to prevent circular loops
            for (int ii = 0; ii < Items.Length; ii++)
            {
                if (!XmlQualifiedNameEqualityComparer.Default.Equals(
                    Items[ii].SymbolicId,
                    other.Items[ii].SymbolicId))
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (Items is null || Items.Length == 0)
            {
                return 0;
            }
            var hash = new HashCode();
            foreach (InstanceDesign child in Items)
            {
                hash.Add(XmlQualifiedNameEqualityComparer.Default.GetHashCode(child.SymbolicId));
            }
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(ListOfChildren left, ListOfChildren right)
        {
            return EqualityComparer<ListOfChildren>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ListOfChildren left, ListOfChildren right)
        {
            return !(left == right);
        }
    }

    public partial class RolePermissionSet : IEquatable<RolePermissionSet>
    {
        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is RolePermissionSet other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(RolePermissionSet other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is not null &&
                ArrayEqualityComparer<RolePermission>.Default.Equals(RolePermission, other.RolePermission) &&
                Name == other.Name &&
                DoNotInheirit == other.DoNotInheirit;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(RolePermission, ArrayEqualityComparer<RolePermission>.Default);
            hash.Add(Name);
            hash.Add(DoNotInheirit);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(RolePermissionSet left, RolePermissionSet right)
        {
            return EqualityComparer<RolePermissionSet>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(RolePermissionSet left, RolePermissionSet right)
        {
            return !(left == right);
        }
    }

    public partial class RolePermission : IEquatable<RolePermission>
    {
        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is RolePermission other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(RolePermission other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is not null &&
                ArrayEqualityComparer<Permissions>.Default.Equals(Permission, other.Permission) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(Role, other.Role);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Permission, ArrayEqualityComparer<Permissions>.Default);
            hash.Add(Role, XmlQualifiedNameEqualityComparer.Default);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(RolePermission left, RolePermission right)
        {
            return EqualityComparer<RolePermission>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(RolePermission left, RolePermission right)
        {
            return !(left == right);
        }
    }

    public partial class ReferenceTypeDesign : IEquatable<ReferenceTypeDesign>
    {
        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is ReferenceTypeDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(ReferenceTypeDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                base.Equals(other) &&
                EqualityComparer<LocalizedText>.Default.Equals(InverseName, other.InverseName) &&
                Symmetric == other.Symmetric &&
                SymmetricSpecified == other.SymmetricSpecified;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), InverseName, Symmetric, SymmetricSpecified);
        }

        /// <inheritdoc/>
        public static bool operator ==(ReferenceTypeDesign left, ReferenceTypeDesign right)
        {
            return EqualityComparer<ReferenceTypeDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ReferenceTypeDesign left, ReferenceTypeDesign right)
        {
            return !(left == right);
        }
    }

    public partial class EncodingDesign : IEquatable<EncodingDesign>
    {
        /// <inheritdoc/>
        public bool Equals(EncodingDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is not null && base.Equals(other);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is EncodingDesign other && base.Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    public partial class ObjectDesign : IEquatable<ObjectDesign>
    {
        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is ObjectDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(ObjectDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is not null &&
                base.Equals(other) &&
                SupportsEvents == other.SupportsEvents &&
                SupportsEventsSpecified == other.SupportsEventsSpecified;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), SupportsEvents, SupportsEventsSpecified);
        }

        /// <inheritdoc/>
        public static bool operator ==(ObjectDesign left, ObjectDesign right)
        {
            return EqualityComparer<ObjectDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ObjectDesign left, ObjectDesign right)
        {
            return !(left == right);
        }
    }

    public partial class ObjectTypeDesign : IEquatable<ObjectTypeDesign>
    {
        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is ObjectTypeDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(ObjectTypeDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is not null &&
                base.Equals(other) &&
                SupportsEvents == other.SupportsEvents &&
                SupportsEventsSpecified == other.SupportsEventsSpecified;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), SupportsEvents, SupportsEventsSpecified);
        }

        /// <inheritdoc/>
        public static bool operator ==(ObjectTypeDesign left, ObjectTypeDesign right)
        {
            return EqualityComparer<ObjectTypeDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ObjectTypeDesign left, ObjectTypeDesign right)
        {
            return !(left == right);
        }
    }

    public partial class DictionaryDesign : IEquatable<DictionaryDesign>
    {
        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is DictionaryDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(DictionaryDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                base.Equals(other) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(EncodingName, other.EncodingName);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.GetHashCode());
            hash.Add(EncodingName, XmlQualifiedNameEqualityComparer.Default);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(DictionaryDesign left, DictionaryDesign right)
        {
            return EqualityComparer<DictionaryDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(DictionaryDesign left, DictionaryDesign right)
        {
            return !(left == right);
        }
    }

    public partial class PropertyDesign : IEquatable<PropertyDesign>
    {
        /// <inheritdoc/>
        public bool Equals(PropertyDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is not null && base.Equals(other);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is PropertyDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    public partial class ViewDesign : IEquatable<ViewDesign>
    {
        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is ViewDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(ViewDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is not null &&
                base.Equals(other) &&
                SupportsEvents == other.SupportsEvents &&
                ContainsNoLoops == other.ContainsNoLoops;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), SupportsEvents, ContainsNoLoops);
        }

        /// <inheritdoc/>
        public static bool operator ==(ViewDesign left, ViewDesign right)
        {
            return EqualityComparer<ViewDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ViewDesign left, ViewDesign right)
        {
            return !(left == right);
        }
    }

    public partial class ModelDesign : IEquatable<ModelDesign>
    {
        /// <summary>
        /// Namespace table
        /// </summary>
        [XmlIgnore]
#pragma warning disable CA2235 // Mark all non-serializable fields
        public NamespaceTable NamespaceUris { get; set; }
#pragma warning restore CA2235 // Mark all non-serializable fields

        /// <summary>
        /// Source file path
        /// </summary>
        [XmlIgnore]
        public string SourceFilePath { get; set; }

        /// <summary>
        /// Is source node set
        /// </summary>
        [XmlIgnore]
        public bool IsSourceNodeSet { get; set; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is ModelDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(ModelDesign design)
        {
            if (ReferenceEquals(this, design))
            {
                return true;
            }
            return
                ArrayEqualityComparer<Namespace>.Default.Equals(Namespaces, design.Namespaces) &&
                ArrayEqualityComparer<RolePermissionSet>.Default.Equals(PermissionSets, design.PermissionSets) &&
                ArrayEqualityComparer<NodeDesign>.Default.Equals(Items, design.Items) &&
                XmlElementArrayStringEqualityComparer.Default.Equals(Extensions, design.Extensions) &&
                TargetNamespace == design.TargetNamespace &&
                TargetVersion == design.TargetVersion &&
                TargetPublicationDate == design.TargetPublicationDate &&
                TargetXmlNamespace == design.TargetXmlNamespace &&
                DefaultLocale == design.DefaultLocale;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Namespaces, ArrayEqualityComparer<Namespace>.Default);
            hash.Add(PermissionSets, ArrayEqualityComparer<RolePermissionSet>.Default);
            hash.Add(Items, ArrayEqualityComparer<NodeDesign>.Default);
            hash.Add(Extensions, XmlElementArrayStringEqualityComparer.Default);
            hash.Add(TargetNamespace);
            hash.Add(TargetVersion);
            hash.Add(TargetPublicationDate);
            hash.Add(TargetXmlNamespace);
            hash.Add(DefaultLocale);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(ModelDesign left, ModelDesign right)
        {
            return EqualityComparer<ModelDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ModelDesign left, ModelDesign right)
        {
            return !(left == right);
        }
    }

    public partial class LocalizedText : IEquatable<LocalizedText>
    {
        /// <summary>
        /// Text is autogenerated
        /// </summary>
        [XmlIgnore]
        public bool IsAutogenerated { get; set; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is LocalizedText other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(LocalizedText other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is not null &&
                Key == other.Key &&
                DoNotIgnore == other.DoNotIgnore &&
                Value == other.Value &&
                IsAutogenerated == other.IsAutogenerated;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Key, DoNotIgnore, Value, IsAutogenerated);
        }

        /// <inheritdoc/>
        public static bool operator ==(LocalizedText left, LocalizedText right)
        {
            return EqualityComparer<LocalizedText>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(LocalizedText left, LocalizedText right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Hierarchy node information
    /// </summary>
    public record class HierarchyNode
    {
        /// <summary>
        /// Relative path
        /// </summary>
        public string RelativePath { get; set; }

        /// <summary>
        /// Instance
        /// </summary>
        public NodeDesign Instance { get; set; }

        /// <summary>
        /// Overridden nodes
        /// </summary>
        public List<NodeDesign> OverriddenNodes { get; set; }

        /// <summary>
        /// Explicitly defined
        /// </summary>
        public bool ExplicitlyDefined { get; set; }

        /// <summary>
        /// Ad hoc instance
        /// </summary>
        public bool AdHocInstance { get; set; }

        /// <summary>
        /// Static value
        /// </summary>
        public bool StaticValue { get; set; }

        /// <summary>
        /// Inherited
        /// </summary>
        public bool Inherited { get; set; }

        /// <summary>
        /// Identifier
        /// </summary>
        public object Identifier { get; set; }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public override string ToString()
        {
            if (Instance != null && Instance.SymbolicId != null)
            {
                return CoreUtils.Format("{0}={1}", RelativePath, Instance.SymbolicId.Name);
            }
            return RelativePath;
        }
    }

    /// <summary>
    /// Reference between hierarchy nodes
    /// </summary>
    public record class HierarchyReference
    {
        /// <summary>
        /// Source path
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// reference type
        /// </summary>
        public XmlQualifiedName ReferenceType { get; set; }

        /// <summary>
        /// Is inverse
        /// </summary>
        public bool IsInverse { get; set; }

        /// <summary>
        /// Target path
        /// </summary>
        public string TargetPath { get; set; }

        /// <summary>
        /// Target id
        /// </summary>
        public XmlQualifiedName TargetId { get; set; }

        /// <summary>
        /// Defined on type
        /// </summary>
        public bool DefinedOnType { get; set; }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public override string ToString()
        {
            if (TargetId != null)
            {
                return CoreUtils.Format("{0} => {1}", SourcePath, TargetId.Name);
            }

            return CoreUtils.Format("{0} => {1}", SourcePath, TargetPath);
        }
    }

    /// <summary>
    /// Hierarchy information for a type or instance.
    /// </summary>
    public record class Hierarchy
    {
        /// <summary>
        /// Type
        /// </summary>
        public TypeDesign Type { get; set; }

        /// <summary>
        /// Nodes
        /// </summary>
        public Dictionary<string, HierarchyNode> Nodes { get; } = [];

        /// <summary>
        /// Node list
        /// </summary>
        public List<HierarchyNode> NodeList { get; } = [];

        /// <summary>
        /// References
        /// </summary>
        public List<HierarchyReference> References { get; set; } = [];
    }

    /// <summary>
    /// A class that stores the model design for a Node.
    /// </summary>
    public partial class NodeDesign : IFormattable, IEquatable<NodeDesign>
    {
        /// <summary>
        /// Parent node
        /// </summary>
        [XmlIgnore]
        public NodeDesign Parent { get; set; }

        /// <summary>
        /// Has children
        /// </summary>
        [XmlIgnore]
        public bool HasChildren { get; set; }

        /// <summary>
        /// Has references
        /// </summary>
        [XmlIgnore]
        public bool HasReferences { get; set; }

        /// <summary>
        /// Hierarchy
        /// </summary>
        [XmlIgnore]
#pragma warning disable CA2235 // Mark all non-serializable fields
        public Hierarchy Hierarchy { get; set; }
#pragma warning restore CA2235 // Mark all non-serializable fields

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                if (SymbolicName != null)
                {
                    return string.Format(formatProvider, "{0}", SymbolicName.Name);
                }

                return string.Format(formatProvider, "{0}", GetType().Name);
            }

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Create symbol
        /// </summary>
        public static string CreateSymbolicId(XmlQualifiedName parentId, string childName)
        {
            if (parentId == null)
            {
                return childName;
            }

            return CreateSymbolicId(parentId.Name, childName);
        }

        /// <summary>
        /// Create symbol
        /// </summary>
        public static string CreateSymbolicId(string parentId, string childName)
        {
            if (string.IsNullOrEmpty(childName))
            {
                return parentId;
            }

            if (string.IsNullOrEmpty(parentId))
            {
                return childName;
            }

            return CoreUtils.Format("{0}{1}{2}", parentId, PathChar, childName);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is NodeDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(NodeDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                BrowseName == other.BrowseName &&
                EqualityComparer<LocalizedText>.Default.Equals(DisplayName, other.DisplayName) &&
                EqualityComparer<LocalizedText>.Default.Equals(Description, other.Description) &&
                EqualityComparer<ListOfChildren>.Default.Equals(Children, other.Children) &&
                ArrayEqualityComparer<Reference>.Default.Equals(References, other.References) &&
                EqualityComparer<RolePermissionSet>.Default.Equals(RolePermissions, other.RolePermissions) &&
                EqualityComparer<RolePermissionSet>.Default.Equals(DefaultRolePermissions, other.DefaultRolePermissions) &&
                AccessRestrictions == other.AccessRestrictions &&
                AccessRestrictionsSpecified == other.AccessRestrictionsSpecified &&
                DefaultAccessRestrictions == other.DefaultAccessRestrictions &&
                DefaultAccessRestrictionsSpecified == other.DefaultAccessRestrictionsSpecified &&
                XmlElementArrayStringEqualityComparer.Default.Equals(Extensions, other.Extensions) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(SymbolicName, other.SymbolicName) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(SymbolicId, other.SymbolicId) &&
                IsDeclaration == other.IsDeclaration &&
                NumericId == other.NumericId &&
                NumericIdSpecified == other.NumericIdSpecified &&
                StringId == other.StringId &&
                WriteAccess == other.WriteAccess &&
                PartNo == other.PartNo &&
                Category == other.Category &&
                NotInAddressSpace == other.NotInAddressSpace &&
                ReleaseStatus == other.ReleaseStatus &&
                Purpose == other.Purpose &&
                IsDynamic == other.IsDynamic;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(BrowseName);
            hash.Add(DisplayName);
            hash.Add(Description);
            hash.Add(Children);
            hash.Add(References, ArrayEqualityComparer<Reference>.Default);
            hash.Add(RolePermissions);
            hash.Add(DefaultRolePermissions);
            hash.Add(AccessRestrictions);
            hash.Add(AccessRestrictionsSpecified);
            hash.Add(DefaultAccessRestrictions);
            hash.Add(DefaultAccessRestrictionsSpecified);
            hash.Add(Extensions, XmlElementArrayStringEqualityComparer.Default);
            hash.Add(SymbolicName, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(SymbolicId, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(IsDeclaration);
            hash.Add(NumericId);
            hash.Add(NumericIdSpecified);
            hash.Add(StringId);
            hash.Add(WriteAccess);
            hash.Add(PartNo);
            hash.Add(Category);
            hash.Add(NotInAddressSpace);
            hash.Add(ReleaseStatus);
            hash.Add(Purpose);
            hash.Add(IsDynamic);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Path character to use when constructing symbolic ids.
        /// </summary>
        public const char PathChar = '_';

        /// <summary>
        /// Static array for splitting
        /// </summary>
        public static readonly char[] PathChars = [PathChar];

        /// <inheritdoc/>
        public static bool operator ==(NodeDesign left, NodeDesign right)
        {
            return EqualityComparer<NodeDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(NodeDesign left, NodeDesign right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// A class that stores the model design for a Variable.
    /// </summary>
    public partial class VariableDesign : IEquatable<VariableDesign>
    {
        /// <summary>
        /// Decoded value
        /// </summary>
        [XmlIgnore]
        public object DecodedValue { get; set; }

        /// <summary>
        /// Data type node
        /// </summary>
        [XmlIgnore]
        public DataTypeDesign DataTypeNode { get; set; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is VariableDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(VariableDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                base.Equals(other) &&
                XmlElementStringEqualityComparer.Default.Equals(DefaultValue, other.DefaultValue) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(DataType, other.DataType) &&
                ValueRank == other.ValueRank &&
                ArrayDimensions == other.ArrayDimensions &&
                AccessLevel == other.AccessLevel &&
                AccessLevelSpecified == other.AccessLevelSpecified &&
                InstanceAccessLevel == other.InstanceAccessLevel &&
                InstanceAccessLevelSpecified == other.InstanceAccessLevelSpecified &&
                MinimumSamplingInterval == other.MinimumSamplingInterval &&
                MinimumSamplingIntervalSpecified == other.MinimumSamplingIntervalSpecified &&
                Historizing == other.Historizing &&
                HistorizingSpecified == other.HistorizingSpecified;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.GetHashCode());
            hash.Add(DefaultValue, XmlElementStringEqualityComparer.Default);
            hash.Add(DataType, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(ValueRank);
            hash.Add(ArrayDimensions);
            hash.Add(AccessLevel);
            hash.Add(AccessLevelSpecified);
            hash.Add(InstanceAccessLevel);
            hash.Add(InstanceAccessLevelSpecified);
            hash.Add(MinimumSamplingInterval);
            hash.Add(MinimumSamplingIntervalSpecified);
            hash.Add(Historizing);
            hash.Add(HistorizingSpecified);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(VariableDesign left, VariableDesign right)
        {
            return EqualityComparer<VariableDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(VariableDesign left, VariableDesign right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// A class that stores the model design for a VariableType.
    /// </summary>
    public partial class VariableTypeDesign : IEquatable<VariableTypeDesign>
    {
        /// <summary>
        /// Decoded value
        /// </summary>
        [XmlIgnore]
        public object DecodedValue { get; set; }

        /// <summary>
        /// Data type node
        /// </summary>
        [XmlIgnore]
        public DataTypeDesign DataTypeNode { get; set; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is VariableTypeDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(VariableTypeDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                base.Equals(other) &&
                XmlElementStringEqualityComparer.Default.Equals(DefaultValue, other.DefaultValue) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(DataType, other.DataType) &&
                ValueRank == other.ValueRank &&
                ValueRankSpecified == other.ValueRankSpecified &&
                ArrayDimensions == other.ArrayDimensions &&
                AccessLevel == other.AccessLevel &&
                AccessLevelSpecified == other.AccessLevelSpecified &&
                MinimumSamplingInterval == other.MinimumSamplingInterval &&
                MinimumSamplingIntervalSpecified == other.MinimumSamplingIntervalSpecified &&
                Historizing == other.Historizing &&
                HistorizingSpecified == other.HistorizingSpecified &&
                ExposesItsChildren == other.ExposesItsChildren;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.GetHashCode());
            hash.Add(DefaultValue, XmlElementStringEqualityComparer.Default);
            hash.Add(DataType, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(ValueRank);
            hash.Add(ValueRankSpecified);
            hash.Add(ArrayDimensions);
            hash.Add(AccessLevel);
            hash.Add(AccessLevelSpecified);
            hash.Add(MinimumSamplingInterval);
            hash.Add(MinimumSamplingIntervalSpecified);
            hash.Add(Historizing);
            hash.Add(HistorizingSpecified);
            hash.Add(ExposesItsChildren);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(VariableTypeDesign left, VariableTypeDesign right)
        {
            return EqualityComparer<VariableTypeDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(VariableTypeDesign left, VariableTypeDesign right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// A class that stores the model design for a Method.
    /// </summary>
    public partial class MethodDesign : IEquatable<MethodDesign>
    {
        /// <summary>
        /// Has arguments
        /// </summary>
        [XmlIgnore]
        public bool HasArguments { get; set; }

        /// <summary>
        /// Method type node
        /// </summary>
        [XmlIgnore]
        public MethodDesign MethodType { get; set; }

        /// <summary>
        /// Method declaration node
        /// </summary>
        [XmlIgnore]
        public MethodDesign MethodDeclarationNode { get; set; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is MethodDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(MethodDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                base.Equals(other) &&
                ArrayEqualityComparer<Parameter>.Default.Equals(InputArguments, other.InputArguments) &&
                ArrayEqualityComparer<Parameter>.Default.Equals(OutputArguments, other.OutputArguments) &&
                NonExecutable == other.NonExecutable &&
                NonExecutableSpecified == other.NonExecutableSpecified;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.GetHashCode());
            hash.Add(InputArguments, ArrayEqualityComparer<Parameter>.Default);
            hash.Add(OutputArguments, ArrayEqualityComparer<Parameter>.Default);
            hash.Add(NonExecutable);
            hash.Add(NonExecutableSpecified);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(MethodDesign left, MethodDesign right)
        {
            return EqualityComparer<MethodDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(MethodDesign left, MethodDesign right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// A class that stores the model design for a Type.
    /// </summary>
    public partial class TypeDesign : IEquatable<TypeDesign>
    {
        /// <summary>
        /// Base type node
        /// </summary>
        [XmlIgnore]
        public TypeDesign BaseTypeNode { get; set; }

        /// <summary>
        /// Deep copy the type design.
        /// </summary>
        /// <returns></returns>
        public TypeDesign Copy()
        {
            return (TypeDesign)MemberwiseClone();
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is TypeDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(TypeDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                base.Equals(other) &&
                ClassName == other.ClassName &&
                XmlQualifiedNameEqualityComparer.Default.Equals(BaseType, other.BaseType) &&
                IsAbstract == other.IsAbstract &&
                NoClassGeneration == other.NoClassGeneration &&
                EqualityComparer<TypeDesign>.Default.Equals(BaseTypeNode, other.BaseTypeNode);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.GetHashCode());
            hash.Add(ClassName);
            hash.Add(BaseType, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(IsAbstract);
            hash.Add(NoClassGeneration);
            hash.Add(BaseTypeNode);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(TypeDesign left, TypeDesign right)
        {
            return EqualityComparer<TypeDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(TypeDesign left, TypeDesign right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// A class that stores the model design for a Type.
    /// </summary>
    public partial class InstanceDesign : IEquatable<InstanceDesign>
    {
        /// <summary>
        /// Type definition node
        /// </summary>
        [XmlIgnore]
        public TypeDesign TypeDefinitionNode { get; set; }

        /// <summary>
        /// Overidden node
        /// </summary>
        [XmlIgnore]
        public InstanceDesign OveriddenNode { get; set; }

        /// <summary>
        /// Identifier required
        /// </summary>
        [XmlIgnore]
        public bool IdentifierRequired { get; set; }

        /// <summary>
        /// Deep copy the instance design.
        /// </summary>
        public InstanceDesign Copy()
        {
            return (InstanceDesign)MemberwiseClone();
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is InstanceDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(InstanceDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                base.Equals(other) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(ReferenceType, other.ReferenceType) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(Declaration, other.Declaration) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(TypeDefinition, other.TypeDefinition) &&
                ModellingRule == other.ModellingRule &&
                ModellingRuleSpecified == other.ModellingRuleSpecified &&
                MinCardinality == other.MinCardinality &&
                MaxCardinality == other.MaxCardinality &&
                PreserveDefaultAttributes == other.PreserveDefaultAttributes &&
                DesignToolOnly == other.DesignToolOnly &&
                XmlQualifiedNameEqualityComparer.Default.Equals(
                    TypeDefinitionNode?.SymbolicId,
                    other.TypeDefinitionNode?.SymbolicId) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(
                    OveriddenNode?.SymbolicId,
                    other.OveriddenNode?.SymbolicId) &&
                IdentifierRequired == other.IdentifierRequired;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.GetHashCode());
            hash.Add(ReferenceType, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(Declaration, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(TypeDefinition, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(ModellingRule);
            hash.Add(ModellingRuleSpecified);
            hash.Add(MinCardinality);
            hash.Add(MaxCardinality);
            hash.Add(PreserveDefaultAttributes);
            hash.Add(DesignToolOnly);
            hash.Add(TypeDefinitionNode?.SymbolicId, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(OveriddenNode?.SymbolicId, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(IdentifierRequired);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(InstanceDesign left, InstanceDesign right)
        {
            return EqualityComparer<InstanceDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(InstanceDesign left, InstanceDesign right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// A class that stores a reference between nodes.
    /// </summary>
    public partial class Reference : IEquatable<Reference>
    {
        /// <summary>
        /// Source node
        /// </summary>
        [XmlIgnore]
        public NodeDesign SourceNode { get; set; }

        /// <summary>
        /// Source relative path
        /// </summary>
        [XmlIgnore]
#pragma warning disable CA2235 // Mark all non-serializable fields
        public RelativePath SourceRelativePath { get; set; }
#pragma warning restore CA2235 // Mark all non-serializable fields

        /// <summary>
        /// Target node
        /// </summary>
        [XmlIgnore]
        public NodeDesign TargetNode { get; set; }

        /// <summary>
        /// Target relative path
        /// </summary>
        [XmlIgnore]
#pragma warning disable CA2235 // Mark all non-serializable fields
        public RelativePath TargetRelativePath { get; set; }
#pragma warning restore CA2235 // Mark all non-serializable fields

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is Reference other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(Reference other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return
                other is not null &&
                XmlQualifiedNameEqualityComparer.Default.Equals(ReferenceType, other.ReferenceType) &&
                XmlQualifiedNameEqualityComparer.Default.Equals(TargetId, other.TargetId) &&
                IsInverse == other.IsInverse &&
                IsOneWay == other.IsOneWay;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ReferenceType, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(TargetId, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(IsInverse);
            hash.Add(IsOneWay);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(Reference left, Reference right)
        {
            return EqualityComparer<Reference>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(Reference left, Reference right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// A class that stores a parameter for a node.
    /// </summary>
    public partial class Parameter : IEquatable<Parameter>
    {
        /// <summary>
        /// Parent node
        /// </summary>
        [XmlIgnore]
        public NodeDesign Parent { get; set; }

        /// <summary>
        /// Data type node
        /// </summary>
        [XmlIgnore]
        public DataTypeDesign DataTypeNode { get; set; }

        /// <summary>
        /// Identifier is in name
        /// </summary>
        [XmlIgnore]
        public bool IdentifierInName { get; set; }

        /// <summary>
        /// Is inherited
        /// </summary>
        [XmlIgnore]
        public bool IsInherited { get; set; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is Parameter other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(Parameter other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                EqualityComparer<LocalizedText>.Default.Equals(Description, other.Description) &&
                EqualityComparer<XmlElement>.Default.Equals(DefaultValue, other.DefaultValue) &&
                EqualityComparer<LocalizedText>.Default.Equals(DisplayName, other.DisplayName) &&
                Name == other.Name &&
                Identifier == other.Identifier &&
                IdentifierSpecified == other.IdentifierSpecified &&
                BitMask == other.BitMask &&
                XmlQualifiedNameEqualityComparer.Default.Equals(DataType, other.DataType) &&
                ValueRank == other.ValueRank &&
                ArrayDimensions == other.ArrayDimensions &&
                AllowSubTypes == other.AllowSubTypes &&
                IsOptional == other.IsOptional &&
                ReleaseStatus == other.ReleaseStatus;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Description);
            hash.Add(DefaultValue);
            hash.Add(DisplayName);
            hash.Add(Name);
            hash.Add(Identifier);
            hash.Add(IdentifierSpecified);
            hash.Add(BitMask);
            hash.Add(DataType, XmlQualifiedNameEqualityComparer.Default);
            hash.Add(ValueRank);
            hash.Add(ArrayDimensions);
            hash.Add(AllowSubTypes);
            hash.Add(IsOptional);
            hash.Add(ReleaseStatus);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(Parameter left, Parameter right)
        {
            return EqualityComparer<Parameter>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(Parameter left, Parameter right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// The set of basic data types
    /// </summary>
    public enum BasicDataType
    {
        /// <summary>
        /// Boolean data type
        /// </summary>
        Boolean,

        /// <summary>
        /// Signed byte data type
        /// </summary>
        SByte,

        /// <summary>
        /// Unsigned byte data type
        /// </summary>
        Byte,

        /// <summary>
        /// 16-bit signed integer data type
        /// </summary>
        Int16,

        /// <summary>
        /// 16-bit unsigned integer data type
        /// </summary>
        UInt16,

        /// <summary>
        /// 32-bit signed integer data type
        /// </summary>
        Int32,

        /// <summary>
        /// 32-bit unsigned integer data type
        /// </summary>
        UInt32,

        /// <summary>
        /// 64-bit signed integer data type
        /// </summary>
        Int64,

        /// <summary>
        /// 64-bit unsigned integer data type
        /// </summary>
        UInt64,

        /// <summary>
        /// Single-precision floating point data type
        /// </summary>
        Float,

        /// <summary>
        /// Double-precision floating point data type
        /// </summary>
        Double,

        /// <summary>
        /// String data type
        /// </summary>
        String,

        /// <summary>
        /// DateTime data type
        /// </summary>
        DateTime,

        /// <summary>
        /// Globally unique identifier data type
        /// </summary>
        Guid,

        /// <summary>
        /// Byte string data type
        /// </summary>
        ByteString,

        /// <summary>
        /// XML element data type
        /// </summary>
        XmlElement,

        /// <summary>
        /// Node identifier data type
        /// </summary>
        NodeId,

        /// <summary>
        /// Expanded node identifier data type
        /// </summary>
        ExpandedNodeId,

        /// <summary>
        /// Status code data type
        /// </summary>
        StatusCode,

        /// <summary>
        /// Diagnostic information data type
        /// </summary>
        DiagnosticInfo,

        /// <summary>
        /// Qualified name data type
        /// </summary>
        QualifiedName,

        /// <summary>
        /// Localized text data type
        /// </summary>
        LocalizedText,

        /// <summary>
        /// Data value data type
        /// </summary>
        DataValue,

        /// <summary>
        /// Numeric data type
        /// </summary>
        Number,

        /// <summary>
        /// Integer data type
        /// </summary>
        Integer,

        /// <summary>
        /// Unsigned integer data type
        /// </summary>
        UInteger,

        /// <summary>
        /// Enumeration data type
        /// </summary>
        Enumeration,

        /// <summary>
        /// Structure data type
        /// </summary>
        Structure,

        /// <summary>
        /// Base data type
        /// </summary>
        BaseDataType,

        /// <summary>
        /// User-defined data type
        /// </summary>
        UserDefined
    }

    /// <summary>
    /// A class that stores the model design for a DataType.
    /// </summary>
    public partial class DataTypeDesign : IEquatable<DataTypeDesign>
    {
        /// <summary>
        /// Has encodings
        /// </summary>
        [XmlIgnore]
        public bool HasEncodings { get; set; }

        /// <summary>
        /// Has fields
        /// </summary>
        [XmlIgnore]
        public bool HasFields { get; set; }

        /// <summary>
        /// Is structure
        /// </summary>
        [XmlIgnore]
        public bool IsStructure { get; set; }

        /// <summary>
        /// Is enumeration
        /// </summary>
        [XmlIgnore]
        public bool IsEnumeration { get; set; }

        /// <summary>
        /// Name of the service the data type belongs to
        /// </summary>
        [XmlIgnore]
        public Service Service { get; set; }

        /// <summary>
        /// Is service response type
        /// </summary>
        [XmlIgnore]
        public bool IsServiceResponse { get; set; }

        /// <summary>
        /// Built in data type
        /// </summary>
        [XmlIgnore]
        public BasicDataType BasicDataType { get; set; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is DataTypeDesign other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(DataTypeDesign other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                base.Equals(other) &&
                ArrayEqualityComparer<Parameter>.Default.Equals(Fields, other.Fields) &&
                ArrayEqualityComparer<EncodingDesign>.Default.Equals(Encodings, other.Encodings) &&
                IsOptionSet == other.IsOptionSet &&
                IsUnion == other.IsUnion &&
                NoArraysAllowed == other.NoArraysAllowed &&
                ForceEnumValues == other.ForceEnumValues &&
                NoEncodings == other.NoEncodings &&
                IsServiceResponse == other.IsServiceResponse &&
                Service == other.Service;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.GetHashCode());
            hash.Add(Fields, ArrayEqualityComparer<Parameter>.Default);
            hash.Add(Encodings, ArrayEqualityComparer<EncodingDesign>.Default);
            hash.Add(IsOptionSet);
            hash.Add(IsUnion);
            hash.Add(NoArraysAllowed);
            hash.Add(ForceEnumValues);
            hash.Add(NoEncodings);
            hash.Add(Service);
            hash.Add(IsServiceResponse);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(DataTypeDesign left, DataTypeDesign right)
        {
            return EqualityComparer<DataTypeDesign>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(DataTypeDesign left, DataTypeDesign right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Service descriptor
    /// </summary>
    [Serializable]
    public sealed class Service : IEquatable<Service>
    {
        /// <summary>
        /// Service category of the service the data type belongs to
        /// </summary>
        [XmlIgnore]
        public ServiceCategory Category { get; init; }

        /// <summary>
        /// Name of the service the data type belongs to
        /// </summary>
        [XmlIgnore]
        public string Name { get; init; }

        /// <summary>
        /// Service request data type
        /// </summary>
        [XmlIgnore]
        public DataTypeDesign Request { get; set; }

        /// <summary>
        /// Service response data type
        /// </summary>
        [XmlIgnore]
        public DataTypeDesign Response { get; set; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is Service other && Equals(other);
        }

        /// <inheritdoc/>
        public bool Equals(Service other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return
                other is not null &&
                Category == other.Category &&
                Name == other.Name;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Category, Name);
        }

        /// <inheritdoc/>
        public static bool operator ==(Service left, Service right)
        {
            return EqualityComparer<Service>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(Service left, Service right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Service
    /// </summary>
    public enum ServiceCategory
    {
        /// <summary>
        /// Not part of a service
        /// </summary>
        None,

        /// <summary>
        /// Session
        /// </summary>
        Session,

        /// <summary>
        /// Secure channel
        /// </summary>
        SecureChannel,

        /// <summary>
        /// Discovery
        /// </summary>
        Discovery,

        /// <summary>
        /// Registration
        /// </summary>
        Registration,

        /// <summary>
        /// Test
        /// </summary>
        Test
    }

    /// <summary>
    /// Core standard versions
    /// </summary>
    public enum SpecificationVersion
    {
        /// <summary>
        /// Version 1.03
        /// </summary>
        V103 = 103,

        /// <summary>
        /// Version 1.04
        /// </summary>
        V104 = 104,

        /// <summary>
        /// Version 1.05
        /// </summary>
        V105 = 105
    }
}
