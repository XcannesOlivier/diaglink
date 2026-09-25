using UglyToad.PdfPig;

namespace WebApp.Api.Services;

public interface IPdfPageCounter
{
    Task<int> CountPagesAsync(Stream content, CancellationToken cancellationToken = default);
}

public sealed class PdfPigPageCounter : IPdfPageCounter
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    public async Task<int> CountPagesAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead) throw new InvalidDataException("The PDF stream is not readable.");

        Stream readable = content;
        MemoryStream? buffered = null;
        if (!content.CanSeek)
        {
            buffered = new MemoryStream();
            await content.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;
            readable = buffered;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            readable.Position = 0;
            var headerLength = (int)Math.Min(1024, readable.Length);
            var header = new byte[headerLength];
            await readable.ReadExactlyAsync(header, cancellationToken);
            if (header.AsSpan().IndexOf(PdfSignature) >= 0)
            {
                readable.Position = 0;
                using var document = PdfDocument.Open(readable);
                if (document.NumberOfPages <= 0) throw new InvalidDataException("The PDF contains no pages.");
                return document.NumberOfPages;
            }

            throw new InvalidDataException("The file does not contain a PDF signature.");
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception exception)
        {
            throw new InvalidDataException("The PDF cannot be read.", exception);
        }
        finally
        {
            buffered?.Dispose();
            if (content.CanSeek) content.Position = 0;
        }
    }
}
