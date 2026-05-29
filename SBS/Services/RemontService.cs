using SmartRemont.ExportRooms.Models;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.Services
{
    public static class RemontService
    {
        public static IReadOnlyList<RemontOption> GetMockRemonts() =>
            new List<RemontOption>
            {
                new RemontOption { Id = 1, Name = "ЖК «Алатау» — квартира 12" },
                new RemontOption { Id = 2, Name = "ЖК «Комфорт» — корпус 3, кв. 45" },
                new RemontOption { Id = 3, Name = "БЦ «Esentai» — офис 204" },
                new RemontOption { Id = 4, Name = "Частный дом — ул. Абая 15" },
            };
    }
}
