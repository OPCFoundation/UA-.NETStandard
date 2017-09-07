using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
 

namespace PubSubConfigurationUI
{
    /// <summary>
    ///   Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application 
    {
        #region Private Methods

        private void Dispatcher_UnhandledException( object sender, DispatcherUnhandledExceptionEventArgs e )
        {
            try
            {
                var ex = e.Exception;
                // MessageBox.Show(ex.Message);
            }
            finally
            {
                e.Handled = true;
            }
        }

        private void CurrentDomain_UnhandledException( object sender, UnhandledExceptionEventArgs e )
        {
            try
            {
                var ex = ( Exception ) e.ExceptionObject;
            }
            catch ( Exception ex )
            {
            }
        }

        #endregion

        #region Public Methods

        protected override void OnStartup( StartupEventArgs e )
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Dispatcher.UnhandledException += Dispatcher_UnhandledException;
            base.OnStartup( e );

            //if (SingleInstance<App>.InitializeAsFirstInstance(Unique))
            //{
            //    AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            //    this.Dispatcher.UnhandledException += Dispatcher_UnhandledException;
            //    base.OnStartup(e);
            //}
            //else
            //{

            //    SingleInstance<App>.Cleanup();
            //    Environment.Exit(1);
            //}
        }

        public bool SignalExternalCommandLineArgs( IList< string > args )
        {
            return true;
        }

        #endregion

        private const string Unique = "OPCUAClientPubSub";
    }
}