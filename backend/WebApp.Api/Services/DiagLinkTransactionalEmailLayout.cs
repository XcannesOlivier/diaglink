using System.Net;
using System.Text;

namespace WebApp.Api.Services;

internal static class DiagLinkTransactionalEmailLayout
{
    internal const string LogoUrl = "https://diaglink.com/assets/Logo%20DiagLink.png";
    internal const string LoginUrl = "https://app.diaglink.com/login";
    private const string Footer = "DiagLink · Assistant Technique";

    internal static string BuildPlainText(string greeting, IReadOnlyList<string> paragraphs, string? highlight = null)
    {
        var sections = new List<string> { greeting.Trim() };
        sections.AddRange(paragraphs.Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
            .Select(paragraph => paragraph.Trim()));
        if (!string.IsNullOrWhiteSpace(highlight)) sections.Add(highlight.Trim());
        sections.Add($"Connexion : {LoginUrl}");
        sections.Add(Footer);
        return string.Join("\n\n", sections);
    }

    internal static string BuildHtml(string title, string greeting, IReadOnlyList<string> paragraphs,
        string? highlight = null)
    {
        var encodedTitle = Encode(title);
        var encodedGreeting = Encode(greeting);
        var paragraphHtml = new StringBuilder();
        foreach (var paragraph in paragraphs.Where(paragraph => !string.IsNullOrWhiteSpace(paragraph)))
        {
            paragraphHtml.Append("<p style=\"margin:0 0 16px; color:#374151; font-size:15px; line-height:23px;\">")
                .Append(Encode(paragraph.Trim()))
                .Append("</p>");
        }

        var highlightHtml = string.IsNullOrWhiteSpace(highlight)
            ? string.Empty
            : $"<div style=\"margin:20px 0 0; padding:14px 16px; background-color:#f3f4f6; border:1px solid #d1d5db; border-radius:8px; color:#1f2937; font-size:15px; line-height:22px; font-weight:700; text-align:center;\">{Encode(highlight.Trim())}</div>";

        return $"""
            <!doctype html>
            <html lang="fr">
              <body style="margin:0; padding:0; background-color:#f3f4f6; color:#1f2937; font-family:Arial,Helvetica,sans-serif;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%; background-color:#f3f4f6;">
                  <tr>
                    <td align="center" style="padding:24px 16px;">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%; max-width:560px; background-color:#ffffff; border:1px solid #e5e7eb; border-radius:12px;">
                        <tr>
                          <td align="center" style="padding:32px 24px 12px;">
                            <img src="{LogoUrl}" alt="DiagLink" width="190" style="display:block; width:100%; max-width:190px; height:auto; border:0;" />
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:12px 24px 32px;">
                            <h1 style="margin:0 0 24px; color:#111827; font-size:24px; line-height:32px; font-weight:700; text-align:center;">{encodedTitle}</h1>
                            <p style="margin:0 0 16px; color:#374151; font-size:15px; line-height:23px;">{encodedGreeting}</p>
                            {paragraphHtml}
                            {highlightHtml}
                            <div style="text-align:center;">
                              <a href="{LoginUrl}" style="display:inline-block; margin-top:24px; padding:10px 18px; background-color:#1f4e79; border-radius:6px; color:#ffffff; font-size:14px; line-height:20px; font-weight:700; text-decoration:none;">Se connecter à DiagLink</a>
                            </div>
                          </td>
                        </tr>
                        <tr>
                          <td align="center" style="padding:18px 24px; border-top:1px solid #e5e7eb; color:#6b7280; font-size:12px; line-height:18px;">{Footer}</td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
