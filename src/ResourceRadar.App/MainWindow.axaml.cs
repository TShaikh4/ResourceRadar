using Avalonia.Controls;

namespace ResourceRadar.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Closing += (_, _) =>
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        };
    }
}
