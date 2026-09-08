using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using api.Database.Context;
using api.Database.Models;
using api.MQTT;
using api.Services;
using Api.Test.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Api.Test.Services;

public class InspectionRecordServiceTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private TestWebApplicationFactory<Program> _factory = null!;
    private SaraDbContext _context = null!;
    private DatabaseUtilities _db = null!;

    public async ValueTask InitializeAsync()
    {
        (_container, string cs) = await TestSetupHelpers.ConfigurePostgreSqlDatabase();
        _factory = TestSetupHelpers.ConfigureWebApplicationFactory(cs);
        _ = _factory.Services;
        _context = TestSetupHelpers.ConfigurePostgreSqlContext(cs);
        _db = new DatabaseUtilities(_context);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<InspectionRecord?> CreateInScope(IsarInspectionResultMessage message)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInspectionRecordService>();
        return await service.CreateFromMqttMessage(message);
    }

    [Fact]
    public async Task CreateFromMqttMessage_MessageWithoutPose_PersistsNullPoseFields()
    {
        var message = _db.NewIsarInspectionResultMessage();

        var created = await CreateInScope(message);

        Assert.NotNull(created);
        await _context.Entry(created).ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Null(created.RobotPose);
        Assert.Null(created.TargetPosition);
    }

    [Fact]
    public async Task CreateFromMqttMessage_MessageWithFullPose_PersistsPoseValues()
    {
        var robotPose = new Pose(
            new Position(1.0f, 2.0f, 3.0f),
            new Orientation(0.1f, 0.2f, 0.3f, 0.4f)
        );
        var targetPosition = new Position(7.0f, 8.0f, 9.0f);
        var message = _db.NewIsarInspectionResultMessage(
            robotPose: robotPose,
            targetPosition: targetPosition
        );

        var created = await CreateInScope(message);

        Assert.NotNull(created);
        await _context.Entry(created).ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1.0f, created.RobotPose!.Position.X);
        Assert.Equal(0.4f, created.RobotPose.Orientation.W);
        Assert.Equal(8.0f, created.TargetPosition!.Y);
    }

    [Fact]
    public async Task CreateFromMqttMessage_PersistsMissionName()
    {
        var message = _db.NewIsarInspectionResultMessage(
            missionName: "Perimeterrunde - Nordsiden - utenom fase 2",
            inspectionDescription: "Perimeter 2"
        );

        var created = await CreateInScope(message);

        Assert.NotNull(created);
        var persisted = await _context.InspectionRecords.SingleAsync(
            r => r.Id == created.Id,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(message.MissionName, persisted.MissionName);
    }

    [Fact]
    public async Task CreateFromMqttMessage_Duplicate_ReturnsNull()
    {
        var message = _db.NewIsarInspectionResultMessage();

        Assert.NotNull(await CreateInScope(message));
        Assert.Null(await CreateInScope(message));
        Assert.Equal(
            1,
            await _context.InspectionRecords.CountAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task CreateFromMqttMessage_InsertAfterExistenceCheck_ReturnsNull()
    {
        var message = _db.NewIsarInspectionResultMessage(requiredAnalysis: ["per-record-test"]);
        var interceptor = new BeforeInspectionSaveInterceptor(async () =>
            Assert.NotNull(await CreateInScope(message))
        );
        var options = new DbContextOptionsBuilder<SaraDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new SaraDbContext(options);
        using var scope = _factory.Services.CreateScope();
        var service = ActivatorUtilities.CreateInstance<InspectionRecordService>(
            scope.ServiceProvider,
            context
        );

        Assert.Null(await service.CreateFromMqttMessage(message));
        Assert.True(interceptor.Invoked);
        Assert.Equal(1, await _context.Analyses.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await _context.InspectionRecords.CountAsync(TestContext.Current.CancellationToken)
        );
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "IX_AnalysisGroups_GroupId")]
    [InlineData(PostgresErrorCodes.NotNullViolation, "IX_InspectionRecords_InspectionId")]
    public async Task CreateFromMqttMessage_UnrelatedDatabaseFailure_IsNotTreatedAsDuplicate(
        string sqlState,
        string constraintName
    )
    {
        var failure = new DbUpdateException(
            "Save failed",
            new PostgresException(
                "Database failure",
                "ERROR",
                "ERROR",
                sqlState,
                constraintName: constraintName
            )
        );
        var options = new DbContextOptionsBuilder<SaraDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(new BeforeInspectionSaveInterceptor(() => Task.FromException(failure)))
            .Options;
        await using var context = new SaraDbContext(options);
        using var scope = _factory.Services.CreateScope();
        var service = ActivatorUtilities.CreateInstance<InspectionRecordService>(
            scope.ServiceProvider,
            context
        );

        var actual = await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.CreateFromMqttMessage(_db.NewIsarInspectionResultMessage())
        );

        Assert.Same(failure, actual);
    }

    private sealed class BeforeInspectionSaveInterceptor(Func<Task> beforeSave)
        : SaveChangesInterceptor
    {
        public bool Invoked { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                !Invoked
                && eventData
                    .Context!.ChangeTracker.Entries<InspectionRecord>()
                    .Any(entry => entry.State == EntityState.Added)
            )
            {
                await beforeSave();
                Invoked = true;
            }
            return result;
        }
    }
}
