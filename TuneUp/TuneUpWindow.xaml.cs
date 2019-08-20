using System.Windows;
using System.Windows.Controls;
using Dynamo.Core;
using Dynamo.Models;
using Dynamo.Utilities;
using Dynamo.Wpf.Extensions;
using Dynamo.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using Dynamo.Extensions;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;

namespace TuneUp
{
    /// <summary>
    /// Interaction logic for TuneUpWindow.xaml
    /// </summary>
    public partial class TuneUpWindow : Window
    {
        DynamoViewModel dynamoViewModel;

        ViewLoadedParams viewLoadedParams;


        public TuneUpWindow(ViewLoadedParams vlp)
        {
            InitializeComponent();
            dynamoViewModel = (vlp.DynamoWindow.DataContext as DynamoViewModel);
            viewLoadedParams = vlp;
        }

        private void DataGridRow_Selected(object sender, RoutedEventArgs e)
        {
            var nodeModel = ((ProfiledNodeViewModel)((DataGridRow)sender).DataContext).NodeModel;
            nodeModel.Select();

            /*var nodeViewModels = dynamoViewModel.CurrentSpaceViewModel.Nodes;

            NodeViewModel nodeViewModel = null;
            foreach(var n in nodeViewModels)
            {
                if (n.Id == nodeModel.GUID)
                {
                    nodeViewModel = n;
                }
            }

            var nodePt = new Point2D(nodeModel.X, nodeModel.Y);

            var zoomArgs = new ModelEventArgs(nodeModel, nodeModel.X, nodeModel.Y, true);

            dynamoViewModel.CurrentSpaceViewModel.FitViewToNode(nodeViewModel);*/
        }

        private void NodeAnalysisTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var row in e.AddedItems)
            {
                (row as ProfiledNodeViewModel).NodeModel.Select();
            }
            foreach (var row in e.RemovedItems)
            {
                (row as ProfiledNodeViewModel).NodeModel.Deselect();
            }
        }
    }
}
