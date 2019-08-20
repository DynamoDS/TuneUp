using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Dynamo.Wpf.Extensions;
using Dynamo.Graph.Workspaces;
using Dynamo.Graph.Nodes;
using Dynamo.ViewModels;

namespace TuneUp
{
    /// <summary>
    /// This sample view extension demonstrates a sample IViewExtension 
    /// which allows Dynamo users to analyze the performance of graphs
    /// and diagnose bottlenecks and problem areas.
    /// </summary>
    public class TuneUpViewExtension : IViewExtension
    {
        private MenuItem TuneUpMenuItem;
        private TuneUpWindow TuneUpView;
        private TuneUpWindowViewModel ViewModel;

        public void Dispose()
        {
        }

        public void Startup(ViewStartupParams p)
        {

        }

        public void Loaded(ViewLoadedParams p)
        {
            ViewModel = new TuneUpWindowViewModel(p);
            TuneUpView = new TuneUpWindow(p)
            {
                // Set the data context for the main grid in the window.
                NodeAnalysisTable = { DataContext = ViewModel },
                MainGrid = { DataContext = ViewModel }
            };

            TuneUpMenuItem = new MenuItem { Header = "Open Tune Up" };
            TuneUpMenuItem.Click += (sender, args) =>
            {
                p.AddToExtensionsSideBar(this, TuneUpView);
                ViewModel.EnableProfiling();
                
            };
            p.AddMenuItem(MenuBarType.View, TuneUpMenuItem);
        }

        public void Shutdown()
        {
        }

        public string UniqueId
        {
            get
            {
                return Guid.NewGuid().ToString();
            }
        }

        public string Name
        {
            get
            {
                return "TuneUp";
            }
        }

    }
}