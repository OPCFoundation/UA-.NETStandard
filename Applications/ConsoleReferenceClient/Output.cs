using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;

namespace Quickstarts
{
    public interface IOutput
    {
        void WriteLine(object obj);
        void WriteLine(string msg);
        void WriteLine(string msg, params object[] parameters);
    }

    public class ConsoleOutput : IOutput
    {
        public void WriteLine(object obj) => Console.WriteLine(obj);
        public void WriteLine(string msg) => Console.WriteLine(msg);
        public void WriteLine(string msg, params object[] parameters) => Console.WriteLine(msg, parameters);
    }

    public class LogOutput : IOutput
    {
        public void WriteLine(object obj) => Utils.LogInfo("{0}", obj);
        public void WriteLine(string msg) => Utils.LogInfo(msg);
        public void WriteLine(string msg, params object[] parameters) => Utils.LogInfo(msg, parameters);
    }

}
