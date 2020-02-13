using System;
using System.Windows.Controls;
using Dynamo.Wpf.Extensions;

namespace TuneUp
{
    /// <summary>
    /// This sample view extension demonstrates a sample IViewExtension 
    /// which allows Dynamo users to analyze the performance of graphs
    /// and diagnose bottlenecks and problem areas.
    /// </summary>
    public class TuneUpViewExtension : IViewExtension
    {
        internal MenuItem TuneUpMenuItem;
        private TuneUpWindow TuneUpView;
        internal TuneUpWindowViewModel ViewModel;

        public void Dispose()
        {
            TuneUpView.Dispose();
        }

        public void Startup(ViewStartupParams p)
        {
        }

        public void Loaded(ViewLoadedParams p)
        {
            ViewModel = new TuneUpWindowViewModel(p);
            TuneUpView = new TuneUpWindow(p, UniqueId)
            {
                // Set the data context for the main grid in the window.
                NodeAnalysisTable = { DataContext = ViewModel },
                MainGrid = { DataContext = ViewModel }
            };

            TuneUpMenuItem = new MenuItem { Header = "Show TuneUp", IsCheckable = true, IsChecked = false };
            TuneUpMenuItem.Click += (sender, args) =>
            {
                if (TuneUpMenuItem.IsChecked)
                {
                    p.AddToExtensionsSideBar(this, TuneUpView);
                    ViewModel.EnableProfiling();
                }
                else
                {
                    p.CloseExtensioninInSideBar(this);
                }

            };
            p.AddMenuItem(MenuBarType.View, TuneUpMenuItem);
        }

        /// <summary>
        /// Tear down function.
        /// </summary>
        public void Shutdown()
        {
            this.Dispose();
        }

        /// <summary>
        /// ID for the TuneUp extension
        /// </summary>
        public string UniqueId
        {
            get
            {
                return "b318f80b-b1d0-4935-b80e-7ab1be7742b4";
            }
        }

        /// <summary>
        /// Name of this extension
        /// </summary>
        public string Name
        {
            get
            {
                return "TuneUp";
            }
        }
    }
}