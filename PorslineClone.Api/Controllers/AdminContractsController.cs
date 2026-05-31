using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.ContractTemplates;
using PorslineClone.Application.Contracts;
using AppApprovalStepDto = PorslineClone.Application.Contracts.ApprovalStepDto;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.ContractTemplates;
using PorslineClone.Infrastructure.Services.Contracts;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/contracts")]
[Authorize]
public class AdminContractsController(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IWebHostEnvironment env,
    ContractFileStorageService contractFiles,
    ContractWorkflowProcessor workflowProcessor,
    ContractDocumentTemplateService documentTemplates,
    IDocxToPdfConverter pdfConverter,
    IContractIndexEnqueue contractIndexer,
    IContractContentSearchService contractSearch) : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx"
    };

    private bool IsAdmin => User.IsInRole("Admin");
    private bool CanReadAllContracts => ContractVisibilityQuery.CanReadAllContracts(User);
    private bool CanReadContractsArchive =>
        User.IsInRole("Admin")
        || User.HasClaim("permission", "contracts.archive.read")
        || User.HasClaim("permission", "contracts.archive.read.all");
    private bool CanReadAllContractsArchive => ContractVisibilityQuery.CanReadAllContractsArchive(User);

    private static bool UserIsInContractWorkflow(Contract contract, Guid userId)
    {
        var steps = ContractWorkflowProcessor.DeserializeSteps(contract.StepsJson);
        return steps.Any(s => s.UserId == userId);
    }

    private bool UserCanAccessContract(Contract contract, Guid userId) =>
        CanReadAllContracts
        || contract.CreatedByUserId == userId
        || UserIsInContractWorkflow(contract, userId);

    private IQueryable<Contract> ScopeVisibleContracts(IQueryable<Contract> query, Guid userId)
    {
        _ = userId;
        return query.ApplyVisibleContracts(User);
    }

    private IQueryable<Contract> ScopeVisibleArchivedContracts(IQueryable<Contract> query, Guid userId)
    {
        _ = userId;
        return query.ApplyVisibleArchivedContracts(User);
    }

    private bool TryGetCurrentUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult? DenyIfNoContractAccess(Contract contract)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();
        return UserCanAccessContract(contract, userId) ? null : Forbid();
    }

    private IActionResult? DenyIfNoArchivedContractAccess(Contract contract)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();
        if (!CanReadContractsArchive)
            return Forbid();
        if (CanReadAllContractsArchive)
            return null;
        return UserCanAccessContract(contract, userId) ? null : Forbid();
    }

    [HttpGet("archive")]
    [Authorize(Policy = "contracts.archive.read")]
    public async Task<IActionResult> ListArchive(
        [FromQuery] string? q,
        [FromQuery] bool lite = true,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var currentUserGuid))
            return Unauthorized();

        var query = ScopeVisibleArchivedContracts(
            db.Contracts.AsNoTracking().Include(x => x.ContractType).Where(x => x.IsArchived),
            currentUserGuid);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x =>
                x.ContractNumber.Contains(term) ||
                x.Title.Contains(term) ||
                x.FirstName.Contains(term) ||
                x.LastName.Contains(term) ||
                x.NationalId.Contains(term) ||
                x.Phone.Contains(term) ||
                x.SubjectPersonName.Contains(term));
        }

        var list = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        var stepsByContract = list.ToDictionary(x => x.Id, x => DeserializeSteps(x.StepsJson));
        var workflowTemplates = await LoadWorkflowTemplatesAsync(list.Select(x => x.WorkflowTemplateId), ct);
        var userNameLookup = await BuildListUserNameLookupAsync(
            list,
            stepsByContract,
            workflowTemplates,
            ct);
        EnrichStepsWithUserLookup(stepsByContract, userNameLookup);
        var amendedVersionLookup = await ResolveCurrentVersionIsAmendedAsync(list, ct);

        var result = new List<ContractListItemDto>(list.Count);
        foreach (var x in list)
        {
            var steps = stepsByContract[x.Id];
            var creatorName = !string.IsNullOrWhiteSpace(x.CreatedByName)
                ? x.CreatedByName
                : userNameLookup.GetValueOrDefault(x.CreatedByUserId, "");

            result.Add(new ContractListItemDto(
                x.Id,
                x.ContractNumber,
                x.Title,
                x.FirstName,
                x.LastName,
                x.NationalId,
                x.Phone,
                x.ContractTypeId,
                x.ContractType.Name,
                x.SubjectPersonName,
                x.DateFromUtc,
                x.DateToUtc,
                x.FileName,
                x.FilePath,
                !string.IsNullOrWhiteSpace(x.FilePath),
                HasSignedDocument(x),
                HasOriginalDocument(x),
                !string.IsNullOrWhiteSpace(x.PdfFilePath),
                x.CurrentVersionNumber,
                x.IsArchived,
                x.CreatedByUserId,
                creatorName,
                x.CreatedAtUtc,
                x.CurrentStepOrder,
                ToClientStatus(x),
                x.WorkflowTemplateId,
                x.WorkflowName,
                x.WorkflowStartedAtUtc,
                x.WorkflowScheduledStartAtUtc,
                CanStartWorkflow(x),
                CanAssignWorkflow(x),
                CanUnassignWorkflow(x),
                x.ContractDocumentTemplateId,
                x.ContractDocumentTemplateVersionId,
                steps,
                BuildAmendmentView(x, currentUserGuid),
                lite ? [] : WorkflowEventHelper.ToViews(x.WorkflowEventsJson),
                amendedVersionLookup.GetValueOrDefault(x.Id),
                BuildActionPhaseView(x, workflowTemplates, userNameLookup),
                BuildCanAmendContract(x, currentUserGuid),
                BuildCanAmendContractDocument(x, currentUserGuid),
                x.WorkflowValidityEndsAtUtc,
                x.SuspendedPendingUserId,
                x.SuspendedPendingUserId.HasValue
                    ? userNameLookup.GetValueOrDefault(x.SuspendedPendingUserId.Value)
                    : null,
                x.WorkflowIncompleteNote,
                x.WorkflowIncompleteTerminatedAtUtc));
        }

        return Ok(result);
    }

    [HttpGet("party-lookup")]
    [Authorize(Policy = "contracts.read")]
    public async Task<IActionResult> PartyLookup([FromQuery] string q, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var currentUserGuid))
            return Unauthorized();

        var digits = NormalizeDigits(q ?? "");
        if (digits.Length < 3)
            return Ok(Array.Empty<ContractPartyLookupItemDto>());

        var rows = await ScopeVisibleContracts(db.Contracts.AsNoTracking(), currentUserGuid)
            .Where(c => c.NationalId.Contains(digits))
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new
            {
                c.NationalId,
                c.FirstName,
                c.LastName,
                c.Phone,
                c.SubjectPersonName,
                c.Title,
                c.ContractTypeId,
                c.CreatedAtUtc,
            })
            .Take(50)
            .ToListAsync(ct);

        var items = rows
            .GroupBy(r => r.NationalId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.CreatedAtUtc).First();
                return new ContractPartyLookupItemDto(
                    latest.NationalId,
                    latest.FirstName,
                    latest.LastName,
                    latest.Phone,
                    latest.SubjectPersonName,
                    latest.Title,
                    latest.ContractTypeId,
                    g.Count());
            })
            .OrderByDescending(x => x.ContractCount)
            .ThenBy(x => x.NationalId)
            .Take(12)
            .ToList();

        return Ok(items);
    }

    [HttpGet("party/{nationalId}")]
    [Authorize(Policy = "contracts.read")]
    public async Task<IActionResult> PartyByNationalId(string nationalId, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var currentUserGuid))
            return Unauthorized();

        var digits = NormalizeDigits(nationalId ?? "");
        if (string.IsNullOrWhiteSpace(digits))
            return BadRequest(new { message = "کد ملی الزامی است" });

        var contract = await ScopeVisibleContracts(db.Contracts.AsNoTracking(), currentUserGuid)
            .Where(c => c.NationalId == digits)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (contract is null)
            return NotFound(new { message = "قراردادی با این کد ملی یافت نشد" });

        Dictionary<string, string>? templateValues = null;
        if (!string.IsNullOrWhiteSpace(contract.TemplateFieldValuesJson))
            templateValues = ContractTemplateFieldValuesParser.Parse(contract.TemplateFieldValuesJson);

        return Ok(new ContractPartyDetailDto(
            contract.NationalId,
            contract.FirstName,
            contract.LastName,
            contract.Phone,
            contract.SubjectPersonName,
            contract.Title,
            contract.ContractTypeId,
            templateValues));
    }

    [HttpGet("types")]
    [Authorize(Policy = "contracts.read")]
    public async Task<IActionResult> ActiveTypes(CancellationToken ct)
    {
        var items = await db.ContractTypes
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet]
    [Authorize(Policy = "contracts.read")]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? q,
        [FromQuery] bool? archived,
        [FromQuery] bool syncScheduled = false,
        [FromQuery] bool lite = true,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var currentUserGuid))
            return Unauthorized();

        if (syncScheduled)
            await ProcessDueScheduledWorkflowStartsAsync(ct);

        var statusKey = (status ?? "all").ToLowerInvariant();

        var query = ScopeVisibleContracts(
            db.Contracts.AsNoTracking().Include(x => x.ContractType),
            currentUserGuid);

        if (archived == true)
            query = query.Where(x => x.IsArchived);
        else if (archived != true)
            query = query.Where(x => !x.IsArchived);

        query = ApplyContractListStatusPreFilter(query, statusKey);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x =>
                x.ContractNumber.Contains(term) ||
                x.Title.Contains(term) ||
                x.FirstName.Contains(term) ||
                x.LastName.Contains(term) ||
                x.NationalId.Contains(term) ||
                x.Phone.Contains(term) ||
                x.SubjectPersonName.Contains(term));
        }

        var list = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(120)
            .ToListAsync(ct);

        var stepsByContract = list.ToDictionary(x => x.Id, x => DeserializeSteps(x.StepsJson));

        var workflowTemplates = await LoadWorkflowTemplatesAsync(list.Select(x => x.WorkflowTemplateId), ct);

        var userNameLookup = await BuildListUserNameLookupAsync(
            list,
            stepsByContract,
            workflowTemplates,
            ct);

        EnrichStepsWithUserLookup(stepsByContract, userNameLookup);

        var amendedVersionLookup = await ResolveCurrentVersionIsAmendedAsync(list, ct);

        var result = new List<ContractListItemDto>(list.Count);
        foreach (var x in list)
        {
            var steps = stepsByContract[x.Id];
            var creatorName = !string.IsNullOrWhiteSpace(x.CreatedByName)
                ? x.CreatedByName
                : userNameLookup.GetValueOrDefault(x.CreatedByUserId, "");

            result.Add(new ContractListItemDto(
                x.Id,
                x.ContractNumber,
                x.Title,
                x.FirstName,
                x.LastName,
                x.NationalId,
                x.Phone,
                x.ContractTypeId,
                x.ContractType.Name,
                x.SubjectPersonName,
                x.DateFromUtc,
                x.DateToUtc,
                x.FileName,
                x.FilePath,
                !string.IsNullOrWhiteSpace(x.FilePath),
                HasSignedDocument(x),
                HasOriginalDocument(x),
                !string.IsNullOrWhiteSpace(x.PdfFilePath),
                x.CurrentVersionNumber,
                x.IsArchived,
                x.CreatedByUserId,
                creatorName,
                x.CreatedAtUtc,
                x.CurrentStepOrder,
                ToClientStatus(x),
                x.WorkflowTemplateId,
                x.WorkflowName,
                x.WorkflowStartedAtUtc,
                x.WorkflowScheduledStartAtUtc,
                CanStartWorkflow(x),
                CanAssignWorkflow(x),
                CanUnassignWorkflow(x),
                x.ContractDocumentTemplateId,
                x.ContractDocumentTemplateVersionId,
                steps,
                BuildAmendmentView(x, currentUserGuid),
                lite ? [] : WorkflowEventHelper.ToViews(x.WorkflowEventsJson),
                amendedVersionLookup.GetValueOrDefault(x.Id),
                BuildActionPhaseView(x, workflowTemplates, userNameLookup),
                BuildCanAmendContract(x, currentUserGuid),
                BuildCanAmendContractDocument(x, currentUserGuid),
                x.WorkflowValidityEndsAtUtc,
                x.SuspendedPendingUserId,
                x.SuspendedPendingUserId.HasValue
                    ? userNameLookup.GetValueOrDefault(x.SuspendedPendingUserId.Value)
                    : null,
                x.WorkflowIncompleteNote,
                x.WorkflowIncompleteTerminatedAtUtc));
        }

        var filtered = ApplyContractListStatusPostFilter(result, statusKey, currentUserGuid);
        return Ok(filtered);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "contracts.delete")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var currentUserGuid))
            return Unauthorized();

        var contract = await db.Contracts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
            return NotFound(new { message = "قرارداد یافت نشد" });

        if (contract.IsSoftDeleted)
            return BadRequest(new { message = "این قرارداد قبلاً حذف شده است" });

        var accessDenied = DenyIfNoContractAccess(contract);
        if (accessDenied is not null)
            return accessDenied;

        var usage = await ContractSoftDelete.AnalyzeAsync(db, contract, ct);
        await ContractSoftDelete.ApplyAsync(db, contract, currentUserGuid, ct);
        await db.SaveChangesAsync(ct);

        var message = ContractSoftDelete.BuildMessage(contract.ContractNumber, usage);
        return Ok(new
        {
            message,
            usage = new
            {
                usage.WorkflowStarted,
                usage.WorkflowInProgress,
                usage.HasPostApproval,
                usage.ActiveApprovalLinks,
                usage.ActiveActionLinks,
            },
        });
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var x = await db.Contracts.Include(c => c.ContractType).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (x is null) return NotFound(new { message = "قرارداد یافت نشد" });

        if (x.IsArchived)
        {
            if (DenyIfNoArchivedContractAccess(x) is { } archivedDenied)
                return archivedDenied;
        }
        else
        {
            if (!IsAdmin && !User.HasClaim("permission", "contracts.read"))
                return Forbid();
            if (DenyIfNoContractAccess(x) is { } accessDenied)
                return accessDenied;
        }

        await ProcessDueScheduledWorkflowStartsAsync(ct);
        x = await db.Contracts.Include(c => c.ContractType).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (x is null) return NotFound(new { message = "قرارداد یافت نشد" });

        var steps = DeserializeSteps(x.StepsJson);
        var map = new Dictionary<Guid, List<AppApprovalStepDto>> { [x.Id] = steps };
        await EnrichApproverNamesAsync(map, ct);
        await EnrichApprovalLinkOpenedAsync(steps, id, ct);

        var creatorName = !string.IsNullOrWhiteSpace(x.CreatedByName)
            ? x.CreatedByName
            : await ResolveUserDisplayNameAsync(x.CreatedByUserId, ct);

        var currentIsAmended = await db.ContractVersions.AsNoTracking()
            .AnyAsync(v => v.ContractId == id && v.VersionNumber == x.CurrentVersionNumber && v.IsAmendedVersion, ct);

        var workflowTemplates = await LoadWorkflowTemplatesAsync([x.WorkflowTemplateId], ct);
        var actionPhaseNameLookup = await ResolveActionPhaseUserNamesAsync([x], workflowTemplates, ct);
        TryGetCurrentUserId(out var currentUserGuid);
        var amendmentView = BuildAmendmentView(x, currentUserGuid);
        var templateFieldValues = ParseTemplateFieldValues(x.TemplateFieldValuesJson);

        return Ok(new ContractDetailDto(
            x.Id,
            x.ContractNumber,
            x.Title,
            x.FirstName,
            x.LastName,
            x.NationalId,
            x.Phone,
            x.ContractTypeId,
            x.ContractType.Name,
            x.SubjectPersonName,
            x.DateFromUtc,
            x.DateToUtc,
            x.FileName,
            x.FilePath,
            !string.IsNullOrWhiteSpace(x.FilePath),
            HasSignedDocument(x),
            HasOriginalDocument(x),
            !string.IsNullOrWhiteSpace(x.PdfFilePath),
            x.CurrentVersionNumber,
            x.IsArchived,
            x.CreatedByUserId,
            creatorName,
            x.CreatedAtUtc,
            x.CurrentStepOrder,
            ToClientStatus(x),
            x.WorkflowTemplateId,
            x.WorkflowName,
            x.WorkflowStartedAtUtc,
            x.WorkflowScheduledStartAtUtc,
            CanStartWorkflow(x),
            CanAssignWorkflow(x),
            CanUnassignWorkflow(x),
            x.ContractDocumentTemplateId,
            x.ContractDocumentTemplateVersionId,
            steps,
            amendmentView,
            WorkflowEventHelper.ToViews(x.WorkflowEventsJson),
            currentIsAmended,
            BuildActionPhaseView(x, workflowTemplates, actionPhaseNameLookup),
            BuildCanAmendContract(x, currentUserGuid),
            BuildCanAmendContractDocument(x, currentUserGuid),
            templateFieldValues,
            x.WorkflowValidityEndsAtUtc,
            x.SuspendedPendingUserId,
            x.SuspendedPendingUserId.HasValue
                ? await ResolveUserDisplayNameAsync(x.SuspendedPendingUserId.Value, ct)
                : null,
            x.WorkflowIncompleteNote,
            x.WorkflowIncompleteTerminatedAtUtc,
            BuildCanTerminateIncomplete(x, currentUserGuid)));
    }

    [HttpPost("{id:guid}/terminate-incomplete")]
    [Authorize(Policy = "contracts.update")]
    public async Task<IActionResult> TerminateIncomplete(
        Guid id,
        [FromBody] TerminateWorkflowIncompleteRequest? req,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized();

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });

        var accessDenied = DenyIfNoContractAccess(contract);
        if (accessDenied is not null) return accessDenied;

        if (contract.CreatedByUserId != currentUserId)
            return StatusCode(403, new { message = "فقط ایجادکننده قرارداد می‌تواند اتمام ناتمام ثبت کند" });

        if (contract.Status is not (ContractStatus.InProgress or ContractStatus.Suspended))
            return BadRequest(new { message = "فقط گردش در جریان یا معلق قابل اتمام ناتمام است" });

        if (!contract.WorkflowStartedAtUtc.HasValue)
            return BadRequest(new { message = "گردش هنوز شروع نشده است" });

        var now = DateTime.UtcNow;
        var note = req?.Note?.Trim();
        contract.Status = ContractStatus.Incomplete;
        contract.WorkflowIncompleteTerminatedAtUtc = now;
        contract.WorkflowIncompleteNote = string.IsNullOrWhiteSpace(note) ? null : note;
        contract.SuspendedPendingUserId = null;

        var steps = DeserializeSteps(contract.StepsJson);
        var pending = steps.FirstOrDefault(s => s.Order == contract.CurrentStepOrder && s.Status == "pending");
        var actorName = await ResolveUserDisplayNameAsync(currentUserId, ct);
        WorkflowEventHelper.Append(contract, new WorkflowEventDto
        {
            Kind = "workflow_incomplete",
            StepOrder = pending?.Order ?? contract.CurrentStepOrder,
            ActorUserId = currentUserId,
            ActorName = actorName,
            Comment = note ?? "اتمام گردش ناتمام توسط ایجادکننده",
            AtUtc = now
        });

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "اتمام گردش ناتمام ثبت شد" });
    }

    [HttpGet("{id:guid}/versions")]
    [Authorize(Policy = "contracts.read")]
    public async Task<IActionResult> ListVersions(Guid id, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });

        var accessDenied = DenyIfNoContractAccess(contract);
        if (accessDenied is not null) return accessDenied;

        var versions = await db.ContractVersions
            .Where(x => x.ContractId == id)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync(ct);

        var userIds = versions.Select(x => x.CreatedByUserId).Distinct().ToList();
        var names = await userManager.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
            .ToDictionaryAsync(
                u => u.Id,
                u =>
                {
                    var full = $"{u.FirstName} {u.LastName}".Trim();
                    return string.IsNullOrWhiteSpace(full) ? (u.UserName ?? "") : full;
                },
                ct);

        var items = versions.Select(v => new ContractVersionDto(
            v.Id,
            v.VersionNumber,
            v.FileName,
            v.CreatedAtUtc,
            !string.IsNullOrWhiteSpace(v.CreatedByName)
                ? v.CreatedByName
                : names.GetValueOrDefault(v.CreatedByUserId, ""),
            v.ChangeNote,
            v.IsAmendedVersion));
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = "contracts.add")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> Create([FromForm] CreateContractFormRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var creationMode = (req.CreationMode ?? "upload").Trim().ToLowerInvariant();
        if (creationMode is not ("upload" or "template"))
            return BadRequest(new { message = "نوع ایجاد نامعتبر است (upload یا template)" });

        var title = (req.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "عنوان قرارداد الزامی است" });

        if (!Guid.TryParse(req.ContractTypeId, out var typeId))
            return BadRequest(new { message = "نوع قرارداد نامعتبر است" });
        var contractType = await db.ContractTypes.FirstOrDefaultAsync(x => x.Id == typeId && x.IsActive, ct);
        if (contractType is null)
            return BadRequest(new { message = "نوع قرارداد یافت نشد" });

        if (!DateTime.TryParse(req.DateFromUtc, out var dateFrom) || !DateTime.TryParse(req.DateToUtc, out var dateTo))
            return BadRequest(new { message = "بازه تاریخ نامعتبر است" });
        if (dateTo < dateFrom)
            return BadRequest(new { message = "تاریخ پایان باید بعد از تاریخ شروع باشد" });

        var nationalId = NormalizeDigits(req.NationalId ?? "");
        if (string.IsNullOrWhiteSpace(nationalId))
            return BadRequest(new { message = "کد ملی الزامی است" });

        var phone = NormalizeDigits(req.Phone ?? "");
        if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^09\d{9}$"))
            return BadRequest(new { message = "شماره تماس معتبر نیست" });

        IFormFile? file = req.File;
        Guid? documentTemplateId = null;
        Guid? documentTemplateVersionId = null;
        string? templateFieldValuesJson = null;
        string? allocatedContractNumber = null;

        if (creationMode == "template")
        {
            if (!Guid.TryParse(req.ContractDocumentTemplateId, out var templateId))
                return BadRequest(new { message = "قالب قرارداد انتخاب نشده است" });

            Guid? documentTemplateVersionIdParsed = null;
            if (!string.IsNullOrWhiteSpace(req.ContractDocumentTemplateVersionId))
            {
                if (!Guid.TryParse(req.ContractDocumentTemplateVersionId, out var parsedVersionId))
                    return BadRequest(new { message = "نسخه قالب نامعتبر است" });
                documentTemplateVersionIdParsed = parsedVersionId;
            }

            Dictionary<string, string> fieldValues;
            try
            {
                fieldValues = ContractTemplateFieldValuesParser.Parse(req.FieldValuesJson);
            }
            catch (JsonException)
            {
                return BadRequest(new { message = "مقادیر فیلد قالب نامعتبر است" });
            }

            try
            {
                allocatedContractNumber = await ContractDocumentNumberService.AllocateNextAsync(db, ct);
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new
                {
                    message = "خطا در تولید شماره قرارداد. مایگریشن دیتابیس اعمال نشده است؛ API را ری‌استارت کنید.",
                    detail = ex.InnerException?.Message
                });
            }

            try
            {
                var (tempPath, generatedName, versionId) =
                    await documentTemplates.GenerateForContractAsync(
                        templateId,
                        documentTemplateVersionIdParsed,
                        fieldValues,
                        allocatedContractNumber,
                        allowInactiveOrDeleted: false,
                        ct);
                try
                {
                    await using var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var mem = new MemoryStream();
                    await fs.CopyToAsync(mem, ct);
                    mem.Position = 0;
                    var outName = string.IsNullOrWhiteSpace(generatedName)
                        ? "contract.docx"
                        : Path.ChangeExtension(generatedName, ".docx");
                    file = new StreamFormFile(mem, outName, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
                    documentTemplateId = templateId;
                    documentTemplateVersionId = versionId;
                    templateFieldValuesJson = req.FieldValuesJson;
                }
                finally
                {
                    if (System.IO.File.Exists(tempPath))
                        System.IO.File.Delete(tempPath);
                }
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        else
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { message = "فایل قرارداد (PDF یا Word) الزامی است" });
        }

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل قرارداد الزامی است" });

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { message = "فقط فایل PDF یا Word مجاز است" });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم فایل ۲۰ مگابایت است" });

        ContractWorkflowTemplate? workflowTemplate = null;
        if (!string.IsNullOrWhiteSpace(req.WorkflowTemplateId) &&
            Guid.TryParse(req.WorkflowTemplateId, out var workflowTemplateId))
        {
            workflowTemplate = await db.ContractWorkflowTemplates
                .FirstOrDefaultAsync(x => x.Id == workflowTemplateId && x.IsActive, ct);
            if (workflowTemplate is null)
                return BadRequest(new { message = "گردش انتخاب‌شده یافت نشد یا غیرفعال است" });

            if (creationMode == "template" && documentTemplateId is not null)
            {
                var signatureError = await ContractWorkflowSignatureValidator.ValidateAsync(
                    db,
                    documentTemplateId,
                    documentTemplateVersionId,
                    workflowTemplate.StepsJson,
                    ct);
                if (signatureError is not null)
                    return BadRequest(new { message = signatureError });
            }
        }

        var contractId = Guid.NewGuid();
        string contractNumber;
        if (creationMode == "template" && allocatedContractNumber is not null)
        {
            contractNumber = allocatedContractNumber;
        }
        else
        {
            try
            {
                contractNumber = await ContractDocumentNumberService.AllocateNextAsync(db, ct);
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new
                {
                    message = "خطا در تولید شماره سند. مایگریشن دیتابیس اعمال نشده است؛ API را ری‌استارت کنید.",
                    detail = ex.InnerException?.Message
                });
            }
        }

        const int versionNumber = 1;
        var creatorName = await ResolveUserDisplayNameAsync(userId, ct);
        var stored = await contractFiles.SaveAsync(nationalId, versionNumber, contractNumber, file, ct);

        // تبدیل PDF هنگام ثبت غیرفعال — فقط DOCX ذخیره می‌شود؛ پیش‌نمایش PDF با ?format=pdf
        string? pdfRelativePath = null;

        var hasWorkflow = workflowTemplate is not null;
        var steps = hasWorkflow
            ? BuildApprovalStepsFromTemplate(workflowTemplate!.StepsJson, startImmediately: false)
            : [];

        var status = hasWorkflow ? ContractStatus.Pending : ContractStatus.Approved;

        var contract = new Contract
        {
            Id = contractId,
            ContractNumber = contractNumber,
            Title = title,
            FirstName = (req.FirstName ?? "").Trim(),
            LastName = (req.LastName ?? "").Trim(),
            NationalId = nationalId,
            Phone = phone,
            ContractTypeId = typeId,
            SubjectPersonName = (req.SubjectPersonName ?? "").Trim(),
            DateFromUtc = DateTime.SpecifyKind(dateFrom, DateTimeKind.Utc),
            DateToUtc = DateTime.SpecifyKind(dateTo, DateTimeKind.Utc),
            FilePath = stored.relativePath,
            OriginalFilePath = stored.relativePath,
            PdfFilePath = pdfRelativePath,
            FileName = stored.originalFileName,
            CurrentVersionNumber = versionNumber,
            WorkflowTemplateId = workflowTemplate?.Id,
            WorkflowName = workflowTemplate?.Name,
            ContractDocumentTemplateId = documentTemplateId,
            ContractDocumentTemplateVersionId = documentTemplateVersionId,
            TemplateFieldValuesJson = templateFieldValuesJson,
            CreatedByUserId = userId,
            CreatedByName = creatorName,
            CreatedAtUtc = DateTime.UtcNow,
            CurrentStepOrder = hasWorkflow ? 1 : 0,
            Status = status,
            StepsJson = steps.Count > 0 ? JsonSerializer.Serialize(steps) : null
        };

        ContractTextIndexHelper.EnsurePendingIndex(db, contract);
        db.Contracts.Add(contract);
        db.ContractVersions.Add(new ContractVersion
        {
            Id = Guid.NewGuid(),
            ContractId = contractId,
            VersionNumber = versionNumber,
            FilePath = stored.relativePath,
            PdfFilePath = pdfRelativePath,
            FileName = stored.originalFileName,
            CreatedByUserId = userId,
            CreatedByName = creatorName,
            CreatedAtUtc = DateTime.UtcNow,
            ChangeNote = "نسخه اولیه"
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new
            {
                message = "خطا در ذخیره قرارداد در دیتابیس. API را ری‌استارت کنید تا مایگریشن‌ها اعمال شوند.",
                detail = ex.InnerException?.Message
            });
        }

        contractIndexer.EnqueueExtractAndIndex(contract.Id);

        return Accepted(new
        {
            id = contract.Id,
            contractNumber = contract.ContractNumber,
            indexStatus = contract.IndexStatus.ToString(),
            createdByUserId = contract.CreatedByUserId,
            createdByName = contract.CreatedByName,
            workflowName = contract.WorkflowName,
            needsWorkflowStart = hasWorkflow,
            pdfGenerated = pdfRelativePath is not null,
            pdfRequested = false,
            message = hasWorkflow
                ? $"قرارداد {contract.ContractNumber} ثبت شد. برای شروع گردش «{contract.WorkflowName}» دکمه شروع را بزنید."
                : $"قرارداد با شماره سند {contract.ContractNumber} توسط {contract.CreatedByName} ثبت شد"
        });
    }

    [HttpGet("search")]
    [Authorize(Policy = "contracts.read")]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] ContractStatus? status = null,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        IReadOnlyCollection<Guid>? allowedIds = null;
        if (!CanReadAllContracts)
        {
            allowedIds = await ScopeVisibleContracts(db.Contracts.AsNoTracking(), userId)
                .Select(x => x.Id)
                .ToListAsync(ct);
        }

        ContractStatusDto? statusFilter = status.HasValue ? MapContractStatusDto(status.Value) : null;
        var (total, items) = await contractSearch.SearchAsync(
            q,
            skip,
            take,
            allowedIds,
            from,
            to,
            statusFilter,
            ct);

        return Ok(new { total, items });
    }

    private static ContractStatusDto MapContractStatusDto(ContractStatus status) => status switch
    {
        ContractStatus.InProgress => ContractStatusDto.InProgress,
        ContractStatus.Approved => ContractStatusDto.Approved,
        ContractStatus.Rejected => ContractStatusDto.Rejected,
        ContractStatus.Suspended => ContractStatusDto.Suspended,
        ContractStatus.Incomplete => ContractStatusDto.Incomplete,
        _ => ContractStatusDto.Pending,
    };

    [HttpPost("{id:guid}/assign-workflow")]
    [Authorize(Policy = "contracts.update")]
    public async Task<IActionResult> AssignWorkflow(Guid id, [FromBody] AssignWorkflowRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(req.WorkflowTemplateId, out var templateId))
            return BadRequest(new { message = "گردش انتخاب‌شده نامعتبر است" });

        var contract = await GetAccessibleContractAsync(id, ct);
        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });
        if (!CanAssignWorkflow(contract))
            return BadRequest(new { message = "در وضعیت فعلی امکان انتصاب گردش وجود ندارد" });

        var template = await db.ContractWorkflowTemplates
            .FirstOrDefaultAsync(x => x.Id == templateId && x.IsActive, ct);
        if (template is null)
            return BadRequest(new { message = "گردش یافت نشد یا غیرفعال است" });

        var signatureError = await ContractWorkflowSignatureValidator.ValidateAsync(
            db,
            contract.ContractDocumentTemplateId,
            contract.ContractDocumentTemplateVersionId,
            template.StepsJson,
            ct);
        if (signatureError is not null)
            return BadRequest(new { message = signatureError });

        var mode = (req.StartMode ?? "manual").Trim().ToLowerInvariant();
        DateTime? scheduledUtc = null;
        if (mode == "scheduled")
        {
            if (string.IsNullOrWhiteSpace(req.ScheduledStartAtUtc) ||
                !DateTime.TryParse(req.ScheduledStartAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                return BadRequest(new { message = "تاریخ شروع گردش نامعتبر است" });
            scheduledUtc = parsed.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : parsed.ToUniversalTime();
            if (scheduledUtc <= DateTime.UtcNow)
                return BadRequest(new { message = "تاریخ شروع باید در آینده باشد" });
        }

        contract.WorkflowTemplateId = template.Id;
        contract.WorkflowName = template.Name;
        contract.WorkflowStartedAtUtc = null;
        contract.WorkflowScheduledStartAtUtc = mode == "scheduled" ? scheduledUtc : null;
        contract.Status = ContractStatus.Pending;
        contract.CurrentStepOrder = 1;
        contract.StepsJson = JsonSerializer.Serialize(BuildApprovalStepsFromTemplate(template.StepsJson, startImmediately: false));

        await db.SaveChangesAsync(ct);

        if (mode == "now")
        {
            await db.Entry(contract).ReloadAsync(ct);
            var (ok, err) = await TryStartWorkflowAsync(contract, ct);
            if (!ok) return BadRequest(new { message = err ?? "شروع گردش ناموفق بود" });
            return Ok(new
            {
                message = $"گردش «{contract.WorkflowName}» انتصاب و شروع شد؛ پیامک به تأییدکننده اول ارسال شد",
                workflowStartedAtUtc = contract.WorkflowStartedAtUtc,
            });
        }

        if (mode == "scheduled")
        {
            return Ok(new
            {
                message = $"گردش «{contract.WorkflowName}» انتصاب شد و در تاریخ برنامه‌ریزی‌شده شروع می‌شود",
                workflowScheduledStartAtUtc = contract.WorkflowScheduledStartAtUtc,
            });
        }

        return Ok(new
        {
            message = $"گردش «{contract.WorkflowName}» انتصاب شد. برای شروع دکمه «شروع گردش» را بزنید",
            canStartWorkflow = true,
        });
    }

    [HttpPost("{id:guid}/unassign-workflow")]
    [Authorize(Policy = "contracts.update")]
    public async Task<IActionResult> UnassignWorkflow(Guid id, CancellationToken ct)
    {
        var contract = await GetAccessibleContractAsync(id, ct);
        if (contract is null)
            return NotFound(new { message = "قرارداد یافت نشد" });

        if (!CanUnassignWorkflow(contract))
            return BadRequest(new { message = "در وضعیت فعلی امکان حذف گردش وجود ندارد" });

        contract.WorkflowTemplateId = null;
        contract.WorkflowName = null;
        contract.WorkflowStartedAtUtc = null;
        contract.WorkflowScheduledStartAtUtc = null;
        contract.StepsJson = "[]";
        contract.Status = ContractStatus.Pending;
        contract.CurrentStepOrder = 1;

        var links = await db.ContractApprovalLinks
            .Where(x => x.ContractId == id && x.IsActive)
            .ToListAsync(ct);
        foreach (var link in links)
            link.IsActive = false;

        await db.SaveChangesAsync(ct);

        return Ok(new { message = "گردش از قرارداد حذف شد" });
    }

    [HttpPost("{id:guid}/start-workflow")]
    [Authorize(Policy = "contracts.update")]
    public async Task<IActionResult> StartWorkflow(Guid id, CancellationToken ct)
    {
        var contract = await GetAccessibleContractAsync(id, ct);
        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });
        if (contract.IsArchived)
            return BadRequest(new { message = "قرارداد بایگانی‌شده قابل شروع گردش نیست" });
        if (contract.WorkflowTemplateId is null && string.IsNullOrWhiteSpace(contract.StepsJson))
            return BadRequest(new { message = "برای این قرارداد گردش تأیید تعریف نشده است" });
        if (contract.WorkflowStartedAtUtc is not null)
            return BadRequest(new { message = "گردش این قرارداد قبلاً شروع شده است" });
        if (contract.Status != ContractStatus.Pending)
            return BadRequest(new { message = "گردش این قرارداد قبلاً شروع شده یا به پایان رسیده است" });

        var (ok, err) = await TryStartWorkflowAsync(contract, ct);
        if (!ok) return BadRequest(new { message = err ?? "شروع گردش ناموفق بود" });

        return Ok(new { message = $"گردش «{contract.WorkflowName ?? "تأیید"}» شروع شد و پیامک به تأییدکننده اول ارسال شد" });
    }

    [HttpPost("{id:guid}/resend-approval-sms")]
    [Authorize(Policy = "contracts.update")]
    public async Task<IActionResult> ResendApprovalSms(Guid id, CancellationToken ct)
    {
        if (await GetAccessibleContractAsync(id, ct) is null)
            return NotFound(new { message = "قرارداد یافت نشد" });

        var result = await workflowProcessor.ResendPendingApprovalSmsAsync(id, ct);
        if (!result.Success)
        {
            var status = result.HttpStatus ?? 400;
            if (status == 404) return NotFound(new { message = result.Message });
            return BadRequest(new { message = result.Message });
        }

        return Ok(new { message = result.Message });
    }

    [HttpPost("{id:guid}/versions")]
    [Authorize(Policy = "contracts.update")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadVersion(
        Guid id,
        [FromForm] string? changeNote,
        IFormFile? file,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var contract = await GetAccessibleContractAsync(id, ct);
        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });
        if (contract.IsArchived)
            return BadRequest(new { message = "قرارداد بایگانی‌شده قابل ویرایش نیست" });

        if (contract.WorkflowStartedAtUtc is not null && contract.Status == ContractStatus.InProgress)
            return BadRequest(new { message = "در جریان گردش تأیید امکان آپلود نسخه عادی وجود ندارد. در فاز اصلاحیه، از «آپلود نسخه اصلاح‌شده» استفاده کنید." });

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل الزامی است" });

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { message = "فقط فایل PDF یا Word مجاز است" });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم فایل ۲۰ مگابایت است" });

        var nextVersion = contract.CurrentVersionNumber + 1;
        var uploaderName = await ResolveUserDisplayNameAsync(userId, ct);
        var stored = await contractFiles.SaveAsync(contract.NationalId, nextVersion, contract.ContractNumber, file, ct);

        contract.FilePath = stored.relativePath;
        contract.OriginalFilePath = stored.relativePath;
        contract.FileName = stored.originalFileName;
        contract.CurrentVersionNumber = nextVersion;

        db.ContractVersions.Add(new ContractVersion
        {
            Id = Guid.NewGuid(),
            ContractId = id,
            VersionNumber = nextVersion,
            FilePath = stored.relativePath,
            FileName = stored.originalFileName,
            CreatedByUserId = userId,
            CreatedByName = uploaderName,
            CreatedAtUtc = DateTime.UtcNow,
            ChangeNote = string.IsNullOrWhiteSpace(changeNote) ? null : changeNote.Trim()
        });
        ContractTextIndexHelper.EnsurePendingIndex(db, contract);
        await db.SaveChangesAsync(ct);
        contractIndexer.EnqueueExtractAndIndex(contract.Id);

        return Ok(new
        {
            versionNumber = nextVersion,
            message = $"نسخه {nextVersion} ذخیره شد"
        });
    }

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = "contracts.update")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var contract = await GetAccessibleContractAsync(id, ct);
        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });
        if (!CanManuallyArchive(contract))
            return BadRequest(new { message = "تا پایان گردش تأیید، امکان بایگانی دستی وجود ندارد. پس از اتمام گردش (تأیید یا رد قطعی) می‌توانید بایگانی کنید." });
        contract.IsArchived = true;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "قرارداد به بایگانی منتقل شد" });
    }

    [HttpPost("{id:guid}/unarchive")]
    [Authorize(Policy = "contracts.update")]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken ct)
    {
        var contract = await GetAccessibleContractAsync(id, ct);
        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });
        contract.IsArchived = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "قرارداد از بایگانی خارج شد" });
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "contracts.update")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ContractActionRequest req, CancellationToken ct)
        => await ProcessActionAsync(id, approve: true, req.Comment, null, ct);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "contracts.update")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ContractActionRequest req, CancellationToken ct)
        => await ProcessActionAsync(id, approve: false, req.Comment, req.RejectionType, ct);

    [HttpPost("{id:guid}/amendment")]
    [Authorize(Policy = "contracts.read")]
    public async Task<IActionResult> UpdateAmendment(Guid id, [FromBody] ContractAmendmentUpdateRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserGuid))
            return Unauthorized();

        if (await GetAccessibleContractAsync(id, ct) is null)
            return NotFound(new { message = "قرارداد یافت نشد" });

        var result = await workflowProcessor.UpdateAmendmentAsync(
            id,
            currentUserGuid,
            req.AmendmentStatus,
            req.Note,
            ct);
        if (!result.Success)
        {
            var status = result.HttpStatus ?? 400;
            if (status == 403) return StatusCode(403, new { message = result.Message });
            if (status == 404) return NotFound(new { message = result.Message });
            return BadRequest(new { message = result.Message });
        }

        return Ok(new { message = result.Message });
    }

    [HttpPost("{id:guid}/amendment-file")]
    [Authorize(Policy = "contracts.read")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadAmendedVersion(Guid id, IFormFile? file, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var contract = await GetAccessibleContractAsync(id, ct);
        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });
        if (contract.IsArchived)
            return BadRequest(new { message = "قرارداد بایگانی‌شده قابل ویرایش نیست" });

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل الزامی است" });

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { message = "فقط فایل PDF یا Word مجاز است" });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم فایل ۲۰ مگابایت است" });

        var amendState = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        if (!ContractAmendmentHelper.IsActive(amendState))
            return BadRequest(new { message = "فاز اصلاحیه فعالی برای این قرارداد وجود ندارد" });
        if (contract.CreatedByUserId != userId)
            return Forbid();

        var nextVersion = contract.CurrentVersionNumber + 1;
        var uploaderName = await ResolveUserDisplayNameAsync(userId, ct);
        var stored = await contractFiles.SaveAsync(contract.NationalId, nextVersion, contract.ContractNumber, file, ct);

        contract.FilePath = stored.relativePath;
        contract.FileName = stored.originalFileName;
        contract.PdfFilePath = null;
        contract.CurrentVersionNumber = nextVersion;

        db.ContractVersions.Add(new ContractVersion
        {
            Id = Guid.NewGuid(),
            ContractId = id,
            VersionNumber = nextVersion,
            FilePath = stored.relativePath,
            FileName = stored.originalFileName,
            CreatedByUserId = userId,
            CreatedByName = uploaderName,
            CreatedAtUtc = DateTime.UtcNow,
            ChangeNote = "نسخه اصلاح‌شده",
            IsAmendedVersion = true
        });
        ContractTextIndexHelper.EnsurePendingIndex(db, contract);
        await db.SaveChangesAsync(ct);
        contractIndexer.EnqueueExtractAndIndex(contract.Id);

        var result = await workflowProcessor.RegisterAmendedVersionAsync(id, userId, nextVersion, ct);
        if (!result.Success)
            return BadRequest(new { message = result.Message });

        return Ok(new
        {
            versionNumber = nextVersion,
            currentVersionIsAmended = true,
            message = result.Message
        });
    }

    [HttpPost("{id:guid}/amendment-regenerate")]
    [Authorize(Policy = "contracts.read")]
    public async Task<IActionResult> RegenerateAmendedFromTemplate(
        Guid id,
        [FromBody] ContractAmendmentRegenerateRequest req,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var contract = await GetAccessibleContractAsync(id, ct);
        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });
        if (contract.IsArchived)
            return BadRequest(new { message = "قرارداد بایگانی‌شده قابل ویرایش نیست" });

        var amendState = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        if (!ContractAmendmentHelper.IsActive(amendState))
            return BadRequest(new { message = "فاز اصلاحیه فعالی برای این قرارداد وجود ندارد" });
        if (contract.CreatedByUserId != userId)
            return Forbid();
        if (contract.ContractDocumentTemplateId is null || contract.ContractDocumentTemplateId == Guid.Empty)
            return BadRequest(new { message = "این قرارداد از قالب ساخته نشده است" });

        Dictionary<string, string> fieldValues;
        try
        {
            fieldValues = ContractTemplateFieldValuesParser.Parse(req.FieldValuesJson);
        }
        catch (JsonException)
        {
            return BadRequest(new { message = "مقادیر فیلد قالب نامعتبر است" });
        }

        try
        {
            var (tempPath, generatedName, versionId) = await documentTemplates.GenerateForContractAsync(
                contract.ContractDocumentTemplateId.Value,
                contract.ContractDocumentTemplateVersionId,
                fieldValues,
                contract.ContractNumber,
                allowInactiveOrDeleted: true,
                ct);
            try
            {
                await using var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var mem = new MemoryStream();
                await fs.CopyToAsync(mem, ct);
                mem.Position = 0;
                var outName = string.IsNullOrWhiteSpace(generatedName)
                    ? "contract.docx"
                    : Path.ChangeExtension(generatedName, ".docx");
                var file = new StreamFormFile(
                    mem,
                    outName,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

                var nextVersion = contract.CurrentVersionNumber + 1;
                var uploaderName = await ResolveUserDisplayNameAsync(userId, ct);
                var stored = await contractFiles.SaveAsync(contract.NationalId, nextVersion, contract.ContractNumber, file, ct);

                contract.TemplateFieldValuesJson = req.FieldValuesJson;
                contract.ContractDocumentTemplateVersionId = versionId;
                contract.FilePath = stored.relativePath;
                contract.FileName = stored.originalFileName;
                contract.PdfFilePath = null;
                contract.CurrentVersionNumber = nextVersion;

                db.ContractVersions.Add(new ContractVersion
                {
                    Id = Guid.NewGuid(),
                    ContractId = id,
                    VersionNumber = nextVersion,
                    FilePath = stored.relativePath,
                    FileName = stored.originalFileName,
                    CreatedByUserId = userId,
                    CreatedByName = uploaderName,
                    CreatedAtUtc = DateTime.UtcNow,
                    ChangeNote = "نسخه اصلاح‌شده از قالب",
                    IsAmendedVersion = true
                });
                await db.SaveChangesAsync(ct);

                var registerResult = await workflowProcessor.RegisterAmendedVersionAsync(id, userId, nextVersion, ct);
                if (!registerResult.Success)
                    return BadRequest(new { message = registerResult.Message });

                string message = registerResult.Message ?? "نسخه اصلاح‌شده از قالب تولید شد";
                if (req.AutoSubmitToWorkflow && amendState!.Phase == "creator_amendment")
                {
                    var submitResult = await workflowProcessor.UpdateAmendmentAsync(
                        id,
                        userId,
                        "done",
                        amendState.AmendmentNote,
                        ct);
                    if (!submitResult.Success)
                        return BadRequest(new { message = submitResult.Message });
                    message = submitResult.Message ?? message;
                }

                return Ok(new
                {
                    versionNumber = nextVersion,
                    currentVersionIsAmended = true,
                    submittedToWorkflow = req.AutoSubmitToWorkflow && amendState!.Phase == "creator_amendment",
                    message
                });
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/file")]
    [Authorize]
    public async Task<IActionResult> DownloadFile(
        Guid id,
        [FromQuery] Guid? versionId,
        [FromQuery] bool inline = false,
        [FromQuery] string? format = null,
        [FromQuery] string? source = null,
        CancellationToken ct = default)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });

        if (contract.IsArchived)
        {
            if (DenyIfNoArchivedContractAccess(contract) is { } archivedDenied)
                return archivedDenied;
        }
        else
        {
            if (!IsAdmin && !User.HasClaim("permission", "contracts.read"))
                return Forbid();
            if (DenyIfNoContractAccess(contract) is { } accessDenied)
                return accessDenied;
        }

        var wantPdf = string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase);
        var wantOriginal = string.Equals(source, "original", StringComparison.OrdinalIgnoreCase);
        var wantAmended = string.Equals(source, "amended", StringComparison.OrdinalIgnoreCase);
        var wantSigned = string.Equals(source, "signed", StringComparison.OrdinalIgnoreCase);
        var usePdfOutput = wantPdf || wantSigned;

        ContractVersion? versionEntity = null;
        string? sourceRelative;
        string? storedPdfRelative;
        string? displayName;

        if (versionId is not null)
        {
            versionEntity = await db.ContractVersions
                .FirstOrDefaultAsync(x => x.Id == versionId && x.ContractId == id, ct);
            if (versionEntity is null) return NotFound(new { message = "نسخه یافت نشد" });
            sourceRelative = versionEntity.FilePath;
            storedPdfRelative = versionEntity.PdfFilePath;
            displayName = versionEntity.FileName;
        }
        else if (wantAmended)
        {
            var amendState = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
            int? amendedVersionNumber = amendState?.AmendedVersionNumber;
            if (amendedVersionNumber is null)
            {
                amendedVersionNumber = await db.ContractVersions.AsNoTracking()
                    .Where(v => v.ContractId == id && v.IsAmendedVersion)
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => (int?)v.VersionNumber)
                    .FirstOrDefaultAsync(ct);
            }

            if (amendedVersionNumber is null or < 1)
                return NotFound(new { message = "نسخه اصلاح‌شده یافت نشد" });

            versionEntity = await db.ContractVersions
                .FirstOrDefaultAsync(x => x.ContractId == id && x.VersionNumber == amendedVersionNumber.Value, ct);
            if (versionEntity is null)
                return NotFound(new { message = "نسخه اصلاح‌شده یافت نشد" });
            sourceRelative = versionEntity.FilePath;
            storedPdfRelative = versionEntity.PdfFilePath;
            displayName = versionEntity.FileName;
        }
        else if (wantOriginal)
        {
            sourceRelative = await ContractWorkflowProcessor.ResolveOriginalFilePathAsync(contract, db, ct);
            storedPdfRelative = null;
            displayName = contract.FileName;
            if (!string.IsNullOrWhiteSpace(sourceRelative))
            {
                var baseName = Path.GetFileName(sourceRelative);
                if (!string.IsNullOrWhiteSpace(baseName))
                    displayName = baseName;
            }
        }
        else if (wantSigned)
        {
            var signed = await ContractWorkflowProcessor.ResolveSignedFileForDownloadAsync(contract, db, ct);
            if (signed is null)
                return NotFound(new { message = "نسخه امضاشده یافت نشد" });
            sourceRelative = signed.RelativePath;
            storedPdfRelative = signed.PdfRelativePath;
            displayName = signed.DisplayFileName ?? contract.FileName;
        }
        else
        {
            sourceRelative = contract.FilePath;
            storedPdfRelative = contract.PdfFilePath;
            displayName = contract.FileName;
        }

        if (string.IsNullOrWhiteSpace(sourceRelative))
            return NotFound(new { message = "فایل یافت نشد" });

        string relative;
        string? downloadName;
        if (usePdfOutput)
        {
            var pdfResolved = await ResolveOrCreatePdfAsync(
                contract, versionEntity, sourceRelative, storedPdfRelative, displayName, ct);
            if (pdfResolved.Error is not null)
                return pdfResolved.Error;
            relative = pdfResolved.RelativePath!;
            downloadName = pdfResolved.DownloadName;
        }
        else
        {
            relative = sourceRelative;
            downloadName = displayName;
        }

        var relativePath = relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.Combine(env.ContentRootPath, relativePath);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { message = "فایل در سرور موجود نیست" });

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(filePath, out var contentType))
            contentType = "application/octet-stream";

        downloadName ??= Path.GetFileName(filePath);
        if (inline)
            return PhysicalFile(filePath, contentType);

        return PhysicalFile(filePath, contentType, downloadName, enableRangeProcessing: true);
    }

    private async Task<(string? RelativePath, string? DownloadName, IActionResult? Error)> ResolveOrCreatePdfAsync(
        Contract contract,
        ContractVersion? version,
        string sourceRelative,
        string? storedPdfRelative,
        string? displayName,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(storedPdfRelative))
        {
            var storedFull = contractFiles.ResolveFullPath(storedPdfRelative);
            if (System.IO.File.Exists(storedFull))
            {
                return (
                    storedPdfRelative,
                    Path.ChangeExtension(displayName ?? "contract", ".pdf"),
                    null);
            }
        }

        var sourceFull = contractFiles.ResolveFullPath(sourceRelative);
        if (sourceFull.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return (sourceRelative, displayName, null);

        if (!sourceFull.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return (
                null,
                null,
                NotFound(new { message = "نسخه PDF برای این قرارداد موجود نیست" }));
        }

        if (!pdfConverter.IsAvailable)
        {
            return (
                null,
                null,
                NotFound(new
                {
                    message = "تبدیل Word به PDF فعال نیست. LibreOffice را روی سرور API نصب و سرویس را ری‌استارت کنید.",
                }));
        }

        var generatedPdfFull = pdfConverter.TryConvert(sourceFull);
        if (generatedPdfFull is null)
        {
            return (
                null,
                null,
                NotFound(new { message = "تبدیل فایل Word به PDF ناموفق بود." }));
        }

        var versionNumber = version?.VersionNumber ?? contract.CurrentVersionNumber;
        var pdfRelative = await contractFiles.SavePdfCompanionAsync(
            contract.NationalId,
            versionNumber,
            contract.ContractNumber,
            generatedPdfFull,
            ct);

        if (string.IsNullOrWhiteSpace(pdfRelative))
        {
            return (
                null,
                null,
                NotFound(new { message = "ذخیره نسخه PDF ناموفق بود." }));
        }

        if (version is not null)
            version.PdfFilePath = pdfRelative;
        else
            contract.PdfFilePath = pdfRelative;
        await db.SaveChangesAsync(ct);

        return (
            pdfRelative,
            Path.ChangeExtension(displayName ?? "contract", ".pdf"),
            null);
    }

    private async Task<IActionResult> ProcessActionAsync(Guid id, bool approve, string? comment, string? rejectionType, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserGuid))
            return Unauthorized();

        if (await GetAccessibleContractAsync(id, ct) is null)
            return NotFound(new { message = "قرارداد یافت نشد" });

        var result = await workflowProcessor.ProcessActionAsync(id, currentUserGuid, approve, comment, rejectionType, ct);
        if (!result.Success)
        {
            var status = result.HttpStatus ?? 400;
            if (status == 403) return Forbid();
            if (status == 404) return NotFound(new { message = result.Message });
            return BadRequest(new { message = result.Message });
        }

        return Ok(new { message = result.Message });
    }

    private static bool HasSignedDocument(Contract contract) =>
        ContractWorkflowProcessor.HasSignedWorkflowDocument(contract);

    private static bool HasOriginalDocument(Contract contract) =>
        !string.IsNullOrWhiteSpace(contract.OriginalFilePath);

    private async Task<Contract?> GetAccessibleContractAsync(Guid id, CancellationToken ct)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null) return null;
        if (!TryGetCurrentUserId(out var userId)) return null;

        if (contract.IsArchived)
        {
            if (!CanReadContractsArchive) return null;
            if (CanReadAllContractsArchive) return contract;
            return UserCanAccessContract(contract, userId) ? contract : null;
        }

        return UserCanAccessContract(contract, userId) ? contract : null;
    }

    private async Task<string> ResolveUserDisplayNameAsync(Guid userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return "";
        var full = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? (user.UserName ?? "") : full;
    }

    private async Task EnrichApproverNamesAsync(Dictionary<Guid, List<AppApprovalStepDto>> stepsByContract, CancellationToken ct)
    {
        var approverIds = stepsByContract.Values
            .SelectMany(s => s)
            .Select(s => s.UserId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var lookup = await userManager.Users
            .Where(u => approverIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName, u.Email })
            .ToDictionaryAsync(
                x => x.Id,
                x =>
                {
                    var full = $"{x.FirstName} {x.LastName}".Trim();
                    return new { DisplayName = string.IsNullOrWhiteSpace(full) ? x.UserName : full, x.Email };
                },
                ct);

        foreach (var steps in stepsByContract.Values)
        {
            foreach (var step in steps)
            {
                if (!lookup.TryGetValue(step.UserId, out var p)) continue;
                if (string.IsNullOrWhiteSpace(step.UserName)) step.UserName = p.DisplayName ?? "";
                if (string.IsNullOrWhiteSpace(step.UserEmail)) step.UserEmail = p.Email;
            }
        }
    }

    private async Task EnrichApprovalLinkOpenedAsync(
        List<AppApprovalStepDto> steps,
        Guid contractId,
        CancellationToken ct)
    {
        if (steps.Count == 0)
            return;

        var userIds = steps.Select(s => s.UserId).Where(id => id != Guid.Empty).Distinct().ToList();
        if (userIds.Count == 0)
            return;

        var links = await db.ContractApprovalLinks.AsNoTracking()
            .Where(l => l.ContractId == contractId && userIds.Contains(l.AssigneeUserId))
            .Select(l => new { l.AssigneeUserId, l.IsActive, l.CreatedAtUtc, l.LinkOpenedAtUtc })
            .ToListAsync(ct);

        foreach (var step in steps)
        {
            var opened = links
                .Where(l => l.AssigneeUserId == step.UserId && l.LinkOpenedAtUtc.HasValue)
                .OrderByDescending(l => l.IsActive)
                .ThenByDescending(l => l.LinkOpenedAtUtc)
                .Select(l => l.LinkOpenedAtUtc)
                .FirstOrDefault();

            if (opened.HasValue)
                step.ApprovalLinkOpenedAtUtc = opened;
        }
    }

    private async Task<IReadOnlyDictionary<Guid, ContractWorkflowTemplate>> LoadWorkflowTemplatesAsync(
        IEnumerable<Guid?> templateIds,
        CancellationToken ct)
    {
        var ids = templateIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, ContractWorkflowTemplate>();

        return await db.ContractWorkflowTemplates.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveActionPhaseUserNamesAsync(
        IEnumerable<Contract> contracts,
        IReadOnlyDictionary<Guid, ContractWorkflowTemplate> templates,
        CancellationToken ct)
    {
        var assigneeIds = new List<Guid>();
        foreach (var contract in contracts)
        {
            var source = ResolveActionPhaseSource(contract, templates);
            if (source is null) continue;
            assigneeIds.AddRange(source.Value.AssigneeIds);
        }

        var ids = assigneeIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        return await userManager.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
            .ToDictionaryAsync(
                u => u.Id,
                u =>
                {
                    var full = $"{u.FirstName} {u.LastName}".Trim();
                    return string.IsNullOrWhiteSpace(full) ? (u.UserName ?? "") : full;
                },
                ct);
    }

    private static (string Label, List<Guid> AssigneeIds, string? Status, string? UpdatedByUserName, DateTime? UpdatedAtUtc)?
        ResolveActionPhaseSource(Contract contract, IReadOnlyDictionary<Guid, ContractWorkflowTemplate> templates)
    {
        var state = PostApprovalJsonHelper.DeserializeState(contract.PostApprovalJson);
        if (state is not null && state.AssigneeUserIds.Count > 0)
        {
            return (
                state.ActionDirectionLabel,
                state.AssigneeUserIds,
                state.Status,
                state.UpdatedByUserName,
                state.UpdatedAtUtc);
        }

        if (contract.WorkflowTemplateId is null
            || !templates.TryGetValue(contract.WorkflowTemplateId.Value, out var template))
            return null;

        var assigneeIds = PostApprovalJsonHelper.ParseUserIds(template.ActionAssigneeUserIdsJson);
        if (assigneeIds.Count == 0) return null;

        var label = !string.IsNullOrWhiteSpace(template.ActionDirectionLabel)
            ? template.ActionDirectionLabel!
            : PostApprovalDirections.LabelFor(template.ActionDirectionKey) ?? template.ActionDirectionKey ?? "جهت اقدام";

        return (label, assigneeIds, null, null, null);
    }

    private static ContractActionPhaseViewDto? BuildActionPhaseView(
        Contract contract,
        IReadOnlyDictionary<Guid, ContractWorkflowTemplate> templates,
        IReadOnlyDictionary<Guid, string> userNames)
    {
        var source = ResolveActionPhaseSource(contract, templates);
        if (source is null) return null;

        var (label, assigneeIds, status, updatedByUserName, updatedAtUtc) = source.Value;
        var assignees = assigneeIds
            .Select(id => new ContractActionPhaseAssigneeDto(id, userNames.GetValueOrDefault(id, "")))
            .ToList();

        return new ContractActionPhaseViewDto(
            label,
            status,
            status is null ? null : PostApprovalJsonHelper.StatusLabel(status),
            assignees,
            updatedByUserName,
            updatedAtUtc);
    }

    private static string NormalizeDigits(string value)
        => value
            .Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
            .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
            .Trim();

    private static List<AppApprovalStepDto> BuildApprovalStepsFromTemplate(string? workflowJson, bool startImmediately)
    {
        if (string.IsNullOrWhiteSpace(workflowJson)) return [];
        var workflow = JsonSerializer.Deserialize<List<PorslineClone.Application.Contracts.WorkflowStepDto>>(workflowJson) ?? [];
        return workflow
            .OrderBy(x => x.Order)
            .Select((x, i) => new AppApprovalStepDto
            {
                Id = Guid.NewGuid(),
                Order = i + 1,
                UserId = x.UserId,
                Status = startImmediately && i == 0 ? "pending" : "waiting",
                OnReject = x.OnReject is "continue" ? "continue" : "stop",
                Note = x.Note,
                ApprovalDeadlineDays = Math.Max(0, x.ApprovalDeadlineDays ?? 0),
                ApprovalDeadlineHours = Math.Max(0, x.ApprovalDeadlineHours ?? 0),
            })
            .ToList();
    }

    private static IQueryable<Contract> ApplyContractListStatusPreFilter(IQueryable<Contract> query, string statusKey) =>
        statusKey switch
        {
            "pending" => query.Where(x => x.Status == ContractStatus.Pending),
            "in_progress" => query.Where(x => x.Status == ContractStatus.InProgress),
            "rejected" => query.Where(x => x.Status == ContractStatus.Rejected),
            "suspended" => query.Where(x => x.Status == ContractStatus.Suspended),
            "incomplete" => query.Where(x => x.Status == ContractStatus.Incomplete),
            "approved" => query.Where(x => x.Status == ContractStatus.Approved),
            "action" => query.Where(x => x.Status == ContractStatus.Approved),
            "mine" => query.Where(x =>
                x.Status == ContractStatus.InProgress
                || x.Status == ContractStatus.Pending
                || x.Status == ContractStatus.Suspended),
            _ => query,
        };

    private static IEnumerable<ContractListItemDto> ApplyContractListStatusPostFilter(
        IEnumerable<ContractListItemDto> items,
        string statusKey,
        Guid currentUserGuid) =>
        statusKey switch
        {
            "mine" => items.Where(x => x.Steps.Any(s => s.UserId == currentUserGuid && s.Status == "pending")),
            "approved" => items.Where(x => x.OverallStatus == "approved"),
            "rejected" => items.Where(x => x.OverallStatus == "rejected"),
            "in_progress" => items.Where(x => x.OverallStatus == "in_progress"),
            "suspended" => items.Where(x => x.OverallStatus == "suspended"),
            "incomplete" => items.Where(x => x.OverallStatus == "incomplete"),
            "pending" => items.Where(x => x.OverallStatus == "pending"),
            "action" => items.Where(x =>
                x.OverallStatus == "approved"
                && x.ActionPhase is not null
                && !string.Equals(x.ActionPhase.Status, "completed", StringComparison.OrdinalIgnoreCase)),
            _ => items,
        };

    private async Task<Dictionary<Guid, string>> BuildListUserNameLookupAsync(
        IReadOnlyList<Contract> contracts,
        Dictionary<Guid, List<AppApprovalStepDto>> stepsByContract,
        IReadOnlyDictionary<Guid, ContractWorkflowTemplate> workflowTemplates,
        CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        foreach (var c in contracts)
        {
            ids.Add(c.CreatedByUserId);
            if (c.SuspendedPendingUserId.HasValue)
                ids.Add(c.SuspendedPendingUserId.Value);
            if (stepsByContract.TryGetValue(c.Id, out var steps))
            {
                foreach (var s in steps)
                {
                    if (s.UserId != Guid.Empty)
                        ids.Add(s.UserId);
                }
            }

            var source = ResolveActionPhaseSource(c, workflowTemplates);
            if (source is not null)
            {
                foreach (var uid in source.Value.AssigneeIds)
                {
                    if (uid != Guid.Empty)
                        ids.Add(uid);
                }
            }
        }

        return await ResolveUserNamesByIdsAsync(ids, ct);
    }

    private static void EnrichStepsWithUserLookup(
        Dictionary<Guid, List<AppApprovalStepDto>> stepsByContract,
        IReadOnlyDictionary<Guid, string> userNames)
    {
        foreach (var steps in stepsByContract.Values)
        {
            foreach (var step in steps)
            {
                if (step.UserId == Guid.Empty) continue;
                if (string.IsNullOrWhiteSpace(step.UserName)
                    && userNames.TryGetValue(step.UserId, out var name))
                    step.UserName = name;
            }
        }
    }

    private async Task ProcessDueScheduledWorkflowStartsAsync(CancellationToken ct)
    {
        var due = await db.Contracts
            .Where(x => !x.IsArchived
                && x.WorkflowScheduledStartAtUtc != null
                && x.WorkflowScheduledStartAtUtc <= DateTime.UtcNow
                && x.WorkflowStartedAtUtc == null
                && x.WorkflowTemplateId != null)
            .ToListAsync(ct);

        foreach (var contract in due)
            await TryStartWorkflowAsync(contract, ct);
    }

    private Task<(bool Ok, string? Error)> TryStartWorkflowAsync(Contract contract, CancellationToken ct)
        => workflowProcessor.TryStartWorkflowAsync(contract, ct);

    private static bool HasAssignedWorkflow(Contract contract) =>
        contract.WorkflowTemplateId is not null
        || (!string.IsNullOrWhiteSpace(contract.StepsJson) && contract.StepsJson.Trim() != "[]");

    private static bool CanAssignWorkflow(Contract contract) =>
        !contract.IsArchived
        && contract.WorkflowStartedAtUtc is null
        && contract.Status is not ContractStatus.InProgress
        && !HasAssignedWorkflow(contract);

    private static bool CanUnassignWorkflow(Contract contract) =>
        !contract.IsArchived
        && contract.WorkflowStartedAtUtc is null
        && contract.Status is not ContractStatus.InProgress
        && HasAssignedWorkflow(contract);

    private static bool CanStartWorkflow(Contract contract) =>
        contract.Status == ContractStatus.Pending
        && contract.WorkflowTemplateId is not null
        && contract.WorkflowStartedAtUtc is null
        && !string.IsNullOrWhiteSpace(contract.StepsJson)
        && (contract.WorkflowScheduledStartAtUtc is null || contract.WorkflowScheduledStartAtUtc <= DateTime.UtcNow);

    /// <summary>بایگانی دستی فقط وقتی گردش شروع نشده یا به پایان رسیده (تأیید/رد قطعی) مجاز است.</summary>
    private static bool CanManuallyArchive(Contract contract)
    {
        if (contract.IsArchived) return false;
        if (contract.WorkflowStartedAtUtc is null) return true;
        if (ContractAmendmentHelper.IsActive(ContractAmendmentHelper.Deserialize(contract.AmendmentJson)))
            return false;
        return contract.Status is ContractStatus.Approved or ContractStatus.Rejected;
    }

    private static List<PorslineClone.Application.Contracts.ApprovalStepDto> DeserializeSteps(string? json)
        => ContractWorkflowProcessor.DeserializeSteps(json);

    private static string ToClientStatus(Contract contract)
    {
        if (ContractAmendmentHelper.IsActive(ContractAmendmentHelper.Deserialize(contract.AmendmentJson)))
            return "amendment";
        return ToClientStatus(contract.Status);
    }

    private static string ToClientStatus(ContractStatus status) => status switch
    {
        ContractStatus.Pending => "pending",
        ContractStatus.InProgress => "in_progress",
        ContractStatus.Approved => "approved",
        ContractStatus.Rejected => "rejected",
        ContractStatus.Suspended => "suspended",
        ContractStatus.Incomplete => "incomplete",
        _ => "pending"
    };

    private static bool BuildCanTerminateIncomplete(Contract contract, Guid currentUserId) =>
        currentUserId != Guid.Empty
        && contract.CreatedByUserId == currentUserId
        && contract.WorkflowStartedAtUtc is not null
        && contract.Status is ContractStatus.InProgress or ContractStatus.Suspended;

    private async Task<Dictionary<Guid, string>> ResolveUserNamesByIdsAsync(
        IEnumerable<Guid> userIds,
        CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await userManager.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
            .ToDictionaryAsync(
                u => u.Id,
                u =>
                {
                    var full = $"{u.FirstName} {u.LastName}".Trim();
                    return string.IsNullOrWhiteSpace(full) ? (u.UserName ?? "") : full;
                },
                ct);
    }

    private static ContractAmendmentViewDto? BuildAmendmentView(Contract contract, Guid currentUserId)
        => ContractAmendmentHelper.ToView(
            ContractAmendmentHelper.Deserialize(contract.AmendmentJson),
            currentUserId,
            contract.CreatedByUserId);

    private static bool BuildCanAmendContract(Contract contract, Guid currentUserId)
    {
        var state = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        if (!ContractAmendmentHelper.IsActive(state) || currentUserId == Guid.Empty)
            return false;
        return ContractAmendmentHelper.CanUserActOnAmendment(state!, currentUserId, contract.CreatedByUserId);
    }

    private static bool BuildCanAmendContractDocument(Contract contract, Guid currentUserId)
    {
        var state = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        if (!ContractAmendmentHelper.IsActive(state) || currentUserId == Guid.Empty)
            return false;
        if (contract.ContractDocumentTemplateId is null || contract.ContractDocumentTemplateId == Guid.Empty)
            return false;
        return contract.CreatedByUserId == currentUserId;
    }

    private static Dictionary<string, string>? ParseTemplateFieldValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return ContractTemplateFieldValuesParser.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task<Dictionary<Guid, bool>> ResolveCurrentVersionIsAmendedAsync(
        IReadOnlyList<Contract> contracts,
        CancellationToken ct)
    {
        if (contracts.Count == 0) return [];
        var ids = contracts.Select(c => c.Id).ToList();
        var amended = await db.ContractVersions.AsNoTracking()
            .Where(v => ids.Contains(v.ContractId) && v.IsAmendedVersion)
            .Select(v => new { v.ContractId, v.VersionNumber })
            .ToListAsync(ct);
        return contracts.ToDictionary(
            c => c.Id,
            c => amended.Any(v => v.ContractId == c.Id && v.VersionNumber == c.CurrentVersionNumber));
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value is "1" or "true" or "True" or "yes" or "on";
    }
}

public class CreateContractFormRequest
{
    /// <summary>upload | template</summary>
    public string? CreationMode { get; set; }
    public string? ContractDocumentTemplateId { get; set; }
    public string? ContractDocumentTemplateVersionId { get; set; }
    public string? FieldValuesJson { get; set; }
    public string? Title { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? ContractTypeId { get; set; }
    public string? SubjectPersonName { get; set; }
    public string? DateFromUtc { get; set; }
    public string? DateToUtc { get; set; }
    public string? WorkflowTemplateId { get; set; }
    /// <summary>true | false | 1 | 0</summary>
    public string? ExportPdf { get; set; }
    public IFormFile? File { get; set; }
}

public record ContractActionRequest(string? Comment, string? RejectionType);

public record AssignWorkflowRequest(string WorkflowTemplateId, string? StartMode, string? ScheduledStartAtUtc);

public record ContractVersionDto(
    Guid Id,
    int VersionNumber,
    string FileName,
    DateTime CreatedAtUtc,
    string CreatedByName,
    string? ChangeNote,
    bool IsAmendedVersion);

public record ContractListItemDto(
    Guid Id,
    string ContractNumber,
    string Title,
    string FirstName,
    string LastName,
    string NationalId,
    string Phone,
    Guid ContractTypeId,
    string ContractTypeName,
    string SubjectPersonName,
    DateTime DateFromUtc,
    DateTime DateToUtc,
    string? FileName,
    string? FilePath,
    bool HasFile,
    bool HasSignedDocument,
    bool HasOriginalDocument,
    bool HasPdf,
    int CurrentVersionNumber,
    bool IsArchived,
    Guid CreatedByUserId,
    string CreatedByName,
    DateTime CreatedAtUtc,
    int CurrentStepOrder,
    string OverallStatus,
    Guid? WorkflowTemplateId,
    string? WorkflowName,
    DateTime? WorkflowStartedAtUtc,
    DateTime? WorkflowScheduledStartAtUtc,
    bool CanStartWorkflow,
    bool CanAssignWorkflow,
    bool CanUnassignWorkflow,
    Guid? ContractDocumentTemplateId,
    Guid? ContractDocumentTemplateVersionId,
    List<PorslineClone.Application.Contracts.ApprovalStepDto> Steps,
    ContractAmendmentViewDto? Amendment,
    IReadOnlyList<WorkflowEventViewDto> WorkflowEvents,
    bool CurrentVersionIsAmended,
    ContractActionPhaseViewDto? ActionPhase,
    bool CanAmendContract,
    bool CanAmendContractDocument,
    DateTime? WorkflowValidityEndsAtUtc,
    Guid? SuspendedPendingUserId,
    string? SuspendedPendingUserName,
    string? WorkflowIncompleteNote,
    DateTime? WorkflowIncompleteTerminatedAtUtc);

public record ContractDetailDto(
    Guid Id,
    string ContractNumber,
    string Title,
    string FirstName,
    string LastName,
    string NationalId,
    string Phone,
    Guid ContractTypeId,
    string ContractTypeName,
    string SubjectPersonName,
    DateTime DateFromUtc,
    DateTime DateToUtc,
    string? FileName,
    string? FilePath,
    bool HasFile,
    bool HasSignedDocument,
    bool HasOriginalDocument,
    bool HasPdf,
    int CurrentVersionNumber,
    bool IsArchived,
    Guid CreatedByUserId,
    string CreatedByName,
    DateTime CreatedAtUtc,
    int CurrentStepOrder,
    string OverallStatus,
    Guid? WorkflowTemplateId,
    string? WorkflowName,
    DateTime? WorkflowStartedAtUtc,
    DateTime? WorkflowScheduledStartAtUtc,
    bool CanStartWorkflow,
    bool CanAssignWorkflow,
    bool CanUnassignWorkflow,
    Guid? ContractDocumentTemplateId,
    Guid? ContractDocumentTemplateVersionId,
    List<PorslineClone.Application.Contracts.ApprovalStepDto> Steps,
    ContractAmendmentViewDto? Amendment,
    IReadOnlyList<WorkflowEventViewDto> WorkflowEvents,
    bool CurrentVersionIsAmended,
    ContractActionPhaseViewDto? ActionPhase,
    bool CanAmendContract,
    bool CanAmendContractDocument,
    Dictionary<string, string>? TemplateFieldValues,
    DateTime? WorkflowValidityEndsAtUtc,
    Guid? SuspendedPendingUserId,
    string? SuspendedPendingUserName,
    string? WorkflowIncompleteNote,
    DateTime? WorkflowIncompleteTerminatedAtUtc,
    bool CanTerminateIncomplete);

public record TerminateWorkflowIncompleteRequest(string? Note);
