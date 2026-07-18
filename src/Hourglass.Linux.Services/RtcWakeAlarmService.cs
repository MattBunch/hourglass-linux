namespace Hourglass.Linux.Services;

using System.Globalization;
using Hourglass.Platform;

public sealed class RtcWakeAlarmService : IWakeAlarmService
{
    public const string WakeAlarmPathOverrideEnvironmentVariable = "HOURGLASS_WAKE_ALARM_PATH";

    private const string DefaultWakeAlarmPath = "/sys/class/rtc/rtc0/wakealarm";
    private const string UnsupportedMessage = "RTC wake alarms are not available.";
    private const string OccupiedMessage = "Another RTC wake alarm is already scheduled.";
    private const string VerificationFailedMessage = "RTC wake alarm could not be verified after scheduling.";
    private const string ScheduledMessage = "RTC wake alarm scheduled.";

    private readonly string wakeAlarmPath;
    private readonly Func<DateTimeOffset> now;

    public RtcWakeAlarmService()
        : this(
            Environment.GetEnvironmentVariable(WakeAlarmPathOverrideEnvironmentVariable) ?? DefaultWakeAlarmPath,
            () => DateTimeOffset.Now)
    {
    }

    internal RtcWakeAlarmService(string wakeAlarmPath, Func<DateTimeOffset> now)
    {
        this.wakeAlarmPath = string.IsNullOrWhiteSpace(wakeAlarmPath)
            ? throw new ArgumentException("Wake alarm path is required.", nameof(wakeAlarmPath))
            : wakeAlarmPath;
        this.now = now ?? throw new ArgumentNullException(nameof(now));
    }

    public async Task<WakeAlarmScheduleResult> TryScheduleWakeAsync(
        WakeAlarmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(this.wakeAlarmPath))
        {
            return Unsupported(UnsupportedMessage);
        }

        long requestedUnixTime = request.WakeAt.ToUniversalTime().ToUnixTimeSeconds();
        if (requestedUnixTime <= this.now().ToUniversalTime().ToUnixTimeSeconds())
        {
            return Unsupported("Wake alarm time must be in the future.");
        }

        try
        {
            long? currentUnixTime = await this.ReadCurrentAlarmAsync(cancellationToken).ConfigureAwait(false);
            long nowUnixTime = this.now().ToUniversalTime().ToUnixTimeSeconds();
            if (currentUnixTime.HasValue && currentUnixTime.Value > nowUnixTime)
            {
                return Unsupported(OccupiedMessage);
            }

            await File.WriteAllTextAsync(this.wakeAlarmPath, "0", cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                this.wakeAlarmPath,
                requestedUnixTime.ToString(CultureInfo.InvariantCulture),
                cancellationToken).ConfigureAwait(false);

            long? verifiedUnixTime = await this.ReadCurrentAlarmAsync(cancellationToken).ConfigureAwait(false);
            if (verifiedUnixTime != requestedUnixTime)
            {
                return Unsupported(VerificationFailedMessage);
            }

            return new WakeAlarmScheduleResult(
                Supported: true,
                Scheduled: true,
                Message: ScheduledMessage,
                Lease: new RtcWakeAlarmLease(this.wakeAlarmPath, requestedUnixTime));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Unsupported(exception.Message);
        }
    }

    private async Task<long?> ReadCurrentAlarmAsync(CancellationToken cancellationToken)
    {
        string value = await File.ReadAllTextAsync(this.wakeAlarmPath, cancellationToken).ConfigureAwait(false);
        if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixTime)
            || unixTime <= 0)
        {
            return null;
        }

        return unixTime;
    }

    private static WakeAlarmScheduleResult Unsupported(string message)
    {
        return new WakeAlarmScheduleResult(
            Supported: false,
            Scheduled: false,
            Message: message,
            Lease: null);
    }

    private sealed class RtcWakeAlarmLease(string wakeAlarmPath, long scheduledUnixTime) : IWakeAlarmLease
    {
        private int disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            try
            {
                string value = await File.ReadAllTextAsync(wakeAlarmPath).ConfigureAwait(false);
                if (long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long currentUnixTime)
                    && currentUnixTime == scheduledUnixTime)
                {
                    await File.WriteAllTextAsync(wakeAlarmPath, "0").ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
