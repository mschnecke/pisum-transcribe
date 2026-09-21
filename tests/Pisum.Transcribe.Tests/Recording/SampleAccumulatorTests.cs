using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SampleAccumulatorTests
{
    [Fact]
    public void Append_SeveralBuffers_KeepsAllSamplesInOrder()
    {
        // Arrange
        var sut = new SampleAccumulator(100);

        // Act
        var full1 = sut.Append([0.1f, 0.2f]);
        var full2 = sut.Append([0.3f]);
        var full3 = sut.Append([-0.4f, -0.5f, -0.6f]);

        // Assert
        sut.ToArray().ShouldBe([0.1f, 0.2f, 0.3f, -0.4f, -0.5f, -0.6f]);
        sut.Count.ShouldBe(6);
        new[] {full1, full2, full3}.ShouldAllBe(full => !full);
    }

    [Fact]
    public void Append_SamplesOutsideRange_ClampsToBounds()
    {
        // Arrange
        var sut = new SampleAccumulator(100);

        // Act
        sut.Append([1.32f, -1.5f, 1f, -1f, 0.5f]);

        // Assert
        sut.ToArray().ShouldBe([1f, -1f, 1f, -1f, 0.5f]);
    }

    [Fact]
    public void Append_BeyondMaxDuration_TruncatesExactlyAtLimitAndReportsIt()
    {
        // Arrange
        var maxCount = SampleAccumulator.MaxCountFor(TimeSpan.FromSeconds(1.5));
        var sut = new SampleAccumulator(maxCount);
        var buffer = Enumerable.Repeat(0.25f, 320).ToArray();

        // Act
        var appends = 0;
        while (!sut.Append(buffer))
        {
            appends++;
        }

        var afterFull = sut.Append(buffer);

        // Assert
        maxCount.ShouldBe(24_000);
        appends.ShouldBe(74);
        sut.Count.ShouldBe(24_000);
        sut.IsFull.ShouldBeTrue();
        afterFull.ShouldBeTrue();
        sut.ToArray().Length.ShouldBe(24_000);
    }

    [Fact]
    public void Append_ExactlyToLimit_ReportsLimit()
    {
        // Arrange
        var sut = new SampleAccumulator(4);

        // Act
        var full = sut.Append([0.1f, 0.2f, 0.3f, 0.4f]);

        // Assert
        full.ShouldBeTrue();
        sut.ToArray().ShouldBe([0.1f, 0.2f, 0.3f, 0.4f]);
    }

    [Fact]
    public void Clear_AfterAppend_ForgetsSamples()
    {
        // Arrange
        var sut = new SampleAccumulator(100);
        sut.Append([0.1f, 0.2f]);

        // Act
        sut.Clear();

        // Assert
        sut.Count.ShouldBe(0);
        sut.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public void ToArray_Called_ReturnsCopy()
    {
        // Arrange
        var sut = new SampleAccumulator(100);
        sut.Append([0.1f]);

        // Act
        var copy = sut.ToArray();
        sut.Clear();

        // Assert
        copy.ShouldBe([0.1f]);
    }
}
