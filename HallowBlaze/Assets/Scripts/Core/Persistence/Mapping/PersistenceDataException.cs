using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.Persistence.Mapping
{
    public enum PersistenceDataError
    {
        EmptyDocument,
        InvalidJson,
        MissingField,
        UnsupportedSchemaVersion,
        InvalidStableId,
        DuplicateId,
        InvalidValue
    }

    public sealed class PersistenceDataException : Exception
    {
        public PersistenceDataException(
            PersistenceDataError error,
            string fieldPath,
            string message)
            : base(message)
        {
            Error = error;
            FieldPath = fieldPath ?? string.Empty;
        }

        public PersistenceDataError Error { get; }
        public string FieldPath { get; }
    }

    internal static class DtoValidation
    {
        public static void RequireCurrentSchemaVersion(
            int actualVersion,
            int currentVersion)
        {
            if (actualVersion != currentVersion)
            {
                throw new PersistenceDataException(
                    PersistenceDataError.UnsupportedSchemaVersion,
                    "schemaVersion",
                    "The document schema version is not supported.");
            }
        }

        public static void RequireStableId(string value, string fieldPath)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                char.IsWhiteSpace(value[0]) ||
                char.IsWhiteSpace(value[value.Length - 1]))
            {
                throw new PersistenceDataException(
                    PersistenceDataError.InvalidStableId,
                    fieldPath,
                    "A required stable ID is missing or invalid.");
            }
        }

        public static void RequirePositive(int value, string fieldPath)
        {
            if (value <= 0)
                ThrowInvalidValue(fieldPath);
        }

        public static void RequireNonNegative(int value, string fieldPath)
        {
            if (value < 0)
                ThrowInvalidValue(fieldPath);
        }

        public static T[] RequireArray<T>(T[] values, string fieldPath)
        {
            if (values == null)
            {
                throw new PersistenceDataException(
                    PersistenceDataError.MissingField,
                    fieldPath,
                    "A required collection is missing.");
            }

            return values;
        }

        public static void ValidateStableIds(
            string[] values,
            string fieldPath,
            bool requireUnique)
        {
            HashSet<string> uniqueIds = requireUnique
                ? new HashSet<string>(StringComparer.Ordinal)
                : null;

            for (int index = 0; index < values.Length; index++)
            {
                string itemPath = $"{fieldPath}[{index}]";
                RequireStableId(values[index], itemPath);
                if (uniqueIds != null && !uniqueIds.Add(values[index]))
                {
                    throw new PersistenceDataException(
                        PersistenceDataError.DuplicateId,
                        itemPath,
                        "A collection contains a duplicate stable ID.");
                }
            }
        }

        public static void ThrowInvalidValue(string fieldPath)
        {
            throw new PersistenceDataException(
                PersistenceDataError.InvalidValue,
                fieldPath,
                "A persisted value is outside its valid range.");
        }
    }
}