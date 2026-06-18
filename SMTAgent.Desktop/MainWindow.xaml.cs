using System.Windows;
using SMTAgent.Desktop.ViewModels;

namespace SMTAgent.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
