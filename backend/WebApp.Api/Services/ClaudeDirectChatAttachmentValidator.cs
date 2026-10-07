using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>
/// Validates the user attachments accepted by the Claude Direct runtime and converts them to its
/// provider-neutral internal image model.
/// </summary>
public sealed class ClaudeDirectChatAttachmentValidator
{
    public const int MaxImageCount = 5;
    public const int MaxImageBytes = 5 * 1024 * 1024;

    private const string FilesNotSupportedMessage =
        "Les fichiers PDF et texte ne sont pas encore pris en charge dans le chat. " +
        "Utilisez les documents de la machine pour les PDF techniques.";

    private static readonly IReadOnlyDictionary<string, string> SupportedMediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = "image/png",
            ["image/jpeg"] = "image/jpeg",
            ["image/jpg"] = "image/jpeg"
        };

    public IReadOnlyList<ClaudeDirectUserImage> ValidateForClaudeDirect(
        IReadOnlyList<string>? imageDataUris,
        IReadOnlyList<FileAttachment>? fileDataUris)
    {
        if (fileDataUris is { Count: > 0 })
        {
            throw new ChatAttachmentValidationException(
                "chat_file_attachments_not_supported",
                FilesNotSupportedMessage);
        }

        if (imageDataUris is not { Count: > 0 })
        {
            return [];
        }

        if (imageDataUris.Count > MaxImageCount)
        {
            throw new ChatAttachmentValidationException(
                "chat_too_many_images",
                $"Vous pouvez joindre au maximum {MaxImageCount} images par message.");
        }

        var images = new List<ClaudeDirectUserImage>(imageDataUris.Count);
        foreach (var dataUri in imageDataUris)
        {
            images.Add(ParseImage(dataUri));
        }

        return images;
    }

    private static ClaudeDirectUserImage ParseImage(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri) ||
            !dataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidDataUri();
        }

        var commaIndex = dataUri.IndexOf(',');
        if (commaIndex <= "data:".Length || commaIndex == dataUri.Length - 1)
        {
            throw InvalidDataUri();
        }

        var metadata = dataUri["data:".Length..commaIndex].Split(';');
        if (metadata.Length != 2 ||
            string.IsNullOrWhiteSpace(metadata[0]) ||
            !string.Equals(metadata[1], "base64", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidDataUri();
        }

        if (!SupportedMediaTypes.TryGetValue(metadata[0], out var normalizedMediaType))
        {
            throw new ChatAttachmentValidationException(
                "chat_image_mime_not_supported",
                "Format d’image non pris en charge. Utilisez PNG ou JPEG.");
        }

        var base64Data = dataUri[(commaIndex + 1)..];
        var significantCharacterCount = base64Data.Count(character => !char.IsWhiteSpace(character));
        var maximumEncodedLength = ((MaxImageBytes + 2) / 3) * 4;
        if (significantCharacterCount > maximumEncodedLength)
        {
            throw ImageTooLarge();
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Data);
        }
        catch (FormatException)
        {
            throw new ChatAttachmentValidationException(
                "chat_image_invalid_base64",
                "L’image jointe contient des données Base64 invalides.");
        }

        if (bytes.Length == 0)
        {
            throw new ChatAttachmentValidationException(
                "chat_image_invalid_base64",
                "L’image jointe contient des données Base64 invalides.");
        }

        if (bytes.Length > MaxImageBytes)
        {
            throw ImageTooLarge();
        }

        return new ClaudeDirectUserImage(normalizedMediaType, Convert.ToBase64String(bytes));
    }

    private static ChatAttachmentValidationException InvalidDataUri() => new(
        "chat_image_invalid_data_uri",
        "L’image jointe doit utiliser une URI data Base64 valide.");

    private static ChatAttachmentValidationException ImageTooLarge() => new(
        "chat_image_too_large",
        "Chaque image jointe doit faire au maximum 5 Mo.");
}

public sealed class ChatAttachmentValidationException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}
