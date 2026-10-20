namespace EasyClothes.Core.Rage.Meta;

public sealed record MetaEntry(uint Name, uint Offset, byte Type, short ReferenceIndex, uint ReferenceKey);

public sealed record MetaStructInfo(uint Name, uint Key, uint Unknown, uint Size, MetaEntry[] Entries);

public sealed record MetaEnumInfo(uint Name, uint Key, (uint Name, int Value)[] Values);
