using System;
using System.Collections.Generic;

namespace Signet.Core.Misc;

/// <summary>
/// The CSS highlighter of Code View — a state machine recognizing selectors, properties,
/// values, strings and comments. The line state is encoded as
/// <c>state + (save_state &lt;&lt; 16)</c>.
/// </summary>
public sealed class CssHighlighter : ILineSyntaxHighlighter
{
    private const int Selector = 0;
    private const int Property = 1;
    private const int Value = 2;
    private const int Pseudo = 3;
    private const int Pseudo1 = 4;
    private const int Pseudo2 = 5;
    private const int Quote = 6;
    private const int MaybeComment = 7;
    private const int Comment = 8;
    private const int MaybeCommentEnd = 9;

    private const int Alnum = 0;
    private const int LBrace = 1;
    private const int RBrace = 2;
    private const int Colon = 3;
    private const int Semicolon = 4;
    private const int Comma = 5;
    private const int QuoteToken = 6;
    private const int Slash = 7;
    private const int Star = 8;

    // The state machine transitions (rows = current state, columns = character class).
    private static readonly int[,] Transitions =
    {
        { Selector, Property, Selector, Pseudo,  Property, Selector, Quote,   MaybeComment, Selector },        // Selector
        { Property, Property, Selector, Value,   Property, Property, Quote,   MaybeComment, Property },        // Property
        { Value,    Property, Selector, Value,   Property, Value,    Quote,   MaybeComment, Value },           // Value
        { Pseudo1,  Property, Selector, Pseudo2, Selector, Selector, Quote,   MaybeComment, Pseudo },          // Pseudo
        { Pseudo1,  Property, Selector, Pseudo,  Selector, Selector, Quote,   MaybeComment, Pseudo1 },         // Pseudo1
        { Pseudo2,  Property, Selector, Pseudo,  Selector, Selector, Quote,   MaybeComment, Pseudo2 },         // Pseudo2
        { Quote,    Quote,    Quote,    Quote,   Quote,    Quote,    -1,      Quote,        Quote },           // Quote
        { -1,       -1,       -1,       -1,      -1,       -1,       -1,      -1,           Comment },         // MaybeComment
        { Comment,  Comment,  Comment,  Comment, Comment,  Comment,  Comment, Comment,      MaybeCommentEnd }, // Comment
        { Comment,  Comment,  Comment,  Comment, Comment,  Comment,  Comment, -1,           MaybeCommentEnd }, // MaybeCommentEnd
    };

    /// <inheritdoc />
    public int InitialState => -1;

    /// <inheritdoc />
    public int HighlightLine(string text, int previousState, List<SyntaxSpan>? spans)
    {
        ArgumentNullException.ThrowIfNull(text);

        int lastIndex = 0;
        bool lastWasSlash = false;
        int state = previousState;
        int saveState;

        if (state == -1)
        {
            // While the text is empty, the state stays undetermined.
            if (text.Length == 0)
            {
                return -1;
            }

            // Initial state: a ":" without a "{" means a bare property list (an inline style).
            state = saveState = text.Contains(':', StringComparison.Ordinal) && !text.Contains('{', StringComparison.Ordinal)
                ? Property
                : Selector;
        }
        else
        {
            saveState = state >> 16;
            state &= 0x00ff;
        }

        if (state == MaybeCommentEnd)
        {
            state = Comment;
        }
        else if (state == MaybeComment)
        {
            state = saveState;
        }

        for (int i = 0; i < text.Length; i++)
        {
            int token = Alnum;
            char character = text[i] <= 'ÿ' ? text[i] : '\0'; // the Latin-1 character, or '\0' above U+00FF

            if (state == Quote)
            {
                if (character == '\\')
                {
                    lastWasSlash = true;
                }
                else
                {
                    if (character == '"' && !lastWasSlash)
                    {
                        token = QuoteToken;
                    }

                    lastWasSlash = false;
                }
            }
            else
            {
                token = character switch
                {
                    '{' => LBrace,
                    '}' => RBrace,
                    ':' => Colon,
                    ';' => Semicolon,
                    ',' => Comma,
                    '"' => QuoteToken,
                    '/' => Slash,
                    '*' => Star,
                    _ => Alnum,
                };
            }

            int newState = Transitions[state, token];

            if (newState != state)
            {
                bool includeToken = newState == MaybeCommentEnd ||
                                    (state == MaybeCommentEnd && newState != Comment) ||
                                    state == Quote;
                Highlight(spans, text, lastIndex, i - lastIndex + (includeToken ? 1 : 0), state);

                lastIndex = newState == Comment
                    ? i - 1 // including the "/*"
                    : i + (token == Alnum || newState == Quote ? 0 : 1);
            }

            if (newState == -1)
            {
                state = saveState;
            }
            else if (state <= Pseudo2)
            {
                saveState = state;
                state = newState;
            }
            else
            {
                state = newState;
            }
        }

        Highlight(spans, text, lastIndex, text.Length - lastIndex, state);
        return state + (saveState << 16);
    }

    private static void Highlight(List<SyntaxSpan>? spans, string text, int start, int length, int state)
    {
        if (start < 0)
        {
            length += start;
            start = 0;
        }

        if (spans is null || start >= text.Length || length <= 0)
        {
            return;
        }

        SyntaxFormat? format = state switch
        {
            Selector or Pseudo1 or Pseudo2 => SyntaxFormat.CssSelector,
            Property => SyntaxFormat.CssProperty,
            Value => SyntaxFormat.CssValue,
            Quote => SyntaxFormat.CssQuote,
            Comment or MaybeCommentEnd => SyntaxFormat.CssComment,
            _ => null,
        };

        if (format is { } f)
        {
            spans.Add(new SyntaxSpan(start, length, f));
        }
    }
}
