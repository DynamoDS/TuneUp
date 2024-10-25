using System;
using System.Linq;
using System.Windows.Controls;
using Dynamo.Wpf.Extensions;
using Dynamo.Wpf.Properties;

namespace TuneUp
{
    /// <summary>
    /// This sample view extension demonstrates a sample IViewExtension 
    /// which allows Dynamo users to analyze the performance of graphs
    /// and diagnose bottlenecks and problem areas.
    /// </summary>
    public class TuneUpViewExtension : ViewExtensionBase, IViewExtension
    {
        internal MenuItem TuneUpMenuItem;
        private TuneUpWindow TuneUpView;
        internal TuneUpWindowViewModel ViewModel;

        public override void Dispose()
        {
        }

        public override void Startup(ViewStartupParams p)
        {
        }

        public override void Loaded(ViewLoadedParams p)
        {
            // Use dynamic object type of ViewLoadedParams to dynamically call its methods.
            dynamic dp = (dynamic) p;
            ViewModel = new TuneUpWindowViewModel(p);

            TuneUpView = new TuneUpWindow(p, UniqueId)
            {
                // Set the data context for the main grid in the window.
                LatestRunTable = { DataContext = ViewModel },
                PreviousRunTable = { DataContext = ViewModel },
                NotExecutedTable = { DataContext = ViewModel },
                MainGrid = { DataContext = ViewModel },
                Owner = p.DynamoWindow
            };

            TuneUpMenuItem = new MenuItem { Header = Properties.Resources.Button_ShowTuneUp, IsCheckable = true, IsChecked = false };
            TuneUpMenuItem.Click += (sender, args) =>
            {
                if (TuneUpMenuItem.IsChecked)
                {
                    p.AddToExtensionsSideBar(this, TuneUpView);
                    ViewModel.SwitchToManualMode();
                    ViewModel.EnableProfiling();
                }
                else
                {
                    p.CloseExtensioninInSideBar(this);
                    ViewModel.DisableProfiling();
                }
            };

            // Bind the IsChecked property to the IsTuneUpActive property
            TuneUpMenuItem.Checked += (sender, args) => ViewModel.IsTuneUpChecked = true;
            TuneUpMenuItem.Unchecked += (sender, args) => ViewModel.IsTuneUpChecked = false;

            // Add this view extension's menu item to the Extensions tab or View tab accordingly.
            var dynamoMenuItems = p.dynamoMenu.Items.OfType<MenuItem>();
            var extensionsMenuItem = dynamoMenuItems.Where(item => item.Header.ToString() == Resources.DynamoViewExtensionsMenu);

            if (extensionsMenuItem.Count() > 0)
            {
                dp.AddExtensionMenuItem(TuneUpMenuItem);
            }   
            else
            {
                dp.AddMenuItem(MenuBarType.View, TuneUpMenuItem);
            }
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
        public override string UniqueId
        {
            get
            {
                return "b318f80b-b1d0-4935-b80e-7ab1be7742b4";
            }
        }

        /// <summary>
        /// Name of this extension
        /// </summary>
        public override string Name => Properties.Resources.ExtensionName;

        public override void Closed()
        {
            if (this.TuneUpMenuItem != null)
            {
                this.TuneUpMenuItem.IsChecked = false;

                // Reset DataGrid sorting order & direction
                ViewModel.SortingOrder = TuneUpWindowViewModel.SortByNumber;
                ViewModel.SortDirection = System.ComponentModel.ListSortDirection.Ascending;
            }
        }
    }
}