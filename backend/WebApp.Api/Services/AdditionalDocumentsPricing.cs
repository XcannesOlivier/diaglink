namespace WebApp.Api.Services;

public static class AdditionalDocumentsPricing
{
    public const long PricePerPageCents = 27;
    public const long MinimumRequestAmountCents = 50;

    public static long CalculateAmountCents(long totalPages)
    {
        if (totalPages <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalPages),
                "Le nombre de pages doit être supérieur à zéro.");

        var perPageAmountCents = checked(totalPages * PricePerPageCents);
        return Math.Max(perPageAmountCents, MinimumRequestAmountCents);
    }
}
