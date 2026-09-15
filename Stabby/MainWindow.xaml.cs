using Stabby.ViewModels;
using System.Windows;
using System.Windows.Interop;
using Windows.Graphics.Capture;

namespace Stabby;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private async void SelectSource_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new GraphicsCapturePicker();
            var hwnd = new WindowInteropHelper(this).Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var item = await picker.PickSingleItemAsync();
            if (item != null && DataContext is MainViewModel vm)
            {
                vm.SetCaptureSource(item);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Select source failed:\n\n{ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PreviewGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var width = PreviewGrid.ColumnDefinitions[0].ActualWidth - 24;
            vm.UpdatePreviewContainerSize(Math.Max(100, width));
        }
    }
}
