using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class ConversationContextBuilderTests
{
    private static ConversationMessage Msg(string role, string content) => new()
    {
        Role = role,
        Content = content,
        IsSummarized = false,
    };

    [TestMethod]
    public void BuildMessage_NoSummaryNoRecent_ReturnsQuestionUnchanged()
    {
        var result = ConversationContextBuilder.BuildMessage(null, [], "Bruit anormal moteur X");
        Assert.AreEqual("Bruit anormal moteur X", result);
    }

    [TestMethod]
    public void BuildMessage_WhitespaceSummaryNoRecent_ReturnsQuestionUnchanged()
    {
        var result = ConversationContextBuilder.BuildMessage("   ", [], "Bruit anormal moteur X");
        Assert.AreEqual("Bruit anormal moteur X", result);
    }

    [TestMethod]
    public void BuildMessage_WithSummaryOnly_OmitsRecentBlock()
    {
        var result = ConversationContextBuilder.BuildMessage("Machine: presse n3.", [], "Question ?");

        StringAssert.Contains(result, "[MEMOIRE TECHNIQUE]");
        StringAssert.DoesNotMatch(result, new System.Text.RegularExpressions.Regex("DERNIERS ECHANGES"));
    }

    [TestMethod]
    public void BuildMessage_WithRecentOnly_OmitsMemoryBlock()
    {
        var recent = new List<ConversationMessage> { Msg("user", "Premier message") };
        var result = ConversationContextBuilder.BuildMessage(null, recent, "Question ?");

        StringAssert.Contains(result, "[DERNIERS ECHANGES]");
        StringAssert.DoesNotMatch(result, new System.Text.RegularExpressions.Regex("MEMOIRE TECHNIQUE"));
    }

    [TestMethod]
    public void BuildMessage_WithSummaryAndRecent_ContainsAllThreeSections()
    {
        const string summary = "Machine: presse n3. Code defaut: F045.";
        var recent = new List<ConversationMessage>
        {
            Msg("user", "Le voyant clignote"),
            Msg("assistant", "Avez-vous verifie le fusible ?"),
        };
        const string question = "Le fusible est bon, et maintenant ?";

        var result = ConversationContextBuilder.BuildMessage(summary, recent, question);

        StringAssert.Contains(result, "[MEMOIRE TECHNIQUE]");
        StringAssert.Contains(result, summary);
        StringAssert.Contains(result, "[DERNIERS ECHANGES]");
        StringAssert.Contains(result, "Utilisateur: Le voyant clignote");
        StringAssert.Contains(result, "Assistant: Avez-vous verifie le fusible ?");
        StringAssert.Contains(result, "[NOUVELLE QUESTION]");
        StringAssert.Contains(result, question);
    }

    [TestMethod]
    public void BuildMessage_CurrentQuestion_AppearsExactlyOnce()
    {
        const string question = "Le fusible est bon, et maintenant ?";
        var recent = new List<ConversationMessage> { Msg("user", "Message precedent, different") };

        var result = ConversationContextBuilder.BuildMessage("Resume", recent, question);

        int occurrences = CountOccurrences(result, question);
        Assert.AreEqual(1, occurrences);
    }

    [TestMethod]
    public void BuildMessage_PreservesUserAssistantRoles()
    {
        var recent = new List<ConversationMessage>
        {
            Msg("user", "A"),
            Msg("assistant", "B"),
        };

        var result = ConversationContextBuilder.BuildMessage(null, recent, "Q");

        var indexUser = result.IndexOf("Utilisateur: A", StringComparison.Ordinal);
        var indexAssistant = result.IndexOf("Assistant: B", StringComparison.Ordinal);
        Assert.IsTrue(indexUser >= 0);
        Assert.IsTrue(indexAssistant > indexUser);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
