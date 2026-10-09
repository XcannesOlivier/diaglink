using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Data;

/// <summary>
/// EF Core context scoped to this app's conversation-history tables only, isolated in the
/// 'chat' SQL schema — the underlying 'diaglink' Azure SQL database may contain other objects
/// (auth, companies, machines) unknown to this model.
/// </summary>
public class DiagLinkDbContext : DbContext
{
    public const string Schema = "chat";

    public DiagLinkDbContext(DbContextOptions<DiagLinkDbContext> options) : base(options) { }

    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<ConversationMessageVisual> ConversationMessageVisuals => Set<ConversationMessageVisual>();
    public DbSet<ConversationMessageSourceReference> ConversationMessageSourceReferences => Set<ConversationMessageSourceReference>();
    public DbSet<User> Users => Set<User>();
    public DbSet<LoginCode> LoginCodes => Set<LoginCode>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyBranding> CompanyBrandings => Set<CompanyBranding>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<UserMachineAccess> UserMachineAccess => Set<UserMachineAccess>();
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();
    public DbSet<AiPricing> AiPricing => Set<AiPricing>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<MachineBillingPeriod> MachineBillingPeriods => Set<MachineBillingPeriod>();
    public DbSet<CompanyWallet> CompanyWallets => Set<CompanyWallet>();
    public DbSet<CreditLedgerEntry> CreditLedger => Set<CreditLedgerEntry>();
    public DbSet<BillingAccount> BillingAccounts => Set<BillingAccount>();
    public DbSet<StripeMachineAddition> StripeMachineAdditions => Set<StripeMachineAddition>();
    public DbSet<StripeWalletTopUp> StripeWalletTopUps => Set<StripeWalletTopUp>();
    public DbSet<StripeSubscriptionPayment> StripeSubscriptionPayments => Set<StripeSubscriptionPayment>();
    public DbSet<StripeLifecycleEvent> StripeLifecycleEvents => Set<StripeLifecycleEvent>();
    public DbSet<MachineRequestPayment> MachineRequestPayments => Set<MachineRequestPayment>();
    public DbSet<EmailOutbox> EmailOutbox => Set<EmailOutbox>();

    private void GuardTopUpLedger()
    {
        foreach (var entry in ChangeTracker.Entries<CreditLedgerEntry>())
            if (entry.State is EntityState.Modified or EntityState.Deleted
                && (entry.OriginalValues.GetValue<string>(nameof(CreditLedgerEntry.EntryType)) == "TopUp" || entry.Entity.EntryType == "TopUp"))
                throw new InvalidOperationException("TopUp ledger entries are immutable.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    { GuardTopUpLedger(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    { GuardTopUpLedger(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailOutbox>(entity =>
        {
            entity.ToTable("EmailOutbox", "dbo", table =>
            {
                table.HasCheckConstraint("CK_EmailOutbox_NotificationType", "[NotificationType] >= 0 AND [NotificationType] <= 4");
                table.HasCheckConstraint("CK_EmailOutbox_Status", "[Status] >= 0 AND [Status] <= 1");
                table.HasCheckConstraint("CK_EmailOutbox_AttemptCount", "[AttemptCount] >= 0");
                table.HasCheckConstraint("CK_EmailOutbox_Lease", "([LeaseId] IS NULL AND [LockedUntilUtc] IS NULL) OR ([LeaseId] IS NOT NULL AND [LockedUntilUtc] IS NOT NULL)");
                table.HasCheckConstraint("CK_EmailOutbox_Sent", "([Status] = 0 AND [SentAtUtc] IS NULL) OR ([Status] = 1 AND [SentAtUtc] IS NOT NULL)");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedNever();
            entity.Property(item => item.MachineRequestId).IsRequired().HasMaxLength(32).IsUnicode(false);
            entity.Property(item => item.RecipientEmail).IsRequired().HasMaxLength(320);
            entity.Property(item => item.RecipientName).HasMaxLength(200);
            entity.Property(item => item.PayloadJson).IsRequired();
            entity.Property(item => item.ProviderOperationId).HasMaxLength(200);
            entity.Property(item => item.LastError).HasMaxLength(1000);
            entity.Property(item => item.RowVersion).IsRowVersion();
            entity.HasIndex(item => new { item.MachineRequestId, item.NotificationType }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.NextAttemptAtUtc, item.LockedUntilUtc });
            entity.HasIndex(item => item.PaymentRequestId).HasFilter("[PaymentRequestId] IS NOT NULL");
        });
        modelBuilder.Entity<MachineRequestPayment>(entity =>
        {
            entity.ToTable("MachineRequestPayments", "dbo", table =>
            {
                table.HasCheckConstraint("CK_MachineRequestPayments_Pages", "[EstimatedTotalPages] > 0");
                table.HasCheckConstraint("CK_MachineRequestPayments_Amount", "[AmountCents] > 0");
                table.HasCheckConstraint("CK_MachineRequestPayments_Currency", "[Currency] = 'EUR'");
                table.HasCheckConstraint("CK_MachineRequestPayments_Status", "[Status] >= 0 AND [Status] <= 4");
                table.HasCheckConstraint("CK_MachineRequestPayments_RequestKind", "[RequestKind] >= 0 AND [RequestKind] <= 2");
                table.HasCheckConstraint("CK_MachineRequestPayments_Timestamps", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                table.HasCheckConstraint("CK_MachineRequestPayments_Link", "([MachineRequestId] IS NULL AND [RequestLinkedAtUtc] IS NULL) OR ([MachineRequestId] IS NOT NULL AND [RequestLinkedAtUtc] IS NOT NULL)");
                table.HasCheckConstraint("CK_MachineRequestPayments_State", "([Status] = 0 AND [StripePaymentIntentId] IS NULL AND [AuthorizationEventId] IS NULL AND [AuthorizedAtUtc] IS NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 1 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 2 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NOT NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 3 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NOT NULL) OR ([Status] = 4 AND [StripePaymentIntentId] IS NULL AND [AuthorizationEventId] IS NULL AND [AuthorizedAtUtc] IS NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NOT NULL)");
                table.HasCheckConstraint("CK_MachineRequestPayments_ServiceAmount", "[ServiceAmountCents] IS NULL OR ([ServiceAmountCents] >= 0 AND [ServiceAmountCents] <= 1990)");
                table.HasCheckConstraint("CK_MachineRequestPayments_FinalCaptureAmount", "[FinalCaptureAmountCents] IS NULL OR ([FinalCaptureAmountCents] > 0 AND [FinalCaptureAmountCents] <= [AmountCents])");
                table.HasCheckConstraint("CK_MachineRequestPayments_FirstPeriod", "([ActivatedAtUtc] IS NULL AND [FirstPeriodEndUtc] IS NULL) OR ([ActivatedAtUtc] IS NOT NULL AND [FirstPeriodEndUtc] IS NOT NULL AND [ActivatedAtUtc] < [FirstPeriodEndUtc])");
                table.HasCheckConstraint("CK_MachineRequestPayments_ProvisioningStage", "[ProvisioningStage] >= 0 AND [ProvisioningStage] <= 6");
                table.HasCheckConstraint("CK_MachineRequestPayments_ProvisioningCompleted", "([ProvisioningStage] < 6 AND [ProvisioningCompletedAtUtc] IS NULL) OR ([ProvisioningStage] = 6 AND [ProvisioningCompletedAtUtc] IS NOT NULL)");
                table.HasCheckConstraint("CK_MachineRequestPayments_PreparationStatus", "[PreparationStatus] >= 0 AND [PreparationStatus] <= 1");
                table.HasCheckConstraint("CK_MachineRequestPayments_Ready", "([PreparationStatus] = 0 AND [ReadyAtUtc] IS NULL AND [ReadyByUserId] IS NULL) OR ([PreparationStatus] = 1 AND [ReadyAtUtc] IS NOT NULL AND [ReadyByUserId] IS NOT NULL)");
            });
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.Id).ValueGeneratedNever();
            entity.Property(payment => payment.PreparationStatus)
                .HasDefaultValue(MachineRequestPreparationStatus.Pending);
            var requestKind = entity.Property(payment => payment.RequestKind)
                .HasDefaultValue(MachineRequestKind.InitialMachine);
            requestKind.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            var requestedByUserId = entity.Property(payment => payment.RequestedByUserId);
            requestedByUserId.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            var targetMachineId = entity.Property(payment => payment.TargetMachineId);
            targetMachineId.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(payment => payment.Currency).IsRequired().HasMaxLength(3).IsUnicode(false);
            entity.Property(payment => payment.Email).HasMaxLength(320);
            entity.Property(payment => payment.StripeSessionId).HasMaxLength(200);
            entity.Property(payment => payment.StripePaymentIntentId).HasMaxLength(200);
            entity.Property(payment => payment.AuthorizationEventId).HasMaxLength(200);
            entity.Property(payment => payment.MachineRequestId).HasMaxLength(32).IsUnicode(false);
            entity.Property(payment => payment.RowVersion).IsRowVersion();
            entity.HasIndex(payment => payment.StripeSessionId).IsUnique().HasFilter("[StripeSessionId] IS NOT NULL");
            entity.HasIndex(payment => payment.StripePaymentIntentId).IsUnique().HasFilter("[StripePaymentIntentId] IS NOT NULL");
            entity.HasIndex(payment => payment.AuthorizationEventId).IsUnique().HasFilter("[AuthorizationEventId] IS NOT NULL");
            entity.HasIndex(payment => payment.MachineRequestId).IsUnique().HasFilter("[MachineRequestId] IS NOT NULL");
            entity.HasOne<Company>().WithMany().HasForeignKey(payment => payment.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Machine>().WithMany().HasForeignKey(payment => payment.MachineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Machine>().WithMany().HasForeignKey(payment => payment.TargetMachineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(payment => payment.CompanyId).HasFilter("[CompanyId] IS NOT NULL");
            entity.HasIndex(payment => payment.MachineId).IsUnique().HasFilter("[MachineId] IS NOT NULL");
            entity.HasIndex(payment => payment.TargetMachineId).HasFilter("[TargetMachineId] IS NOT NULL");
        });
        modelBuilder.Entity<StripeLifecycleEvent>(e =>
        {
            e.ToTable("StripeLifecycleEvents", "dbo"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(200);
            e.Property(x => x.SubscriptionId).HasMaxLength(200);
            e.Property(x => x.EventType).HasMaxLength(100);
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<StripeSubscriptionPayment>(e =>
        {
            e.ToTable("StripeSubscriptionPayments", "dbo", t =>
            {
                t.HasCheckConstraint("CK_StripeSubscriptionPayments_Cycle", "[PeriodEndUtc] > [PeriodStartUtc]");
                t.HasCheckConstraint("CK_StripeSubscriptionPayments_Amount", "[AmountPaidCents] > 0");
                t.HasCheckConstraint("CK_StripeSubscriptionPayments_Reason", "[BillingReason] IN ('subscription_create', 'subscription_cycle')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.StripeSubscriptionId).HasMaxLength(200);
            e.Property(x => x.StripeInvoiceId).HasMaxLength(200);
            e.Property(x => x.ExternalEventId).HasMaxLength(200);
            e.Property(x => x.BillingReason).HasMaxLength(40);
            e.Property(x => x.PaymentReference).HasMaxLength(2000);
            e.HasIndex(x => x.StripeInvoiceId).IsUnique();
            e.HasIndex(x => x.ExternalEventId).IsUnique();
            e.HasIndex(x => new { x.StripeSubscriptionId, x.PeriodStartUtc }).IsUnique();
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<StripeWalletTopUp>(e =>
        {
            e.ToTable("StripeWalletTopUps", "dbo", t =>
            {
                t.HasCheckConstraint("CK_StripeWalletTopUps_Amount", "[AmountCents] >= 1000 AND [AmountCents] <= 99999999 AND [Currency] = 'EUR'");
                t.HasCheckConstraint("CK_StripeWalletTopUps_Stage", "[Stage] >= 0 AND [Stage] <= 5");
                t.HasCheckConstraint("CK_StripeWalletTopUps_Session", "[Stage] = 0 OR [StripeSessionId] IS NOT NULL");
                t.HasCheckConstraint("CK_StripeWalletTopUps_Payment", "([Stage] < 3 AND [StripePaymentIntentId] IS NULL AND [PaymentConfirmedAtUtc] IS NULL) OR ([Stage] >= 3 AND [StripePaymentIntentId] IS NOT NULL AND [PaymentConfirmedAtUtc] IS NOT NULL AND [ExternalEventId] IS NOT NULL)");
                t.HasCheckConstraint("CK_StripeWalletTopUps_Ledger", "([Stage] < 4 AND [LedgerEntryId] IS NULL) OR ([Stage] >= 4 AND [LedgerEntryId] IS NOT NULL)");
                t.HasCheckConstraint("CK_StripeWalletTopUps_Completed", "([Stage] < 5 AND [CompletedAtUtc] IS NULL) OR ([Stage] = 5 AND [CompletedAtUtc] IS NOT NULL)");
            });
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.StripeCustomerId).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false);
            e.Property(x => x.ReturnUrl).HasMaxLength(2048); e.Property(x => x.PaymentUrl).HasMaxLength(2048);
            e.Property(x => x.StripeSessionId).HasMaxLength(200); e.Property(x => x.StripePaymentIntentId).HasMaxLength(200);
            e.Property(x => x.ExternalEventId).HasMaxLength(200);
            e.HasIndex(x => x.StripeSessionId).IsUnique().HasFilter("[StripeSessionId] IS NOT NULL");
            e.HasIndex(x => x.StripePaymentIntentId).IsUnique().HasFilter("[StripePaymentIntentId] IS NOT NULL");
            e.HasIndex(x => x.ExternalEventId).IsUnique().HasFilter("[ExternalEventId] IS NOT NULL");
            e.HasIndex(x => x.LedgerEntryId).IsUnique().HasFilter("[LedgerEntryId] IS NOT NULL");
            e.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc });
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CreditLedgerEntry>().WithMany().HasForeignKey(x => x.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<StripeMachineAddition>(entity =>
        {
            entity.ToTable("StripeMachineAdditions", "dbo", table =>
            {
                table.HasCheckConstraint("CK_StripeMachineAdditions_Amounts", "[AiAmountCents] = 1000 AND [ServiceAmountCents] >= 0 AND [ServiceAmountCents] <= 1990");
                table.HasCheckConstraint("CK_StripeMachineAdditions_Cycle", "[CycleStartUtc] <= [ActivatedAtUtc] AND [ActivatedAtUtc] < [CycleEndUtc]");
                table.HasCheckConstraint("CK_StripeMachineAdditions_Quantity", "[OriginalQuantity] >= 0 AND [TargetQuantity] = [OriginalQuantity] + 1");
                table.HasCheckConstraint("CK_StripeMachineAdditions_Stage", "[Stage] >= 0 AND [Stage] <= 6");
                table.HasCheckConstraint("CK_StripeMachineAdditions_Payment", "([Stage] < 4 AND [PaymentReference] IS NULL AND [PaymentConfirmedAtUtc] IS NULL) OR ([Stage] >= 4 AND [PaymentReference] IS NOT NULL AND [PaymentReference] <> '' AND [PaymentConfirmedAtUtc] IS NOT NULL)");
                table.HasCheckConstraint("CK_StripeMachineAdditions_Period", "([Stage] < 5 AND [MachineBillingPeriodId] IS NULL) OR ([Stage] >= 5 AND [MachineBillingPeriodId] IS NOT NULL)");
                table.HasCheckConstraint("CK_StripeMachineAdditions_Completed", "([Stage] < 6 AND [CompletedAtUtc] IS NULL) OR ([Stage] = 6 AND [CompletedAtUtc] IS NOT NULL)");
                table.HasCheckConstraint("CK_StripeMachineAdditions_Invoice", "[Stage] < 2 OR [StripeInvoiceId] IS NOT NULL");
            });
            entity.HasKey(a => a.Id);
            entity.Property(a => a.StripeCustomerId).HasMaxLength(200);
            entity.Property(a => a.StripeSubscriptionId).HasMaxLength(200);
            entity.Property(a => a.StripeSubscriptionItemId).HasMaxLength(200);
            entity.Property(a => a.StripePriceId).HasMaxLength(200);
            entity.Property(a => a.StripeInvoiceId).HasMaxLength(200);
            entity.Property(a => a.ExternalEventId).HasMaxLength(200);
            entity.HasIndex(a => a.ExternalEventId).IsUnique().HasFilter("[ExternalEventId] IS NOT NULL");
            entity.Property(a => a.PaymentReference).HasMaxLength(2000);
            entity.HasOne<Company>().WithMany().HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Machine>().WithMany().HasForeignKey(a => a.MachineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BillingAccount>().WithMany().HasForeignKey(a => a.BillingAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MachineBillingPeriod>().WithMany().HasForeignKey(a => a.MachineBillingPeriodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(a => new { a.MachineId, a.CycleStartUtc }).IsUnique();
            entity.HasIndex(a => a.CompanyId).IsUnique().HasFilter("[CompletedAtUtc] IS NULL");
            entity.HasIndex(a => a.StripeInvoiceId).IsUnique().HasFilter("[StripeInvoiceId] IS NOT NULL");
        });
        modelBuilder.Entity<ExchangeRate>(entity =>
        {
            entity.ToTable("ExchangeRates", "dbo", table =>
            {
                table.HasCheckConstraint("CK_ExchangeRates_Rate", "[Rate] > 0");
                table.HasCheckConstraint("CK_ExchangeRates_Interval", "[EffectiveToUtc] IS NULL OR [EffectiveToUtc] > [EffectiveFromUtc]");
            });
            entity.HasKey(r => r.Id);
            entity.Property(r => r.BaseCurrency).IsRequired().HasMaxLength(3).IsUnicode(false);
            entity.Property(r => r.QuoteCurrency).IsRequired().HasMaxLength(3).IsUnicode(false);
            entity.Property(r => r.Rate).HasPrecision(18, 10);
            entity.Property(r => r.Source).IsRequired().HasMaxLength(200);
            entity.HasIndex(r => new { r.BaseCurrency, r.QuoteCurrency, r.EffectiveFromUtc });
        });
        modelBuilder.Entity<AiPricing>(entity =>
        {
            entity.ToTable("AiPricing", "dbo");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Provider).IsRequired().HasMaxLength(100);
            entity.Property(p => p.Model).IsRequired().HasMaxLength(256);
            entity.Property(p => p.UsageType).HasMaxLength(32);
            entity.Property(p => p.InputPricePerMillion).HasPrecision(18, 8);
            entity.Property(p => p.OutputPricePerMillion).HasPrecision(18, 8);
            entity.Property(p => p.CacheReadPricePerMillion).HasPrecision(18, 8);
            entity.Property(p => p.CacheCreation5mPricePerMillion).HasPrecision(18, 8);
            entity.Property(p => p.CacheCreation1hPricePerMillion).HasPrecision(18, 8);
            entity.Property(p => p.WebSearchPricePerRequest).HasPrecision(18, 8);
            entity.Property(p => p.Currency).IsRequired().HasMaxLength(3).IsUnicode(false);
            entity.Property(p => p.EffectiveFromUtc).IsRequired();
            entity.HasIndex(p => new { p.Provider, p.Model, p.UsageType, p.EffectiveFromUtc });
        });

        modelBuilder.Entity<MachineBillingPeriod>(entity =>
        {
            entity.ToTable("MachineBillingPeriods", "dbo");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.IncludedAiBudgetRealCost).HasPrecision(18, 6);
            entity.Property(p => p.IncludedAiUsedRealCost).HasPrecision(18, 6).HasDefaultValue(0m);
            entity.Property(p => p.Status).IsRequired().HasMaxLength(50);
            entity.HasOne<Machine>().WithMany().HasForeignKey(p => p.MachineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(p => p.MachineId);
            entity.HasIndex(p => new { p.MachineId, p.PeriodStartUtc }).IsUnique();
        });

        modelBuilder.Entity<CompanyWallet>(entity =>
        {
            entity.ToTable("CompanyWallets", "dbo");
            entity.HasKey(w => w.CompanyId);
            entity.Property(w => w.CompanyId).ValueGeneratedNever();
            entity.Property(w => w.Balance).HasPrecision(18, 6);
            entity.Property(w => w.Currency).IsRequired().HasMaxLength(3).IsUnicode(false);
            entity.HasOne<Company>().WithOne().HasForeignKey<CompanyWallet>(w => w.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CreditLedgerEntry>(entity =>
        {
            entity.ToTable("CreditLedger", "dbo", t => t.UseSqlOutputClause(false));
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntryType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.BucketType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.RealAiCost).HasPrecision(18, 6);
            entity.Property(e => e.CommercialCreditAmount).HasPrecision(18, 6);
            entity.Property(e => e.BalanceAfter).HasPrecision(18, 6);
            entity.Property(e => e.Currency).HasMaxLength(3).IsUnicode(false);
            entity.Property(e => e.ExternalEventId).HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Machine>().WithMany().HasForeignKey(e => e.MachineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MachineBillingPeriod>().WithMany().HasForeignKey(e => e.MachineBillingPeriodId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AiUsageRecord>().WithMany().HasForeignKey(e => e.AiUsageRecordId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.CompanyId, e.CreatedAtUtc });
            entity.HasIndex(e => new { e.MachineId, e.CreatedAtUtc });
            entity.HasIndex(e => e.MachineBillingPeriodId);
            // A single usage can be split between the included budget and the company wallet.
            entity.HasIndex(e => e.AiUsageRecordId);
            entity.HasIndex(e => e.AiUsageRecordId, "UX_CreditLedger_MachineIncludedUsage")
                .IsUnique().HasFilter("[AiUsageRecordId] IS NOT NULL AND [BucketType] = 'MachineIncluded' AND [EntryType] = 'AiUsage'");
            entity.HasIndex(e => e.ExternalEventId).IsUnique()
                .HasFilter("[ExternalEventId] IS NOT NULL");
            entity.HasIndex(e => e.AiUsageRecordId, "UX_CreditLedger_CompanyWalletUsage")
                .IsUnique().HasFilter("[AiUsageRecordId] IS NOT NULL AND [BucketType] = 'CompanyWallet' AND [EntryType] = 'AiUsage'");
        });

        modelBuilder.Entity<BillingAccount>(entity =>
        {
            entity.ToTable("BillingAccounts", "dbo");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.StripeCustomerId).HasMaxLength(200);
            entity.Property(a => a.StripeSubscriptionId).HasMaxLength(200);
            entity.Property(a => a.SubscriptionStatus).HasMaxLength(50);
            entity.Property(a => a.LatestInvoiceId).HasMaxLength(200);
            entity.Property(a => a.LatestInvoiceStatus).HasMaxLength(50);
            entity.HasOne<Company>().WithOne().HasForeignKey<BillingAccount>(a => a.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(a => a.CompanyId).IsUnique();
            entity.HasIndex(a => a.StripeCustomerId).IsUnique().HasFilter("[StripeCustomerId] IS NOT NULL");
            entity.HasIndex(a => a.StripeSubscriptionId).IsUnique().HasFilter("[StripeSubscriptionId] IS NOT NULL");
        });

        modelBuilder.Entity<AiUsageRecord>(entity =>
        {
            entity.ToTable("AiUsageRecords", Schema);
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).ValueGeneratedNever();
            entity.Property(u => u.UsageType).HasConversion<string>().HasMaxLength(32);
            entity.Property(u => u.ConversationPublicId).HasMaxLength(200);
            entity.Property(u => u.ResponseId).HasMaxLength(200);
            entity.Property(u => u.CallId).HasMaxLength(200);
            entity.Property(u => u.ParentResponseId).HasMaxLength(200);
            entity.Property(u => u.Model).HasMaxLength(256);
            entity.Property(u => u.Provider).HasMaxLength(100);
            entity.Property(u => u.Deployment).HasMaxLength(200);
            entity.Property(u => u.ModelSource).HasMaxLength(32);
            entity.HasIndex(u => u.CreatedAtUtc);
            entity.HasIndex(u => new { u.CompanyId, u.CreatedAtUtc });
            entity.HasIndex(u => new { u.MachineId, u.CreatedAtUtc });
            entity.HasIndex(u => new { u.UserId, u.CreatedAtUtc });
            entity.HasIndex(u => new { u.UsageType, u.CreatedAtUtc });
        });

        // Pre-existing 'dbo.Users' table, owned outside this app — read-only, never migrated by this context.
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users", "dbo", t => t.ExcludeFromMigrations());
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(320);
            entity.Property(u => u.Role).IsRequired().HasMaxLength(50);
            entity.Property(u => u.Status).IsRequired().HasMaxLength(50);
            entity.Property(u => u.EntraObjectId).HasMaxLength(100);
            entity.Property(u => u.FirstName).HasMaxLength(100);
            entity.Property(u => u.LastName).HasMaxLength(100);
            entity.Property(u => u.PhoneNumber).HasMaxLength(30);

            entity.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("Conversations", Schema);
            entity.HasKey(c => c.Id);
            entity.Property(c => c.ConversationPublicId).IsRequired().HasMaxLength(200);
            entity.Property(c => c.UserObjectId).IsRequired().HasMaxLength(200);

            entity.HasIndex(c => c.ConversationPublicId);
            entity.HasIndex(c => c.UserObjectId);
            entity.HasIndex(c => c.CreatedAtUtc);
            entity.HasIndex(c => c.MachineId);

            entity.HasMany(c => c.Messages)
                  .WithOne(m => m.Conversation!)
                  .HasForeignKey(m => m.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Machine>()
                  .WithMany()
                  .HasForeignKey(c => c.MachineId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConversationMessage>(entity =>
        {
            entity.ToTable("ConversationMessages", Schema);
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Role).IsRequired().HasMaxLength(50);
            entity.Property(m => m.Content).IsRequired();
            entity.Property(m => m.SuggestionsJson);

            entity.HasIndex(m => m.ConversationId);

            entity.HasMany(m => m.Visuals)
                  .WithOne(v => v.ConversationMessage!)
                  .HasForeignKey(v => v.ConversationMessageId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(m => m.Sources)
                .WithOne(source => source.ConversationMessage!)
                .HasForeignKey(source => source.ConversationMessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConversationMessageVisual>(entity =>
        {
            entity.ToTable("ConversationMessageVisuals", Schema, table =>
            {
                table.HasCheckConstraint("CK_ConversationMessageVisuals_Page", "[Page] > 0");
                table.HasCheckConstraint("CK_ConversationMessageVisuals_DisplayOrder", "[DisplayOrder] >= 0");
                table.HasCheckConstraint("CK_ConversationMessageVisuals_AssetType", "[AssetType] IN ('full', 'tile')");
                if (Database.IsSqlServer())
                {
                    table.HasCheckConstraint(
                        "CK_ConversationMessageVisuals_RequiredStrings",
                        "LEN([DocumentId]) > 0 AND LEN([Name]) > 0 AND LEN([AssetKey]) > 0");
                }
            });
            entity.HasKey(v => v.Id);
            entity.Property(v => v.DocumentId).IsRequired().HasMaxLength(200);
            entity.Property(v => v.AssetType).IsRequired().HasMaxLength(4).IsUnicode(false);
            entity.Property(v => v.Tile).HasMaxLength(7).IsUnicode(false);
            entity.Property(v => v.Name).IsRequired().HasMaxLength(512);
            entity.Property(v => v.AssetKey).IsRequired().HasMaxLength(768);

            entity.HasIndex(v => v.ConversationMessageId);
            entity.HasIndex(v => new { v.ConversationMessageId, v.AssetKey }).IsUnique();
        });

        modelBuilder.Entity<ConversationMessageSourceReference>(entity =>
        {
            entity.ToTable("ConversationMessageSourceReferences", Schema, table =>
            {
                table.HasCheckConstraint("CK_ConversationMessageSourceReferences_PdfPage", "[PdfPage] > 0");
                table.HasCheckConstraint("CK_ConversationMessageSourceReferences_TextRange", "[StartIndex] >= 0 AND [EndIndex] > [StartIndex]");
                table.HasCheckConstraint("CK_ConversationMessageSourceReferences_DisplayOrder", "[DisplayOrder] >= 0");
                if (Database.IsSqlServer())
                {
                    table.HasCheckConstraint(
                        "CK_ConversationMessageSourceReferences_RequiredStrings",
                        "LEN([DocumentId]) > 0 AND LEN([DisplayPage]) > 0 AND LEN([Label]) > 0");
                }
            });
            entity.HasKey(source => source.Id);
            entity.Property(source => source.DocumentId).IsRequired().HasMaxLength(200);
            entity.Property(source => source.DisplayPage).IsRequired().HasMaxLength(64);
            entity.Property(source => source.Label).IsRequired().HasMaxLength(128);

            entity.HasIndex(source => source.ConversationMessageId);
            entity.HasIndex(source => new { source.ConversationMessageId, source.DisplayOrder }).IsUnique();
        });

        modelBuilder.Entity<LoginCode>(entity =>
        {
            entity.ToTable("LoginCodes", Schema);
            entity.HasKey(l => l.Id);
            entity.Property(l => l.CodeHash).IsRequired().HasMaxLength(200);

            entity.HasIndex(l => l.UserId);
            entity.HasIndex(l => l.ExpiresAtUtc);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("UserSessions", Schema);
            entity.HasKey(s => s.Id);
            entity.Property(s => s.TokenHash).IsRequired().HasMaxLength(200);

            entity.HasIndex(s => s.TokenHash);
            entity.HasIndex(s => s.UserId);
            entity.HasIndex(s => s.ExpiresAtUtc);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            // Use legacy dbo.Companies (pre-existing business table). Do not include in migrations.
            entity.ToTable("Companies", "dbo", t => t.ExcludeFromMigrations());
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
            entity.Property(c => c.Status).IsRequired().HasMaxLength(50);

            // Map audit timestamps to legacy column names in dbo.Companies
            entity.Property(c => c.CreatedAtUtc).HasColumnName("CreatedAt");
            entity.Property(c => c.UpdatedAtUtc).HasColumnName("UpdatedAt");

            entity.HasIndex(c => c.Name);
        });

        modelBuilder.Entity<CompanyBranding>(entity =>
        {
            entity.ToTable("CompanyBrandings", Schema);
            entity.HasKey(branding => branding.CompanyId);
            entity.Property(branding => branding.CompanyId).ValueGeneratedNever();
            entity.Property(branding => branding.AccentColor).HasMaxLength(7).IsUnicode(false);
            entity.Property(branding => branding.LogoBlobName).HasMaxLength(512);
            entity.Property(branding => branding.LogoContentType).HasMaxLength(100).IsUnicode(false);
            entity.Property(branding => branding.UpdatedAtUtc).IsRequired();
            entity.HasOne<Company>().WithOne().HasForeignKey<CompanyBranding>(branding => branding.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Machine>(entity =>
        {
            // Map to legacy dbo.Machines and reuse existing columns. Do not include in migrations.
            entity.ToTable("Machines", "dbo", t => t.ExcludeFromMigrations());
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Name).IsRequired().HasMaxLength(200);
            // Mapping for legacy dbo.Machines: we reuse existing business columns only.
            entity.Property(m => m.Status).IsRequired().HasMaxLength(50);

            entity.Property(m => m.Reference).HasMaxLength(200).HasColumnName("Reference");
            entity.Property(m => m.VectorStoreId).HasMaxLength(200).HasColumnName("VectorStoreId");
            entity.Property(m => m.BlobPrefix).HasMaxLength(200).HasColumnName("BlobPrefix");
            entity.Property(m => m.ProjectEndpoint).HasMaxLength(500).HasColumnName("ProjectEndpoint");

            // Map audit columns to legacy names
            entity.Property(m => m.CreatedAtUtc).HasColumnName("CreatedAt");
            entity.Property(m => m.UpdatedAtUtc).HasColumnName("UpdatedAt");

            // Real FK: both tables are owned by this app. dbo.Users.CompanyId (which references the same
            // Company conceptually) intentionally has NO SQL FK — dbo.Users is excluded from migrations.
            entity.HasOne(m => m.Company)
                  .WithMany()
                  .HasForeignKey(m => m.CompanyId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(m => m.CompanyId);
            entity.HasIndex(m => new { m.CompanyId, m.Name });
            // (No legacy serial number column is mapped for dbo.Machines.)
        });

        modelBuilder.Entity<UserMachineAccess>(entity =>
        {
            // Map user-machine associations to legacy dbo.UserMachines. Do not include in migrations.
            entity.ToTable("UserMachines", "dbo", t => t.ExcludeFromMigrations());
            entity.HasKey(a => new { a.UserId, a.MachineId });

            // Real FK to Machine (owned by this app). UserId stays a logical Guid — see entity doc comment.
            entity.HasOne(a => a.Machine)
                  .WithMany()
                  .HasForeignKey(a => a.MachineId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(a => a.UserId);
            entity.Property(a => a.CreatedAtUtc).HasColumnName("CreatedAt");
        });

        
    }
}
