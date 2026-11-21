// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Adapter.Actions
{
    /// <summary>
    /// Resolves a PubSub Action target to the external OPC UA object and
    /// method that an <see cref="ServerActionHandler"/> calls when the
    /// action is invoked. The optional output field names are applied, in
    /// order, to the method's output arguments when the result is mapped back
    /// to a PubSub Action response; positions without a configured name fall
    /// back to a generated <c>Output{i}</c> name.
    /// </summary>
    public readonly record struct ActionMethodBinding
    {
        /// <summary>
        /// Initializes a new <see cref="ActionMethodBinding"/>.
        /// </summary>
        /// <param name="objectId">
        /// The external object that provides the method to call.
        /// </param>
        /// <param name="methodId">
        /// The external method invoked for the action.
        /// </param>
        /// <param name="outputFieldNames">
        /// Optional output field names applied, in order, to the method's
        /// output arguments. Defaults to empty, in which case generated names
        /// are used.
        /// </param>
        public ActionMethodBinding(
            NodeId objectId,
            NodeId methodId,
            ArrayOf<string> outputFieldNames = default)
        {
            ObjectId = objectId;
            MethodId = methodId;
            OutputFieldNames = outputFieldNames;
        }

        /// <summary>
        /// The external object that provides the method to call.
        /// </summary>
        public NodeId ObjectId { get; init; }

        /// <summary>
        /// The external method invoked for the action.
        /// </summary>
        public NodeId MethodId { get; init; }

        /// <summary>
        /// Output field names applied, in order, to the method's output
        /// arguments. Empty when generated names should be used.
        /// </summary>
        public ArrayOf<string> OutputFieldNames { get; init; }
    }
}
