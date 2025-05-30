namespace ActualLab.Fusion.Blazor;

public sealed class ByUuidParameterComparer : ParameterComparer
{
    public static ByUuidParameterComparer Instance { get; } = new();

    public override bool AreEqual(object? oldValue, object? newValue)
    {
        if (ReferenceEquals(oldValue, newValue))
            return true; // Might be the most frequent case
        if (oldValue == null)
            return newValue == null;
        if (newValue == null)
            return false;

        var oldUuid = ((IHasUuid)oldValue).Uuid;
        var newUuid = ((IHasUuid)newValue).Uuid;
        return string.Equals(oldUuid, newUuid, StringComparison.Ordinal);
    }
}
