#region File Name
// Log.cs
#endregion
using System;
using NLog; 

namespace NLogManager
{
    public static class Log
    {
        #region Private Member 

        private static readonly Logger _AuditLogger = LogManager.GetLogger("AuditLogger");
        private static readonly Logger _ErrorMessageLogger = LogManager.GetLogger("ErrorMessageLogger");
        private static readonly Logger _SimpleMessageLogger = LogManager.GetLogger("SimpleMessageLogger");

        #endregion

        #region Constructors

        static Log()
        {

        }

        #endregion

        #region Public Methods

        

        public static void Debug(string message)
        {
            _SimpleMessageLogger.Debug(" : " + message);
        }

        public static void EnterBlockMessage(string message)
        {
            _SimpleMessageLogger.Info("Entering : " + message);
        }

        public static void Error(string message)
        {
            _ErrorMessageLogger.Error(" : " + message);
        }

        public static void ErrorException(string message, Exception exception)
        {
            _ErrorMessageLogger.Error(" : " + message, exception, null);
        }

        public static void Fatal(string message)
        {
            _SimpleMessageLogger.Fatal(" : " + message);
        }

        public static void Info(string message)
        {
            _SimpleMessageLogger.Info(" : " + message);
        }

        public static void LeaveBlockMessage(string message)
        {
            _SimpleMessageLogger.Info("Leaving : " + message);
        }

        public static void WriteErrorMessage(string message)
        {
            _SimpleMessageLogger.Info("Error : " + message);
        }

        public static void WriteExceptionMessage(string message)
        {
            _SimpleMessageLogger.Info("Exception : " + message);
        }

        public static void WriteMessage(string message)
        {
            _SimpleMessageLogger.Info(" : " + message);
        }

        #endregion
    }
}