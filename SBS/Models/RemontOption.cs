namespace SmartRemont.ExportRooms.Models
{
    public class RemontOption
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public override string ToString() => Name;
    }
}
