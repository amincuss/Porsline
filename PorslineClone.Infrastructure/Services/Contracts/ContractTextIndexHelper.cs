using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Contracts;

public static class ContractTextIndexHelper
{
    public const string ExtractorVersion = "1";

    public static void EnsurePendingIndex(AppDbContext db, Contract contract)
    {
        if (!db.ContractTextIndexes.Any(x => x.ContractId == contract.Id))
        {
            db.ContractTextIndexes.Add(new ContractTextIndex
            {
                ContractId = contract.Id,
                ExtractorVersion = ExtractorVersion,
                ContractVersionNumber = contract.CurrentVersionNumber,
            });
        }
        else
        {
            var row = db.ContractTextIndexes.First(x => x.ContractId == contract.Id);
            row.ContractVersionNumber = contract.CurrentVersionNumber;
            row.LastError = null;
        }

        contract.IndexStatus = ContractIndexStatus.Pending;
    }
}
