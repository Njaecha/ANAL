using MessagePack;

namespace AmazingNewAccessoryLogic
{
    [MessagePackObject]
    public class TriggerProperty
    {
        [Key("Coordinate")] public int Coordinate { get; set; }
        [Key("Slot")] public int Slot { get; set; }
        [Key("RefKind")] public int ClothingSlot { get; set; }
        [Key("RefState")] public int ClothingState { get; set; }
        [Key("Visible")] public bool Visible { get; set; }
        [Key("Priority")] public int Priority { get; set; }
    }
}