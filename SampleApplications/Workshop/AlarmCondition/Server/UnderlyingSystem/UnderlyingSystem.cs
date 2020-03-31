/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;

namespace Quickstarts.AlarmConditionServer
{
    /// <summary>
    /// An object that provides access to the underlying system.
    /// </summary>
    public class UnderlyingSystem : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="UnderlyingSystem"/> class.
        /// </summary>
        public UnderlyingSystem()
        {
            m_sources = new Dictionary<string, UnderlyingSystemSource>();
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~UnderlyingSystem() 
        {
            Dispose(false);
        }
        
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {  
            if (disposing)
            {
                if (m_simulationTimer != null)
                {
                    m_simulationTimer.Dispose();
                    m_simulationTimer = null;
                }
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Creates a source.
        /// </summary>
        /// <param name="sourcePath">The source path.</param>
        /// <param name="alarmChangeCallback">The callback invoked when an alarm changes.</param>
        /// <returns>The source.</returns>
        public UnderlyingSystemSource CreateSource(string sourcePath, AlarmChangedEventHandler alarmChangeCallback)
        {
            UnderlyingSystemSource source = null;

            lock (m_lock)
            {
                // create a new source.
                source = new UnderlyingSystemSource();

                // extract the name from the path.
                string name = sourcePath;

                int index = name.LastIndexOf('/');

                if (index != -1)
                {
                    name = name.Substring(index+1);
                }

                // extract the type from the path.
                string type = sourcePath;

                index = type.IndexOf('/');

                if (index != -1)
                {
                    type = type.Substring(0, index);
                }

                // create the source.
                source.SourcePath = sourcePath;
                source.Name = name;
                source.SourceType = type;
                source.OnAlarmChanged = alarmChangeCallback;

                m_sources.Add(sourcePath, source);
            }


            // add the alarms based on the source type.
            // note that the source and alarm types used here are types defined by the underlying system.
            // the node manager will need to map these types to UA defined types.
            switch (source.SourceType)
            {
                case "Colours":
                {
                    source.CreateAlarm("Red", "HighAlarm");
                    source.CreateAlarm("Yellow", "HighLowAlarm");
                    source.CreateAlarm("Green", "TripAlarm");
                    break;
                }

                case "Metals":
                {
                    source.CreateAlarm("Gold", "HighAlarm");
                    source.CreateAlarm("Silver", "HighLowAlarm");
                    source.CreateAlarm("Bronze", "TripAlarm");
                    break;
                }
            }

            // return the new source.
            return source;
        }

        /// <summary>
        /// Starts a simulation which causes the alarm states to change.
        /// </summary>
        /// <remarks>
        /// This simulation randomly activates the alarms that belong to the sources.
        /// Once an alarm is active it has to be acknowledged and confirmed.
        /// Once an alarm is confirmed it go to the inactive state.
        /// If the alarm stays active the severity will be gradually increased.
        /// </remarks>
        public void StartSimulation()
        {
            lock (m_lock)
            {
                if (m_simulationTimer != null)
                {
                    m_simulationTimer.Dispose();
                    m_simulationTimer = null;
                }

                m_simulationTimer = new Timer(DoSimulation, null, 1000, 1000);
            }
        }

        /// <summary>
        /// Stops the simulation.
        /// </summary>
        public void StopSimulation()
        {
            lock (m_lock)
            {
                if (m_simulationTimer != null)
                {
                    m_simulationTimer.Dispose();
                    m_simulationTimer = null;
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Simulates a source by updating the state of the alarms belonging to the condition.
        /// </summary>
        private void DoSimulation(object state)
        {
            try
            {
                // get the list of sources.
                List<UnderlyingSystemSource> sources = null;

                lock (m_lock)
                {
                    m_simulationCounter++;
                    sources = new List<UnderlyingSystemSource>(m_sources.Values);
                }

                // run simulation for each source.
                for (int ii = 0; ii < sources.Count; ii++)
                {
                    sources[ii].DoSimulation(m_simulationCounter, ii);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error running simulation for system");
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private Dictionary<string,UnderlyingSystemSource> m_sources;
        private Timer m_simulationTimer;
        private long m_simulationCounter;
        #endregion
    }
}
