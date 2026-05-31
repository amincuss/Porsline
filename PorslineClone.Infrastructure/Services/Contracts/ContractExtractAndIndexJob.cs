using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.Contracts;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Infrastructure.Services.Contracts;

public sealed class ContractExtractAndIndexJob(
    AppDbContext db,
    ContractFileStorageService storage,
    TextExtractorResolver extractorResolver,
    IPersianTextNormalizer normalizer,
    ILogger<ContractExtractAndIndexJob> logger) : IContractExtractAndIndexJob
{
    private const int MinPdfTextChars = 40;

    public async Task ExtractAndIndexAsync(Guid contractId, bool force = false, CancellationToken cancellationToken = default)
    {
        var contract = await db.Contracts
            .Include(x => x.TextIndex)
            .FirstOrDefaultAsync(x => x.Id == contractId && !x.IsSoftDeleted, cancellationToken);

        if (contract is null)
        {
            logger.LogWarning("Contract extract/index skipped: contract {ContractId} not found", contractId);
            return;
        }

        if (!force
            && contract.IndexStatus == ContractIndexStatus.Indexed
            && contract.TextIndex?.NormalizedText is { Length: > 0 } existing
            && contract.TextIndex.ContractVersionNumber == contract.CurrentVersionNumber)
        {
            logger.LogInformation("Contract {ContractId} already indexed for v{Version}", contractId, contract.CurrentVersionNumber);
            return;
        }

        contract.IndexStatus = ContractIndexStatus.Processing;
        ContractTextIndexHelper.EnsurePendingIndex(db, contract);
        await db.SaveChangesAsync(cancellationToken);

        var index = await db.ContractTextIndexes.FirstAsync(x => x.ContractId == contractId, cancellationToken);

        try
        {
            var filePath = contract.FilePath ?? contract.OriginalFilePath;
            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidOperationException("مسیر فایل قرارداد موجود نیست");

            var fullPath = storage.ResolveFullPath(filePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("فایل فیزیکی قرارداد یافت نشد", fullPath);

            var ext = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) && !string.IsNullOrWhiteSpace(contract.FileName))
                ext = Path.GetExtension(contract.FileName).TrimStart('.').ToLowerInvariant();

            var extractor = extractorResolver.Resolve(ext);
            if (extractor is null)
                throw new InvalidOperationException($"استخراج متن برای .{ext} پشتیبانی نمی‌شود");

            var extracted = await extractor.ExtractAsync(fullPath, cancellationToken);
            var normalized = normalizer.Normalize(extracted);

            if (ext == "pdf" && normalized.Length < MinPdfTextChars)
            {
                contract.IndexStatus = ContractIndexStatus.NeedsOcr;
                index.ExtractedText = extracted;
                index.NormalizedText = normalized;
                index.ExtractedAt = DateTime.UtcNow;
                index.LastError = "PDF اسکن‌شده یا بدون متن قابل استخراج — OCR پیاده‌سازی نشده";
                index.ContractVersionNumber = contract.CurrentVersionNumber;
                index.ExtractorVersion = ContractTextIndexHelper.ExtractorVersion;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                contract.IndexStatus = ContractIndexStatus.Failed;
                index.LastError = "متنی استخراج نشد";
            }
            else
            {
                contract.IndexStatus = ContractIndexStatus.Indexed;
                index.LastError = null;
            }

            index.ExtractedText = extracted;
            index.NormalizedText = normalized;
            index.ExtractedAt = DateTime.UtcNow;
            index.ContractVersionNumber = contract.CurrentVersionNumber;
            index.ExtractorVersion = ContractTextIndexHelper.ExtractorVersion;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Contract {ContractId} indexed v{Version} chars={Chars} status={Status}",
                contractId,
                contract.CurrentVersionNumber,
                normalized.Length,
                contract.IndexStatus);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Contract extract/index failed {ContractId}", contractId);
            contract.IndexStatus = ContractIndexStatus.Failed;
            index.LastError = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            index.ExtractedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
