namespace WebApp.Api.Models.Entities;

/// <summary>A physical machine belonging to exactly one Company — owned by this app (chat.Machines).</summary>
public class Machine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string Name { get; set; }
    // Use `Reference` as the secondary identifier for machines.
    public required string Status { get; set; }

    // Columns present in the legacy dbo.Machines table we need to reuse
    public string? Reference { get; set; }
    public string? VectorStoreId { get; set; }
    public string? BlobPrefix { get; set; }

    // Audit timestamps stored as CreatedAt / UpdatedAt in dbo
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    
    // Machine-scoped Claude Direct configuration
    public string? ProjectEndpoint { get; set; }

    public Company? Company { get; set; }
}
