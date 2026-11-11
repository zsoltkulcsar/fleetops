using FleetOps.Modules.Absences;
namespace FleetOps.Api;
public static class AbsencePreviewRequest
{
    public static AbsenceType ParseType(string value) => value switch
    {
        "vacation" => AbsenceType.Vacation,
        "sick" => AbsenceType.Sick,
        "training" => AbsenceType.Training,
        "unpaid" => AbsenceType.Unpaid,
        "time_off_in_lieu" => AbsenceType.TimeOffInLieu,
        "other" => AbsenceType.Other,
        _ => throw new AbsenceValidationException("type")
    };
}
