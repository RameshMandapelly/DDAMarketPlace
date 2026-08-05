using MWFinance.Domain.Entities;
using MWFinance.Domain.Interfaces;
//using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MWFinance.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


namespace MWFinance.Infrastructure.Repositories
{
    public class DdaRepository : IDdaRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DdaRepository> _logger;
        public DdaRepository(ApplicationDbContext context,ILogger<DdaRepository> logger)
        {
            _context = context;
            _logger= logger;
        }

        public async Task AddAsync(DirectDebitAuthority dda)
        {            
            await _context.DirectDebitAuthorities.AddAsync(dda);
        }

        public async Task SaveChangesAsync()
        {
            
            //await _context.SaveChangesAsync();
            try
            {
                int affectedRows = await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "DdaRepository.SaveChangesAsync: committed successfully, AffectedRows={AffectedRows}",
                    affectedRows);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx)
            {
                // Postgres-specific error (constraint violation, bad data, etc.)
                // pgEx.SqlState gives you the exact Postgres error code (e.g. "23505" = unique violation)
                _logger.LogError(ex,
                    "DdaRepository.SaveChangesAsync: Postgres error. SqlState={SqlState}, Detail={Detail}",
                    pgEx.SqlState, pgEx.MessageText);
                throw;
            }
            catch (Npgsql.NpgsqlException ex) when (ex.IsTransient)
            {
                // Transient = likely timeout, connection drop, or temporary network issue
                // Safe to consider retrying these in the future if you add retry logic
                _logger.LogError(ex,
                    "DdaRepository.SaveChangesAsync: transient Npgsql error (likely timeout/network issue)");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                // Usually means the command exceeded its timeout window, or the HTTP request was aborted
                _logger.LogError(ex, "DdaRepository.SaveChangesAsync: operation timed out or was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                // True catch-all for anything unanticipated
                _logger.LogError(ex, "DdaRepository.SaveChangesAsync: unexpected exception while committing changes");
                throw;
            }
        }

        public async Task<DirectDebitAuthority?> GetByReferenceAsync(string ddaRefNo)
        {
            
            return await _context.DirectDebitAuthorities.FirstOrDefaultAsync( x=>x.DdaReferenceNumber == ddaRefNo) ?? null;
        }

        public async Task<DirectDebitAuthority?> GetByIdentifierStringAsync(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return null;

            string cleanId = identifier.Trim();

            // 1. Scan primary user reference code tracker
            var record = await _context.DirectDebitAuthorities
                .FirstOrDefaultAsync(x => x.DdaReferenceNumber == cleanId);

            // 2. Fallback: Scan internal table auto-increment identity if numeric
            if (record == null && int.TryParse(cleanId, out int intId))
            {
                record = await _context.DirectDebitAuthorities
                    .FirstOrDefaultAsync(x => x.Id == intId);
            }

            return record;
        }


    }
}
