using RaDuty.Domain;

namespace RaDuty.Infrastructure;

internal static class DormRoomNaming
{
    public static string Code(DormRoom room) =>
        room.SourceCode ?? $"{Prefix(room.ResidenceHall.Code)}-{room.SuiteNumber}{room.RoomLetter}";

    public static string Prefix(string hallCode) => hallCode switch
    {
        "LEWS" => "LEWI",
        "TWRS" => "TWIN",
        _ => hallCode
    };
}
