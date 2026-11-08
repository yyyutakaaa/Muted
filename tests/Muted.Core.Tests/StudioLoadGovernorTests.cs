using Muted.Core.Audio;

namespace Muted.Core.Tests;

public sealed class StudioLoadGovernorTests
{
    private const StudioModel Maximum = StudioModel.DpdfNet8;
    private const StudioModel Balanced = StudioModel.DpdfNet2;

    private static StudioModel Run(StudioLoadGovernor governor, StudioModel active, float load, double seconds, bool underrun = false)
    {
        var result = Maximum;
        var frames = (int)(seconds * StudioLoadGovernor.FramesPerSecond);
        for (var frame = 0; frame < frames; frame++)
        {
            result = governor.Observe(Maximum, active, switching: false, load, underrun && frame % 25 == 0, enabled: true);
        }

        return result;
    }

    [Fact]
    public void KeepsMaximumWhileItRunsOnTime()
    {
        var governor = new StudioLoadGovernor();
        Assert.Equal(Maximum, Run(governor, Maximum, 0.7f, 120));

        // An occasional late frame is not a crackle.
        for (var second = 0; second < 60; second++)
        {
            Run(governor, Maximum, 0.7f, 0.99);
            Assert.Equal(Maximum, Run(governor, Maximum, 1.3f, 0.01));
        }
    }

    [Fact]
    public void StepsDownWhenFramesAreLateAndComesBackWhenThereIsRoom()
    {
        var governor = new StudioLoadGovernor();
        Assert.Equal(Balanced, Run(governor, Maximum, 1.2f, 0.2));
        Assert.True(governor.SteppedDown);

        // Still busy on Balanced: stay there.
        Assert.Equal(Balanced, Run(governor, Balanced, 0.6f, 120));

        // The game closed: a minute of room, then Maximum again.
        Assert.Equal(Balanced, Run(governor, Balanced, 0.25f, 59));
        Assert.Equal(Maximum, Run(governor, Balanced, 0.25f, 3));
    }

    [Fact]
    public void UnderrunsCountAsCrackles()
    {
        var governor = new StudioLoadGovernor();
        Assert.Equal(Balanced, Run(governor, Maximum, 0.8f, 1, underrun: true));
    }

    [Fact]
    public void WaitsLongerAfterComingBackTooSoon()
    {
        var governor = new StudioLoadGovernor();
        Run(governor, Maximum, 1.2f, 0.2);
        Run(governor, Balanced, 0.25f, 70);
        Assert.False(governor.SteppedDown);

        // Maximum was tried again but the game is still on.
        Assert.Equal(Balanced, Run(governor, Maximum, 1.2f, 0.2));
        Assert.Equal(Balanced, Run(governor, Balanced, 0.25f, 110));
        Assert.Equal(Maximum, Run(governor, Balanced, 0.25f, 30));
        Assert.Equal(2, governor.StepDowns);
    }

    [Fact]
    public void LeavesEveryOtherChoiceAlone()
    {
        var governor = new StudioLoadGovernor();
        for (var frame = 0; frame < 500; frame++)
        {
            Assert.Equal(Balanced, governor.Observe(Balanced, Balanced, false, 1.5f, true, enabled: true));
            Assert.Equal(Maximum, governor.Observe(Maximum, Maximum, false, 1.5f, true, enabled: false));
        }

        Assert.False(governor.SteppedDown);
    }

    [Fact]
    public void IgnoresFramesWhileAModelSwitches()
    {
        var governor = new StudioLoadGovernor();
        for (var frame = 0; frame < 500; frame++)
        {
            Assert.Equal(Maximum, governor.Observe(Maximum, Maximum, switching: true, 2f, true, enabled: true));
        }
    }
}
