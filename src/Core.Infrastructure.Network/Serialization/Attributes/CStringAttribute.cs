namespace Core.Infrastructure.Network.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CStringAttribute : Attribute
{
    public int? Length { get; }

    /// <summary>Null-terminated C-string with no fixed field width.</summary>
    public CStringAttribute()
    {
        Length = null;
    }

    /// <summary>Fixed-width C-string field of exactly <paramref name="length"/> bytes.</summary>
    public CStringAttribute(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "CString length must be positive");
        Length = length;
    }
}