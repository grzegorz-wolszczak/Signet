using System;
using System.IO;
using AutoFixture;
using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests.TestSupport;

public sealed class TempDirTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void Dispose_deletes_the_directory_with_its_files()
    {
        // Arrange
        TempDir sut = new();
        File.WriteAllText(sut.Combine(_fixture.Create<string>()), _fixture.Create<string>());

        // Act
        sut.Dispose();

        // Assert
        Directory.Exists(sut.Path).Should().BeFalse();
    }

    [Fact]
    public void Dispose_throws_naming_the_file_that_is_still_open()
    {
        // Arrange
        TempDir sut = new();
        string name = _fixture.Create<Guid>().ToString("N") + ".lock";
        using FileStream held = new(sut.Combine(name), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // Act & Assert
        sut.Invoking(x => x.Dispose()).Should().Throw<IOException>().WithMessage($"*{name}*");

        // Act
        held.Dispose();
        sut.Dispose();

        // Assert
        Directory.Exists(sut.Path).Should().BeFalse();
    }

    [Fact]
    public void Dispose_succeeds_when_a_short_lived_lock_is_released_during_the_retries()
    {
        // Arrange
        TempDir sut = new();
        FileStream held = new(sut.Combine(_fixture.Create<Guid>().ToString("N")), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // A thread of its own: the thread pool of a busy test run can be late by more than the retries last.
        System.Threading.Thread release = new(() =>
        {
            System.Threading.Thread.Sleep(50);
            held.Dispose();
        });
        release.Start();

        // Act
        sut.Dispose();
        release.Join();

        // Assert
        Directory.Exists(sut.Path).Should().BeFalse();
    }
}
