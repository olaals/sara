using api.Configurations;
using api.Database.Context;
using api.Database.Models;
using api.MQTT;
using api.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace api.Services;

public interface IInspectionRecordService
{
    /// <summary>Returns null when a record with the inspection ID already exists.</summary>
    public Task<InspectionRecord?> CreateFromMqttMessage(IsarInspectionResultMessage message);

    public Task<InspectionRecord> Create(InspectionRecord inspectionRecord);

    public Task<InspectionRecord> CreateAndTrigger(CreateInspectionRecordRequest request);

    public Task<InspectionRecord?> ReadById(Guid id);

    public Task<InspectionRecord?> ReadByInspectionId(string inspectionId);

    public Task<bool> ExistsByInspectionId(string inspectionId);

    public Task<PagedList<InspectionRecord>> GetInspectionRecords(
        InspectionRecordParameters parameters
    );

    public Task<PagedList<InspectionRecord>> GetThermalInspectionRecords(
        int pageNumber,
        int pageSize
    );

    public Task Delete(Guid id);

    public Task<InspectionRecord> AddAnalysis(
        Guid inspectionRecordId,
        AnalysisTypeEnum analysisName
    );
}

public class InspectionRecordParameters
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public List<string>? InspectionIds { get; set; }
    public string? Tag { get; set; }
    public string? InstallationCode { get; set; }
    public DateTime? MinCreationTime { get; set; }
    public DateTime? MaxCreationTime { get; set; }
    public List<AnalysisTypeEnum>? AnalysisTypes { get; set; } = [];
}

public class CreateInspectionRecordRequest
{
    public required string InspectionId { get; set; }
    public required string InstallationCode { get; set; }
    public required BlobStorageLocation BlobStorageLocation { get; set; }
    public string? InspectionType { get; set; }
    public string? Tag { get; set; }
    public string? InspectionDescription { get; set; }
    public string? MissionName { get; set; }
    public string? RobotName { get; set; }
    public DateTime? Timestamp { get; set; }
    public List<AnalysisTypeEnum>? RequiredAnalysis { get; set; }
    public CreateInspectionRecordAnalysisGroup? AnalysisGroup { get; set; }
}

public class CreateInspectionRecordAnalysisGroup
{
    public required string AnalysisGroupId { get; set; }
    public required int AnalysisGroupSize { get; set; }
    public required List<string> AnalysisGroupAnalyses { get; set; }
}

public class InspectionRecordService(
    SaraDbContext context,
    IAnalysisTriggerService analysisTriggerService,
    IOptions<AnalysisOptions> analysisOptions,
    ILogger<InspectionRecordService> logger
) : IInspectionRecordService
{
    private readonly AnalysisOptions _analysisOptions = analysisOptions.Value;

    public async Task<InspectionRecord?> CreateFromMqttMessage(IsarInspectionResultMessage message)
    {
        var inspectionId = Sanitize.SanitizeUserInput(message.InspectionId);

        if (await ExistsByInspectionId(inspectionId))
        {
            return null;
        }

        var analysisGroup =
            message.AnalysisGroup != null
                ? await GetOrCreateAnalysisGroup(
                    message.AnalysisGroup.AnalysisGroupId,
                    message.AnalysisGroup.AnalysisGroupSize
                )
                : null;

        List<Analysis> analyses = [];

        if (message.RequiredAnalysis != null)
        {
            var analysesInGroup =
                analysisGroup?.Analyses.Where(
                    (a) => message.RequiredAnalysis.Contains(a.AnalysisType)
                )
                ?? [];

            var newAnalyses = message
                .RequiredAnalysis.Where(
                    (r) => !analysesInGroup.Select((a) => a.AnalysisType).Contains(r)
                )
                .Select((r) => new Analysis { AnalysisType = r, AnalysisGroup = analysisGroup })
                .ToList();

            if (analysisGroup != null)
                foreach (var analysis in newAnalyses)
                    analysisGroup.Analyses.Add(analysis);

            await context.SaveChangesAsync();

            analyses.AddRange(analysesInGroup);
            analyses.AddRange(newAnalyses);
        }

        var inspectionRecord = new InspectionRecord
        {
            InspectionId = inspectionId,
            FlotillaMissionId = message.MissionId is null
                ? null
                : Sanitize.SanitizeUserInput(message.MissionId),
            InstallationCode = Sanitize.SanitizeUserInput(message.InstallationCode),
            BlobStorageLocation = new BlobStorageLocation
            {
                StorageAccount = message.InspectionDataPath.StorageAccount,
                BlobContainer = message.InspectionDataPath.BlobContainer,
                BlobName = message.InspectionDataPath.BlobName,
            },
            InspectionType = message.InspectionType,
            Tag = Sanitize.SanitizeUserInput(message.TagId),
            InspectionDescription = Sanitize.SanitizeUserInput(message.InspectionDescription),
            MissionName = Sanitize.SanitizeUserInput(message.MissionName),
            RobotName = message.RobotName,
            Timestamp = message.Timestamp,
            RobotPose = message.RobotPose,
            TargetPosition = message.TargetPosition,
            Analyses = analyses,
            AnalysisGroup = analysisGroup,
            AnalysisGroupId = analysisGroup?.Id,
        };

        try
        {
            return await Create(inspectionRecord);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException
                    is PostgresException
                    {
                        SqlState: PostgresErrorCodes.UniqueViolation,
                        ConstraintName: "IX_InspectionRecords_InspectionId",
                    }
            )
        {
            // Another delivery may have inserted the record after the existence check.
            return null;
        }
    }

    private async Task<AnalysisGroup> GetOrCreateAnalysisGroup(
        string analysisGroupId,
        int analysisGroupSize
    )
    {
        var existing = await context
            .AnalysisGroups.Include((g) => g.Analyses)
            .FirstOrDefaultAsync(g => g.GroupId == analysisGroupId);

        if (existing is not null)
        {
            return existing;
        }

        var timeoutMinutes = analysisOptions.Value.AnalysisGroupTimeoutMinutes;
        var group = new AnalysisGroup
        {
            GroupId = analysisGroupId,
            ExpectedSize = analysisGroupSize,
            TimeoutAt = DateTime.UtcNow.AddMinutes(timeoutMinutes),
        };

        await context.AnalysisGroups.AddAsync(group);
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Created analysis group {GroupId} expecting {ExpectedSize} records, timeout at {TimeoutAt}",
            Sanitize.SanitizeUserInput(group.Id.ToString()),
            group.ExpectedSize,
            group.TimeoutAt
        );

        return group;
    }

    public async Task<InspectionRecord> Create(InspectionRecord inspectionRecord)
    {
        await context.InspectionRecords.AddAsync(inspectionRecord);
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Created inspection record with InspectionId: {InspectionId}",
            inspectionRecord.InspectionId
        );

        foreach (var analysis in inspectionRecord.Analyses)
        {
            if (analysis.AnalysisGroup != null)
                context.Entry(analysis.AnalysisGroup).State = EntityState.Detached;
            context.Entry(analysis).State = EntityState.Detached;
        }
        context.Entry(inspectionRecord).State = EntityState.Detached;

        return inspectionRecord;
    }

    public async Task<InspectionRecord?> ReadById(Guid id)
    {
        return await context
            .InspectionRecords.Include(ir => ir.Analyses)
                .ThenInclude(a => a.Runs)
                    .ThenInclude(r => r.Workflows)
            .FirstOrDefaultAsync(ir => ir.Id == id);
    }

    public async Task<InspectionRecord?> ReadByInspectionId(string inspectionId)
    {
        return await context
            .InspectionRecords.Include(ir => ir.Analyses)
                .ThenInclude(a => a.Runs)
                    .ThenInclude(r => r.Workflows)
            .Include(ir => ir.Analyses)
                .ThenInclude(a => a.Runs)
                    .ThenInclude(r => r.Feedback)
            .FirstOrDefaultAsync(ir => ir.InspectionId == inspectionId);
    }

    public async Task<bool> ExistsByInspectionId(string inspectionId)
    {
        return await context.InspectionRecords.AnyAsync(ir => ir.InspectionId == inspectionId);
    }

    public async Task<InspectionRecord> CreateAndTrigger(CreateInspectionRecordRequest request)
    {
        var inspectionId = Sanitize.SanitizeUserInput(request.InspectionId);

        if (await ExistsByInspectionId(inspectionId))
        {
            throw new InvalidOperationException(
                $"Inspection record with inspection id {inspectionId} already exists"
            );
        }

        var analysisGroup =
            request.AnalysisGroup != null
                ? await GetOrCreateAnalysisGroup(
                    request.AnalysisGroup.AnalysisGroupId,
                    request.AnalysisGroup.AnalysisGroupSize
                )
                : null;

        var analysisTypes = request
            .RequiredAnalysis?.Select((a) => Analysis.GetAnalysisTypeFromAnalysisEnum(a))
            .ToList();

        var analyses =
            analysisTypes != null
                ? analysisTypes
                    .Select(
                        (r) =>
                            analysisGroup?.Analyses.Find((a) => a.AnalysisType == r)
                            ?? new Analysis { AnalysisType = r, AnalysisGroup = analysisGroup }
                    )
                    .ToList()
                : [];

        var inspectionRecord = new InspectionRecord
        {
            InspectionId = inspectionId,
            InstallationCode = Sanitize.SanitizeUserInput(request.InstallationCode),
            BlobStorageLocation = request.BlobStorageLocation,
            InspectionType = request.InspectionType,
            Tag = request.Tag is null ? null : Sanitize.SanitizeUserInput(request.Tag),
            InspectionDescription = request.InspectionDescription is null
                ? null
                : Sanitize.SanitizeUserInput(request.InspectionDescription),
            MissionName = request.MissionName is null
                ? null
                : Sanitize.SanitizeUserInput(request.MissionName),
            RobotName = request.RobotName,
            Timestamp = request.Timestamp,
            Analyses = analyses,
            AnalysisGroup = analysisGroup,
            AnalysisGroupId = analysisGroup?.Id,
        };

        var created = await Create(inspectionRecord);

        await analysisTriggerService.OnInspectionRecordCreated(created);

        return created;
    }

    public async Task<PagedList<InspectionRecord>> GetThermalInspectionRecords(
        int pageNumber,
        int pageSize
    )
    {
        var query = context
            .InspectionRecords.Include(ir => ir.Analyses)
                .ThenInclude(a => a.Runs)
                    .ThenInclude(r => r.Workflows)
            .Where(ir =>
                ir.Analyses.Any(a =>
                    a.Runs.Any(r => r.Workflows.Any(w => w.WorkflowType == "thermal-reading"))
                )
                && ir.Analyses.Any(a =>
                    a.Runs.Any(r =>
                        r.Workflows.Any(w =>
                            w.WorkflowType == "anonymizer" && w.Status == WorkflowStatus.Succeeded
                        )
                    )
                )
            )
            .OrderByDescending(ir => ir.CreatedAt)
            .ThenByDescending(ir => ir.Id);

        return await PagedList<InspectionRecord>.ToPagedListAsync(query, pageNumber, pageSize);
    }

    public async Task Delete(Guid id)
    {
        var record = await context
            .InspectionRecords.Include(ir => ir.Analyses)
                .ThenInclude(a => a.Runs)
                    .ThenInclude(r => r.Workflows)
            .FirstOrDefaultAsync(ir => ir.Id == id);

        if (record is null)
        {
            throw new KeyNotFoundException($"Inspection record with id {id} not found");
        }

        // Detach analyses that have other inspection records; cascade-delete those
        // that belong only to this record (along with their runs and workflows).
        var orphanedAnalyses = new List<Analysis>();
        foreach (var analysis in record.Analyses.ToList())
        {
            await context.Entry(analysis).Collection(a => a.InspectionRecords).LoadAsync();
            analysis.InspectionRecords.Remove(record);
            if (analysis.InspectionRecords.Count == 0)
            {
                orphanedAnalyses.Add(analysis);
            }
        }

        foreach (var analysis in orphanedAnalyses)
        {
            foreach (var run in analysis.Runs)
            {
                context.Workflows.RemoveRange(run.Workflows);
            }
            context.AnalysisRuns.RemoveRange(analysis.Runs);
            context.Analyses.Remove(analysis);
        }

        context.InspectionRecords.Remove(record);
        await context.SaveChangesAsync();

        logger.LogInformation("Deleted inspection record {Id}", id);
    }

    public async Task<PagedList<InspectionRecord>> GetInspectionRecords(
        InspectionRecordParameters parameters
    )
    {
        var query = context
            .InspectionRecords.Include(ir => ir.Analyses)
                .ThenInclude(a => a.Runs)
                    .ThenInclude(r => r.Workflows)
            .Include(ir => ir.Analyses)
                .ThenInclude(a => a.Runs)
                    .ThenInclude(r => r.Feedback)
            .AsQueryable();

        if (parameters.InspectionIds != null && parameters.InspectionIds.Count > 0)
            query = query.Where(ir =>
                parameters
                    .InspectionIds.Select((i) => i.ToLower())
                    .Contains(ir.InspectionId.ToLower())
            );

        if (!string.IsNullOrWhiteSpace(parameters.Tag))
            query = query.Where(ir =>
                ir.Tag != null && ir.Tag.ToLower().Contains(parameters.Tag.ToLower())
            );

        if (!string.IsNullOrWhiteSpace(parameters.InstallationCode))
            query = query.Where(ir =>
                ir.InstallationCode.ToLower().Contains(parameters.InstallationCode.ToLower())
            );

        if (parameters.MinCreationTime != null)
            query = query.Where(ir => ir.CreatedAt > parameters.MinCreationTime);

        if (parameters.MaxCreationTime != null)
            query = query.Where(ir => ir.CreatedAt < parameters.MaxCreationTime);

        if (parameters.AnalysisTypes != null && parameters.AnalysisTypes.Count > 0)
        {
            List<string> analysisTypeStrings =
            [
                .. parameters.AnalysisTypes.Select(
                    (a) => Analysis.GetAnalysisTypeFromAnalysisEnum(a)
                ),
            ];
            query = query.Where(ir =>
                ir.Analyses.Any((a) => analysisTypeStrings.Contains(a.AnalysisType))
            );
        }

        query = query.OrderByDescending(ir => ir.CreatedAt).ThenByDescending(ir => ir.Id);

        return await PagedList<InspectionRecord>.ToPagedListAsync(
            query,
            parameters.PageNumber,
            parameters.PageSize
        );
    }

    public async Task<InspectionRecord> AddAnalysis(
        Guid inspectionRecordId,
        AnalysisTypeEnum analysisName
    )
    {
        var workflowType = Analysis.GetAnalysisTypeFromAnalysisEnum(analysisName);
        if (!_analysisOptions.Analyses.ContainsKey(workflowType))
        {
            throw new InvalidOperationException(
                $"Unknown analysis '{workflowType}'. Valid analyses are: "
                    + string.Join(", ", _analysisOptions.Analyses.Keys)
            );
        }

        var record =
            await ReadById(inspectionRecordId)
            ?? throw new KeyNotFoundException(
                $"Inspection record with id {inspectionRecordId} not found"
            );

        await analysisTriggerService.OnInspectionRecordCreated(record);

        return record;
    }
}
