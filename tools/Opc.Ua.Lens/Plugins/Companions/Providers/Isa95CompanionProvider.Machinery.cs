// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
