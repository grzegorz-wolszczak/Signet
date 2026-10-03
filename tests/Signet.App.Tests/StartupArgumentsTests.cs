using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Opening the file given on the command line.</summary>
public sealed class StartupArgumentsTests
{
    [Fact]
    public void No_arguments_mean_no_file()
    {
        StartupArguments.FileToOpen(Array.Empty<string>(), Path.GetTempPath()).Should().BeNull();
        StartupArguments.FileToOpen(null, Path.GetTempPath()).Should().BeNull();
        StartupArguments.FileToOpen([" "], Path.GetTempPath()).Should().BeNull();
    }

    [Fact]
    public void Relative_path_is_resolved_against_the_current_directory()
    {
        using TempDir temp = new();

        string? path = StartupArguments.FileToOpen(["book.epub", "ignored"], temp.Path);

        path.Should().Be(Path.Combine(temp.Path, "book.epub"));
    }

    [Fact]
    public void Path_with_spaces_and_a_leading_at_sign_is_a_plain_file()
    {
        using TempDir temp = new();
        StartupArguments.FileToOpen(["@my book.epub"], temp.Path)
            .Should().Be(temp.Combine("@my book.epub"), "\"@\" does not denote a response file here");
    }

    [Fact]
    public async Task Startup_opens_the_file_from_the_command_line()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp, "from-cli.epub");
        MainWindowViewModel vm = new();
        vm.AttachFileWorkflowPrompts(new FakeFileWorkflowPrompts());
        vm.StartupFilePath = epub;

        await vm.RunStartupAsync();

        ((IBookWorkspace)vm).CurrentBook.Should().NotBeNull();
        vm.WindowTitle.Should().Contain("from-cli.epub");
        vm.CheckpointHistory.Dispose();
    }

    [Fact]
    public async Task Missing_file_from_the_command_line_shows_an_error_and_starts_with_a_new_book()
    {
        using TempDir temp = new();
        MainWindowViewModel vm = new();
        FakeFileWorkflowPrompts prompts = new();
        vm.AttachFileWorkflowPrompts(prompts);
        vm.StartupFilePath = temp.Combine("missing.epub");

        await vm.RunStartupAsync();

        prompts.LastError.Should().NotBeNull();
        ((IBookWorkspace)vm).CurrentBook.Should().NotBeNull("after the error a new, empty book is created");
        vm.CheckpointHistory.Dispose();
    }
}
