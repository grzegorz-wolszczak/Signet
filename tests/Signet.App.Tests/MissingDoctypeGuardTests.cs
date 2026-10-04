using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Moq;
using Signet.App.Services;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests of <see cref="MissingDoctypeGuard"/>: when the "missing DOCTYPE" dialog is shown, and that
/// "Don't ask again" is remembered only together with continuing.
/// </summary>
public sealed class MissingDoctypeGuardTests
{
    private static SettingsStore NewSettings(TempDir temp) => new(Path.Combine(temp.Path, "settings.json"));

    private static (MissingDoctypeGuard Guard, Mock<IMissingDoctypePrompt> Prompt) NewGuard(
        SettingsStore settings, MissingDoctypeAnswer answer)
    {
        Mock<IMissingDoctypePrompt> prompt = new();
        prompt.Setup(p => p.AskAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).ReturnsAsync(answer);
        return (new MissingDoctypeGuard(settings) { Prompt = prompt.Object }, prompt);
    }

    [Theory]
    [AutoData]
    public async Task Continue_runs_the_operation_and_keeps_asking(string operation, List<string> files)
    {
        using TempDir temp = new();
        SettingsStore settings = NewSettings(temp);
        var (guard, prompt) = NewGuard(settings, MissingDoctypeAnswer.Continue);

        bool proceed = await guard.ConfirmAsync(operation, files);

        proceed.Should().BeTrue();
        settings.WarnMissingDoctype.Should().BeTrue();
        prompt.Verify(p => p.AskAsync(operation, files), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task Cancel_stops_the_operation_and_keeps_asking(string operation, List<string> files)
    {
        using TempDir temp = new();
        SettingsStore settings = NewSettings(temp);
        var (guard, _) = NewGuard(settings, MissingDoctypeAnswer.Cancel);

        bool proceed = await guard.ConfirmAsync(operation, files);

        proceed.Should().BeFalse();
        settings.WarnMissingDoctype.Should().BeTrue();
    }

    [Theory]
    [AutoData]
    public async Task Dont_ask_again_runs_the_operation_and_persists_the_choice(string operation, List<string> files)
    {
        using TempDir temp = new();
        SettingsStore settings = NewSettings(temp);
        var (guard, prompt) = NewGuard(settings, MissingDoctypeAnswer.ContinueAndDontAskAgain);

        bool first = await guard.ConfirmAsync(operation, files);
        bool second = await guard.ConfirmAsync(operation, files);

        first.Should().BeTrue();
        second.Should().BeTrue();
        prompt.Verify(p => p.AskAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Once);
        NewSettings(temp).WarnMissingDoctype.Should().BeFalse("the choice is saved to the settings file");
    }

    [Theory]
    [AutoData]
    public async Task No_files_without_doctype_means_no_question(string operation)
    {
        using TempDir temp = new();
        var (guard, prompt) = NewGuard(NewSettings(temp), MissingDoctypeAnswer.Cancel);

        bool proceed = await guard.ConfirmAsync(operation, new List<string>());

        proceed.Should().BeTrue();
        prompt.VerifyNoOtherCalls();
    }

    [Theory]
    [AutoData]
    public async Task Disabled_warning_means_no_question(string operation, List<string> files)
    {
        using TempDir temp = new();
        SettingsStore settings = NewSettings(temp);
        settings.WarnMissingDoctype = false;
        var (guard, prompt) = NewGuard(settings, MissingDoctypeAnswer.Cancel);

        bool proceed = await guard.ConfirmAsync(operation, files);

        proceed.Should().BeTrue();
        prompt.VerifyNoOtherCalls();
    }

    [Theory]
    [AutoData]
    public async Task Without_a_prompt_the_operation_runs(string operation, List<string> files)
    {
        using TempDir temp = new();
        MissingDoctypeGuard guard = new(NewSettings(temp));

        bool proceed = await guard.ConfirmAsync(operation, files);

        proceed.Should().BeTrue();
    }
}
