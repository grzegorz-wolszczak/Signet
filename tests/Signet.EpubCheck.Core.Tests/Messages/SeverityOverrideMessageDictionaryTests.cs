using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Messages;

public sealed class SeverityOverrideMessageDictionaryTests
{
    [Fact]
    public void GetMessage_GivesTheOverriddenSeverity_AndKeepsTheOriginalOne()
    {
        // Arrange
        LocalizedMessageDictionary inner = new(System.Globalization.CultureInfo.GetCultureInfo("en"));
        Message original = inner.GetMessage(MessageId.OPF_003);
        SeverityOverrideMessageDictionary sut = new(inner, new Dictionary<MessageId, Severity> { [MessageId.OPF_003] = Severity.Suppressed });

        // Act
        Message message = sut.GetMessage(MessageId.OPF_003);

        // Assert
        message.Severity.Should().Be(Severity.Suppressed);
        message.OriginalSeverity.Should().Be(original.OriginalSeverity);
        message.Text.Should().Be(original.Text);
    }

    [Fact]
    public void GetMessage_LeavesTheOtherMessagesAlone()
    {
        // Arrange
        LocalizedMessageDictionary inner = new(System.Globalization.CultureInfo.GetCultureInfo("en"));
        SeverityOverrideMessageDictionary sut = new(inner, new Dictionary<MessageId, Severity> { [MessageId.OPF_003] = Severity.Error });

        // Act
        Message message = sut.GetMessage(MessageId.RSC_005);

        // Assert
        message.Should().Be(inner.GetMessage(MessageId.RSC_005));
    }

    [Fact]
    public void Format_IsTheInnerDictionarys()
    {
        // Arrange
        LocalizedMessageDictionary inner = new(System.Globalization.CultureInfo.GetCultureInfo("en"));
        SeverityOverrideMessageDictionary sut = new(inner, new Dictionary<MessageId, Severity>());
        Message message = inner.GetMessage(MessageId.RSC_005);

        // Act & Assert
        sut.Format(message, "x").Should().Be(inner.Format(message, "x"));
    }
}
