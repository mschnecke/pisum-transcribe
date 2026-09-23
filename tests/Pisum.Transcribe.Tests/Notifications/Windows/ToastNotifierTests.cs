using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Notifications;

namespace Pisum.Transcribe.Tests.Notifications;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ToastNotifierTests
{
    [Fact]
    public void BuildToastXml_TitleAndMessage_HasToastGenericWithBothTexts()
    {
        // Act
        var xml = ToastNotifier.BuildToastXml("Title", "Message");

        // Assert
        var binding = XElement.Parse(xml).Element("visual")!.Element("binding")!;
        binding.Attribute("template")!.Value.ShouldBe("ToastGeneric");
        binding.Elements("text").Select(text => text.Value).ShouldBe(["Title", "Message"]);
    }

    [Fact]
    public void BuildToastXml_TextWithXmlCharacters_EscapesThem()
    {
        // Act
        var xml = ToastNotifier.BuildToastXml("<b>&", "\"x\" </text><text>y");

        // Assert
        xml.ShouldContain("&lt;b&gt;&amp;");
        XElement.Parse(xml).Descendants("text").Select(text => text.Value).ShouldBe(["<b>&", "\"x\" </text><text>y"]);
    }

    [Fact]
    public void Show_NotifierThrows_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var sender = A.Fake<IToastSender>();
        var exception = new InvalidOperationException("Notifications are off");
        A.CallTo(() => sender.Send(A<string>._)).Throws(exception);
        var logger = new CapturingLogger<ToastNotifier>();
        var sut = new ToastNotifier(sender, logger);

        // Act
        Should.NotThrow(() => sut.Show("Secret title", "Secret message"));

        // Assert
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeSameAs(exception);
        entry.Message.ShouldNotContain("Secret");
    }
}
