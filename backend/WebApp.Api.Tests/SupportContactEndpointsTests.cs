using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class SupportContactEndpointsTests
{
    [TestMethod]
    public async Task Route_UsesPostAndRequiresSupportContactPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization(options => options.AddPolicy("SupportContact", policy =>
            policy.RequireRole(DiagLinkRoles.Technician, DiagLinkRoles.CompanyAdmin)));
        builder.Services.AddDbContext<DiagLinkDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        builder.Services.AddScoped<DiagLinkUserLookupService>();
        builder.Services.AddScoped<MachineAccessService>();
        builder.Services.AddSingleton<ITransactionalEmailSender>(new Sender("unused", null));
        await using var app = builder.Build();
        app.MapSupportContact();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Cast<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == SupportContactEndpoints.Route);

        Assert.IsTrue(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(metadata => metadata.Policy == "SupportContact"));
        CollectionAssert.AreEqual(new[] { HttpMethods.Post },
            endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.ToArray());
    }

    [TestMethod]
    public async Task CreateAsync_TechnicianWithAccessibleMachine_SendsServerResolvedContext()
    {
        await using var fixture = await Fixture.CreateAsync(DiagLinkRoles.Technician, assignMachine: true);

        var result = await fixture.SubmitAsync(new SupportContactRequest("  La presse affiche le code E42.  ", fixture.MachineId));

        Assert.AreEqual(StatusCodes.Status200OK, Status(result));
        Assert.AreEqual(1, fixture.Sender.Calls);
        Assert.AreEqual("contact@example.test", fixture.Sender.RecipientEmail);
        Assert.AreEqual("alice@example.test", fixture.Sender.ReplyToEmail);
        Assert.AreEqual("Alice Martin", fixture.Sender.ReplyToName);
        Assert.AreEqual("DiagLink — Message d’un utilisateur connecté", fixture.Sender.Subject);
        StringAssert.Contains(fixture.Sender.TextBody!, "Utilisateur : Alice Martin");
        StringAssert.Contains(fixture.Sender.TextBody!, "Entreprise : Ateliers Martin");
        StringAssert.Contains(fixture.Sender.TextBody!, $"Rôle : {DiagLinkRoles.Technician}");
        StringAssert.Contains(fixture.Sender.TextBody!, "Machine : Presse 4 (PR-004)");
        StringAssert.Contains(fixture.Sender.TextBody!, "La presse affiche le code E42.");
    }

    [TestMethod]
    public async Task CreateAsync_CompanyAdminWithoutMachine_SendsSuccessfully()
    {
        await using var fixture = await Fixture.CreateAsync(DiagLinkRoles.CompanyAdmin);

        var result = await fixture.SubmitAsync(new SupportContactRequest("Besoin d’aide.", null));

        Assert.AreEqual(StatusCodes.Status200OK, Status(result));
        StringAssert.Contains(fixture.Sender.TextBody!, "Machine : Non renseignée");
    }

    [TestMethod]
    public async Task CreateAsync_InaccessibleOrUnknownMachine_ReturnsSameGenericNotFound()
    {
        await using var fixture = await Fixture.CreateAsync(DiagLinkRoles.Technician, assignMachine: false);

        var inaccessible = await fixture.SubmitAsync(new SupportContactRequest("Besoin d’aide.", fixture.MachineId));
        var unknown = await fixture.SubmitAsync(new SupportContactRequest("Besoin d’aide.", Guid.NewGuid()));

        Assert.AreEqual(StatusCodes.Status404NotFound, Status(inaccessible));
        Assert.AreEqual(StatusCodes.Status404NotFound, Status(unknown));
        Assert.AreEqual(0, fixture.Sender.Calls);
    }

    [TestMethod]
    public async Task CreateAsync_CrossCompanyMachine_ReturnsNotFoundWithoutSending()
    {
        await using var fixture = await Fixture.CreateAsync(DiagLinkRoles.CompanyAdmin);

        var result = await fixture.SubmitAsync(new SupportContactRequest("Besoin d’aide.", fixture.OtherMachineId));

        Assert.AreEqual(StatusCodes.Status404NotFound, Status(result));
        Assert.AreEqual(0, fixture.Sender.Calls);
    }

    [TestMethod]
    public async Task CreateAsync_InvalidMessage_ReturnsValidationProblemWithoutSending()
    {
        await using var fixture = await Fixture.CreateAsync(DiagLinkRoles.CompanyAdmin);

        var blank = await fixture.SubmitAsync(new SupportContactRequest("   ", null));
        var oversized = await fixture.SubmitAsync(new SupportContactRequest(new string('x', 5001), null));

        Assert.AreEqual(StatusCodes.Status400BadRequest, Status(blank));
        Assert.AreEqual(StatusCodes.Status400BadRequest, Status(oversized));
        Assert.AreEqual(0, fixture.Sender.Calls);
    }

    [TestMethod]
    public async Task CreateAsync_MissingUserClaim_ReturnsUnauthorizedWithoutSending()
    {
        await using var fixture = await Fixture.CreateAsync(DiagLinkRoles.CompanyAdmin);
        fixture.Context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, DiagLinkRoles.CompanyAdmin)], "Test"));

        var result = await fixture.SubmitAsync(new SupportContactRequest("Besoin d’aide.", null));

        Assert.AreEqual(StatusCodes.Status401Unauthorized, Status(result));
        Assert.AreEqual(0, fixture.Sender.Calls);
    }

    [TestMethod]
    public async Task CreateAsync_UnconfirmedOrFailedDelivery_ReturnsBadGateway()
    {
        await using var unconfirmed = await Fixture.CreateAsync(DiagLinkRoles.CompanyAdmin, operationId: null);
        await using var failed = await Fixture.CreateAsync(DiagLinkRoles.CompanyAdmin,
            senderException: new IOException("sensitive provider detail"));

        var unconfirmedResult = await unconfirmed.SubmitAsync(new SupportContactRequest("Besoin d’aide.", null));
        var failedResult = await failed.SubmitAsync(new SupportContactRequest("Besoin d’aide.", null));

        Assert.AreEqual(StatusCodes.Status502BadGateway, Status(unconfirmedResult));
        Assert.AreEqual(StatusCodes.Status502BadGateway, Status(failedResult));
    }

    private static int? Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode;

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly DiagLinkDbContext db;
        private readonly IConfiguration configuration;

        private Fixture(DiagLinkDbContext db, DefaultHttpContext context, Sender sender,
            IConfiguration configuration, Guid machineId, Guid otherMachineId)
        {
            this.db = db;
            Context = context;
            Sender = sender;
            this.configuration = configuration;
            MachineId = machineId;
            OtherMachineId = otherMachineId;
        }

        public DefaultHttpContext Context { get; }
        public Sender Sender { get; }
        public Guid MachineId { get; }
        public Guid OtherMachineId { get; }

        public static async Task<Fixture> CreateAsync(string role, bool assignMachine = false,
            string? operationId = "acs-support-42", Exception? senderException = null)
        {
            var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new DiagLinkDbContext(options);
            var companyId = Guid.NewGuid();
            var otherCompanyId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var machineId = Guid.NewGuid();
            var otherMachineId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            db.Companies.AddRange(
                new Company { Id = companyId, Name = "Ateliers Martin", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now },
                new Company { Id = otherCompanyId, Name = "Autre entreprise", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Users.Add(new User
            {
                Id = userId,
                CompanyId = companyId,
                Email = "alice@example.test",
                FirstName = "Alice",
                LastName = "Martin",
                Role = role,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Machines.AddRange(
                new Machine { Id = machineId, CompanyId = companyId, Name = "Presse 4", Reference = "PR-004", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now },
                new Machine { Id = otherMachineId, CompanyId = otherCompanyId, Name = "Tour 2", Reference = "TR-002", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            if (assignMachine)
            {
                db.UserMachineAccess.Add(new UserMachineAccess { UserId = userId, MachineId = machineId, CreatedAtUtc = now });
            }
            await db.SaveChangesAsync();

            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(DiagLinkClaimTypes.UserId, userId.ToString()),
                    new Claim(DiagLinkClaimTypes.CompanyId, companyId.ToString()),
                    new Claim(ClaimTypes.Role, role),
                ], "Test")),
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Contact:RecipientAddress"] = "contact@example.test" }).Build();
            return new Fixture(db, context, new Sender(operationId, senderException), configuration,
                machineId, otherMachineId);
        }

        public Task<IResult> SubmitAsync(SupportContactRequest request) =>
            SupportContactEndpoints.CreateAsync(request, Context, configuration, db,
                new DiagLinkUserLookupService(db), new MachineAccessService(db), Sender,
                NullLoggerFactory.Instance, default);

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class Sender(string? operationId, Exception? exception) : ITransactionalEmailSender
    {
        public int Calls { get; private set; }
        public string? RecipientEmail { get; private set; }
        public string? Subject { get; private set; }
        public string? TextBody { get; private set; }
        public string? ReplyToEmail { get; private set; }
        public string? ReplyToName { get; private set; }

        public Task<string?> SendAsync(string recipientEmail, string subject, string textBody,
            string? htmlBody, CancellationToken cancellationToken,
            string? replyToEmail = null, string? replyToName = null)
        {
            Calls++;
            RecipientEmail = recipientEmail;
            Subject = subject;
            TextBody = textBody;
            ReplyToEmail = replyToEmail;
            ReplyToName = replyToName;
            return exception is null ? Task.FromResult(operationId) : Task.FromException<string?>(exception);
        }
    }
}