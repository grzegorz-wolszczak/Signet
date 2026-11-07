using System;
using System.Collections.Frozen;
using System.Globalization;

namespace Signet.EpubCheck.Core.Util;

/// <summary>The URI schemes EPUBCheck knows (upstream's <c>URISchemes</c>).</summary>
public static class UriSchemes
{
    // Upstream's enum constants, in its order; its lookup upper-cases a scheme and turns '.' into '_', so the schemes that
    // upstream spells with '_' (iris.beep, soap.beep, z39.50r, …) are written here the way a document spells them.
    private static readonly FrozenSet<string> s_schemes = FrozenSet.ToFrozenSet(
        [
            "aaa", "aaas", "acap", "cap", "cid", "crid", "data", "dav", "dict", "dns", "fax", "file", "ftp", "go", "gopher",
            "h323", "http", "https", "icap", "im", "imap", "info", "ipp", "irc", "iris", "iris.beep", "iris.xpc", "iris.xpcs",
            "iris.lwz", "javascript", "ldap", "mailto", "mid", "modem", "msrp", "msrps", "mtqp", "mupdate", "news", "nfs",
            "nntp", "opaquelocktoken", "pop", "pres", "rtsp", "service", "shttp", "sip", "sips", "snmp", "soap.beep",
            "soap.beeps", "tag", "tel", "telnet", "tftp", "thismessage", "tip", "tv", "urn", "vemmi", "xmlrpc.beep",
            "xmlrpc.beeps", "xmpp", "z39.50r", "z39.50s", "afs", "dtn", "iax", "mailserver", "pack", "tn3270", "prospero",
            "snews", "videotex", "wais",
        ],
        StringComparer.Ordinal);

    /// <summary>Whether <paramref name="scheme"/> (case aside) is a scheme upstream knows.</summary>
    public static bool Contains(string? scheme)
    {
        return scheme is not null && s_schemes.Contains(scheme.ToLower(CultureInfo.InvariantCulture));
    }
}
