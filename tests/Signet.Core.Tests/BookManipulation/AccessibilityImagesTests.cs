using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of the image descriptions of Tools → Accessibility (<see cref="AccessibilityImages"/>).</summary>
public sealed class AccessibilityImagesTests
{
    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"en\">\n<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    private static (Book Book, HtmlResource Html) NewBook(string body)
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml(body));
        return (book, html);
    }

    private static ImageDescriptionEdit Edit(AccessibilityImage image, string description, bool decorative = false) =>
        new(image.BookPath, image.Offset, description, decorative);

    // --- listing ---

    [Fact]
    public void The_images_are_listed_in_reading_order_with_their_description_and_state()
    {
        // Arrange
        (Book book, HtmlResource first) = NewBook(
            "<img src=\"../Images/a.png\" alt=\"A cat\"/>\n<img src=\"b.png\"/>\n<img src=\"c.png\" alt=\"\"/>\n"
            + "<img src=\"d.png\" role=\"presentation\"/>\n<img src=\"e.png\" alt=\"E\" aria-hidden=\"true\"/>");
        using (book)
        {
            // Act
            IReadOnlyList<AccessibilityImage> images = AccessibilityImages.List(book);

            // Assert
            images.Select(i => i.Kind).Should().OnlyContain(k => k == AccessibilityImageKind.Img);
            images.Select(i => i.BookPath).Should().OnlyContain(p => p == first.BookPath);
            images.Select(i => i.Line).Should().Equal(8, 9, 10, 11, 12);
            images.Select(i => i.Description).Should().Equal("A cat", string.Empty, string.Empty, string.Empty, "E");
            images.Select(i => i.HasDescription).Should().Equal(true, false, false, false, true);
            images.Select(i => i.IsDecorative).Should().Equal(false, false, true, true, true);
            images[0].Source.Should().Be(Core.BookPath.BuildBookPath("../Images/a.png", first.Folder));
            images.Select(i => i.Offset).Should().BeInAscendingOrder();
        }
    }

    [Fact]
    public void The_outermost_svg_images_are_listed_with_the_description_a_screen_reader_would_use()
    {
        // Arrange
        (Book book, _) = NewBook(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><title>Title text</title><svg><title>nested</title></svg></svg>\n"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\" aria-label=\"Label\"><title>Other</title></svg>\n"
            + "<p id=\"d\">Referenced text</p><svg xmlns=\"http://www.w3.org/2000/svg\" aria-labelledby=\"d\" aria-label=\"Ignored\"/>\n"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\" aria-labelledby=\"nowhere\"/>\n"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\" aria-hidden=\"true\"/>");
        using (book)
        {
            // Act
            IReadOnlyList<AccessibilityImage> images = AccessibilityImages.List(book);

            // Assert
            images.Should().HaveCount(5, "the nested svg is part of the first image");
            images.Select(i => i.Kind).Should().OnlyContain(k => k == AccessibilityImageKind.Svg);
            images.Select(i => i.Description).Should().Equal("Title text", "Label", "Referenced text", string.Empty, string.Empty);
            images.Select(i => i.IsDecorative).Should().Equal(false, false, false, false, true);
        }
    }

    [Fact]
    public void The_files_are_listed_in_the_order_of_the_spine()
    {
        // Arrange
        (Book book, HtmlResource first) = NewBook("<img src=\"a.png\" alt=\"one\"/>");
        using (book)
        {
            HtmlResource second = book.CreateEmptyHtmlFile();
            second.SetText(Xhtml("<img src=\"b.png\" alt=\"two\"/>"));
            book.GetOpf().UpdateSpineOrder([second, first]);

            // Act
            IReadOnlyList<AccessibilityImage> images = AccessibilityImages.List(book);

            // Assert
            images.Select(i => i.Description).Should().Equal("two", "one");
        }
    }

    // --- applying ---

    [Fact]
    public void A_description_is_written_to_an_img_and_replaces_the_one_it_had()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<img src=\"a.png\"/>\n<img src=\"b.png\" alt=\"old\" class=\"c\"/>");
        using (book)
        {
            IReadOnlyList<AccessibilityImage> images = AccessibilityImages.List(book);

            // Act
            int changed = AccessibilityImages.Apply(book, [Edit(images[0], "A \"cat\" & dog"), Edit(images[1], "new")]);

            // Assert
            changed.Should().Be(2);
            html.GetText().Should().Contain("<img src=\"a.png\" alt=\"A &quot;cat&quot; &amp; dog\"/>").And.Contain("<img src=\"b.png\" alt=\"new\" class=\"c\"/>");
            AccessibilityImages.List(book).Select(i => i.Description).Should().Equal("A \"cat\" & dog", "new");
        }
    }

    [Fact]
    public void An_img_marked_as_decoration_gets_an_empty_alt_and_a_presentation_role_and_unmarking_it_removes_them()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<img src=\"a.png\" alt=\"x\"/>");
        using (book)
        {
            // Act
            AccessibilityImages.Apply(book, [Edit(AccessibilityImages.List(book)[0], string.Empty, decorative: true)]);

            // Assert
            html.GetText().Should().Contain("alt=\"\"").And.Contain("role=\"presentation\"");
            AccessibilityImages.List(book)[0].IsDecorative.Should().BeTrue();

            // Act
            AccessibilityImages.Apply(book, [Edit(AccessibilityImages.List(book)[0], "A cat")]);

            // Assert
            html.GetText().Should().Contain("alt=\"A cat\"").And.NotContain("role=");
            AccessibilityImages.List(book)[0].IsDecorative.Should().BeFalse();
        }
    }

    [Fact]
    public void An_img_without_description_that_is_not_decorative_has_no_alt()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<img src=\"a.png\" alt=\"x\"/>");
        using (book)
        {
            // Act
            AccessibilityImages.Apply(book, [Edit(AccessibilityImages.List(book)[0], string.Empty)]);

            // Assert
            html.GetText().Should().NotContain("alt=");
        }
    }

    [Fact]
    public void Only_real_changes_are_written()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<img src=\"a.png\" alt=\"same\"/>\n<img src=\"b.png\" alt=\"\" role=\"presentation\"/>");
        using (book)
        {
            string text = html.GetText();
            IReadOnlyList<AccessibilityImage> images = AccessibilityImages.List(book);

            // Act
            int changed = AccessibilityImages.Apply(book, [Edit(images[0], "same"), Edit(images[1], string.Empty, decorative: true)]);

            // Assert
            changed.Should().Be(0);
            html.GetText().Should().Be(text);
        }
    }

    [Fact]
    public void An_edit_for_a_place_that_is_no_longer_an_image_is_skipped()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<img src=\"a.png\"/>");
        using (book)
        {
            AccessibilityImage image = AccessibilityImages.List(book)[0];
            html.SetText(Xhtml("<p>changed</p>"));

            // Act
            int changed = AccessibilityImages.Apply(book, [Edit(image, "x")]);

            // Assert
            changed.Should().Be(0);
            html.GetText().Should().NotContain("alt=");
        }
    }

    [Fact]
    public void A_description_of_an_svg_is_an_aria_label_that_replaces_the_reference_and_keeps_the_title_in_step()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p id=\"d\">Referenced</p><svg xmlns=\"http://www.w3.org/2000/svg\" aria-labelledby=\"d\"><title>Old title</title></svg>\n"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle/></svg>");
        using (book)
        {
            IReadOnlyList<AccessibilityImage> images = AccessibilityImages.List(book);

            // Act
            AccessibilityImages.Apply(book, [Edit(images[0], "New <text>"), Edit(images[1], "Second")]);

            // Assert
            string text = html.GetText();
            text.Should().Contain("aria-label=\"New &lt;text&gt;\"").And.NotContain("aria-labelledby").And.Contain("<title>New &lt;text&gt;</title>")
                .And.Contain("<p id=\"d\">Referenced</p>", "the text a reference named is never changed")
                .And.Contain("aria-label=\"Second\"");
            AccessibilityImages.List(book).Select(i => i.Description).Should().Equal("New <text>", "Second");
        }
    }

    [Fact]
    public void An_svg_can_be_marked_as_decoration_and_have_its_description_removed()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" aria-label=\"L\"><title>T</title></svg>\n"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\" aria-label=\"L2\"><title id=\"t2\">T2</title></svg>");
        using (book)
        {
            IReadOnlyList<AccessibilityImage> images = AccessibilityImages.List(book);

            // Act
            AccessibilityImages.Apply(book, [Edit(images[0], string.Empty), Edit(images[1], string.Empty, decorative: true)]);

            // Assert
            string text = html.GetText();
            text.Should().NotContain("aria-label=\"L\"").And.NotContain(">T<", "the title of the first image went with its description");
            text.Should().Contain("aria-hidden=\"true\"");
            IReadOnlyList<AccessibilityImage> after = AccessibilityImages.List(book);
            after[0].HasDescription.Should().BeFalse();
            after[1].IsDecorative.Should().BeTrue();

            // Act
            AccessibilityImages.Apply(book, [Edit(after[1], "Described again")]);

            // Assert
            html.GetText().Should().NotContain("aria-hidden").And.Contain("aria-label=\"Described again\"");
        }
    }
}
