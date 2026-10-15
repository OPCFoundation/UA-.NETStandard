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

namespace Opc.Ua.AMB.Client
{
    public sealed partial class AmbClient
    {
        private NodeId ReadSourceOf(ReferenceDescription reference)
        {
            NodeId source = ToNodeId(reference.NodeId);
            if (m_options is not null && source.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "A browsed AMB variable has no local source node.");
            }
            return source;
        }

        private async ValueTask<AssetReadValue[]> ReadFieldsAsync(
            NodeId parent,
            QualifiedName[][] paths,
            CancellationToken cancellationToken)
        {
            NodeId[] nodes = await ResolveAsync(parent, paths, cancellationToken).ConfigureAwait(false);
            DataValue[] values = await ReadAsync(nodes, Attributes.Value, cancellationToken).ConfigureAwait(false);
            var fields = new AssetReadValue[values.Length];
            for (int ii = 0; ii < fields.Length; ii++)
            {
                fields[ii] = new AssetReadValue(nodes[ii], paths[ii].ToArrayOf(), Attributes.Value, values[ii]);
            }
            return fields;
        }

        private ArrayOf<AssetReadValue> EvidenceOf(AssetReadValue[] fields)
        {
            return m_options is null ? default : fields.ToArrayOf();
        }

        private void RequireValueType(AssetReadValue field, bool valid)
        {
            if (m_options is not null && !field.IsAbsent && !field.Value.WrappedValue.IsNull && !valid)
            {
                throw new AmbValueReadException(StatusCodes.BadTypeMismatch, field);
            }
        }

        private static Variant[] VariantsOf(AssetReadValue[] fields)
        {
            var values = new Variant[fields.Length];
            for (int ii = 0; ii < fields.Length; ii++)
            {
                DataValue value = fields[ii].Value;
                values[ii] = StatusCode.IsGood(value.StatusCode) ? value.WrappedValue : Variant.Null;
            }
            return values;
        }
    }
}
