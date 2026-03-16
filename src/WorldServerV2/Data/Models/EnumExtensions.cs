namespace WorldServerV2.Data.Models;

public static class EnumExtensions
{
    extension(Race race)
    {
        public Faction GetFaction()
        {
            return race switch
            {
                Race.Dwarf or Race.HighElf or Race.Empire => Faction.Order,
                Race.Orc or Race.Goblin or Race.DarkElf or Race.Chaos => Faction.Destruction,
                _ => throw new ArgumentOutOfRangeException(nameof(race), race, null)
            };
        }
    }
}