﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dynamo.Extensions;
using Dynamo.Models;
using Dynamo.Utilities;
using Dynamo.ViewModels;
using Dynamo.Wpf.Extensions;
using Dynamo.Graph.Nodes;
using System.Reflection;

namespace TuneUp
{
    /// <summary>
    /// Interaction logic for TuneUpWindow.xaml
    /// </summary>
    public partial class TuneUpWindow : Window
    {
        DynamoViewModel dynamoViewModel;

        ViewLoadedParams viewLoadedParams;

        ICommandExecutive commandExecutive;

        /// <summary>
        /// Create the TuneUp Window
        /// </summary>
        /// <param name="vlp"></param>
        public TuneUpWindow(ViewLoadedParams vlp)
        {
            InitializeComponent();
            dynamoViewModel = (vlp.DynamoWindow.DataContext as DynamoViewModel);
            viewLoadedParams = vlp;

            commandExecutive = vlp.CommandExecutive;
        }

        private void NodeAnalysisTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Get NodeModel(s) that correspond to selected row(s)
            var selectedNodes = new List<NodeModel>();
            foreach(var item in e.AddedItems)
            {
                selectedNodes.Add((item as ProfiledNodeViewModel).NodeModel);
            }

            if (selectedNodes.Count() > 0)
            {
                // Select
                var command = new DynamoModel.SelectModelCommand(selectedNodes.Select(nm => nm.GUID), ModifierKeys.None);
                viewLoadedParams.CommandExecutive.ExecuteCommand(command, Guid.NewGuid().ToString(), "TuneUp");

                // Focus on selected
                var ce = viewLoadedParams.GetType().GetProperty("ViewModelCommandExecutive");
                if ( ce != null)
                {
                    // Use the new viewModelCommandExecutive if it exists
                    var method = ce.PropertyType.GetMethod("FindByIdCommand", BindingFlags.NonPublic | BindingFlags.Instance);
                    method.Invoke(ce, new object[] { selectedNodes.First().GUID.ToString() });
                }
                else
                {
                    // Otherwise use the view model "hack"
                    (viewLoadedParams.DynamoWindow.DataContext as DynamoViewModel).CurrentSpaceViewModel.FindByIdCommand.Execute(selectedNodes.First().GUID.ToString());
                }
            }
        }
    }
}