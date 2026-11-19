using EasyDay.Core.Ai;
using EasyDay.Core.Models;

namespace EasyDay.Core.Tests;

public class AiResponseMapperTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 10, 0, 0);

    [Fact]
    public void Maps_questions_from_ai_response()
    {
        const string json = """
        {
          "events": [
            {
              "title": "项目评审",
              "start": "2026-11-18T15:00:00",
              "end": "2026-11-18T16:00:00",
              "confidence": 0.8,
              "questions": [
                { "field": "time", "question": "是下午 3 点还是 4 点？", "options": ["下午 3 点", "下午 4 点"] }
              ]
            }
          ]
        }
        """;

        var drafts = AiResponseMapper.Map(json, "明天评审", EventSource.Clipboard, Now);

        var draft = Assert.Single(drafts);
        var question = Assert.Single(draft.Questions);
        Assert.Equal("time", question.Field);
        Assert.Equal(2, question.Options.Count);
    }

    [Fact]
    public void Adds_fallback_question_for_low_confidence()
    {
        const string json = """
        {"events":[{"title":"开会","start":"2026-11-18T09:00:00","confidence":0.4}]}
        """;

        var drafts = AiResponseMapper.Map(json, "开会", EventSource.QuickAdd, Now);

        var draft = Assert.Single(drafts);
        Assert.Single(draft.Questions);
        Assert.Contains("具体", draft.Questions[0].Question);
    }

    [Fact]
    public void No_questions_for_confident_event()
    {
        const string json = """
        {"events":[{"title":"项目评审","start":"2026-11-18T15:00:00","end":"2026-11-18T16:00:00","confidence":0.95,"questions":[]}]}
        """;

        var drafts = AiResponseMapper.Map(json, "明天15点评审", EventSource.Clipboard, Now);

        Assert.Empty(Assert.Single(drafts).Questions);
    }
}
