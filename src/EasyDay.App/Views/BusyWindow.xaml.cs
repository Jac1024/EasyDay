using System.Windows;

namespace EasyDay.App.Views;

public partial class BusyWindow : Window
{
    public BusyWindow(string message = "AI 识别中…")
    {
        InitializeComponent();
        MessageText.Text = message;
    }
}
