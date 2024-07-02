using System.Linq;
using System.Windows.Controls;
using Dynamo.Wpf.Extensions;

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
            TuneUpView.Dispose();
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
                NodeAnalysisTable = { DataContext = ViewModel },
                MainGrid = { DataContext = ViewModel },
                Owner = p.DynamoWindow
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

            // Add this view extension's menu item to the Extensions tab or View tab accordingly.
            var dynamoMenuItems = p.dynamoMenu.Items.OfType<MenuItem>();
            // TODO: Investigate why TuneUpMenuItem is not added to the specified MenuItem
            // When _Extensions is specified, TuneUpMenuItem goes to the View menu in Dynamo 3.0-
            // and does not appear in Dynamo 3.1+.
            // We need to specify _View for TuneUpMenuItem to be added to the Extensions menu
            var extensionsMenuItem = dynamoMenuItems.Where(item => item.Header.ToString() == "_View");

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
        public override string Name
        {
            get
            {
                return "TuneUp";
            }
        }

        public override void Closed()
        {
            if (this.TuneUpMenuItem != null)
            {
                this.TuneUpMenuItem.IsChecked = false;
            }
        }
    }
}