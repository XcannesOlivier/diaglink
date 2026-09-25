namespace WebApp.Api.Services;

public sealed record MachineRequestFirstPeriod(
    DateTime ActivatedAtUtc,
    DateTime CycleStartUtc,
    DateTime FirstPeriodEndUtc,
    int ServiceAmountCents);

public static class MachineRequestFirstPeriodPricing
{
    public const string CommercialTimeZoneId = "Europe/Paris";

    public static MachineRequestFirstPeriod Calculate(DateTime activatedAtUtc)
    {
        if (activatedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The activation instant must be UTC.", nameof(activatedAtUtc));

        var zone = TimeZoneInfo.FindSystemTimeZoneById(CommercialTimeZoneId);
        var localActivation = TimeZoneInfo.ConvertTimeFromUtc(activatedAtUtc, zone);
        var localCycleStart = new DateTime(localActivation.Year, localActivation.Month, 1, 0, 0, 0,
            DateTimeKind.Unspecified);
        var localCycleEnd = localCycleStart.AddMonths(1);
        var cycleStartUtc = TimeZoneInfo.ConvertTimeToUtc(localCycleStart, zone);
        var cycleEndUtc = TimeZoneInfo.ConvertTimeToUtc(localCycleEnd, zone);
        var serviceAmountCents = StripeMachineAdditionService.CalculateServiceCents(
            cycleStartUtc, cycleEndUtc, activatedAtUtc);

        return new(activatedAtUtc, cycleStartUtc, cycleEndUtc, serviceAmountCents);
    }
}
