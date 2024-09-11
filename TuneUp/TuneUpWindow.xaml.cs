using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Dynamo.Extensions;
using Dynamo.Graph.Nodes;
using Dynamo.Models;
using Dynamo.Utilities;
using Dynamo.Wpf.Extensions;
using static Dynamo.ViewModels.SearchViewModel;

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
        /// A flag indicating whether the current selection change in the DataGrid 
        /// is initiated by the user (true) or programmatically (false).
        /// </summary>
        private bool isUserInitiatedSelection = false;

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
            commandExecutive = vlp.CommandExecutive;
            viewModelCommandExecutive = vlp.ViewModelCommandExecutive;
            uniqueId = id;
        }

        private void DynamoWindow_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            // Update the new height of datagrid
            //this.NodeAnalysisTable.Height = e.NewSize.Height - sidebarHeightOffset;
        }

        private void NodeAnalysisTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isUserInitiatedSelection) return;

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

        /// <summary>
        /// Handles the PreviewMouseDown event for the NodeAnalysisTable DataGrid.
        /// Sets a flag to indicate that the selection change is user-initiated.
        /// </summary>
        private void NodeAnalysisTable_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isUserInitiatedSelection = true;
        }

        /// <summary>
        /// Handles the MouseLeave event for the NodeAnalysisTable DataGrid.
        /// Resets the flag to indicate that the selection change is no longer user-initiated.
        /// </summary>
        private void NodeAnalysisTable_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            isUserInitiatedSelection = false;
        }

        internal void Dispose()
        {
            viewLoadedParams.DynamoWindow.SizeChanged -= DynamoWindow_SizeChanged;
        }

        private void RecomputeGraph_Click(object sender, RoutedEventArgs e)
        {
            (LatestRunTable.DataContext as TuneUpWindowViewModel).ResetProfiling();
        }

        /// <summary>
        /// Handles the sorting event for the NodeAnalysisTable DataGrid.
        /// Updates the SortingOrder property in the view model based on the column header clicked by the user.
        /// </summary>
        private void LatestRunTable_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var viewModel = LatestRunTable.DataContext as TuneUpWindowViewModel;
            if (viewModel != null)
            {
                viewModel.SortingOrder = e.Column.Header switch
                {
                    "#" => "number",
                    "Name" => "name",
                    "Execution Time (ms)" => "time",
                    _ => viewModel.SortingOrder
                };

                // Set the sorting direction of the datagrid column
                e.Column.SortDirection = viewModel.SortDirection == ListSortDirection.Descending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;

                // Apply custom sorting to ensure total times are at the bottom
                viewModel.ApplyCustomSorting();
                e.Handled = true;
            }
        }

        private void NotExecutedTable_Sorting(object sender, DataGridSortingEventArgs e)
        {
            //var viewModel = NotExecutedTable.DataContext as TuneUpWindowViewModel;
            //if (viewModel != null)
            //{
            //    // Always set the sorting order to "name", no matter which column is clicked
            //    viewModel.SortingOrder = "name";

            //    e.Column.SortDirection = ListSortDirection.Ascending;
            //    viewModel.ApplyCustomSorting();

            //    e.Handled = true;
            //}
            e.Handled = true;
        }

        private void ExportToJson_Click(object sender, RoutedEventArgs e)
        {
            (LatestRunTable.DataContext as TuneUpWindowViewModel)?.ExportToJson();
        }

        private void ExportToCsv_Click(object sender, RoutedEventArgs e)
        {
            (LatestRunTable.DataContext as TuneUpWindowViewModel)?.ExportToCsv();
        }

        private void ExportButton_OnClick(object sender, RoutedEventArgs e)
        {
            ExportButton.ContextMenu.IsOpen = true;
        }

        
    }

    #region Converters

    public class IsGroupToMarginMultiConverter : IMultiValueConverter
    {
        private static readonly Guid DefaultGuid = Guid.Empty;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 3 &&
            values[0] is bool isGroup &&
            values[1] is Guid groupGuid &&
            values[2] is bool showGroupIndicator && showGroupIndicator)
            {
                if ( isGroup || !groupGuid.Equals(DefaultGuid)) return new System.Windows.Thickness(5,0,0,0);
            }
            return new System.Windows.Thickness(-4,0,0,0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsInGroupToColorBrushMultiConverter : IMultiValueConverter
    {
        private static readonly Guid DefaultGuid = Guid.Empty;
        private static readonly SolidColorBrush TransparentBrush = Brushes.Transparent;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 4 &&
            values[0] is bool isGroup &&
            values[1] is Guid groupGuid &&
            values[2] is SolidColorBrush groupColorBrush &&
            values[3] is bool showGroupIndicator)
            {
                if (showGroupIndicator && groupColorBrush != null)
                {
                    if (isGroup || groupGuid != DefaultGuid) return groupColorBrush;
                }                
            }
            return TransparentBrush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsGroupToColorBrushMultiConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush TransparentBrush = Brushes.Transparent;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 &&
            values[0] is bool isGroup &&
            values[1] is SolidColorBrush groupColorBrush)
            {
                if (isGroup && groupColorBrush != null) return groupColorBrush;
            }
            return TransparentBrush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsGroupToColorBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush GroupBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));
        private static readonly SolidColorBrush DarkThemeBodyMediumBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool isGroup && isGroup ? GroupBrush : DarkThemeBodyMediumBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }    

    public class IsGroupToVisibilityMultiConverter : IMultiValueConverter
    {
        private static readonly Guid DefaultGuid = Guid.Empty;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 &&
                values[0] is bool isGroup &&
                values[1] is Guid groupGuid)
            {
                if (isGroup || groupGuid == DefaultGuid)
                {
                    return Visibility.Visible;
                }
                return Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsRenamedToVisibilityMultiConverter : IMultiValueConverter
    {        
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 &&
            values[0] is bool isGroup &&
            values[1] is bool isRenamed && !isGroup)
            {
                if (isRenamed) return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ExecutionOrderNumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ProfiledNodeViewModel node)
            {
                return node.ShowGroupIndicator ? node.GroupExecutionOrderNumber : node.ExecutionOrderNumber;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ExecutionOrderNumberVisibilityConverter : IMultiValueConverter
    {
        private static readonly Guid DefaultGuid = Guid.Empty;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 3 &&
                values[0] is bool isGroup &&
                values[1] is Guid groupGuid &&
                values[2] is bool showGroups)
            {
                if (showGroups)
                {
                    if (isGroup || groupGuid == DefaultGuid) return Visibility.Visible;
                    else return Visibility.Collapsed;
                }
                if (!showGroups) return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ContainsStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string name)
            {
                return (name.StartsWith(ProfiledNodeViewModel.ExecutionTimelString) ||
                    name.Equals(ProfiledNodeViewModel.GroupExecutionTimeString));
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ContainsStringConverter1 : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string name)
            {
                return (name.StartsWith(ProfiledNodeViewModel.ExecutionTimelString));
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AlternatingBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                // Alternate background colors based on index
                return index % 2 == 0 ? new SolidColorBrush(Color.FromRgb(67, 67, 67)) : new SolidColorBrush(Color.FromRgb(211, 211, 211));
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion
}
