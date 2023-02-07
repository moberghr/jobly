namespace Handfire.Core.Interfaces;
public interface IConcurrencyToken
{
    public Guid Version { get; set; }
}
