using System.IO;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>The locks that tell the instances apart that have the same book file open (<see cref="BookFileLocks"/>).</summary>
public sealed class BookFileLocksTests
{
    private static string BookPath(TempDir dir) => dir.Combine(new Fixture().Create<string>() + ".epub");

    [Fact]
    public void A_file_held_by_one_lock_cannot_be_locked_again_until_it_is_released()
    {
        // Arrange
        using TempDir dir = new();
        BookFileLocks locks = new(dir.Combine("locks"));
        string book = BookPath(dir);

        // Act
        using BookFileLock? first = locks.TryAcquire(book);

        // Assert
        locks.TryAcquire(book).Should().BeNull("another instance has the file");

        first.Should().NotBeNull();

        // Act
        first!.Dispose();
        using BookFileLock? again = locks.TryAcquire(book);

        // Assert
        again.Should().NotBeNull("the file is free once the lock is released");
    }

    [Fact]
    public void Different_files_do_not_lock_each_other_and_the_book_file_itself_is_not_touched()
    {
        // Arrange
        using TempDir dir = new();
        BookFileLocks locks = new(dir.Combine("locks"));
        string a = BookPath(dir);
        string b = BookPath(dir);

        // Act
        using BookFileLock? lockA = locks.TryAcquire(a);
        using BookFileLock? lockB = locks.TryAcquire(b);

        // Assert
        lockA.Should().NotBeNull();
        lockB.Should().NotBeNull();
        File.Exists(a).Should().BeFalse("the lock does not create anything next to the book");
        Directory.GetFiles(dir.Path).Should().BeEmpty();
    }

    [Fact]
    public void Two_spellings_of_the_same_path_are_the_same_file()
    {
        // Arrange
        using TempDir dir = new();
        BookFileLocks locks = new(dir.Combine("locks"));
        string book = BookPath(dir);
        string alias = Path.Combine(dir.Path, "sub", "..", Path.GetFileName(book));

        // Act
        using BookFileLock? first = locks.TryAcquire(book);

        // Act & Assert
        BookFileLocks.KeyOf(alias).Should().Be(BookFileLocks.KeyOf(book));

        // Act & Assert
        locks.TryAcquire(alias).Should().BeNull();

        // Assert
        first.Should().NotBeNull();
    }

    [Fact]
    public void A_lock_that_cannot_be_created_does_not_stop_the_book_from_being_opened()
    {
        // Arrange
        using TempDir dir = new();
        string blocker = dir.Combine("not-a-folder");
        File.WriteAllText(blocker, "x");
        BookFileLocks locks = new(blocker);

        // Act
        using BookFileLock? taken = locks.TryAcquire(BookPath(dir));

        // Assert
        taken.Should().NotBeNull("the file is treated as free when no lock can be taken");
    }
}
