using System.Collections.Frozen;
using WorldServerV2.Data.Entities;
using WorldServerV2.Data.Models;

namespace WorldServerV2.Data.Domain;

public readonly record struct ClassData(
    FrozenDictionary<Class, ClassInfo> Infos,
    FrozenDictionary<Class, List<ClassInfoItem>> Items);
    