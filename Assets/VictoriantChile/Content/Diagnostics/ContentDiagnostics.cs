using System;

namespace VictoriantChile.Content.Diagnostics
{
    public enum ContentDiagnosticSeverity
    {
        Warning,
        Error
    }

    public enum ContentDiagnosticCode
    {
        SourceReadFailed,
        ManifestMissing,
        JsonMalformed,
        InvalidUtf8,
        DuplicateJsonProperty,
        MissingRequiredProperty,
        UnknownProperty,
        InvalidPropertyType,
        InvalidValue,
        UnsupportedContentSchemaVersion,
        IncompatibleGameSchemaVersion,
        UnsafeManifestPath,
        InvalidHashFormat,
        MissingDeclaredFile,
        HashMismatch,
        MissingRequiredManifestEntry,
        DuplicateId,
        InvalidId,
        UnsupportedSchemaVersion,
        InvalidEnum,
        DuplicateTargetPattern,
        InvalidTargetPattern,
        InvalidTargetReference,
        InvalidTargetOperation,
        InvalidRange,
        InvalidWeightSum,
        MissingLocalizationKey,
        InvalidMacrozone,
        RegionWeightTotalMismatch
    }

    public sealed class ContentDiagnostic
    {
        public ContentDiagnostic(
            ContentDiagnosticSeverity severity,
            ContentDiagnosticCode code,
            string relativeFile,
            string jsonPath,
            string message)
        {
            Severity = severity;
            Code = code;
            RelativeFile = relativeFile ?? string.Empty;
            JsonPath = jsonPath ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public ContentDiagnosticSeverity Severity { get; }

        public ContentDiagnosticCode Code { get; }

        public string RelativeFile { get; }

        public string JsonPath { get; }

        public string Message { get; }

        public override string ToString()
        {
            return $"{Severity} {Code} {RelativeFile} {JsonPath}: {Message}";
        }
    }
}
