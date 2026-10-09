namespace WebApp.Api.Services;

public sealed class CommercialPolicyOptions
{
    public const string SectionName = "CommercialPolicy";

    public bool Enabled { get; set; }
    public string[] Instructions { get; set; } = [];
}
