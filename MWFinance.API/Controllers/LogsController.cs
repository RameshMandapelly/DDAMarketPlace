using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using System.Text;
using System.Text.Encodings.Web;

namespace MWFinance.API.Controllers
{
          /// <summary>
          /// Simple browser-based log viewer, reads directly from the
          /// mwfddamarketplacelogs table created by the Serilog MySQL sink.
          /// Protected by a secret key in the query string (not JWT) so it can
          /// be bookmarked and shared with ops, similar to the Circularo log page.
          /// </summary>
          [Route("logs")]
          [ApiController]
          public class LogsController : ControllerBase
          {
                    private readonly IConfiguration _configuration;

                    public LogsController(IConfiguration configuration)
                    {
                              _configuration = configuration;
                    }

                    [HttpGet]
                    public async Task<IActionResult> ViewLogs(
                        [FromQuery] string? key,
                        [FromQuery] string? level,
                        [FromQuery] string? search,
                        [FromQuery] int take = 200)
                    {
                              // ── 1. Simple key check ─────────────────────────────────────────
                              var expectedKey = _configuration["LogsViewer:SecretKey"];
                              if (string.IsNullOrEmpty(expectedKey) || key != expectedKey)
                              {
                                        return Content("Unauthorized. Append ?key=<your-key> to the URL.", "text/plain");
                              }

                              // Clamp "take" to something sane
                              if (take <= 0) take = 200;
                              if (take > 5000) take = 5000;

                              level = string.IsNullOrWhiteSpace(level) ? "" : level.Trim();
                              search = string.IsNullOrWhiteSpace(search) ? "" : search.Trim();

                              // ── 2. Query the log table directly (not an EF-mapped entity) ───
                              var connectionString = _configuration.GetConnectionString("DefaultConnection");
                              var rows = new List<(int Id, string Timestamp, string Level, string Message)>();

                              await using (var conn = new MySqlConnector.MySqlConnection(connectionString))
                              {
                                        await conn.OpenAsync();

                                        var sql = @"SELECT id, Timestamp, Level, Message
                            FROM mwfddamarketplacelogs
                            WHERE (@level = '' OR Level = @level)
                              AND (@search = '' OR Message LIKE CONCAT('%', @search, '%'))
                            ORDER BY id DESC
                            LIMIT @take";

                                        await using var cmd = new MySqlCommand(sql, conn);
                                        cmd.Parameters.AddWithValue("@level", level);
                                        cmd.Parameters.AddWithValue("@search", search);
                                        cmd.Parameters.AddWithValue("@take", take);

                                        await using var reader = await cmd.ExecuteReaderAsync();
                                        while (await reader.ReadAsync())
                                        {
                                                  rows.Add((
                                                      reader.GetInt32("id"),
                                                      reader.IsDBNull(reader.GetOrdinal("Timestamp")) ? "" : reader.GetString("Timestamp"),
                                                      reader.IsDBNull(reader.GetOrdinal("Level")) ? "" : reader.GetString("Level"),
                                                      reader.IsDBNull(reader.GetOrdinal("Message")) ? "" : reader.GetString("Message")
                                                  ));
                                        }
                              }

                              // ── 3. Render HTML page ──────────────────────────────────────────
                              var html = BuildHtml(rows, key!, level, search, take);
                              return Content(html, "text/html");
                    }

                    private static string BuildHtml(
                        List<(int Id, string Timestamp, string Level, string Message)> rows,
                        string key, string level, string search, int take)
                    {
                              string LevelColor(string lvl) => lvl switch
                              {
                                        "Error" => "#c0392b",
                                        "Warning" => "#d68910",
                                        "Debug" => "#7f8c8d",
                                        _ => "#1e8449" // Information / default
                              };

                              var sb = new StringBuilder();
                              sb.Append($@"
<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<title>MWFinance API – Logs</title>
<meta http-equiv='refresh' content='30'>
<style>
  body {{ font-family: Segoe UI, Arial, sans-serif; margin: 24px; color: #222; }}
  h1 {{ margin-bottom: 4px; }}
  .subtitle {{ color: #666; margin-bottom: 20px; font-size: 13px; }}
  .filters {{ margin-bottom: 16px; display: flex; gap: 12px; align-items: center; }}
  .filters label {{ font-weight: 600; font-size: 13px; }}
  select, input[type=text], input[type=number] {{ padding: 5px 8px; border: 1px solid #ccc; border-radius: 4px; }}
  button {{ padding: 6px 14px; border: 1px solid #888; border-radius: 4px; background: #f5f5f5; cursor: pointer; }}
  table {{ border-collapse: collapse; width: 100%; font-size: 13px; }}
    th {{ background: #1a3c6e; color: white; text-align: left; padding: 10px 18px; }}
  td {{ padding: 8px 18px; border-bottom: 1px solid #eee; vertical-align: top; }}
  td:nth-child(2) {{ white-space: nowrap; }}
  tr:hover {{ background: #f9f9f9; }}
</style>
</head>
<body>
<h1>MWFinance API – Logs</h1>
<div class='subtitle'>Source: mwfddamarketplacelogs · auto-refreshes every 30s · newest first</div>

<form method='get' class='filters'>
  <input type='hidden' name='key' value='{HtmlEncoder.Default.Encode(key)}'>
  <label>Level:
    <select name='level'>
      <option value=''{(level == "" ? " selected" : "")}>All</option>
      <option value='Information'{(level == "Information" ? " selected" : "")}>Information</option>
      <option value='Warning'{(level == "Warning" ? " selected" : "")}>Warning</option>
      <option value='Error'{(level == "Error" ? " selected" : "")}>Error</option>
      <option value='Debug'{(level == "Debug" ? " selected" : "")}>Debug</option>
    </select>
  </label>
  <label>Search: <input type='text' name='search' placeholder='e.g. initiate' value='{HtmlEncoder.Default.Encode(search)}'></label>
  <label>Rows: <input type='number' name='take' value='{take}' style='width:80px'></label>
  <button type='submit'>Apply</button>
</form>

<table>
<tr><th>Id</th><th>Timestamp</th><th>Level</th><th>Message</th></tr>
");

                              foreach (var row in rows)
                              {
                                        sb.Append($@"<tr>
<td>{row.Id}</td>
<td>{HtmlEncoder.Default.Encode(row.Timestamp)}</td>
<td style='color:{LevelColor(row.Level)}; font-weight:600'>{HtmlEncoder.Default.Encode(row.Level)}</td>
<td>{HtmlEncoder.Default.Encode(row.Message)}</td>
</tr>
");
                              }

                              sb.Append("</table></body></html>");
                              return sb.ToString();
                    }
          }
}