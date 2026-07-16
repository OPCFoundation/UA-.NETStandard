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
using Opc.Ua.Export;
using Opc.Ua.Schema.Types;

namespace Opc.Ua.Schema.Model
{
    /// <summary>
    /// Model design represents the content of a set of validated design files.
    /// This includes access to only nodes that are not excluded per exclusion
    /// criteria and to add nodes that are implicit.  Also assigns identifiers
    /// as per input identifier file or in sequence of the designs.
    /// </summary>
    public interface IModelDesign
    {
        /// <summary>
        /// Use the true type instead of ExtensionObject when subtypes are allowed.
        /// </summary>
        bool UseAllowSubtypes { get; }

        /// <summary>
        /// Get all node designs in the model including declarations etc.
        /// </summary>
        NodeDesign[] Nodes { get; }

        /// <summary>
        /// Namespaces used in the model design.
        /// </summary>
        Namespace[] Namespaces { get; }

        /// <summary>
        /// Target namespace information
        /// </summary>
        Namespace TargetNamespace { get; }

        /// <summary>
        /// Target publication date if specified
        /// </summary>
        DateTime? TargetPublicationDate { get; }

        /// <summary>
        /// Target version if specified
        /// </summary>
        string TargetVersion { get; }

        /// <summary>
        /// Model depdendencies or null if none
        /// </summary>
        IEnumerable<ModelTableEntry> Dependencies { get; }

        /// <summary>
        /// Namespace table
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Default role permissions to apply to nodes.
        /// </summary>
        IReadOnlyDictionary<string, RolePermissionSet> RolePermissions { get; }

        /// <summary>
        /// Default access restrictions to apply to nodes.
        /// </summary>
        IReadOnlyDictionary<string, AccessRestrictions?> AccessRestrictions { get; }

        /// <summary>
        /// Returns a list of nodes with nodes excluded per exclusion criteria.
        /// </summary>
        IEnumerable<NodeDesign> GetNodeDesigns();

        /// <summary>
        /// Returns a list of services filtered by their service category.
        /// </summary>
        Service[] GetListOfServices(params ServiceCategory[] serviceCategories);

        /// <summary>
        /// Determines if a node is excluded.
        /// </summary>
        bool IsExcluded(NodeDesign node);

        /// <summary>
        /// Determines if a data type is excluded.
        /// </summary>
        bool IsExcluded(DataType dataType);

        /// <summary>
        /// Determines if a field/parameter is excluded.
        /// </summary>
        bool IsExcluded(Parameter field);

        /// <summary>
        /// Try to find node design.
        /// </summary>
        bool TryFindNode(
            XmlQualifiedName symbolicId,
            string sourceName,
            string referenceName,
            out NodeDesign target);
    }

    /// <summary>
    /// Extensions on top of the model design validator
    /// </summary>
    internal static class ModelDesignValidatorExtensions
    {
        /// <summary>
        /// Find nodedesign of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="InvalidOperationException"></exception>
        public static T FindNode<T>(
            this IModelDesign validator,
            XmlQualifiedName symbolicId,
            string sourceName,
            string referenceName) where T : NodeDesign
        {
            if (!validator.TryFindNode(
                symbolicId,
                sourceName,
                referenceName,
                out T target))
            {
                throw new InvalidOperationException(CoreUtils.Format(
                    "The {0} reference for node {1} is not the expected type: {2}.",
                    referenceName,
                    sourceName,
                    typeof(T).Name));
            }
            return target;
        }

        /// <summary>
        /// Find node design
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public static NodeDesign FindNode(
            this IModelDesign validator,
            XmlQualifiedName symbolicId,
            string sourceName,
            string referenceName)
        {
            if (!validator.TryFindNode(
                symbolicId,
                sourceName,
                referenceName,
                out NodeDesign target))
            {
                throw new InvalidOperationException(CoreUtils.Format(
                    "The {0} reference for node {1} is not valid: {2}.",
                    referenceName,
                    sourceName,
                    symbolicId.Name));
            }
            return target;
        }

        /// <summary>
        /// Find nodedesign of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static bool TryFindNode<T>(
            this IModelDesign validator,
            XmlQualifiedName symbolicId,
            string sourceName,
            string referenceName,
            out T target) where T : NodeDesign
        {
            if (!validator.TryFindNode(
                symbolicId,
                sourceName,
                referenceName,
                out NodeDesign node) ||
                node is not T design)
            {
                target = default;
                return false;
            }
            target = design;
            return true;
        }
    }
}
