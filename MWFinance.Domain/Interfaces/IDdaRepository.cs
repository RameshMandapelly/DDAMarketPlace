using MWFinance.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MWFinance.Domain.Interfaces
{
    public interface IDdaRepository
    {
        Task<DirectDebitAuthority?> GetByReferenceAsync(string ddaRefNo);
        Task<DirectDebitAuthority?> GetByIdentifierStringAsync(string identifier);
        Task AddAsync(DirectDebitAuthority dda);
        Task SaveChangesAsync();
    }
}
