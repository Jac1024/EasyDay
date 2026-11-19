using System.Collections.ObjectModel;
using System.Windows.Input;
using EasyDay.App.Infrastructure;
using EasyDay.Core.Models;

namespace EasyDay.App.ViewModels;

/// <summary>确认卡上的一条追问：用户可以点候选，也可以自己补充。</summary>
public sealed class QuestionViewModel : ObservableObject
{
    private string _answer = string.Empty;

    public QuestionViewModel(ClarificationQuestion question)
    {
        Field = question.Field;
        Text = question.Question;
        Options = new ObservableCollection<string>(question.Options);
        SelectCommand = new RelayCommand(parameter =>
        {
            if (parameter is string option)
            {
                Answer = option;
            }
        });
    }

    public string Field { get; }

    public string Text { get; }

    public ObservableCollection<string> Options { get; }

    public ICommand SelectCommand { get; }

    public string Answer
    {
        get => _answer;
        set => SetProperty(ref _answer, value);
    }

    public bool HasOptions => Options.Count > 0;
}
