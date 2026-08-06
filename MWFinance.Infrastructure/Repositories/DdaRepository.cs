using MWFinance.Domain.Entities;
using MWFinance.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MWFinance.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySqlConnector;   // <-- replaces Npgsql; comes in via Pomelo.EntityFrameworkCore.MySql


namespace MWFinance.Infrastructure.Repositories
{
    public class DdaRepository : IDdaRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DdaRepository> _logger;
        public DdaRepository(ApplicationDbContext context, ILogger<DdaRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task AddAsync(DirectDebitAuthority dda)
        {
            await _context.DirectDebitAuthorities.AddAsync(dda);
        }

        public async Task SaveChangesAsync()
        {
            try
            {
                int affectedRows = await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "DdaRepository.SaveChangesAsync: committed successfully, AffectedRows={AffectedRows}",
                    affectedRows);
            }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlException mysqlEx)
            {
                // MySQL-specific error (constraint violation, bad data, etc.)
                // mysqlEx.Number gives you the exact MySQL error code (e.g. 1062 = duplicate entry / unique violation)
                _logger.LogError(ex,
                    "DdaRepository.SaveChangesAsync: MySQL error. ErrorCode={ErrorCode}, Message={Message}",
                    mysqlEx.Number, mysqlEx.Message);
                throw;
            }
            catch (MySqlException ex) when (ex.IsTransient)
            {
                // Transient = likely timeout, connection drop, or temporary network issue
                _logger.LogError(ex,
                    "DdaRepository.SaveChangesAsync: transient MySQL error (likely timeout/network issue)");
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
            return await _context.DirectDebitAuthorities.FirstOrDefaultAsync(x => x.DdaReferenceNumber == ddaRefNo) ?? null;
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
