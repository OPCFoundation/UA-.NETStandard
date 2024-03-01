using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Opc.Ua.Client.Methods
{
    /// <summary>
    /// Return values of a method call
    /// </summary>
    public class MethodCallReturnValue
    {
        /// <summary>
        /// Result of the method call
        /// </summary>
        public StatusCode Result { get; set; }

        /// <summary>
        /// Return values from the method call
        /// </summary>
        public ReadOnlyCollection<OutputArgument> ReturnValue { get; set; }

        /// <summary>
        /// Results of the parameters
        /// </summary>
        public ReadOnlyCollection<StatusCode> ParameterResult { get; set; }

        internal MethodCallReturnValue(StatusCode s, List<OutputArgument> returnValue, List<StatusCode> parameterResult)
        {
            Result = s;
            ReturnValue = new ReadOnlyCollection<OutputArgument>(returnValue);
            ParameterResult = new ReadOnlyCollection<StatusCode>(parameterResult);
        }
    }
}
