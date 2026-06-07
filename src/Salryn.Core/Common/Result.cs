namespace Salryn.Core.Common;

public enum ErrorType
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
    Failure = 6,
    Unexpected = 7
}

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error None => new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Validation(string code, string message)
    {
        return new Error(code, message, ErrorType.Validation);
    }

    public static Error NotFound(string code, string message)
    {
        return new Error(code, message, ErrorType.NotFound);
    }

    public static Error Conflict(string code, string message)
    {
        return new Error(code, message, ErrorType.Conflict);
    }

    public static Error Unauthorized(string code, string message)
    {
        return new Error(code, message, ErrorType.Unauthorized);
    }

    public static Error Forbidden(string code, string message)
    {
        return new Error(code, message, ErrorType.Forbidden);
    }

    public static Error Failure(string code, string message)
    {
        return new Error(code, message, ErrorType.Failure);
    }

    public static Error Unexpected(string code, string message)
    {
        return new Error(code, message, ErrorType.Unexpected);
    }
}

public sealed class Result
{
    private Result(bool isSuccess, global::Salryn.Core.Common.Error errorDetail)
    {
        if (isSuccess && errorDetail != global::Salryn.Core.Common.Error.None)
        {
            throw new InvalidOperationException("Successful result cannot have an error.");
        }

        if (!isSuccess && errorDetail == global::Salryn.Core.Common.Error.None)
        {
            throw new InvalidOperationException("Failed result must have an error.");
        }

        IsSuccess = isSuccess;
        ErrorDetail = errorDetail;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public global::Salryn.Core.Common.Error ErrorDetail { get; }

    public string Error => ErrorDetail.Message;

    public static Result Success()
    {
        return new Result(true, global::Salryn.Core.Common.Error.None);
    }

    public static Result Fail(string error)
    {
        return new Result(false, global::Salryn.Core.Common.Error.Failure("General.Failure", error.Trim()));
    }

    public static Result Fail(global::Salryn.Core.Common.Error error)
    {
        return new Result(false, error);
    }
}

public sealed class Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, global::Salryn.Core.Common.Error errorDetail)
    {
        if (isSuccess && errorDetail != global::Salryn.Core.Common.Error.None)
        {
            throw new InvalidOperationException("Successful result cannot have an error.");
        }

        if (!isSuccess && errorDetail == global::Salryn.Core.Common.Error.None)
        {
            throw new InvalidOperationException("Failed result must have an error.");
        }

        IsSuccess = isSuccess;
        _value = value;
        ErrorDetail = errorDetail;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public global::Salryn.Core.Common.Error ErrorDetail { get; }

    public string Error => ErrorDetail.Message;

    public T Value
    {
        get
        {
            if (IsFailure)
            {
                throw new InvalidOperationException("Cannot access value of failed result.");
            }

            return _value!;
        }
    }

    public static Result<T> Success(T value)
    {
        return new Result<T>(true, value, global::Salryn.Core.Common.Error.None);
    }

    public static Result<T> Fail(string error)
    {
        return new Result<T>(false, default, global::Salryn.Core.Common.Error.Failure("General.Failure", error.Trim()));
    }

    public static Result<T> Fail(global::Salryn.Core.Common.Error error)
    {
        return new Result<T>(false, default, error);
    }
}
