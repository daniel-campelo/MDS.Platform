using System.Security.Cryptography;

namespace MDS.SharedKernel;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}

public class Result
{
    public bool IsSuccess { get; }
    public Error Error { get; }
    protected Result(bool success, Error error) => (IsSuccess, Error) = (success, error);
    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
}

public sealed class Result<T> : Result
{
    public T? Value { get; }
    private Result(T value) : base(true, Error.None) => Value = value;
    private Result(Error error) : base(false, error) { }
    public static Result<T> Success(T value) => new(value);
    public new static Result<T> Failure(Error error) => new(error);
}

public readonly record struct CorrelationId(Guid Value)
{
    public static CorrelationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}
public readonly record struct UserId(Guid Value);
public readonly record struct AgentId(Guid Value);
public readonly record struct FileAssetId(Guid Value);

public readonly record struct Length(decimal Millimeters)
{
    public static Length FromMillimeters(decimal value) => new(value);
    public static Length FromCentimeters(decimal value) => new(value * 10m);
    public static Length FromMeters(decimal value) => new(value * 1000m);
    public decimal Centimeters => Millimeters / 10m;
    public decimal Meters => Millimeters / 1000m;
}

public readonly record struct FileHash(string Value)
{
    public static FileHash FromBytes(byte[] bytes) => new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
}

public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
