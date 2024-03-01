using System;
using System.Linq;

namespace Opc.Ua.Client.Events
{
    /// <summary>
    /// 
    /// </summary>
    public static class Extensions
    {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="session"></param>
        /// <param name="nodeId"></param>
        /// <param name="browseDirection"></param>
        /// <param name="referenceTypeId"></param>
        /// <returns></returns>
        public static ReferenceDescriptionCollection FetchReferences(this ISession session, NodeId nodeId, BrowseDirection browseDirection, NodeId referenceTypeId)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            // browse for all references.
            byte[] continuationPoint;
            ReferenceDescriptionCollection descriptions;

            session.Browse(
                null,
                null,
                nodeId,
                0,
                browseDirection,
                referenceTypeId,
                true,
                0,
                out continuationPoint,
                out descriptions);

            // process any continuation point.
            while (continuationPoint != null)
            {
                byte[] revisedContinuationPoint;
                ReferenceDescriptionCollection additionalDescriptions;

                session.BrowseNext(
                    null,
                    false,
                    continuationPoint,
                    out revisedContinuationPoint,
                    out additionalDescriptions);

                continuationPoint = revisedContinuationPoint;

                descriptions.AddRange(additionalDescriptions);
            }

            return descriptions;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="session"></param>
        /// <param name="rvic"></param>
        /// <returns></returns>
        public static DataValueCollection Read(this ISession session, ReadValueIdCollection rvic)
        {
            session.Read(null, 0, TimestampsToReturn.Neither, rvic, out DataValueCollection dt, out DiagnosticInfoCollection dg);
            return dt;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="session"></param>
        /// <param name="rvi"></param>
        /// <returns></returns>
        public static DataValue Read(this ISession session, ReadValueId rvi)
        {
            return session.Read(new ReadValueIdCollection { rvi }).First();
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static NodeId ToNodeId(this ReferenceDescription id)
        {
            return ExpandedNodeId.ToNodeId(id.NodeId, new NamespaceTable());
        }
    }
}
