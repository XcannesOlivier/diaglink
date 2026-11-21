using System.Buffers.Binary;
using System.Text.RegularExpressions;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class OtpEmailContentTests
{
    [TestMethod]
    public void BuildLoginCodeContent_ProvidesPlainTextAndHtmlAlternatives()
    {
        const string code = "984295";
        const string loginUrl = "https://app.diaglink.com/login";

        var content = EmailService.BuildLoginCodeContent(code);

        Assert.AreEqual("Votre code de connexion DiagLink", content.Subject);
        StringAssert.Contains(content.PlainText, "Votre code de connexion DiagLink");
        StringAssert.Contains(content.PlainText, code);
        StringAssert.Contains(content.PlainText, "10 minutes");
        StringAssert.Contains(content.Html, code);
        StringAssert.Contains(content.Html, "10 minutes");
        StringAssert.Contains(content.Html, "src=\"cid:diaglink-logo\"");
        StringAssert.Contains(content.Html, "alt=\"DiagLink\"");
        Assert.IsFalse(content.Html.Contains("https://diaglink.com/assets/Logo", StringComparison.Ordinal));
        StringAssert.Contains(content.PlainText, $"Connexion : {loginUrl}");
        StringAssert.Contains(content.Html, "Se connecter à DiagLink");

        var loginLink = Regex.Match(content.Html, "href=\"(?<url>https://app\\.diaglink\\.com/login)\"");
        Assert.IsTrue(loginLink.Success);
        Assert.AreEqual(Uri.UriSchemeHttps, new Uri(loginLink.Groups["url"].Value).Scheme);
        Assert.IsFalse(loginLink.Groups["url"].Value.Contains(code, StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildLoginCodeMessage_AttachesOptimizedInlinePngLogo()
    {
        var logoContent = EmailService.LoadEmbeddedLogo();
        Assert.IsNotNull(logoContent);

        var message = EmailService.BuildLoginCodeMessage(
            "connexion@diaglink.com",
            "client@example.test",
            "984295",
            logoContent);

        Assert.AreEqual(1, message.Attachments.Count);
        var attachment = message.Attachments.Single();
        Assert.AreEqual("diaglink-logo.png", attachment.Name);
        Assert.AreEqual("image/png", attachment.ContentType);
        Assert.AreEqual("diaglink-logo", attachment.ContentId);
        var logoBytes = attachment.Content.ToArray();
        CollectionAssert.AreEqual(
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a },
            logoBytes[..8]);
        Assert.AreEqual(570, BinaryPrimitives.ReadInt32BigEndian(logoBytes.AsSpan(16, 4)));
        Assert.AreEqual(190, BinaryPrimitives.ReadInt32BigEndian(logoBytes.AsSpan(20, 4)));
        Assert.IsTrue(logoBytes.Length < 80 * 1024,
            $"Le logo fait {logoBytes.Length} octets.");
        StringAssert.Contains(message.Content.Html, "src=\"cid:diaglink-logo\"");
        StringAssert.Contains(message.Content.Html, "alt=\"DiagLink\"");

        Assert.IsTrue(EmailService.TryAddInlineLogo(message, message.Content.Html, logoContent));
        Assert.AreEqual(1, message.Attachments.Count);
    }

    [TestMethod]
    public void BuildLoginCodeMessage_MissingLogoStillBuildsCompleteOtpEmail()
    {
        const string code = "984295";
        var message = EmailService.BuildLoginCodeMessage(
            "connexion@diaglink.com",
            "client@example.test",
            code,
            logoContent: null);

        Assert.AreEqual(0, message.Attachments.Count);
        Assert.AreEqual("Votre code de connexion DiagLink", message.Content.Subject);
        StringAssert.Contains(message.Content.PlainText, code);
        StringAssert.Contains(message.Content.PlainText, "10 minutes");
        StringAssert.Contains(message.Content.PlainText, "Connexion : https://app.diaglink.com/login");
        StringAssert.Contains(message.Content.Html, "alt=\"DiagLink\"");
    }
}
