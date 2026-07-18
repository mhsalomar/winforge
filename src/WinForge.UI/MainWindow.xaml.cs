using System.Windows;

namespace WinForge.UI;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
