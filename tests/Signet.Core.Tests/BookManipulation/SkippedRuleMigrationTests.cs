using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

public sealed class SkippedRuleMigrationTests
{
    [Fact]
    public void Migrate_TurnsAReplacedRuleIntoTheEpubCheckMessage_AndKeepsTheOthers()
    {
        // Act
        System.Collections.Generic.IReadOnlyList<string> result =
            SkippedRuleMigration.Migrate(["Validation_FontCorrupt", "Validation_DeadLink", "OPF-003"]);

        // Assert
        result.Should().Equal("Validation_FontCorrupt", "RSC-007", "OPF-003");
    }

    [Fact]
    public void Migrate_MakesEveryCodeAppearOnce()
    {
        // Act
        System.Collections.Generic.IReadOnlyList<string> result =
            SkippedRuleMigration.Migrate(["Validation_DeadLink", "Validation_CssDeadUrl", "RSC-007"]);

        // Assert
        result.Should().Equal("RSC-007");
    }

    [Fact]
    public void Migrate_DropsAReplacedRuleWhoseMessageWouldHideMoreThanItDid()
    {
        // Act
        System.Collections.Generic.IReadOnlyList<string> result = SkippedRuleMigration.Migrate(["Validation_OpfSpineEmpty", "Validation_Css"]);

        // Assert
        result.Should().Equal("Validation_Css");
    }

    [Theory]
    [InlineData("Validation_DeadLink", true)]
    [InlineData("Validation_OpfDuplicateId", true)]
    [InlineData("Validation_FontCorrupt", false)]
    [InlineData("RSC-007", false)]
    public void NeedsMigration_SaysWhetherAnythingWouldChange(string code, bool expected)
    {
        // Act & Assert
        SkippedRuleMigration.NeedsMigration([code]).Should().Be(expected);
    }
}
