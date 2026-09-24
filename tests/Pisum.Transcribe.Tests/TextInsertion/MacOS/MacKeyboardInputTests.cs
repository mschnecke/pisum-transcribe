using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacKeyboardInputTests
{
    private const ulong ShiftFlag = 0x0002_0000;
    private const ulong ControlFlag = 0x0004_0000;
    private const ulong OptionFlag = 0x0008_0000;
    private const ulong CommandFlag = 0x0010_0000;

    private readonly RecordingKeyEvents _events = new();
    private readonly IKeyboardLayout _layout = A.Fake<IKeyboardLayout>();
    private readonly MacKeyboardInput _sut;
    private Action? _layoutChanged;

    public MacKeyboardInputTests()
    {
        A.CallTo(() => _layout.ObserveChanges(A<Action>._))
            .ReturnsLazily((Action changed) =>
            {
                _layoutChanged = changed;
                return A.Fake<IDisposable>();
            });
        _sut = new MacKeyboardInput(_events, _layout, new InlineUiDispatcher(),
            new CapturingLogger<MacKeyboardInput>());
    }

    [Fact]
    public void SendPaste_BeforeStart_PostsUsKeyOfVWithCommandFlagOnly()
    {
        // Act
        _sut.SendPaste();

        // Assert
        _events.Posted.ShouldBe([
            $"key {MacKeyboardInput.DefaultPasteKeyCode} down {CommandFlag:x}",
            $"key {MacKeyboardInput.DefaultPasteKeyCode} up {CommandFlag:x}",
        ]);
    }

    [Fact]
    public async Task SendPaste_LayoutWithVElsewhere_PostsThatKey()
    {
        // Arrange
        A.CallTo(() => _layout.FindPasteKeyCode()).Returns((ushort) 47);
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _sut.SendPaste();

        // Assert
        _events.Posted.ShouldBe([$"key 47 down {CommandFlag:x}", $"key 47 up {CommandFlag:x}"]);
    }

    [Fact]
    public async Task StartAsync_LayoutWithoutV_UsesUsKeyOfV()
    {
        // Arrange
        A.CallTo(() => _layout.FindPasteKeyCode()).Returns(null);

        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _sut.PasteKeyCode.ShouldBe(MacKeyboardInput.DefaultPasteKeyCode);
    }

    [Fact]
    public async Task StartAsync_LayoutThrows_UsesUsKeyOfV()
    {
        // Arrange
        A.CallTo(() => _layout.FindPasteKeyCode()).Throws<InvalidOperationException>();

        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _sut.PasteKeyCode.ShouldBe(MacKeyboardInput.DefaultPasteKeyCode);
    }

    [Fact]
    public async Task LayoutChanged_AfterStart_ReadsThePasteKeyAgain()
    {
        // Arrange
        A.CallTo(() => _layout.FindPasteKeyCode()).Returns((ushort) 9);
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        A.CallTo(() => _layout.FindPasteKeyCode()).Returns((ushort) 47);

        // Act
        _layoutChanged.ShouldNotBeNull().Invoke();

        // Assert
        _sut.PasteKeyCode.ShouldBe((ushort) 47);
    }

    [Fact]
    public async Task StopAsync_AfterStart_StopsObservingTheLayout()
    {
        // Arrange
        var observer = A.Fake<IDisposable>();
        A.CallTo(() => _layout.ObserveChanges(A<Action>._)).Returns(observer);
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await _sut.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        A.CallTo(() => observer.Dispose()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void TypeText_TwoLinesWithEmptyLine_PostsReturnBetweenLinesAndNoEmptyChunk()
    {
        // Act
        _sut.TypeText("Hallo\r\n\nWelt");

        // Assert
        _events.Posted.ShouldBe([
            "text Hallo",
            "key 36 down 0", "key 36 up 0",
            "key 36 down 0", "key 36 up 0",
            "text Welt",
        ]);
    }

    [Fact]
    public void TypeText_LongLine_PostsChunksThatJoinToTheLine()
    {
        // Arrange
        var line = string.Concat(Enumerable.Repeat("Grüße 👋 ", 30));

        // Act
        _sut.TypeText(line);

        // Assert
        var chunks = _events.Posted.Select(posted => posted["text ".Length..]).ToList();
        string.Concat(chunks).ShouldBe(line);
        chunks.ShouldAllBe(chunk => chunk.Length <= MacKeyboardInput.ChunkLength);
    }

    [Fact]
    public void SplitIntoChunks_SurrogatePairAtTheBoundary_KeepsThePairTogether()
    {
        // Arrange: 19 letters, then an emoji whose high surrogate would be the 20th unit.
        var text = new string('a', 19) + "👋" + "b";

        // Act
        var chunks = MacKeyboardInput.SplitIntoChunks(text).ToList();

        // Assert
        chunks.ShouldBe([new string('a', 19), "👋b"]);
    }

    [Fact]
    public void SplitIntoChunks_Empty_ReturnsNoChunk()
    {
        // Act
        var chunks = MacKeyboardInput.SplitIntoChunks("");

        // Assert
        chunks.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0UL, false, false)]
    [InlineData(ShiftFlag, false, true)]
    [InlineData(OptionFlag, false, true)]
    [InlineData(ControlFlag, false, true)]
    [InlineData(CommandFlag, false, false)]
    [InlineData(CommandFlag, true, true)]
    [InlineData(0x0001_0000UL, true, false)] // Caps Lock
    public void AreModifiersDown_Flags_CountsCommandOnlyWhenAsked(ulong flags, bool includePasteModifier, bool expected)
    {
        // Arrange
        _events.Flags = flags;

        // Act
        var down = _sut.AreModifiersDown(includePasteModifier);

        // Assert
        down.ShouldBe(expected);
    }

    private sealed class RecordingKeyEvents : IMacKeyEvents
    {
        public List<string> Posted { get; } = [];

        public ulong Flags { get; set; }

        public void PostKey(ushort keyCode, bool down, ulong flags)
        {
            Posted.Add($"key {keyCode} {(down ? "down" : "up")} {flags:x}");
        }

        public void PostText(string text)
        {
            Posted.Add($"text {text}");
        }

        public ulong ReadFlags()
        {
            return Flags;
        }
    }
}
