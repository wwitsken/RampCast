using CsvHelper.Configuration.Attributes;

namespace RampCast.Functions.Models;

/// <summary>
/// One row of the daily-grain timesheet CSV export (docs/csv-input-schema.md).
/// Every column is mapped, so CsvHelper's header validation fails loudly when a
/// required column — including the wbsN_name columns — is absent from the export.
/// </summary>
public sealed class TimesheetRow
{
    [Name("wbs1")] public string Wbs1 { get; set; } = "";
    [Name("wbs1_name")] public string Wbs1Name { get; set; } = "";
    [Name("wbs2")] public string Wbs2 { get; set; } = "";
    [Name("wbs2_name")] public string Wbs2Name { get; set; } = "";
    [Name("wbs3")] public string Wbs3 { get; set; } = "";
    [Name("wbs3_name")] public string Wbs3Name { get; set; } = "";
    [Name("role")] public string Role { get; set; } = "";
    [Name("day")] public DateOnly Day { get; set; }
    [Name("hours")] public decimal Hours { get; set; }
    [Name("comment")] public string Comment { get; set; } = "";
}
