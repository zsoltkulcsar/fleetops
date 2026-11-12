using System.Globalization;
using System.Text;

namespace FleetOps.Modules.Payroll;

public sealed class CsvGenericPayrollFormat : IPayrollExportFormat
{
    public string Code => "csv_generic";

    public byte[] Write(IReadOnlyList<PayrollDay> days)
    {
        var csv = new StringBuilder("personnel_number;name;date;driving_h;other_work_h;availability_h;nights_country;absence_type;sunday;holiday\r\n");
        foreach (var day in days)
        {
            csv.AppendLine(string.Join(';', new[] { Cell(day.PersonnelNumber ?? ""), Cell(day.Name), day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Number(day.DrivingHours), Number(day.OtherWorkHours), Number(day.AvailabilityHours),
                Cell(string.Join('|', day.Nights.OrderBy(value => value.Key).Select(value => value.Key + ":" + Number(value.Value)))),
                Cell(string.Join('|', day.Absences.OrderBy(value => value.Key).Select(value => value.Key + ":" + Number(value.Value)))),
                day.Sunday ? "1" : "0", day.Holiday ? "1" : "0" }));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.GetCultureInfo("de-DE"));
    private static string Cell(string value)
    {
        // Quoting alone does not prevent spreadsheet formula execution.
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
