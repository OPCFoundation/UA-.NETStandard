using System;
using System.Collections.Generic;
using System.Linq;
using Opc.Ua.Client.Events;

namespace Opc.Ua.Client.Methods
{
    /// <summary>
    /// Base object for method object
    /// </summary>
    internal abstract class MethodBase
    {
        private readonly ISession _session;
        private NodeId _inputArgumentsNodeId;

        /// <summary>
        /// Node id of the input arguments, null if it does not exist
        /// </summary>
        protected NodeId InputArgumentsNodeId
        {
            get
            {
                if (_inputArgumentsNodeId == null)
                {
                    LoadArguments();
                }
                return _inputArgumentsNodeId;
            }
        }

        private NodeId _outputArgumentsNodeId;

        /// <summary>
        /// Node id of the output arguments, null if it does not exist
        /// </summary>
        protected NodeId OutputArgumentsNodeId
        {
            get
            {
                if (_outputArgumentsNodeId == null)
                {
                    LoadArguments();
                }
                return _outputArgumentsNodeId;
            }
        }

        /// <summary>
        /// NodeId of the method
        /// </summary>
        public NodeId MethodNodeId { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        protected MethodBase(ISession session, NodeId methodNodeId)
        {
            _session = session;
            MethodNodeId = methodNodeId;
        }

        private void LoadArguments()
        {
            var references = _session.FetchReferences(MethodNodeId/*, BrowseDirection.Forward, ReferenceTypeIds.HasProperty*/);
            foreach (var reference in references)
            {
                if (reference.BrowseName.Name.Equals("InputArguments", StringComparison.Ordinal))
                {
                    _inputArgumentsNodeId = reference.ToNodeId();
                }
                else if (reference.BrowseName.Name.Equals("OutputArguments", StringComparison.Ordinal))
                {
                    _outputArgumentsNodeId = reference.ToNodeId();
                }
            }
        }

        /// <summary>
        /// Internal function call
        /// </summary>
        /// <param name="objectNodeId">object NodeId</param>
        /// <param name="parameters">the list of parameter. if there are none, an empty list</param>
        protected MethodCallReturnValue Call(NodeId objectNodeId, IEnumerable<Variant> parameters)
        {
            var callMethodRequestCollection = new CallMethodRequestCollection
            {
                new CallMethodRequest()
                {
                    ObjectId = objectNodeId,
                    MethodId = MethodNodeId,
                    InputArguments = new VariantCollection(parameters)
                }
            };
            CallMethodResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            _session.Call(GetHeader(), callMethodRequestCollection, out results, out diagnosticInfos);

            VariantCollection outputValues = results[0].OutputArguments;
            List<Argument> arguments = null;
            if (OutputArgumentsNodeId != null)
            {
                var extObjs = (ExtensionObject[])_session.ReadValue(OutputArgumentsNodeId).Value;
                arguments = extObjs.Select(x => (Argument)x.Body).ToList();
            }

            var convertedOutputArgs = GetOutputParameters(outputValues, arguments);

            return new MethodCallReturnValue(results[0].StatusCode, convertedOutputArgs.ToList(), results[0].InputArgumentResults.ToList());

        }

        private RequestHeader GetHeader()
        {
            return new RequestHeader();
        }

        private List<OutputArgument> GetOutputParameters(VariantCollection values, IEnumerable<Argument> arguments)
        {
            var convertedOutputArgs = new List<OutputArgument>();
            for (int i = 0; i < values.Count; i++)
            {
                convertedOutputArgs.Add(GetOutputParameter(values[i], arguments.ElementAt(i)));
            }
            return convertedOutputArgs;
        }

        /// <summary>
        /// Get output parameters
        /// </summary>
        protected virtual OutputArgument GetOutputParameter(Variant value, Argument argument)
        {
            return new OutputArgument() { Name = argument.Name, Value = value.Value };
        }
    }
}
