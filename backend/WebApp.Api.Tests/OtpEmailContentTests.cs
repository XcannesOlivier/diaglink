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
        StringAssert.Contains(content.Html, "https://diaglink.com/assets/Logo%20DiagLink.png");
        StringAssert.Contains(content.Html, "alt=\"DiagLink\"");
        StringAssert.Contains(content.PlainText, $"Connexion : {loginUrl}");
        StringAssert.Contains(content.Html, "Se connecter à DiagLink");

        var loginLink = Regex.Match(content.Html, "href=\"(?<url>https://app\\.diaglink\\.com/login)\"");
        Assert.IsTrue(loginLink.Success);
        Assert.AreEqual(Uri.UriSchemeHttps, new Uri(loginLink.Groups["url"].Value).Scheme);
        Assert.IsFalse(loginLink.Groups["url"].Value.Contains(code, StringComparison.Ordinal));
    }
}
