using System;
using System.Net;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Builds a local HTML page with an auto-submitted form for the W3C CSS validator. Signet does not
/// make the network request itself: it writes this page to a temporary file and opens
/// it in the user's default browser (<c>ExternalOpen</c> in the App layer).
/// </summary>
public static class W3CValidation
{
    /// <summary>The address of the W3C CSS validation service the form posts its data to.</summary>
    public const string ValidatorUrl = "https://jigsaw.w3.org/css-validator/validator";

    private const string CssTextPlaceholder = "[[SIGNET_CSS_TEXT]]";
    private const string ProfilePlaceholder = "[[SIGNET_PROFILE]]";

    // string.Format is not suitable here — the page contains literal '{' '}' in <script> blocks.
    private const string FormHtml =
        "<html>" +
        " <body>" +
        "  <p>Signet will send your stylesheet data to the <a href='https://jigsaw.w3.org/css-validator/'>W3C Validation Service</a>.</p>" +
        "  <p><b>This page should disappear once loaded after 3 seconds.</b></p>" +
        "  <p>If your browser does not have javascript enabled, click on the button below.</p>" +
        "  <p><input id='button' form='form' type='submit' value='Check' /></p>" +
        "  <div>" +
        "   <form id='form' enctype='multipart/form-data' action='" + ValidatorUrl + "' method='post'>" +
        "    <p><textarea name='text' rows='12' cols='70'>" + CssTextPlaceholder + "</textarea></p>" +
        "    <input type='hidden' name='lang' value='en' />" +
        "    <input type='hidden' name='profile' value='" + ProfilePlaceholder + "' />" +
        "   </form>" +
        "  </div>" +
        "  <script type='text/javascript'>" +
        "   function mySubmit() { var frm=document.getElementById('form'); frm.submit(); }" +
        "   window.onload = function() { window.setTimeout(function() { mySubmit(); }, 3000); };" +
        "  </script>" +
        " </body>" +
        "</html>";

    /// <summary>
    /// Builds the HTML page content for the given CSS stylesheet and validation profile (<c>css21</c>,
    /// <c>css30</c>, ...). The CSS text is HTML-encoded, which protects the structure of the
    /// generated page from <c>&amp;</c>/<c>&lt;</c>/<c>&gt;</c> characters in the stylesheet content.
    /// </summary>
    public static string BuildCssValidationHtml(string cssText, string profile)
    {
        ArgumentNullException.ThrowIfNull(cssText);
        ArgumentNullException.ThrowIfNull(profile);

        return FormHtml
            .Replace(CssTextPlaceholder, WebUtility.HtmlEncode(cssText), StringComparison.Ordinal)
            .Replace(ProfilePlaceholder, WebUtility.HtmlEncode(profile), StringComparison.Ordinal);
    }
}
