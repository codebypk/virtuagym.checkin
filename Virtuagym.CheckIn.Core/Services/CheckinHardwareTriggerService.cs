using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jablotron.API.Services;
using Virtuagym.CheckIn.Core.Abstractions;
using Virtuagym.CheckIn.Core.Helper;
using Virtuagym.CheckIn.Core.Models;
using Hardware.Services;

namespace Virtuagym.CheckIn.Core.Services;

/// <summary>
/// Controls hardware components (relay, PG gate) after a successful check-in.
/// Platform-independent – uses <see cref="IAppSettings"/> for Jablotron credentials.
/// </summary>
public class CheckinHardwareTriggerService
{
    private readonly ILogWriter _logger;
    private readonly CheckinClientMapping _mapping;
    private readonly IAppSettings _settings;

    private static readonly SemaphoreSlim _gateSemaphore = new(1, 1);
    private static volatile Task _activeGateTask = Task.CompletedTask;
    private static JablotronCloudServiceFactory? _jablotronFactory;
    private static readonly object _factoryLock = new();

    private JablotronCloudServiceFactory GetOrCreateFactory()
    {
        if (_jablotronFactory != null) return _jablotronFactory;
        lock (_factoryLock)
        {
            _jablotronFactory ??= new JablotronCloudServiceFactory(
                _settings.JablotronApiUrl,
                _settings.JablotronApiUsername,
                _settings.JablotronApiPassword);
        }
        return _jablotronFactory;
    }

    /// <summary>
    /// Resets the Jablotron client factory (e.g. on credential change or shutdown).
    /// </summary>
    public static void ResetJablotronClient()
    {
        lock (_factoryLock)
        {
            _jablotronFactory?.Dispose();
            _jablotronFactory = null;
        }
    }

    /// <summary>
    /// Waits for a running PG gate cycle (ON → delay → OFF) to complete.
    /// </summary>
    public static async Task WaitForPendingGateAsync(TimeSpan? timeout = null)
    {
        var task = _activeGateTask;
        if (task == null || task.IsCompleted)
            return;

        if (timeout.HasValue)
            await Task.WhenAny(task, Task.Delay(timeout.Value));
        else
            await task;
    }

    public CheckinHardwareTriggerService(ILogWriter logger, CheckinClientMapping mapping, IAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(settings);
        _logger = logger;
        _mapping = mapping;
        _settings = settings;
    }

    public void TriggerRelayIfEnabled(string displayName)
    {
        if (_mapping.RelayEnabled && !string.IsNullOrWhiteSpace(_mapping.RelayComPort))
        {
            _logger.WriteToLog($"[{displayName}] {L.T("Log_RelayTriggered")}: {_mapping.RelayComPort} #{_mapping.RelayNumber}");
            var relay = new RelayController(new HardwareLoggerAdapter(_logger));
            relay.CycleRelay(_mapping.RelayComPort, _mapping.RelayNumber, _mapping.RelayBaudRate, _mapping.RelayTriggerTime);
        }
    }

    public void TriggerPgGateIfEnabled(string displayName, CancellationToken cancellationToken = default)
    {
        if (!_mapping.PgEnabled || string.IsNullOrWhiteSpace(_mapping.PgGateComponentId))
            return;

        int PgGateRetryCount = 3;
        int PgGateRetryDelayMs = 500;

        string componentId = _mapping.PgGateComponentId;
        string serviceId = _mapping.PgGateServiceId;
        double triggerTimeSec = _mapping.PgTriggerTimeSec;
        string gateName = _mapping.PgGateName ?? componentId;
        string conditionMode = _mapping.PgConditionMode ?? "Always";
        string condComponentId = _mapping.PgConditionGateComponentId;
        string condServiceId = _mapping.PgConditionGateServiceId;
        string condGateName = _mapping.PgConditionGateName ?? condComponentId;

        var task = Task.Run(async () =>
        {
            await _gateSemaphore.WaitAsync(cancellationToken);
            try
            {
                var client = GetOrCreateFactory().GetClient();

                int? parsedServiceId = null;
                if (int.TryParse(serviceId, out var sid) && sid > 0)
                    parsedServiceId = sid;

                if (conditionMode == "OnlyIfOn" || conditionMode == "OnlyIfOff")
                {
                    if (string.IsNullOrWhiteSpace(condComponentId))
                    {
                        _logger.WriteToLog($"[{displayName}] {L.T("Log_PgConditionGateNotConfigured")}", Constants.LogWarning);
                        return;
                    }

                    int? condParsedServiceId = null;
                    if (int.TryParse(condServiceId, out var csid) && csid > 0)
                        condParsedServiceId = csid;

                    var gatesData = client.GetProgrammableGates(condParsedServiceId);
                    string? condGateState = gatesData?.States?
                        .FirstOrDefault(s => string.Equals(s.CloudComponentId, condComponentId, StringComparison.OrdinalIgnoreCase))
                        ?.State;

                    bool isOn = string.Equals(condGateState, "ON", StringComparison.OrdinalIgnoreCase);
                    bool isOff = !isOn;

                    if (conditionMode == "OnlyIfOn" && !isOn)
                    {
                        _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_PgConditionNotMet"), condGateName, condGateState ?? "unknown", "ON")}", Constants.LogInfo);
                        return;
                    }
                    if (conditionMode == "OnlyIfOff" && !isOff)
                    {
                        _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_PgConditionNotMet"), condGateName, condGateState ?? "unknown", "OFF")}", Constants.LogInfo);
                        return;
                    }
                }
                else if (conditionMode == "TimeRange")
                {
                    string timeFrom = _mapping.PgConditionTimeFrom ?? "";
                    string timeTo = _mapping.PgConditionTimeTo ?? "";
                    if (TimeSpan.TryParse(timeFrom, out var from) && TimeSpan.TryParse(timeTo, out var to))
                    {
                        var now = DateTime.Now.TimeOfDay;
                        bool inRange = from <= to
                            ? (now >= from && now <= to)
                            : (now >= from || now <= to);
                        if (!inRange)
                        {
                            _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_PgTimeRangeNotMet"), timeFrom, timeTo, now.ToString(@"hh\:mm"))}", Constants.LogInfo);
                            return;
                        }
                    }
                    else
                    {
                        _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_PgTimeRangeInvalid"), timeFrom, timeTo)}", Constants.LogWarning);
                        return;
                    }
                }

                bool onSuccess = await SendGateCommandWithRetryAsync(
                    client, parsedServiceId, componentId, "ON",
                    gateName, displayName, PgGateRetryCount, PgGateRetryDelayMs, cancellationToken);

                if (!onSuccess)
                    return;

                _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_PgGateOnWaiting"), gateName, triggerTimeSec)}");

                if (triggerTimeSec > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(triggerTimeSec), cancellationToken);

                    bool offSuccess = await SendGateCommandWithRetryAsync(
                        client, parsedServiceId, componentId, "OFF",
                        gateName, displayName, PgGateRetryCount, PgGateRetryDelayMs, cancellationToken);

                    if (offSuccess)
                        _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_PgGateOffSuccess"), gateName, triggerTimeSec)}");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_PgGateCancelled"), gateName)}", Constants.LogWarning);
            }
            catch (Exception ex)
            {
                _logger.WriteToLog($"[{displayName}] {L.T("Log_PgGateError")}: {ex.Message}", Constants.LogWarning);
            }
            finally
            {
                _gateSemaphore.Release();
            }
        }, cancellationToken);

        task.ContinueWith(t =>
        {
            if (t.Exception != null)
                _logger.WriteToLog($"[{displayName}] {L.T("Log_PgGateUnobservedError")}: {t.Exception.GetBaseException().Message}", Constants.LogError);
        }, TaskContinuationOptions.OnlyOnFaulted);

        _activeGateTask = task;
    }

    private async Task<bool> SendGateCommandWithRetryAsync(
        JablotronCloudService client, int? serviceId, string componentId, string command,
        string gateName, string displayName, int retryCount, int retryDelayMs, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= retryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (client.ControlProgrammableGate(serviceId, componentId, command))
                    return true;

                var failedMessage = command == "ON"
                    ? string.Format(L.T("Log_PgGateOnFailed"), gateName)
                    : string.Format(L.T("Log_PgGateOffFailed"), gateName);

                _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_PgGateRetryAttempt"), failedMessage, attempt, retryCount)}", Constants.LogWarning);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var failedMessage = command == "ON"
                    ? string.Format(L.T("Log_PgGateOnFailed"), gateName)
                    : string.Format(L.T("Log_PgGateOffFailed"), gateName);

                _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_PgGateRetryAttemptWithError"), failedMessage, attempt, retryCount, ex.Message)}", Constants.LogWarning);
            }

            if (attempt < retryCount)
                await Task.Delay(retryDelayMs, cancellationToken);
        }

        var finalFailedMessage = command == "ON"
            ? string.Format(L.T("Log_PgGateOnFailed"), gateName)
            : string.Format(L.T("Log_PgGateOffFailed"), gateName);

        _logger.WriteToLog($"[{displayName}] {string.Format(L.T("Log_PgGateAllRetryAttemptsFailed"), finalFailedMessage, retryCount)}", Constants.LogWarning);
        return false;
    }
}
