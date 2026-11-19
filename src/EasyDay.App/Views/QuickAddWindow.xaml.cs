using System.Windows;
using System.Windows.Input;

namespace EasyDay.App.Views;

public partial class QuickAddWindow : Window
{
    public QuickAddWindow(string? initialText = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(initialText))
        {
            InputBox.Text = initialText;
            InputBox.CaretIndex = InputBox.Text.Length;
            StatusText.Text = $"已从剪贴板填入 {initialText.Length} 字 · Enter 解析 · Esc 关闭";
        }
        else
        {
            StatusText.Text = "直接输入，或点「粘贴」· Enter 解析 · Esc 关闭";
        }

        Loaded += (_, _) => InputBox.Focus();
    }

    public string InputText => InputBox.Text.Trim();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        InputBox.Clear();
        StatusText.Text = "已清空";
        InputBox.Focus();
    }

    private void Parse_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text))
        {
            InputBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                InputBox.Text = Clipboard.GetText();
                InputBox.CaretIndex = InputBox.Text.Length;
                InputBox.Focus();
            }
        }
        catch
        {
            // 剪贴板被占用，忽略
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            Parse_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
