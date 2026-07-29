using BackgroundStudio.Core;
using Xunit;

namespace BackgroundStudio.Tests;

public sealed class EditHistoryTests
{
    [Fact]
    public void HistoryKeepsIndependentOriginalBasedStatesAndSkipsDuplicates()
    {
        var original = Options(ForegroundFilter.Original);
        var comic = Options(ForegroundFilter.Comic);
        var grayscale = Options(ForegroundFilter.Grayscale);
        var history = new EditHistory();

        history.Reset("분리 원본", new EditState(original, null));
        Assert.True(history.Commit("필터 · 코믹 하이라이트", new EditState(comic, null)));
        Assert.True(history.Commit("필터 · 흑백", new EditState(grayscale, null)));
        Assert.False(history.Commit("중복", new EditState(grayscale, null)));

        var selected = history.Select(history.Items[1].Id);

        Assert.Equal(comic, selected?.State.Options);
        Assert.Equal(original, history.Items[0].State.Options);
        Assert.Equal(3, history.Items.Count);
    }

    [Fact]
    public void HistoryKeepsOnlyTheMostRecentEntries()
    {
        var history = new EditHistory();
        for (var index = 0; index < EditHistory.MaximumEntries + 5; index++)
        {
            history.Commit(
                $"편집 {index}",
                new EditState(Options(ForegroundFilter.Original) with { Rotation = index }, null));
        }

        Assert.Equal(EditHistory.MaximumEntries, history.Items.Count);
        Assert.Equal(EditHistory.MaximumEntries + 4, history.Selected?.State.Options.Rotation);
    }

    private static EditOptions Options(ForegroundFilter filter) => new(
        BackgroundMode.Transparent,
        "#FFFFFF",
        18,
        0,
        0.35,
        0,
        12,
        0.45,
        0.18,
        filter,
        RenderMode.Composite,
        1,
        0,
        0,
        true,
        3,
        "#111111",
        1,
        1,
        1,
        0,
        0,
        1,
        0,
        false,
        false,
        0,
        CanvasAspect.Original);
}
