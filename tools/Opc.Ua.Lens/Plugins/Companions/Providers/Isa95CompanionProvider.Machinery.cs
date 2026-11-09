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
using Opc.Ua;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class Isa95CompanionProvider
    {
        private static async Task<bool> IsMachineryPairAsync(
            CompanionContext context, NodeId parent, NodeId order, NodeId provider,
            IndustrialBrowseBudget budget, CancellationToken cancellationToken)
        {
            const string model = Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs;
            if (context.Session.NamespaceUris.GetIndex(model) < 0)
            {
                return false;
            }
            int types = 0;
            bool matches = false;
            await foreach (ReferenceDescription type in IndustrialCompanionAccess.BrowseAsync(
                context, parent, BrowseDirection.Forward, ReferenceTypeIds.HasTypeDefinition,
                NodeClass.ObjectType, budget, cancellationToken).ConfigureAwait(false))
            {
                if (++types > 1)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "The JobManagement owner has ambiguous type metadata.");
                }
                matches = await IndustrialCompanionAccess.ClassifyAsync(
                    context, type.NodeId, s_machineryJobTypes, budget, cancellationToken).ConfigureAwait(false)
                    is not null;
            }
            if (!matches)
            {
                return false;
            }
            NodeId declaredOrder = await IndustrialCompanionAccess.ResolveChildAsync(
                context, parent, model, Opc.Ua.Machinery.Jobs.BrowseNames.JobOrderControl, true, cancellationToken)
                .ConfigureAwait(false);
            NodeId declaredProvider = await IndustrialCompanionAccess.ResolveChildAsync(
                context, parent, model, Opc.Ua.Machinery.Jobs.BrowseNames.JobOrderResults, true, cancellationToken)
                .ConfigureAwait(false);
            return declaredOrder == order && declaredProvider == provider;
        }

        private static readonly ArrayOf<IndustrialCompanionType> s_machineryJobTypes =
            [new(Opc.Ua.Machinery.Jobs.ObjectTypeIds.JobManagementType, "Machinery JobManagement")];
    }
}
