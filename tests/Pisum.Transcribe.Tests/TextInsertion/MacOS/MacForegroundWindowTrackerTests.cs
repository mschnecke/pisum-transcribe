using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacForegroundWindowTrackerTests
{
    private const int ProcessId = 42;

    private readonly FakeReader _reader = new();
    private readonly MacForegroundWindowTracker _sut;

    public MacForegroundWindowTrackerTests()
    {
        _sut = new MacForegroundWindowTracker(_reader);
    }

    [Fact]
    public void CaptureForeground_FocusedWindow_ReturnsANonzeroTokenAndTheProcess()
    {
        // Arrange
        _reader.Focus(ProcessId, 100);

        // Act
        var target = _sut.CaptureForeground();

        // Assert
        target.Window.ShouldNotBe(0);
        target.ProcessId.ShouldBe(ProcessId);
        target.IsElevated.ShouldBeFalse();
    }

    [Fact]
    public void CaptureForeground_NoFocusedWindow_ReturnsToken0()
    {
        // Act
        var target = _sut.CaptureForeground();

        // Assert
        target.ShouldBe(new InsertionTarget(0, 0, false));
    }

    [Fact]
    public void IsForeground_SameWindowStillFocused_ReturnsTrueAndReleasesTheReadElement()
    {
        // Arrange
        _reader.Focus(ProcessId, 100);
        var target = _sut.CaptureForeground();

        // Act
        var isForeground = _sut.IsForeground(target);

        // Assert
        isForeground.ShouldBeTrue();
        _reader.Outstanding.ShouldBe(1);
    }

    [Fact]
    public void IsForeground_AnotherWindowOfTheSameApplication_ReturnsFalse()
    {
        // Arrange
        _reader.Focus(ProcessId, 100);
        var target = _sut.CaptureForeground();
        _reader.Focus(ProcessId, 200);

        // Act
        var isForeground = _sut.IsForeground(target);

        // Assert
        isForeground.ShouldBeFalse();
    }

    [Fact]
    public void IsForeground_FocusedWindowCannotBeRead_ReturnsFalse()
    {
        // Arrange
        _reader.Focus(ProcessId, 100);
        var target = _sut.CaptureForeground();
        _reader.Unfocus();

        // Act
        var isForeground = _sut.IsForeground(target);

        // Assert
        isForeground.ShouldBeFalse();
    }

    [Fact]
    public void IsForeground_TokenOfAnOlderCapture_ReturnsFalse()
    {
        // Arrange
        _reader.Focus(ProcessId, 100);
        var old = _sut.CaptureForeground();
        _sut.CaptureForeground();

        // Act
        var isForeground = _sut.IsForeground(old);

        // Assert
        isForeground.ShouldBeFalse();
    }

    [Fact]
    public void CaptureForeground_Twice_ReleasesTheFirstElementOnce()
    {
        // Arrange
        _reader.Focus(ProcessId, 100);
        _sut.CaptureForeground();

        // Act
        _sut.CaptureForeground();

        // Assert
        _reader.Outstanding.ShouldBe(1);
        _reader.DoubleReleases.ShouldBe(0);
    }

    [Fact]
    public void CaptureForeground_NoWindowAfterAWindow_ReleasesTheOldElement()
    {
        // Arrange
        _reader.Focus(ProcessId, 100);
        _sut.CaptureForeground();
        _reader.Unfocus();

        // Act
        _sut.CaptureForeground();

        // Assert
        _reader.Outstanding.ShouldBe(0);
    }

    [Fact]
    public void Dispose_AfterCapture_ReleasesEveryElement()
    {
        // Arrange
        _reader.Focus(ProcessId, 100);
        var target = _sut.CaptureForeground();
        _sut.IsForeground(target);

        // Act
        _sut.Dispose();

        // Assert
        _reader.Outstanding.ShouldBe(0);
        _reader.DoubleReleases.ShouldBe(0);
    }

    /// <summary>
    /// Hands out a new element per read, whose identity is the focused window, and counts retains and releases.
    /// </summary>
    private sealed class FakeReader : IFocusedWindowReader
    {
        private readonly Dictionary<nint, int> _windowOfElement = [];
        private int? _processId;
        private int _window;
        private nint _nextElement = 1;

        public int Outstanding => _windowOfElement.Count;

        public int DoubleReleases { get; private set; }

        public void Focus(int processId, int window)
        {
            _processId = processId;
            _window = window;
        }

        public void Unfocus()
        {
            _processId = null;
        }

        public bool TryRead(out int processId, out nint window)
        {
            processId = _processId ?? 0;
            window = 0;
            if (_processId is null)
            {
                return false;
            }

            window = _nextElement++;
            _windowOfElement[window] = _window;
            return true;
        }

        public bool AreSameWindow(nint first, nint second)
        {
            return _windowOfElement[first] == _windowOfElement[second];
        }

        public void Release(nint window)
        {
            if (!_windowOfElement.Remove(window))
            {
                DoubleReleases++;
            }
        }
    }
}
