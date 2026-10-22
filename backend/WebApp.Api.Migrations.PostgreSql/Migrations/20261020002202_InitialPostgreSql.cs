using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WebApp.Api.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgreSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.EnsureSchema(
                name: "chat");

            migrationBuilder.CreateTable(
                name: "AiPricing",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UsageType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    InputPricePerMillion = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    OutputPricePerMillion = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    CacheReadPricePerMillion = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    CacheCreation5mPricePerMillion = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    CacheCreation1hPricePerMillion = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    WebSearchPricePerRequest = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", unicode: false, maxLength: 3, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPricing", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiUsageRecords",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsageType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Available = table.Column<bool>(type: "boolean", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssistantMessageId = table.Column<long>(type: "bigint", nullable: true),
                    ConversationPublicId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResponseId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CallId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ParentResponseId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Deployment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ModelSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CallBreakdownJson = table.Column<string>(type: "text", nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    TotalTokens = table.Column<int>(type: "integer", nullable: true),
                    CacheReadInputTokens = table.Column<int>(type: "integer", nullable: true),
                    CacheCreationInputTokens = table.Column<int>(type: "integer", nullable: true),
                    CacheCreation5mInputTokens = table.Column<int>(type: "integer", nullable: true),
                    CacheCreation1hInputTokens = table.Column<int>(type: "integer", nullable: true),
                    WebSearchRequests = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailOutbox",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MachineRequestId = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotificationType = table.Column<int>(type: "integer", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderOperationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOutbox", x => x.Id);
                    table.CheckConstraint("CK_EmailOutbox_AttemptCount", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_EmailOutbox_Lease", "(\"LeaseId\" IS NULL AND \"LockedUntilUtc\" IS NULL) OR (\"LeaseId\" IS NOT NULL AND \"LockedUntilUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_EmailOutbox_NotificationType", "\"NotificationType\" >= 0 AND \"NotificationType\" <= 4");
                    table.CheckConstraint("CK_EmailOutbox_Sent", "(\"Status\" = 0 AND \"SentAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"SentAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_EmailOutbox_Status", "\"Status\" >= 0 AND \"Status\" <= 1");
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseCurrency = table.Column<string>(type: "character varying(3)", unicode: false, maxLength: 3, nullable: false),
                    QuoteCurrency = table.Column<string>(type: "character varying(3)", unicode: false, maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,10)", precision: 18, scale: 10, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                    table.CheckConstraint("CK_ExchangeRates_Interval", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
                    table.CheckConstraint("CK_ExchangeRates_Rate", "\"Rate\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "LoginCodes",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntraObjectId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingAccounts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SubscriptionStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    LatestInvoiceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LatestInvoiceStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AmountRemainingCents = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingAccounts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyBrandings",
                schema: "chat",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccentColor = table.Column<string>(type: "character varying(7)", unicode: false, maxLength: 7, nullable: true),
                    LogoBlobName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LogoContentType = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBrandings", x => x.CompanyId);
                    table.ForeignKey(
                        name: "FK_CompanyBrandings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyWallets",
                schema: "dbo",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", unicode: false, maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyWallets", x => x.CompanyId);
                    table.ForeignKey(
                        name: "FK_CompanyWallets_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Machines",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VectorStoreId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BlobPrefix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProjectEndpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Machines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Machines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StripeLifecycleEvents",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeLifecycleEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StripeLifecycleEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StripeSubscriptionPayments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StripeSubscriptionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StripeInvoiceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BillingReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AmountPaidCents = table.Column<long>(type: "bigint", nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    MachineIdsJson = table.Column<string>(type: "text", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeSubscriptionPayments", x => x.Id);
                    table.CheckConstraint("CK_StripeSubscriptionPayments_Amount", "\"AmountPaidCents\" > 0");
                    table.CheckConstraint("CK_StripeSubscriptionPayments_Cycle", "\"PeriodEndUtc\" > \"PeriodStartUtc\"");
                    table.CheckConstraint("CK_StripeSubscriptionPayments_Reason", "\"BillingReason\" IN ('subscription_create', 'subscription_cycle')");
                    table.ForeignKey(
                        name: "FK_StripeSubscriptionPayments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationPublicId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserObjectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Entreprise = table.Column<string>(type: "text", nullable: true),
                    Machine = table.Column<string>(type: "text", nullable: true),
                    AgentName = table.Column<string>(type: "text", nullable: true),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: true),
                    TechnicalSummary = table.Column<string>(type: "text", nullable: true),
                    TechnicalStateJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conversations_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "dbo",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MachineBillingPeriods",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IncludedAiBudgetRealCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    IncludedAiUsedRealCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineBillingPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineBillingPeriods_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "dbo",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MachineRequestPayments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKind = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EstimatedTotalPages = table.Column<int>(type: "integer", nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", unicode: false, maxLength: 3, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    StripeSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AuthorizationEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MachineRequestId = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestLinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstPeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ServiceAmountCents = table.Column<int>(type: "integer", nullable: true),
                    FinalCaptureAmountCents = table.Column<long>(type: "bigint", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetMachineId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProvisioningStage = table.Column<int>(type: "integer", nullable: false),
                    ProvisioningCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreparationStatus = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ReadyAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadyByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineRequestPayments", x => x.Id);
                    table.CheckConstraint("CK_MachineRequestPayments_Amount", "\"AmountCents\" > 0");
                    table.CheckConstraint("CK_MachineRequestPayments_Currency", "\"Currency\" = 'EUR'");
                    table.CheckConstraint("CK_MachineRequestPayments_FinalCaptureAmount", "\"FinalCaptureAmountCents\" IS NULL OR (\"FinalCaptureAmountCents\" > 0 AND \"FinalCaptureAmountCents\" <= \"AmountCents\")");
                    table.CheckConstraint("CK_MachineRequestPayments_FirstPeriod", "(\"ActivatedAtUtc\" IS NULL AND \"FirstPeriodEndUtc\" IS NULL) OR (\"ActivatedAtUtc\" IS NOT NULL AND \"FirstPeriodEndUtc\" IS NOT NULL AND \"ActivatedAtUtc\" < \"FirstPeriodEndUtc\")");
                    table.CheckConstraint("CK_MachineRequestPayments_Link", "(\"MachineRequestId\" IS NULL AND \"RequestLinkedAtUtc\" IS NULL) OR (\"MachineRequestId\" IS NOT NULL AND \"RequestLinkedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_MachineRequestPayments_Pages", "\"EstimatedTotalPages\" > 0");
                    table.CheckConstraint("CK_MachineRequestPayments_PreparationStatus", "\"PreparationStatus\" >= 0 AND \"PreparationStatus\" <= 1");
                    table.CheckConstraint("CK_MachineRequestPayments_ProvisioningCompleted", "(\"ProvisioningStage\" < 6 AND \"ProvisioningCompletedAtUtc\" IS NULL) OR (\"ProvisioningStage\" = 6 AND \"ProvisioningCompletedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_MachineRequestPayments_ProvisioningStage", "\"ProvisioningStage\" >= 0 AND \"ProvisioningStage\" <= 6");
                    table.CheckConstraint("CK_MachineRequestPayments_Ready", "(\"PreparationStatus\" = 0 AND \"ReadyAtUtc\" IS NULL AND \"ReadyByUserId\" IS NULL) OR (\"PreparationStatus\" = 1 AND \"ReadyAtUtc\" IS NOT NULL AND \"ReadyByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_MachineRequestPayments_RequestKind", "\"RequestKind\" >= 0 AND \"RequestKind\" <= 2");
                    table.CheckConstraint("CK_MachineRequestPayments_ServiceAmount", "\"ServiceAmountCents\" IS NULL OR (\"ServiceAmountCents\" >= 0 AND \"ServiceAmountCents\" <= 1990)");
                    table.CheckConstraint("CK_MachineRequestPayments_State", "(\"Status\" = 0 AND \"StripePaymentIntentId\" IS NULL AND \"AuthorizationEventId\" IS NULL AND \"AuthorizedAtUtc\" IS NULL AND \"CapturedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"StripePaymentIntentId\" IS NOT NULL AND \"AuthorizationEventId\" IS NOT NULL AND \"AuthorizedAtUtc\" IS NOT NULL AND \"CapturedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL) OR (\"Status\" = 2 AND \"StripePaymentIntentId\" IS NOT NULL AND \"AuthorizationEventId\" IS NOT NULL AND \"AuthorizedAtUtc\" IS NOT NULL AND \"CapturedAtUtc\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL) OR (\"Status\" = 3 AND \"StripePaymentIntentId\" IS NOT NULL AND \"AuthorizationEventId\" IS NOT NULL AND \"AuthorizedAtUtc\" IS NOT NULL AND \"CapturedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NOT NULL) OR (\"Status\" = 4 AND \"StripePaymentIntentId\" IS NULL AND \"AuthorizationEventId\" IS NULL AND \"AuthorizedAtUtc\" IS NULL AND \"CapturedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_MachineRequestPayments_Status", "\"Status\" >= 0 AND \"Status\" <= 4");
                    table.CheckConstraint("CK_MachineRequestPayments_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_MachineRequestPayments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineRequestPayments_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "dbo",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineRequestPayments_Machines_TargetMachineId",
                        column: x => x.TargetMachineId,
                        principalSchema: "dbo",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserMachines",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMachines", x => new { x.UserId, x.MachineId });
                    table.ForeignKey(
                        name: "FK_UserMachines_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "dbo",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SuggestionsJson = table.Column<string>(type: "text", nullable: true),
                    TokenCount = table.Column<int>(type: "integer", nullable: true),
                    IsSummarized = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationMessages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "chat",
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CreditLedger",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: true),
                    MachineBillingPeriodId = table.Column<Guid>(type: "uuid", nullable: true),
                    AiUsageRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntryType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BucketType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RealAiCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    CommercialCreditAmount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", unicode: false, maxLength: 3, nullable: true),
                    ExternalEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditLedger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditLedger_AiUsageRecords_AiUsageRecordId",
                        column: x => x.AiUsageRecordId,
                        principalSchema: "chat",
                        principalTable: "AiUsageRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditLedger_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditLedger_MachineBillingPeriods_MachineBillingPeriodId",
                        column: x => x.MachineBillingPeriodId,
                        principalSchema: "dbo",
                        principalTable: "MachineBillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditLedger_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "dbo",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StripeMachineAdditions",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CycleStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CycleEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StripeSubscriptionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StripeSubscriptionItemId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StripePriceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OriginalQuantity = table.Column<long>(type: "bigint", nullable: false),
                    TargetQuantity = table.Column<long>(type: "bigint", nullable: false),
                    AiAmountCents = table.Column<int>(type: "integer", nullable: false),
                    ServiceAmountCents = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    MachineBillingPeriodId = table.Column<Guid>(type: "uuid", nullable: true),
                    StripeInvoiceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PaymentConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeMachineAdditions", x => x.Id);
                    table.CheckConstraint("CK_StripeMachineAdditions_Amounts", "\"AiAmountCents\" = 1000 AND \"ServiceAmountCents\" >= 0 AND \"ServiceAmountCents\" <= 1990");
                    table.CheckConstraint("CK_StripeMachineAdditions_Completed", "(\"Stage\" < 6 AND \"CompletedAtUtc\" IS NULL) OR (\"Stage\" = 6 AND \"CompletedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_StripeMachineAdditions_Cycle", "\"CycleStartUtc\" <= \"ActivatedAtUtc\" AND \"ActivatedAtUtc\" < \"CycleEndUtc\"");
                    table.CheckConstraint("CK_StripeMachineAdditions_Invoice", "\"Stage\" < 2 OR \"StripeInvoiceId\" IS NOT NULL");
                    table.CheckConstraint("CK_StripeMachineAdditions_Payment", "(\"Stage\" < 4 AND \"PaymentReference\" IS NULL AND \"PaymentConfirmedAtUtc\" IS NULL) OR (\"Stage\" >= 4 AND \"PaymentReference\" IS NOT NULL AND \"PaymentReference\" <> '' AND \"PaymentConfirmedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_StripeMachineAdditions_Period", "(\"Stage\" < 5 AND \"MachineBillingPeriodId\" IS NULL) OR (\"Stage\" >= 5 AND \"MachineBillingPeriodId\" IS NOT NULL)");
                    table.CheckConstraint("CK_StripeMachineAdditions_Quantity", "\"OriginalQuantity\" >= 0 AND \"TargetQuantity\" = \"OriginalQuantity\" + 1");
                    table.CheckConstraint("CK_StripeMachineAdditions_Stage", "\"Stage\" >= 0 AND \"Stage\" <= 6");
                    table.ForeignKey(
                        name: "FK_StripeMachineAdditions_BillingAccounts_BillingAccountId",
                        column: x => x.BillingAccountId,
                        principalSchema: "dbo",
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StripeMachineAdditions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StripeMachineAdditions_MachineBillingPeriods_MachineBilling~",
                        column: x => x.MachineBillingPeriodId,
                        principalSchema: "dbo",
                        principalTable: "MachineBillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StripeMachineAdditions_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "dbo",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessageSourceReferences",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationMessageId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PdfPage = table.Column<int>(type: "integer", nullable: false),
                    DisplayPage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartIndex = table.Column<int>(type: "integer", nullable: false),
                    EndIndex = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessageSourceReferences", x => x.Id);
                    table.CheckConstraint("CK_ConversationMessageSourceReferences_DisplayOrder", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("CK_ConversationMessageSourceReferences_PdfPage", "\"PdfPage\" > 0");
                    table.CheckConstraint("CK_ConversationMessageSourceReferences_RequiredStrings", "char_length(\"DocumentId\") > 0 AND char_length(\"DisplayPage\") > 0 AND char_length(\"Label\") > 0");
                    table.CheckConstraint("CK_ConversationMessageSourceReferences_TextRange", "\"StartIndex\" >= 0 AND \"EndIndex\" > \"StartIndex\"");
                    table.ForeignKey(
                        name: "FK_ConversationMessageSourceReferences_ConversationMessages_Co~",
                        column: x => x.ConversationMessageId,
                        principalSchema: "chat",
                        principalTable: "ConversationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessageVisuals",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationMessageId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    AssetType = table.Column<string>(type: "character varying(4)", unicode: false, maxLength: 4, nullable: false),
                    Tile = table.Column<string>(type: "character varying(7)", unicode: false, maxLength: 7, nullable: true),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AssetKey = table.Column<string>(type: "character varying(768)", maxLength: 768, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessageVisuals", x => x.Id);
                    table.CheckConstraint("CK_ConversationMessageVisuals_AssetType", "\"AssetType\" IN ('full', 'tile')");
                    table.CheckConstraint("CK_ConversationMessageVisuals_DisplayOrder", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("CK_ConversationMessageVisuals_Page", "\"Page\" > 0");
                    table.CheckConstraint("CK_ConversationMessageVisuals_RequiredStrings", "char_length(\"DocumentId\") > 0 AND char_length(\"Name\") > 0 AND char_length(\"AssetKey\") > 0");
                    table.ForeignKey(
                        name: "FK_ConversationMessageVisuals_ConversationMessages_Conversatio~",
                        column: x => x.ConversationMessageId,
                        principalSchema: "chat",
                        principalTable: "ConversationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessageWebCitations",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationMessageId = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", unicode: false, maxLength: 2048, nullable: false),
                    Label = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Quote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessageWebCitations", x => x.Id);
                    table.CheckConstraint("CK_ConversationMessageWebCitations_DisplayOrder", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("CK_ConversationMessageWebCitations_RequiredStrings", "char_length(\"Url\") > 0 AND char_length(\"Label\") > 0");
                    table.ForeignKey(
                        name: "FK_ConversationMessageWebCitations_ConversationMessages_Conver~",
                        column: x => x.ConversationMessageId,
                        principalSchema: "chat",
                        principalTable: "ConversationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StripeWalletTopUps",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AmountCents = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", unicode: false, maxLength: 3, nullable: false),
                    ReturnUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    StripeSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaymentUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaymentConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeWalletTopUps", x => x.Id);
                    table.CheckConstraint("CK_StripeWalletTopUps_Amount", "\"AmountCents\" >= 1000 AND \"AmountCents\" <= 99999999 AND \"Currency\" = 'EUR'");
                    table.CheckConstraint("CK_StripeWalletTopUps_Completed", "(\"Stage\" < 5 AND \"CompletedAtUtc\" IS NULL) OR (\"Stage\" = 5 AND \"CompletedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_StripeWalletTopUps_Ledger", "(\"Stage\" < 4 AND \"LedgerEntryId\" IS NULL) OR (\"Stage\" >= 4 AND \"LedgerEntryId\" IS NOT NULL)");
                    table.CheckConstraint("CK_StripeWalletTopUps_Payment", "(\"Stage\" < 3 AND \"StripePaymentIntentId\" IS NULL AND \"PaymentConfirmedAtUtc\" IS NULL) OR (\"Stage\" >= 3 AND \"StripePaymentIntentId\" IS NOT NULL AND \"PaymentConfirmedAtUtc\" IS NOT NULL AND \"ExternalEventId\" IS NOT NULL)");
                    table.CheckConstraint("CK_StripeWalletTopUps_Session", "\"Stage\" = 0 OR \"StripeSessionId\" IS NOT NULL");
                    table.CheckConstraint("CK_StripeWalletTopUps_Stage", "\"Stage\" >= 0 AND \"Stage\" <= 5");
                    table.ForeignKey(
                        name: "FK_StripeWalletTopUps_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StripeWalletTopUps_CreditLedger_LedgerEntryId",
                        column: x => x.LedgerEntryId,
                        principalSchema: "dbo",
                        principalTable: "CreditLedger",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiPricing_Provider_Model_UsageType_EffectiveFromUtc",
                schema: "dbo",
                table: "AiPricing",
                columns: new[] { "Provider", "Model", "UsageType", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_CompanyId_CreatedAtUtc",
                schema: "chat",
                table: "AiUsageRecords",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_CreatedAtUtc",
                schema: "chat",
                table: "AiUsageRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_MachineId_CreatedAtUtc",
                schema: "chat",
                table: "AiUsageRecords",
                columns: new[] { "MachineId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_UsageType_CreatedAtUtc",
                schema: "chat",
                table: "AiUsageRecords",
                columns: new[] { "UsageType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_UserId_CreatedAtUtc",
                schema: "chat",
                table: "AiUsageRecords",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_CompanyId",
                schema: "dbo",
                table: "BillingAccounts",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_StripeCustomerId",
                schema: "dbo",
                table: "BillingAccounts",
                column: "StripeCustomerId",
                unique: true,
                filter: "\"StripeCustomerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_StripeSubscriptionId",
                schema: "dbo",
                table: "BillingAccounts",
                column: "StripeSubscriptionId",
                unique: true,
                filter: "\"StripeSubscriptionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Name",
                schema: "dbo",
                table: "Companies",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationId",
                schema: "chat",
                table: "ConversationMessages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageSourceReferences_ConversationMessageId",
                schema: "chat",
                table: "ConversationMessageSourceReferences",
                column: "ConversationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageSourceReferences_ConversationMessageId_D~",
                schema: "chat",
                table: "ConversationMessageSourceReferences",
                columns: new[] { "ConversationMessageId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageVisuals_ConversationMessageId",
                schema: "chat",
                table: "ConversationMessageVisuals",
                column: "ConversationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageVisuals_ConversationMessageId_AssetKey",
                schema: "chat",
                table: "ConversationMessageVisuals",
                columns: new[] { "ConversationMessageId", "AssetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageWebCitations_ConversationMessageId",
                schema: "chat",
                table: "ConversationMessageWebCitations",
                column: "ConversationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageWebCitations_ConversationMessageId_Displ~",
                schema: "chat",
                table: "ConversationMessageWebCitations",
                columns: new[] { "ConversationMessageId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageWebCitations_ConversationMessageId_Url",
                schema: "chat",
                table: "ConversationMessageWebCitations",
                columns: new[] { "ConversationMessageId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ConversationPublicId",
                schema: "chat",
                table: "Conversations",
                column: "ConversationPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CreatedAtUtc",
                schema: "chat",
                table: "Conversations",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_MachineId",
                schema: "chat",
                table: "Conversations",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserObjectId",
                schema: "chat",
                table: "Conversations",
                column: "UserObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_AiUsageRecordId",
                schema: "dbo",
                table: "CreditLedger",
                column: "AiUsageRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_CompanyId_CreatedAtUtc",
                schema: "dbo",
                table: "CreditLedger",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_ExternalEventId",
                schema: "dbo",
                table: "CreditLedger",
                column: "ExternalEventId",
                unique: true,
                filter: "\"ExternalEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_MachineBillingPeriodId",
                schema: "dbo",
                table: "CreditLedger",
                column: "MachineBillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_MachineId_CreatedAtUtc",
                schema: "dbo",
                table: "CreditLedger",
                columns: new[] { "MachineId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CreditLedger_CompanyWalletUsage",
                schema: "dbo",
                table: "CreditLedger",
                column: "AiUsageRecordId",
                unique: true,
                filter: "\"AiUsageRecordId\" IS NOT NULL AND \"BucketType\" = 'CompanyWallet' AND \"EntryType\" = 'AiUsage'");

            migrationBuilder.CreateIndex(
                name: "UX_CreditLedger_MachineIncludedUsage",
                schema: "dbo",
                table: "CreditLedger",
                column: "AiUsageRecordId",
                unique: true,
                filter: "\"AiUsageRecordId\" IS NOT NULL AND \"BucketType\" = 'MachineIncluded' AND \"EntryType\" = 'AiUsage'");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_MachineRequestId_NotificationType",
                schema: "dbo",
                table: "EmailOutbox",
                columns: new[] { "MachineRequestId", "NotificationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_PaymentRequestId",
                schema: "dbo",
                table: "EmailOutbox",
                column: "PaymentRequestId",
                filter: "\"PaymentRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_Status_NextAttemptAtUtc_LockedUntilUtc",
                schema: "dbo",
                table: "EmailOutbox",
                columns: new[] { "Status", "NextAttemptAtUtc", "LockedUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_BaseCurrency_QuoteCurrency_EffectiveFromUtc",
                schema: "dbo",
                table: "ExchangeRates",
                columns: new[] { "BaseCurrency", "QuoteCurrency", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LoginCodes_ExpiresAtUtc",
                schema: "chat",
                table: "LoginCodes",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LoginCodes_UserId",
                schema: "chat",
                table: "LoginCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineBillingPeriods_MachineId",
                schema: "dbo",
                table: "MachineBillingPeriods",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineBillingPeriods_MachineId_PeriodStartUtc",
                schema: "dbo",
                table: "MachineBillingPeriods",
                columns: new[] { "MachineId", "PeriodStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_AuthorizationEventId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "AuthorizationEventId",
                unique: true,
                filter: "\"AuthorizationEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_CompanyId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "CompanyId",
                filter: "\"CompanyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_MachineId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "MachineId",
                unique: true,
                filter: "\"MachineId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_MachineRequestId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "MachineRequestId",
                unique: true,
                filter: "\"MachineRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_StripePaymentIntentId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "StripePaymentIntentId",
                unique: true,
                filter: "\"StripePaymentIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_StripeSessionId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "StripeSessionId",
                unique: true,
                filter: "\"StripeSessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_TargetMachineId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "TargetMachineId",
                filter: "\"TargetMachineId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_CompanyId",
                schema: "dbo",
                table: "Machines",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_CompanyId_Name",
                schema: "dbo",
                table: "Machines",
                columns: new[] { "CompanyId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_StripeLifecycleEvents_CompanyId",
                schema: "dbo",
                table: "StripeLifecycleEvents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_BillingAccountId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "BillingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_CompanyId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "CompanyId",
                unique: true,
                filter: "\"CompletedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_ExternalEventId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "ExternalEventId",
                unique: true,
                filter: "\"ExternalEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_MachineBillingPeriodId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "MachineBillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_MachineId_CycleStartUtc",
                schema: "dbo",
                table: "StripeMachineAdditions",
                columns: new[] { "MachineId", "CycleStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_StripeInvoiceId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "StripeInvoiceId",
                unique: true,
                filter: "\"StripeInvoiceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeSubscriptionPayments_CompanyId",
                schema: "dbo",
                table: "StripeSubscriptionPayments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StripeSubscriptionPayments_ExternalEventId",
                schema: "dbo",
                table: "StripeSubscriptionPayments",
                column: "ExternalEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeSubscriptionPayments_StripeInvoiceId",
                schema: "dbo",
                table: "StripeSubscriptionPayments",
                column: "StripeInvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeSubscriptionPayments_StripeSubscriptionId_PeriodStart~",
                schema: "dbo",
                table: "StripeSubscriptionPayments",
                columns: new[] { "StripeSubscriptionId", "PeriodStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeWalletTopUps_CompanyId_CreatedAtUtc",
                schema: "dbo",
                table: "StripeWalletTopUps",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWalletTopUps_ExternalEventId",
                schema: "dbo",
                table: "StripeWalletTopUps",
                column: "ExternalEventId",
                unique: true,
                filter: "\"ExternalEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWalletTopUps_LedgerEntryId",
                schema: "dbo",
                table: "StripeWalletTopUps",
                column: "LedgerEntryId",
                unique: true,
                filter: "\"LedgerEntryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWalletTopUps_StripePaymentIntentId",
                schema: "dbo",
                table: "StripeWalletTopUps",
                column: "StripePaymentIntentId",
                unique: true,
                filter: "\"StripePaymentIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWalletTopUps_StripeSessionId",
                schema: "dbo",
                table: "StripeWalletTopUps",
                column: "StripeSessionId",
                unique: true,
                filter: "\"StripeSessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserMachines_MachineId",
                schema: "dbo",
                table: "UserMachines",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMachines_UserId",
                schema: "dbo",
                table: "UserMachines",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                schema: "dbo",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_EntraObjectId_NotNull",
                schema: "dbo",
                table: "Users",
                column: "EntraObjectId",
                unique: true,
                filter: "\"EntraObjectId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_ExpiresAtUtc",
                schema: "chat",
                table: "UserSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_TokenHash",
                schema: "chat",
                table: "UserSessions",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                schema: "chat",
                table: "UserSessions",
                column: "UserId");

            // SQL Server's default collation made the historical unique email index
            // case-insensitive. Preserve that business rule explicitly on PostgreSQL.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_Users_Email_Normalized"
                    ON "dbo"."Users" (lower("Email"));
                """);

            // Defense in depth for immutable TopUp entries. The DbContext guard remains active,
            // while this trigger also protects writes made outside EF Core.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION "dbo"."protect_credit_ledger_topup_immutable"()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'TopUp ledger entries are immutable.';
                    RETURN NULL;
                END;
                $function$;

                CREATE TRIGGER "TR_CreditLedger_TopUpImmutable"
                    BEFORE UPDATE OR DELETE ON "dbo"."CreditLedger"
                    FOR EACH ROW
                    WHEN (OLD."EntryType" = 'TopUp')
                    EXECUTE FUNCTION "dbo"."protect_credit_ledger_topup_immutable"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_CreditLedger_TopUpImmutable" ON "dbo"."CreditLedger";
                DROP FUNCTION IF EXISTS "dbo"."protect_credit_ledger_topup_immutable"();
                """);

            migrationBuilder.DropTable(
                name: "AiPricing",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CompanyBrandings",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "CompanyWallets",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ConversationMessageSourceReferences",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "ConversationMessageVisuals",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "ConversationMessageWebCitations",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "EmailOutbox",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ExchangeRates",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "LoginCodes",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "MachineRequestPayments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "StripeLifecycleEvents",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "StripeMachineAdditions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "StripeSubscriptionPayments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "StripeWalletTopUps",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "UserMachines",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "UserSessions",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "ConversationMessages",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "BillingAccounts",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CreditLedger",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Conversations",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "AiUsageRecords",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "MachineBillingPeriods",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Machines",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Companies",
                schema: "dbo");
        }
    }
}
