namespace WebApp.Api.Services;

public static class MachineRequestPreparationPricing
{
    public const int IncludedPages = 400;
    public const decimal BasePrice = 99.90m;
    public const decimal AdditionalPagePrice = 0.27m;
    public const decimal MaximumFirstSubscriptionPrice = 29.90m;
    public const long MaximumFirstSubscriptionCents = 2990;

    public static decimal Calculate(int totalPages)
    {
        if (totalPages <= 0) throw new ArgumentOutOfRangeException(nameof(totalPages), "Le nombre de pages doit être supérieur à zéro.");
        return BasePrice + Math.Max(0, totalPages - IncludedPages) * AdditionalPagePrice;
    }

    public static long ToCents(decimal amount) => checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    public static long CalculatePreparationCents(int totalPages) => ToCents(Calculate(totalPages));

    public static long CalculateMaximumAuthorizationCents(int totalPages) =>
        checked(CalculatePreparationCents(totalPages) + MaximumFirstSubscriptionCents);

    public static bool IncludesMaximumFirstSubscription(int totalPages, long amountCents)
    {
        var preparationCents = CalculatePreparationCents(totalPages);
        if (amountCents == preparationCents) return false;
        if (amountCents == checked(preparationCents + MaximumFirstSubscriptionCents)) return true;
        throw new InvalidOperationException("Le montant autorisé ne correspond à aucun modèle tarifaire Machine Request pris en charge.");
    }
}
