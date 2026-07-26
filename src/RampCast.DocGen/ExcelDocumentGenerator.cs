using ClosedXML.Excel;
using RampCast.DocGen.Models;

namespace RampCast.DocGen;

/// <summary>
/// Renders a <see cref="StaffingPlan"/> into a populated .xlsx: a merged
/// plan-summary line, a header row of project weeks, and phase → task → resource
/// rows with collapsible outline grouping. Weekly hours span the week columns;
/// each resource's rationale is a hover-to-reveal cell comment on its name.
/// Stateless — no DI registration needed; call the static member directly.
/// </summary>
public static class ExcelDocumentGenerator
{
    private const string SheetName = "Staffing Plan";
    private const string FontName = "Arial";
    private const string CommentAuthor = "RampCast";
    // Excel's default comment box (~96x59pt) truncates most rationale text.
    // Points, matching IXLDrawingSize's unit.
    private const double CommentWidth = 100;
    private const double CommentHeight = 100;

    private static readonly XLColor HeaderFill = XLColor.FromHtml("#1F4E78");
    private static readonly XLColor HeaderText = XLColor.FromHtml("#FFFFFF");
    private static readonly XLColor PhaseFill = XLColor.FromHtml("#D9E1F2");
    private static readonly XLColor TaskFill = XLColor.FromHtml("#F2F2F2");
    private static readonly XLColor ZeroText = XLColor.FromHtml("#BFBFBF");
    private static readonly XLColor TextBlack = XLColor.FromHtml("#000000");
    private static readonly XLColor SummaryText = XLColor.FromHtml("#404040");

    /// <summary>Render the plan to .xlsx bytes.</summary>
    public static byte[] Generate(StaffingPlan plan)
    {
        var weeks = plan.TotalDurationWeeks;
        var lastCol = 1 + weeks; // column A + one column per week

        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet(SheetName);

        // Phase/task header rows sit ABOVE their detail rows.
        ws.Outline.SummaryVLocation = XLOutlineSummaryVLocation.Top;

        ws.Column(1).Width = 32;
        for (var c = 2; c <= lastCol; c++)
            ws.Column(c).Width = 9;

        WriteSummaryRow(ws, plan.PlanSummary, lastCol);
        WriteHeaderRow(ws, weeks);

        // Freeze the summary + header rows and the label column.
        ws.SheetView.Freeze(2, 1);

        var row = 3;
        foreach (var phase in plan.Phases)
        {
            WritePhaseRow(ws, row++, phase);

            if (phase.Tasks.Count == 0)
            {
                // Leaf phase: resources hang directly off the phase (level 1).
                foreach (var role in phase.Roles)
                    WriteResourceRow(ws, row++, role, weeks, outlineLevel: 1);
            }
            else
            {
                // Phase with tasks: task header at level 1, its resources at level 2.
                foreach (var task in phase.Tasks)
                {
                    WriteTaskRow(ws, row++, phase, task);
                    foreach (var role in task.Roles)
                        WriteResourceRow(ws, row++, role, weeks, outlineLevel: 2);
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSummaryRow(IXLWorksheet ws, string summary, int lastCol)
    {
        var cell = ws.Cell(1, 1);
        cell.Value = summary;
        ws.Range(1, 1, 1, lastCol).Merge();

        var font = cell.Style.Font;
        font.FontName = FontName;
        font.Italic = true;
        font.FontSize = 10;
        font.FontColor = SummaryText;

        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Alignment.WrapText = true;
        ws.Row(1).Height = 50;
    }

    private static void WriteHeaderRow(IXLWorksheet ws, int weeks)
    {
        StyleHeaderCell(ws.Cell(2, 1), "Phase / Task / Resource", centered: false);
        for (var w = 1; w <= weeks; w++)
            StyleHeaderCell(ws.Cell(2, 1 + w), $"Week {w}", centered: true);
    }

    private static void StyleHeaderCell(IXLCell cell, string text, bool centered)
    {
        cell.Value = text;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontColor = HeaderText;
        cell.Style.Fill.BackgroundColor = HeaderFill;
        if (centered)
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void WritePhaseRow(IXLWorksheet ws, int row, PlanPhase phase)
    {
        // Outline level 0 (default). Only column A is filled, matching the mockup.
        var cell = ws.Cell(row, 1);
        cell.Value = $"{phase.WbsCode}  {phase.Name}";
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = PhaseFill;
    }

    private static void WriteTaskRow(IXLWorksheet ws, int row, PlanPhase phase, PlanTask task)
    {
        var cell = ws.Cell(row, 1);
        cell.Value = $"{phase.WbsCode}.{task.WbsCode}  {task.Name}";
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.Bold = true;
        cell.Style.Font.Italic = true;
        cell.Style.Fill.BackgroundColor = TaskFill;
        ws.Row(row).OutlineLevel = 1;
    }

    private static void WriteResourceRow(IXLWorksheet ws, int row, PlanRole role, int weeks, int outlineLevel)
    {
        // The schema documents this invariant but JSON Schema can't enforce a
        // cross-field array length, so enforce it here — fail loud.
        if (role.RampPattern.Count != weeks)
            throw new InvalidOperationException(
                $"Role '{role.RoleName}' has a rampPattern of {role.RampPattern.Count} week(s) but the project's " +
                $"totalDurationWeeks is {weeks}. Ramp patterns must be full-width and zero-padded to the project week axis.");

        var nameCell = ws.Cell(row, 1);
        nameCell.Value = role.RoleName;
        nameCell.Style.Font.FontName = FontName;

        // Rationale as a hidden hover comment on the resource name cell. Sized
        // up from Excel's default (~96x59pt) so the rationale text isn't clipped.
        var comment = nameCell.CreateComment();
        comment.SetAuthor(CommentAuthor);
        comment.AddText(role.Rationale);
        comment.Visible = false;

        comment.Style.Size.Width = CommentWidth;
        comment.Style.Size.Height = CommentHeight;
        // comment.Style.Size.AutomaticSize = true;

        for (var w = 0; w < weeks; w++)
        {
            var hours = role.RampPattern[w];
            var cell = ws.Cell(row, 2 + w);
            cell.Value = hours;
            cell.Style.Font.FontName = FontName;
            cell.Style.NumberFormat.SetFormat("0");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            // Grey a zero so "explicitly not active" reads differently from a low number.
            cell.Style.Font.FontColor = hours == 0m ? ZeroText : TextBlack;
        }

        ws.Row(row).OutlineLevel = outlineLevel;
    }
}
