/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// component that is specialized in calculating and executing a routine for a given interval
    /// </summary>
    public class IntervalRunner : IDisposable
    {
        private const int kMinInterval = 10;
        private readonly object m_lock = new object();

        private double m_interval = kMinInterval;
        private DateTime m_nextPublishTime = DateTime.MinValue;
        // event used to cancel run
        private CancellationTokenSource m_cancellationToken = new CancellationTokenSource();

        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="IntervalRunner"/>.
        /// </summary>
        public IntervalRunner(object id, double interval, Func<bool> canExecuteFunc, Action intervalAction)
        {
            Id = id;
            Interval = interval;
            CanExecuteFunc = canExecuteFunc;
            IntervalAction = intervalAction;
        }
        #endregion

        /// <summary>
        /// Identifier of current IntervalRunner
        /// </summary>
        public object Id { get; private set; }

        /// <summary>
        /// Get/set the Interval between Runs
        /// </summary>
        public double Interval
        {
            get { return m_interval; }
            set
            {
                lock (m_lock)
                {
                    if (value < kMinInterval)
                    {
                        value = kMinInterval;
                    }

                    m_interval = value;
                }
            }
        }

        /// <summary>
        /// Get the function that decides if the configured action can be executed when the Interval elapses
        /// </summary>
        public Func<bool> CanExecuteFunc { get; private set; }

        /// <summary>
        /// Get the action that will be executed at each interval
        /// </summary>
        public Action IntervalAction { get; private set; }

        #region Public Methods

        /// <summary>
        /// Starts the IntervalRunner and makes it ready to execute the code.
        /// </summary>
        public void Start()
        {
            Task.Run(Process).ConfigureAwait(false);
            Utils.Trace("IntervalRunner with id: {0} was started.", Id);
        }

        /// <summary>
        /// Stop the publishing thread.
        /// </summary>
        public virtual void Stop()
        {
            lock (m_lock)
            {
                m_cancellationToken?.Cancel();
            }

            Utils.Trace("IntervalRunner with id: {0} was stopped.", Id);
        }
        #endregion

        #region IDisposable Implementation
        /// <summary>
        /// Releases all resources used by the current instance of the <see cref="UaPublisher"/> class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///  When overridden in a derived class, releases the unmanaged resources used by that class 
        ///  and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"> true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();

                if (m_cancellationToken != null)
                {
                    m_cancellationToken.Dispose();
                    m_cancellationToken = null;
                }
            }
        }
        #endregion

        /// <summary>
        /// Periodically executes the .
        /// </summary>
        private async Task Process()
        {
            do
            {
                int sleepCycle = 0;
                DateTime now = DateTime.UtcNow;
                DateTime nextPublishTime = DateTime.MinValue;

                lock (m_lock)
                {
                    sleepCycle = Convert.ToInt32(m_interval);

                    nextPublishTime = m_nextPublishTime;
                }

                if (nextPublishTime > now)
                {
                    sleepCycle = (int)Math.Min((nextPublishTime - now).TotalMilliseconds, sleepCycle);
                    sleepCycle = (int)Math.Max(kMinInterval, sleepCycle);
                    await Task.Delay(TimeSpan.FromMilliseconds(sleepCycle), m_cancellationToken.Token).ConfigureAwait(false);
                }

                lock (m_lock)
                {
                    var nextCycle = Convert.ToInt32(m_interval);
                    m_nextPublishTime = DateTime.UtcNow.AddMilliseconds(nextCycle);

                    if (IntervalAction != null && CanExecuteFunc != null && CanExecuteFunc())
                    {
                        // call on a new thread
                        Task.Run(() => {
                            IntervalAction();
                        });
                    }
                }
            }
            while (true);
        }
    }
}
