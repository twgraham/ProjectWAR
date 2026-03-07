using System;

namespace Core.Infrastructure.Network;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CStringAttribute : Attribute
{
    public int Length { get; }

    public CStringAttribute(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "CString length must be positive");
        Length = length;
    }
}