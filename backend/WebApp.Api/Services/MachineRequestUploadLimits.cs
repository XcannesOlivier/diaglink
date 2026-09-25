namespace WebApp.Api.Services;

public static class MachineRequestUploadLimits
{
    public const int MaxDocumentCount = 10;
    public const long MaxDocumentBytes = 50L * 1024 * 1024;
    public const long MaxCombinedDocumentBytes = 200L * 1024 * 1024;
    // Allows multipart headers around the 200 MiB maximum combined document content.
    public const long MaxRequestBodyBytes = 205L * 1024 * 1024;
    public const int MaxShortTextLength = 200;
    public const int MaxDescriptionLength = 2_000;
}
