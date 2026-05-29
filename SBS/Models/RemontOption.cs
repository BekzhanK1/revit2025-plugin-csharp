namespace SmartRemont.ExportRooms.Models
{
    public class RemontOption
    {
        public int ClientRequestId { get; set; }
        public int? RemontId { get; set; }

        /// <summary>ID ремонта для обратной совместимости (0 если remont_id отсутствует).</summary>
        public int Id
        {
            get => RemontId ?? 0;
            set => RemontId = value > 0 ? value : null;
        }

        public string Name { get; set; }

        public override string ToString() => Name;
    }
}
