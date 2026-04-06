using System.Collections.Frozen;
using Core.Domain.Entities;
using Core.Domain.ValueObjects;

namespace Core.GameWorld.DataStore.Models;

public readonly record struct ClassData(
    FrozenDictionary<Class, ClassInfo> Infos,
    FrozenDictionary<Class, List<ClassInfoItem>> Items);
    