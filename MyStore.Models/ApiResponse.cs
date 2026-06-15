using System.Collections.Generic;

namespace MyStore.Models;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, List<string>>? FieldErrors { get; set; }

    public static ApiResponse<T> SuccessResponse(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    public static ApiResponse<T> ErrorResponse(string message, List<string>? errors = null, T? data = default)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Errors = errors ?? new List<string>(),
            Data = data
        };
    }

    public static ApiResponse<T> ValidationErrorResponse(string message, Dictionary<string, List<string>> fieldErrors)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            FieldErrors = fieldErrors
        };
    }
}

