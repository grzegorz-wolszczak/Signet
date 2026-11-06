using System.Text;

namespace Signet.EpubCheck.Core.Jing.Datatype.Xsd;

/// <summary>How a datatype normalizes the white space of a literal (the <c>whiteSpace</c> facet).</summary>
internal enum WhiteSpace
{
    Preserve,
    Replace,
    Collapse,
}

/// <summary>Values that can be ordered (for the min/max facets).</summary>
internal interface IOrderRelation
{
    bool IsLessThan(object obj1, object obj2);
}

/// <summary>Values that have a length (for the length facets).</summary>
internal interface IMeasure
{
    int GetLength(object obj);
}

/// <summary>
/// The base of Jing's XML Schema datatypes (Jing's <c>DatatypeBase</c>): a literal is normalized, checked against the
/// lexical space, then turned into a value (which can still be refused by a facet). The messages come from the
/// datatype bundle in the language of the schema reader.
/// </summary>
internal abstract class XsdDatatypeBase(JingLocalizer localizer, WhiteSpace whiteSpace = WhiteSpace.Collapse) : IDatatype2
{
    private const string LexicalSpaceKeyPrefix = "lexical_space_";

    public WhiteSpace WhiteSpaceMode { get; } = whiteSpace;

    public JingLocalizer Localizer { get; } = localizer;

    public abstract string LexicalSpaceKey { get; }

    public virtual IOrderRelation? OrderRelation => null;

    public virtual IMeasure? Measure => null;

    public virtual XsdDatatypeBase Primitive => this;

    public virtual bool IsContextDependent => false;

    public virtual bool AlwaysValid => false;

    public virtual IdType IdType => IdType.Null;

    /// <summary>The description of the lexical space a facet message names.</summary>
    public virtual string DescriptionForRestriction => LexicalSpaceDescription(LexicalSpaceKey);

    public bool IsValid(string literal, IValidationContext context)
    {
        literal = NormalizeWhiteSpace(literal);
        return LexicallyAllows(literal) && AllowsValue(literal, context);
    }

    public void CheckValid(string literal, IValidationContext context)
    {
        literal = NormalizeWhiteSpace(literal);
        CheckLexicallyAllows(literal);
        GetValue(literal, context);
    }

    public object? CreateValue(string literal, IValidationContext context)
    {
        literal = NormalizeWhiteSpace(literal);
        if (!LexicallyAllows(literal))
        {
            return null;
        }

        try
        {
            return GetValue(literal, context);
        }
        catch (DatatypeException)
        {
            return null;
        }
    }

    public virtual bool SameValue(object value1, object value2) => value1.Equals(value2);

    public virtual int ValueHashCode(object value) => value.GetHashCode();

    public IDatatypeStreamingValidator CreateStreamingValidator(IValidationContext context) => new BufferingStreamingValidator(this, context);

    public abstract bool LexicallyAllows(string str);

    /// <summary>The value of a lexically allowed literal.</summary>
    /// <exception cref="DatatypeException">A constraint on the value space refuses it.</exception>
    public abstract object GetValue(string str, IValidationContext? vc);

    /// <exception cref="DatatypeException">The literal is not in the lexical space.</exception>
    public virtual void CheckLexicallyAllows(string str)
    {
        if (!LexicallyAllows(str))
        {
            throw CreateLexicallyInvalidException();
        }
    }

    public string NormalizeWhiteSpace(string str) => WhiteSpaceMode switch
    {
        WhiteSpace.Collapse => CollapseWhiteSpace(str),
        WhiteSpace.Replace => ReplaceWhiteSpace(str),
        _ => str,
    };

    public string LexicalSpaceDescription(string key) => Localizer.Message(LexicalSpaceKeyPrefix + key);

    public DatatypeException CreateLexicallyInvalidException() =>
        new(Localizer.Message("lexical_violation", LexicalSpaceDescription(LexicalSpaceKey)));

    // Requires LexicallyAllows to be true.
    public virtual bool AllowsValue(string str, IValidationContext? vc)
    {
        try
        {
            GetValue(str, vc);
            return true;
        }
        catch (DatatypeException)
        {
            return false;
        }
    }

    private static string CollapseWhiteSpace(string s)
    {
        int i = CollapseStart(s);
        if (i < 0)
        {
            return s;
        }

        StringBuilder buf = new(s[..i]);
        bool collapsing = i == 0 || s[i - 1] == ' ';
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '\r' or '\n' or '\t' or ' ')
            {
                if (!collapsing)
                {
                    buf.Append(' ');
                    collapsing = true;
                }
            }
            else
            {
                collapsing = false;
                buf.Append(c);
            }
        }

        if (buf.Length > 0 && buf[^1] == ' ')
        {
            buf.Length--;
        }

        return buf.ToString();
    }

    private static int CollapseStart(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            switch (s[i])
            {
                case ' ':
                    if (i == 0 || s[i - 1] == ' ' || i == s.Length - 1)
                    {
                        return i;
                    }

                    break;
                case '\r' or '\n' or '\t':
                    return i;
            }
        }

        return -1;
    }

    private static string ReplaceWhiteSpace(string s)
    {
        int i = s.IndexOfAny(['\r', '\n', '\t']);
        if (i < 0)
        {
            return s;
        }

        char[] buf = s.ToCharArray();
        for (; i < buf.Length; i++)
        {
            if (buf[i] is '\r' or '\n' or '\t')
            {
                buf[i] = ' ';
            }
        }

        return new string(buf);
    }
}
