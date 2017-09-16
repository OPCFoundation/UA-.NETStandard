﻿using System;
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
            
            
        }

        #endregion

        #region Public Methods

        protected override void OnStartup( StartupEventArgs e )
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Dispatcher.UnhandledException += Dispatcher_UnhandledException;
            base.OnStartup( e );
        }

        public bool SignalExternalCommandLineArgs( IList< string > args )
        {
            return true;
        }

        #endregion

        private const string Unique = "OPCUAClientPubSub";
    }
}