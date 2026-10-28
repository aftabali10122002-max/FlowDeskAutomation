namespace FlowDisk.Api.Attachments;

public sealed class RequestAttachmentOptions
{
    public const long MaximumMultipartBodyBytes = 27L * 1024 * 1024;
    private const long MaximumConfigurableTotalBytes = 26L * 1024 * 1024;

    public long MaximumFileSizeBytes { get; }

    public int MaximumFilesPerRequest { get; }

    public long MaximumTotalSizeBytes { get; }

    public int CleanupIntervalSeconds { get; }

    public int CleanupBatchSize { get; }

    public int MaximumConcurrentUploadsPerUser { get; }

    public int ScanIntervalSeconds { get; }

    public int ScanBatchSize { get; }

    public int MaximumScanAttempts { get; }

    public int ScanLeaseSeconds { get; }

    public int ScanRetryBaseSeconds { get; }

    public int ScanRetryMaximumSeconds { get; }

    public int OrphanFileGraceHours { get; }

    private RequestAttachmentOptions(
        long maximumFileSizeBytes,
        int maximumFilesPerRequest,
        long maximumTotalSizeBytes,
        int cleanupIntervalSeconds,
        int cleanupBatchSize,
        int maximumConcurrentUploadsPerUser,
        int scanIntervalSeconds,
        int scanBatchSize,
        int maximumScanAttempts,
        int scanLeaseSeconds,
        int scanRetryBaseSeconds,
        int scanRetryMaximumSeconds,
        int orphanFileGraceHours)
    {
        MaximumFileSizeBytes = maximumFileSizeBytes;
        MaximumFilesPerRequest = maximumFilesPerRequest;
        MaximumTotalSizeBytes = maximumTotalSizeBytes;
        CleanupIntervalSeconds = cleanupIntervalSeconds;
        CleanupBatchSize = cleanupBatchSize;
        MaximumConcurrentUploadsPerUser =
            maximumConcurrentUploadsPerUser;
        ScanIntervalSeconds = scanIntervalSeconds;
        ScanBatchSize = scanBatchSize;
        MaximumScanAttempts = maximumScanAttempts;
        ScanLeaseSeconds = scanLeaseSeconds;
        ScanRetryBaseSeconds = scanRetryBaseSeconds;
        ScanRetryMaximumSeconds = scanRetryMaximumSeconds;
        OrphanFileGraceHours = orphanFileGraceHours;
    }

    public static RequestAttachmentOptions FromConfiguration(
        IConfiguration configuration)
    {
        if (configuration.GetValue<bool>(
            "Attachments:AllowUnscannedDownloads"))
        {
            throw new InvalidOperationException(
                "Unscanned attachment downloads are not supported.");
        }

        var maximumFileSize = ReadPositiveLong(
            configuration,
            "Attachments:MaximumFileSizeBytes",
            10L * 1024 * 1024);
        var maximumFiles = ReadPositiveInt(
            configuration,
            "Attachments:MaximumFilesPerRequest",
            5);
        var maximumTotalSize = ReadPositiveLong(
            configuration,
            "Attachments:MaximumTotalSizeBytes",
            25L * 1024 * 1024);
        if (maximumFiles > 20 ||
            maximumTotalSize > MaximumConfigurableTotalBytes ||
            maximumFileSize > maximumTotalSize ||
            maximumTotalSize < maximumFileSize)
        {
            throw new InvalidOperationException(
                "Attachment size and count limits exceed the supported multipart request bounds.");
        }

        var maximumScanAttempts = ReadPositiveInt(
            configuration,
            "Attachments:Scanner:MaximumAttempts",
            6);
        var scanRetryBaseSeconds = ReadPositiveInt(
            configuration,
            "Attachments:Scanner:RetryBaseSeconds",
            30);
        var scanRetryMaximumSeconds = ReadPositiveInt(
            configuration,
            "Attachments:Scanner:RetryMaximumSeconds",
            3600);
        var scannerTimeoutSeconds = ReadPositiveInt(
            configuration,
            "Attachments:Scanner:TimeoutSeconds",
            30);
        var scanLeaseSeconds = ReadPositiveInt(
            configuration,
            "Attachments:Scanner:LeaseSeconds",
            180);
        var cleanupIntervalSeconds = ReadPositiveInt(
            configuration,
            "Attachments:CleanupIntervalSeconds",
            60);
        var cleanupBatchSize = ReadPositiveInt(
            configuration,
            "Attachments:CleanupBatchSize",
            25);
        var maximumConcurrentUploads = ReadPositiveInt(
            configuration,
            "Attachments:MaximumConcurrentUploadsPerUser",
            2);
        var scanIntervalSeconds = ReadPositiveInt(
            configuration,
            "Attachments:Scanner:IntervalSeconds",
            15);
        var scanBatchSize = ReadPositiveInt(
            configuration,
            "Attachments:Scanner:BatchSize",
            10);
        var orphanFileGraceHours = ReadPositiveInt(
            configuration,
            "Attachments:OrphanFileGraceHours",
            24);
        if (maximumScanAttempts > 20 ||
            scanRetryMaximumSeconds < scanRetryBaseSeconds ||
            scanRetryBaseSeconds > 3600 ||
            scanRetryMaximumSeconds > 86400 ||
            scannerTimeoutSeconds > 120 ||
            scanLeaseSeconds < scannerTimeoutSeconds + 30 ||
            scanLeaseSeconds > 600 ||
            cleanupIntervalSeconds > 3600 ||
            cleanupBatchSize > 100 ||
            maximumConcurrentUploads > 100 ||
            scanIntervalSeconds > 3600 ||
            scanBatchSize > 100 ||
            orphanFileGraceHours > 24 * 30)
        {
            throw new InvalidOperationException(
                "Attachment scanner retry settings or lease duration are invalid.");
        }

        return new RequestAttachmentOptions(
            maximumFileSize,
            maximumFiles,
            maximumTotalSize,
            cleanupIntervalSeconds,
            cleanupBatchSize,
            maximumConcurrentUploads,
            scanIntervalSeconds,
            scanBatchSize,
            maximumScanAttempts,
            scanLeaseSeconds,
            scanRetryBaseSeconds,
            scanRetryMaximumSeconds,
            orphanFileGraceHours);
    }

    private static long ReadPositiveLong(
        IConfiguration configuration,
        string key,
        long defaultValue)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            return defaultValue;
        }

        if (!long.TryParse(configuration[key], out var value) || value <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be a positive integer.");
        }

        return value;
    }

    private static int ReadPositiveInt(
        IConfiguration configuration,
        string key,
        int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            return defaultValue;
        }

        if (!int.TryParse(configuration[key], out var value) || value <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be a positive integer.");
        }

        return value;
    }
}
