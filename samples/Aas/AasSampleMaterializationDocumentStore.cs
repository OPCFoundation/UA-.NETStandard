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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Aas.Server.Materialization;

namespace AasSample
{
    /// <summary>
    /// Small in-memory document store used by the sample materialization coordinator.
    /// </summary>
    public sealed class AasSampleMaterializationDocumentStore : IAasMaterializationDocumentStore
    {
        /// <summary>
        /// Initializes the store.
        /// </summary>
        public AasSampleMaterializationDocumentStore(ArrayOf<AasMaterializationDocument> documents)
        {
            m_documents = documents.IsNull ? [] : documents;
        }

        /// <summary>
        /// Gets the last states applied by the coordinator.
        /// </summary>
        public ArrayOf<AasMaterializationDocumentState> States { get; private set; } = [];

        /// <inheritdoc/>
        public ValueTask<ArrayOf<AasMaterializationDocument>> GetDocumentsAsync(
            ArrayOf<string> targets,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (targets.IsNull || targets.Count == 0)
            {
                return new ValueTask<ArrayOf<AasMaterializationDocument>>(m_documents);
            }

            var documents = new List<AasMaterializationDocument>();
            for (int ii = 0; ii < targets.Count; ii++)
            {
                for (int jj = 0; jj < m_documents.Count; jj++)
                {
                    if (string.Equals(targets[ii], m_documents[jj].Xid, StringComparison.Ordinal))
                    {
                        documents.Add(m_documents[jj]);
                    }
                }
            }
            return new ValueTask<ArrayOf<AasMaterializationDocument>>(new ArrayOf<AasMaterializationDocument>(
                documents.ToArray()));
        }

        /// <inheritdoc/>
        public ValueTask ApplyMaterializationAsync(
            ArrayOf<AasMaterializationDocumentState> states,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            States = states.IsNull ? [] : states;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask<AasMaterializationDocument> UpdateValueAsync(
            AasValueWriteBackRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new ServiceResultException(StatusCodes.BadNotWritable);
        }

        private readonly ArrayOf<AasMaterializationDocument> m_documents;
    }
}
