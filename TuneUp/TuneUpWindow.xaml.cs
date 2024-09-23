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
using Dynamo.UI;
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
        /// A flag indicating whether the current selection change in the DataGrid 
        /// is initiated by the user (true) or programmatically (false).
        /// </summary>
        private bool isUserInitiatedSelection = false;
                
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

        /// <summary>
        /// Forwards the mouse wheel scroll event from the DataGrid to the parent ScrollViewer,
        /// enabling scrolling when the mouse is over the DataGrid.
        /// </summary>
        private void DataGrid_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            ScrollViewer scrollViewer = FindParent<ScrollViewer>((DataGrid)sender);

            if (scrollViewer != null)
            {
                if (e.Delta > 0)
                {
                    scrollViewer.LineUp();
                }
                else
                {
                    scrollViewer.LineDown();
                }

                e.Handled = true;
            }
        }

        /// <summary>
        /// Recursively searches the visual tree to find the parent of the specified type T for a given child element.
        /// </summary>
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;

            T parent = parentObject as T;
            if (parent != null)
            {
                return parent;
            }
            else
            {
                return FindParent<T>(parentObject);
            }
        }

        private void RecomputeGraph_Click(object sender, RoutedEventArgs e)
        {
            (LatestRunTable.DataContext as TuneUpWindowViewModel).ResetProfiling();
        }

        /// <summary>
        /// Handles the sorting event for the LatestRunTable DataGrid.
        /// Updates the SortingOrder property in the view model based on the column header clicked by the user.
        /// </summary>
        private void LatestRunTable_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var viewModel = LatestRunTable.DataContext as TuneUpWindowViewModel;
            if (viewModel != null)
            {
                viewModel.SortingOrder = e.Column.Header switch
                {
                    "#" => TuneUpWindowViewModel.SortByNumber,
                    "Name" => TuneUpWindowViewModel.SortByName,
                    "Execution Time (ms)" => TuneUpWindowViewModel.SortByTime,
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
            return new System.Windows.Thickness(-3,0,0,0);
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

    public class IsGroupAndBackgroundToForegroundConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush LightBrush = new SolidColorBrush((Color)SharedDictionaryManager.DynamoColorsAndBrushesDictionary["WhiteColor"]);
        private static readonly SolidColorBrush DarkBrush = new SolidColorBrush((Color)SharedDictionaryManager.DynamoColorsAndBrushesDictionary["DarkerGrey"]);

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 2 ||
                !(values[0] is bool isGroup) ||
                !(values[1] is SolidColorBrush backgroundBrush))
            {
                return LightBrush;
            }

            if (!isGroup)
            {
                return LightBrush;
            }
            var backgroundColor = backgroundBrush.Color;
            var contrastRatio = GetContrastRatio((Color)SharedDictionaryManager.DynamoColorsAndBrushesDictionary["DarkerGrey"], backgroundColor);

            return contrastRatio < 4.5 ? LightBrush : DarkBrush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private double GetContrastRatio(Color foreground, Color background)
        {
            double L1 = GetRelativeLuminance(foreground);
            double L2 = GetRelativeLuminance(background);

            return L1 > L2 ? (L1 + 0.05) / (L2 + 0.05) : (L2 + 0.05) / (L1 + 0.05);
        }

        private double GetRelativeLuminance(Color color)
        {
            var R = color.R / 255.0;
            var G = color.G / 255.0;
            var B = color.B / 255.0;

            R = R < 0.03928 ? R / 12.92 : Math.Pow((R + 0.055) / 1.055, 2.4);
            G = G < 0.03928 ? G / 12.92 : Math.Pow((G + 0.055) / 1.055, 2.4);
            B = B < 0.03928 ? B / 12.92 : Math.Pow((B + 0.055) / 1.055, 2.4);

            return 0.2126 * R + 0.7152 * G + 0.0722 * B;
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

    #endregion
}
