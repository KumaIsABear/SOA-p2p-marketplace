namespace Admin.API.Application.Common;

public sealed class AdminResult<T> {
    public bool IsSuccess { get; }
    public T? Value { get; }
    public AdminError? Error { get; }

    private AdminResult(T value) {
        IsSuccess = true;
        Value = value;
    }

    private AdminResult(AdminError error) {
        IsSuccess = false;
        Error = error;
    }

    public static AdminResult<T> Success(T value) => new(value);
    public static AdminResult<T> Failure(AdminError error) => new(error);
}
