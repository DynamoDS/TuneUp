using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Dynamo.Extensions;
using Dynamo.Graph;
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

            // Ctrl+C and the table context menu copy the selected rows
            foreach (var table in new[] { LatestRunTable, PreviousRunTable, NotExecutedTable })
            {
                table.CommandBindings.Add(new System.Windows.Input.CommandBinding(
                    System.Windows.Input.ApplicationCommands.Copy,
                    CopySelectedRows_Executed,
                    CopySelectedRows_CanExecute));
                table.PreviewKeyDown += NodeAnalysisTable_PreviewKeyDown;
            }
        }

        /// <summary>
        /// Copies the selected rows on Ctrl+C, before the key can reach Dynamo, where Ctrl+C copies nodes.
        /// </summary>
        private void NodeAnalysisTable_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.C ||
                System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control)
            {
                return;
            }

            var table = (DataGrid)sender;
            if (table.SelectedItems.Count == 0) return;

            CopySelectedRows(table);
            e.Handled = true;
        }

        private void NodeAnalysisTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isUserInitiatedSelection) return;

            var addedItem = e.AddedItems.OfType<ProfiledNodeViewModel>().LastOrDefault();

            if (addedItem == null) return;

            // Extract the GUID and type (NodeModel or GroupModel)
            var addedModel = addedItem.NodeModel ?? (ModelBase)addedItem.GroupModel;

            if (addedModel != null)
            {
                // Clear the selection in other DataGrids based on the sender
                if (sender == LatestRunTable)
                {
                    NotExecutedTable.UnselectAll();
                    PreviousRunTable.UnselectAll();
                }
                else if (sender == NotExecutedTable)
                {
                    LatestRunTable.UnselectAll();
                    PreviousRunTable.UnselectAll();
                }
                else if (sender == PreviousRunTable)
                {
                    LatestRunTable.UnselectAll();
                    NotExecutedTable.UnselectAll();
                }

                // Select every node and group selected in the table, and zoom to the one just added
                var selectedGuids = ((DataGrid)sender).SelectedItems.OfType<ProfiledNodeViewModel>()
                    .Select(item => item.NodeModel ?? (ModelBase)item.GroupModel)
                    .Where(model => model != null)
                    .Select(model => model.GUID)
                    .ToArray();

                // Dynamo only replaces the canvas selection when one GUID is selected, so clear it first for several
                if (selectedGuids.Length > 1)
                {
                    var clearCommand = new DynamoModel.SelectModelCommand(Guid.Empty, ModifierKeys.None);
                    commandExecutive.ExecuteCommand(clearCommand, uniqueId, "TuneUp");
                }

                var command = new DynamoModel.SelectModelCommand(selectedGuids, ModifierKeys.None);
                commandExecutive.ExecuteCommand(command, uniqueId, "TuneUp");

                viewModelCommandExecutive.FindByIdCommand(addedModel.GUID.ToString());

                // Running a Dynamo command moves keyboard focus to the Dynamo window, so keys like Ctrl+C
                // and Ctrl+A would act on the canvas instead of this table. Give focus back to the table.
                var table = (DataGrid)sender;
                table.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Input,
                    new Action(() => table.Focus()));
            }
        }

        private void CopySelectedRows_CanExecute(object sender, System.Windows.Input.CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ((DataGrid)sender).SelectedItems.Count > 0;
            e.Handled = true;
        }

        private void CopySelectedRows_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            CopySelectedRows((DataGrid)sender);
            e.Handled = true;
        }

        /// <summary>
        /// Copies the selected rows to the clipboard as tab-separated text, in the order the table shows them.
        /// Each row includes the node or group GUID, so the rows can be matched to the graph.
        /// </summary>
        private void CopySelectedRows(DataGrid table)
        {
            var viewModel = table.DataContext as TuneUpWindowViewModel;
            if (viewModel == null) return;

            var selectedRows = table.Items.OfType<ProfiledNodeViewModel>()
                .Where(row => table.SelectedItems.Contains(row));

            Clipboard.SetText(viewModel.GetRowsAsTabSeparatedText(selectedRows));
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

        /// <summary>
        /// Sets focus on the ScrollViewer when it is loaded to ensure immediate scroll functionality.
        /// </summary>
        private void ScrollViewer_Loaded(object sender, RoutedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.Focus();
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
                string headerText = e.Column.Header.ToString();

                viewModel.SortingOrder = headerText switch
                {
                    var h when h == Properties.Resources.Header_ExecutionOrder => TuneUpWindowViewModel.SortByNumber,
                    var h when h == Properties.Resources.Header_Name => TuneUpWindowViewModel.SortByName,
                    var h when h == Properties.Resources.Header_ExecutionTime => TuneUpWindowViewModel.SortByTime,
                    _ => viewModel.SortingOrder
                };

                // Set the sorting direction of the datagrid column
                e.Column.SortDirection = viewModel.SortDirection == ListSortDirection.Descending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;

                // Apply custom sorting to ensure total times are at the bottom
                viewModel.ApplyCustomSortingToAllCollections();
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
