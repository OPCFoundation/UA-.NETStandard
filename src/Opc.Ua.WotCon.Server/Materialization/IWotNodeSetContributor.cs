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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Contributes additional nodes to a converted document before it is materialized into the
    /// AddressSpace — typically custom <c>StructureType</c> DataTypes that have no NodeSet to
    /// import because they are specific to one controller program.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Many industrial protocols expose user-defined types — Rockwell / Studio 5000 UDTs, TIA
    /// Portal PLC data types, Beckhoff TwinCAT structured types — that must be generated from the
    /// controller's own symbol table at onboarding time. A contributor runs once per resource,
    /// after the Thing Description has been converted to a NodeSet and before any variable is
    /// created, which is the point at which such a DataType has to exist: a
    /// <c>uav:mapByFieldPath</c> mapping into a structured type can only resolve once that type is
    /// registered.
    /// </para>
    /// <para>
    /// A document that can already express its types declaratively does not need this seam — the
    /// native projection (<c>uav:NodeModel</c>) carries <c>DataType</c> nodes with their
    /// <c>DataTypeDefinition</c> directly. This interface is for types that can only be discovered
    /// programmatically.
    /// </para>
    /// <para>
    /// Contributors are resolved from dependency injection; registering none leaves conversion
    /// unchanged. Implementations must be safe for concurrent calls across resources.
    /// </para>
    /// </remarks>
    public interface IWotNodeSetContributor
    {
        /// <summary>
        /// Contributes nodes for <paramref name="resource"/> to <paramref name="nodeSet"/>.
        /// </summary>
        /// <param name="resource">The registry resource being materialized.</param>
        /// <param name="nodeSet">
        /// The converted NodeSet, mutated in place. A contributor adds nodes; it must not remove or
        /// rewrite nodes produced by the conversion.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask ContributeAsync(
            WotResource resource,
            UANodeSet nodeSet,
            CancellationToken cancellationToken = default);
    }
}
