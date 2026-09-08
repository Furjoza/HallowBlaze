using System;

namespace HallowBlaze.Core.Persistence.Storage
{
    /// <summary>
    /// Result of a save/load operation.
    /// </summary>
    public enum SaveStoreResultType
    {
        Success,
        Missing,
        Corrupt,
        Recovered,
        UnsupportedFutureSchema,
        IoError
    }

    /// <summary>
    /// Result of a save/load operation.
    /// </summary>
    /// <typeparam name="T">The type of data in the result.</typeparam>
    public class SaveStoreResult<T>
    {
        /// <summary>
        /// Gets the result type.
        /// </summary>
        public SaveStoreResultType Type { get; }

        /// <summary>
        /// Gets the data if the operation was successful.
        /// </summary>
        public T Data { get; }

        /// <summary>
        /// Gets the error message if the operation failed.
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SaveStoreResult{T}"/> class.
        /// </summary>
        /// <param name="type">The result type.</param>
        /// <param name="data">The data if the operation was successful.</param>
        /// <param name="errorMessage">The error message if the operation failed.</param>
        public SaveStoreResult(SaveStoreResultType type, T data, string errorMessage = null)
        {
            Type = type;
            Data = data;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Determines whether the operation was successful.
        /// </summary>
        public bool IsSuccess => Type == SaveStoreResultType.Success || Type == SaveStoreResultType.Recovered;

        /// <summary>
        /// Determines whether the operation failed.
        /// </summary>
        public bool IsFailure => !IsSuccess;
    }

    /// <summary>
    /// Result of a save/load operation without data.
    /// </summary>
    public class SaveStoreResult : SaveStoreResult<object>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SaveStoreResult"/> class.
        /// </summary>
        /// <param name="type">The result type.</param>
        /// <param name="errorMessage">The error message if the operation failed.</param>
        public SaveStoreResult(SaveStoreResultType type, string errorMessage = null)
            : base(type, null, errorMessage)
        {
        }

        /// <summary>
        /// Creates a success result.
        /// </summary>
        public static SaveStoreResult Success() => new SaveStoreResult(SaveStoreResultType.Success);

        /// <summary>
        /// Creates a missing result.
        /// </summary>
        /// <param name="errorMessage">The error message.</param>
        public static SaveStoreResult Missing(string errorMessage = null) => new SaveStoreResult(SaveStoreResultType.Missing, errorMessage);

        /// <summary>
        /// Creates a corrupt result.
        /// </summary>
        /// <param name="errorMessage">The error message.</param>
        public static SaveStoreResult Corrupt(string errorMessage = null) => new SaveStoreResult(SaveStoreResultType.Corrupt, errorMessage);

        /// <summary>
        /// Creates a recovered result.
        /// </summary>
        public static SaveStoreResult Recovered() => new SaveStoreResult(SaveStoreResultType.Recovered);

        /// <summary>
        /// Creates an unsupported future schema result.
        /// </summary>
        /// <param name="errorMessage">The error message.</param>
        public static SaveStoreResult UnsupportedFutureSchema(string errorMessage = null) => new SaveStoreResult(SaveStoreResultType.UnsupportedFutureSchema, errorMessage);

        /// <summary>
        /// Creates an I/O error result.
        /// </summary>
        /// <param name="errorMessage">The error message.</param>
        public static SaveStoreResult IoError(string errorMessage = null) => new SaveStoreResult(SaveStoreResultType.IoError, errorMessage);
    }

    /// <summary>
    /// Result of a save/load operation with data.
    /// </summary>
    /// <typeparam name="T">The type of data in the result.</typeparam>
    public static class SaveStoreResultExtensions
    {
        /// <summary>
        /// Creates a success result with data.
        /// </summary>
        /// <typeparam name="T">The type of data in the result.</typeparam>
        /// <param name="data">The data.</param>
        /// <returns>A success result with the data.</returns>
        public static SaveStoreResult<T> Success<T>(T data) => new SaveStoreResult<T>(SaveStoreResultType.Success, data);

        /// <summary>
        /// Creates a missing result with data.
        /// </summary>
        /// <typeparam name="T">The type of data in the result.</typeparam>
        /// <param name="errorMessage">The error message.</param>
        /// <returns>A missing result.</returns>
        public static SaveStoreResult<T> Missing<T>(string errorMessage = null) => new SaveStoreResult<T>(SaveStoreResultType.Missing, default, errorMessage);

        /// <summary>
        /// Creates a corrupt result with data.
        /// </summary>
        /// <typeparam name="T">The type of data in the result.</typeparam>
        /// <param name="errorMessage">The error message.</param>
        /// <returns>A corrupt result.</returns>
        public static SaveStoreResult<T> Corrupt<T>(string errorMessage = null) => new SaveStoreResult<T>(SaveStoreResultType.Corrupt, default, errorMessage);

        /// <summary>
        /// Creates a recovered result with data.
        /// </summary>
        /// <typeparam name="T">The type of data in the result.</typeparam>
        /// <param name="data">The recovered data.</param>
        /// <returns>A recovered result with the data.</returns>
        public static SaveStoreResult<T> Recovered<T>(T data) => new SaveStoreResult<T>(SaveStoreResultType.Recovered, data);

        /// <summary>
        /// Creates an unsupported future schema result with data.
        /// </summary>
        /// <typeparam name="T">The type of data in the result.</typeparam>
        /// <param name="errorMessage">The error message.</param>
        /// <returns>An unsupported future schema result.</returns>
        public static SaveStoreResult<T> UnsupportedFutureSchema<T>(string errorMessage = null) => new SaveStoreResult<T>(SaveStoreResultType.UnsupportedFutureSchema, default, errorMessage);

        /// <summary>
        /// Creates an I/O error result with data.
        /// </summary>
        /// <typeparam name="T">The type of data in the result.</typeparam>
        /// <param name="errorMessage">The error message.</param>
        /// <returns>An I/O error result.</returns>
        public static SaveStoreResult<T> IoError<T>(string errorMessage = null) => new SaveStoreResult<T>(SaveStoreResultType.IoError, default, errorMessage);
    }
}