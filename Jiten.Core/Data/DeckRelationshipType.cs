namespace Jiten.Core.Data;

public enum DeckRelationshipType
{
    Sequel = 1,
    Prequel = 2,
    Fandisc = 3,
    Spinoff = 4,
    SideStory = 5,
    Adaptation = 6,
    Alternative = 7,

    // Inverse relationships
    HasFandisc = 103,
    HasSpinoff = 104,
    HasSideStory = 105,
    SourceMaterial = 106
}
