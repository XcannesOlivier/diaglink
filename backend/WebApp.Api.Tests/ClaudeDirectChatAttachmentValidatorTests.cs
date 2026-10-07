using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ClaudeDirectChatAttachmentValidatorTests
{
    private readonly ClaudeDirectChatAttachmentValidator _validator = new();

    [TestMethod]
    public void ValidPng_IsDecodedAndCanonicalized()
    {
        var images = _validator.ValidateForClaudeDirect(
            ["data:image/png;base64,iVBORw0KGgo="],
            null);

        Assert.HasCount(1, images);
        Assert.AreEqual("image/png", images[0].MediaType);
        Assert.AreEqual("iVBORw0KGgo=", images[0].Base64Data);
    }

    [TestMethod]
    public void ValidJpeg_IsDecodedAndCanonicalized()
    {
        var images = _validator.ValidateForClaudeDirect(
            ["data:image/jpeg;base64,/9j/2Q=="],
            null);

        Assert.HasCount(1, images);
        Assert.AreEqual("image/jpeg", images[0].MediaType);
        Assert.AreEqual("/9j/2Q==", images[0].Base64Data);
    }

    [TestMethod]
    public void JpgAlias_IsNormalizedToJpeg()
    {
        var images = _validator.ValidateForClaudeDirect(
            ["data:image/jpg;base64,/9j/2Q=="],
            null);

        Assert.AreEqual("image/jpeg", images.Single().MediaType);
    }

    [TestMethod]
    public void InvalidBase64_IsRejected()
    {
        var exception = Assert.ThrowsExactly<ChatAttachmentValidationException>(() =>
            _validator.ValidateForClaudeDirect(["data:image/png;base64,not-base64!"], null));

        Assert.AreEqual("chat_image_invalid_base64", exception.Code);
    }

    [TestMethod]
    public void MissingBase64Marker_IsRejected()
    {
        var exception = Assert.ThrowsExactly<ChatAttachmentValidationException>(() =>
            _validator.ValidateForClaudeDirect(["data:image/png,iVBORw0KGgo="], null));

        Assert.AreEqual("chat_image_invalid_data_uri", exception.Code);
    }

    [TestMethod]
    [DataRow("image/gif")]
    [DataRow("image/webp")]
    [DataRow("text/plain")]
    public void MimeWithoutProvenClaudeDirectContract_IsRejected(string mimeType)
    {
        var exception = Assert.ThrowsExactly<ChatAttachmentValidationException>(() =>
            _validator.ValidateForClaudeDirect([$"data:{mimeType};base64,AA=="], null));

        Assert.AreEqual("chat_image_mime_not_supported", exception.Code);
    }

    [TestMethod]
    public void ImageLargerThanFiveMegabytes_IsRejected()
    {
        var bytes = new byte[ClaudeDirectChatAttachmentValidator.MaxImageBytes + 1];
        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";

        var exception = Assert.ThrowsExactly<ChatAttachmentValidationException>(() =>
            _validator.ValidateForClaudeDirect([dataUri], null));

        Assert.AreEqual("chat_image_too_large", exception.Code);
    }

    [TestMethod]
    public void MoreThanFiveImages_IsRejected()
    {
        var images = Enumerable.Repeat("data:image/png;base64,AA==", 6).ToArray();

        var exception = Assert.ThrowsExactly<ChatAttachmentValidationException>(() =>
            _validator.ValidateForClaudeDirect(images, null));

        Assert.AreEqual("chat_too_many_images", exception.Code);
    }

    [TestMethod]
    public void FileAttachment_IsRejectedWithStableFrontendContract()
    {
        var files = new[]
        {
            new FileAttachment
            {
                DataUri = "data:application/pdf;base64,JVBERg==",
                FileName = "manual.pdf",
                MimeType = "application/pdf"
            }
        };

        var exception = Assert.ThrowsExactly<ChatAttachmentValidationException>(() =>
            _validator.ValidateForClaudeDirect(null, files));

        Assert.AreEqual("chat_file_attachments_not_supported", exception.Code);
        Assert.AreEqual(
            "Les fichiers PDF et texte ne sont pas encore pris en charge dans le chat. Utilisez les documents de la machine pour les PDF techniques.",
            exception.Message);
    }
}
