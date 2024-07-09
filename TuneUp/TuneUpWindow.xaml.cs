using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dynamo.Extensions;
using Dynamo.Graph.Nodes;
using Dynamo.Models;
using Dynamo.Utilities;
using Dynamo.Wpf.Extensions;

namespace TuneUp
{
    /// <summary>
    /// Interaction logic for TuneUpWindow.xaml
    /// </summary>
    public partial class TuneUpWindow : Window
    {
        private ViewLoadedParams viewLoadedParams;

        private ICommandExecutive commandExecutive;

        private ViewModelCommandExecutive viewModelCommandExecutive;

        /// <summary>
        /// The unique ID for the TuneUp view extension. 
        /// Used to identify the view extension when sending recordable commands.
        /// </summary>
        private string uniqueId;

        /// <summary>
        /// Since there is no API for height offset comparing to
        /// DynamoWindow height. Define it as static for now.
        /// </summary>
        private static double sidebarHeightOffset = 200;

        /// <summary>
        /// Create the TuneUp Window
        /// </summary>
        /// <param name="vlp"></param>
        public TuneUpWindow(ViewLoadedParams vlp, string id)
        {
            InitializeComponent();
            viewLoadedParams = vlp;
            // Initialize the height of the datagrid in order to make sure
            // vertical scrollbar can be displayed correctly.
            this.NodeAnalysisTable.Height = vlp.DynamoWindow.Height - sidebarHeightOffset;
            vlp.DynamoWindow.SizeChanged += DynamoWindow_SizeChanged;
            commandExecutive = vlp.CommandExecutive;
            viewModelCommandExecutive = vlp.ViewModelCommandExecutive;
            uniqueId = id;
        }

        private void DynamoWindow_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            // Update the new height of datagrid
            this.NodeAnalysisTable.Height = e.NewSize.Height - sidebarHeightOffset;
        }

        private void NodeAnalysisTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Get NodeModel(s) that correspond to selected row(s)
            var selectedNodes = new List<NodeModel>();
            foreach (var item in e.AddedItems)
            {
                // Check NodeModel valid before actual selection
                var nodeModel = (item as ProfiledNodeViewModel).NodeModel;
                if (nodeModel != null)
                {
                    selectedNodes.Add(nodeModel);
                }
            }

            if (selectedNodes.Count() > 0)
            {
                // Select
                var command = new DynamoModel.SelectModelCommand(selectedNodes.Select(nm => nm.GUID), ModifierKeys.None);
                commandExecutive.ExecuteCommand(command, uniqueId, "TuneUp");

                // Focus on selected
                viewModelCommandExecutive.FindByIdCommand(selectedNodes.First().GUID.ToString());
            }
        }

        internal void Dispose()
        {
            viewLoadedParams.DynamoWindow.SizeChanged -= DynamoWindow_SizeChanged;
        }

        private void RecomputeGraph_Click(object sender, RoutedEventArgs e)
        {
            (NodeAnalysisTable.DataContext as TuneUpWindowViewModel).ResetProfiling();
        }

        /// <summary>
        /// Handles the sorting event for the NodeAnalysisTable DataGrid.
        /// Updates the SortingOrder property in the view model based on the column header clicked by the user.
        /// </summary>
        private void NodeAnalysisTable_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var column = e.Column;
            var viewModel = NodeAnalysisTable.DataContext as TuneUpWindowViewModel;

            if (viewModel != null)
            {
                switch (column.Header.ToString())
                {
                    case "#":
                        viewModel.SortingOrder = "number";
                        break;
                    case "Name":
                        viewModel.SortingOrder = "name";
                        break;
                    case "Execution Time (ms)":
                        viewModel.SortingOrder = "time";
                        break;
                }
                // Apply custom sorting to ensure total times are at the bottom
                viewModel.ApplySorting();
                e.Handled = true;
            }
        }
    }
}
