using System;
using Avalonia.Controls;

namespace Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        Closing += (_, _) => OnClosing();
    }

    private void OnClosing()
    {
        if (Content is MainView mainView) mainView.Close();
    }
}
