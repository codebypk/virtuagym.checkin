using System;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Virtuagym.API;

namespace Virtuagym.CheckIn.Core.Models;

/// <summary>
/// Mapping of an input device (RFID reader or QR code camera) to a Virtuagym check-in client.
/// Each device has its own name, check-in key (club secret) and relay configuration.
/// </summary>
public class CheckinClientMapping : INotifyPropertyChanged
{
    /// <summary>
    /// Global default for <see cref="DoubleScanThresholdMs"/>.
    /// Must be set at application startup from <see cref="Abstractions.IAppSettings.DefaultDoubleScanThresholdMs"/>.
    /// </summary>
    public static long GlobalDefaultDoubleScanThresholdMs { get; set; } = 60000;

    /// <summary>
    /// Global default for <see cref="EffectiveDuplicateTimeoutSeconds"/>.
    /// Must be set at application startup from <see cref="Abstractions.IAppSettings.DuplicateTimeoutSeconds"/>.
    /// </summary>
    public static int GlobalDefaultDuplicateTimeoutSeconds { get; set; } = 5;

    private string _uuid = "";
    private string _name = "";
    private string _inputType = InputTypeRfid;
    private string _deviceID = "";
    private int _cameraIndex;
    private int _cameraResolutionWidth = 640;
    private int _cameraResolutionHeight = 480;
    private string _cameraBackend = "ANY";
    private string _checkinKey = "";
    private bool _relayEnabled;
    private string _relayComPort = "";
    private int _relayNumber = 1;
    private double _relayTriggerTime = 0.05;
    private int _relayBaudRate = 9600;
    private long _doubleScanThresholdMs;
    private int? _repeatTimeMs = null;
    private string _hidProfile = "";
    private string _deviceDescription = "";
    private string _manufacturer = "";
    private bool _isDeviceAvailable = true;
    private string _creditServiceId = "";
    private int _creditClubId;
    private int _autoCheckoutMinutes;
    private int? _duplicateTimeoutSeconds;
    private bool _pgEnabled;
    private string _pgGateComponentId = "";
    private string _pgGateServiceId = "";
    private string _pgGateName = "";
    private double _pgTriggerTimeSec = 3;
    private string _pgConditionMode = "Always";
    private string _pgConditionGateComponentId = "";
    private string _pgConditionGateServiceId = "";
    private string _pgConditionGateName = "";
    private string _pgConditionTimeFrom = "";
    private string _pgConditionTimeTo = "";
    private double _apiVersion = -1;
    private int _cardIdMode;

    public CheckinClientMapping()
    {
        _doubleScanThresholdMs = GlobalDefaultDoubleScanThresholdMs;
    }

    /// <summary>Input type: RFID reader.</summary>
    public const string InputTypeRfid = "USBReader";
    /// <summary>Input type: QR code camera.</summary>
    public const string InputTypeQrCode = "QRCode";
    /// <summary>Input type: CCID smart card reader (PC/SC).</summary>
    public const string InputTypeCcid = "CCID";

    /// <summary>
    /// Internal unique ID of the mapping. Auto-generated if empty.
    /// </summary>
    public string Uuid
    {
        get => _uuid;
        set { _uuid = value; OnPropertyChanged(nameof(Uuid)); }
    }

    /// <summary>
    /// Ensures a UUID is present. Generates a new one if empty.
    /// </summary>
    public void EnsureUuid()
    {
        if (string.IsNullOrWhiteSpace(_uuid))
            Uuid = Guid.NewGuid().ToString("D");
    }

    /// <summary>Display name of the mapping.</summary>
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    /// <summary>Input type: "USBReader", "QRCode" or "CCID".</summary>
    public string InputType
    {
        get => _inputType;
        set { _inputType = value; OnPropertyChanged(nameof(InputType)); }
    }

    /// <summary>USB device ID of the RFID reader. Only relevant for InputType=USBReader.</summary>
    public string DeviceID
    {
        get => _deviceID;
        set { _deviceID = value; OnPropertyChanged(nameof(DeviceID)); }
    }

    /// <summary>Camera index (0-based). Only relevant for InputType=QRCode.</summary>
    public int CameraIndex
    {
        get => _cameraIndex;
        set { _cameraIndex = value; OnPropertyChanged(nameof(CameraIndex)); }
    }

    /// <summary>Camera resolution width in pixels. Default: 640.</summary>
    public int CameraResolutionWidth
    {
        get => _cameraResolutionWidth;
        set { _cameraResolutionWidth = value; OnPropertyChanged(nameof(CameraResolutionWidth)); }
    }

    /// <summary>Camera resolution height in pixels. Default: 480.</summary>
    public int CameraResolutionHeight
    {
        get => _cameraResolutionHeight;
        set { _cameraResolutionHeight = value; OnPropertyChanged(nameof(CameraResolutionHeight)); }
    }

    /// <summary>Video backend for OpenCV (e.g. "ANY", "MSMF", "DSHOW").</summary>
    public string CameraBackend
    {
        get => _cameraBackend;
        set { _cameraBackend = value ?? "ANY"; OnPropertyChanged(nameof(CameraBackend)); }
    }

    /// <summary>Check-in key (club secret) for the Virtuagym API check-in.</summary>
    public string CheckinKey
    {
        get => _checkinKey;
        set { _checkinKey = value; OnPropertyChanged(nameof(CheckinKey)); }
    }

    /// <summary>Relay control enabled.</summary>
    public bool RelayEnabled
    {
        get => _relayEnabled;
        set { _relayEnabled = value; OnPropertyChanged(nameof(RelayEnabled)); }
    }

    /// <summary>COM port of the relay controller.</summary>
    public string RelayComPort
    {
        get => _relayComPort;
        set { _relayComPort = value; OnPropertyChanged(nameof(RelayComPort)); }
    }

    /// <summary>Relay number (1-based).</summary>
    public int RelayNumber
    {
        get => _relayNumber;
        set { _relayNumber = value; OnPropertyChanged(nameof(RelayNumber)); }
    }

    /// <summary>Trigger time in seconds.</summary>
    public double RelayTriggerTime
    {
        get => _relayTriggerTime;
        set { _relayTriggerTime = value; OnPropertyChanged(nameof(RelayTriggerTime)); }
    }

    /// <summary>Baud rate for serial communication with the relay controller.</summary>
    public int RelayBaudRate
    {
        get => _relayBaudRate;
        set { _relayBaudRate = value; OnPropertyChanged(nameof(RelayBaudRate)); }
    }

    /// <summary>
    /// Double-scan protection threshold in milliseconds.
    /// 0 = disabled.
    /// </summary>
    public long DoubleScanThresholdMs
    {
        get => _doubleScanThresholdMs;
        set { _doubleScanThresholdMs = value; OnPropertyChanged(nameof(DoubleScanThresholdMs)); }
    }

    /// <summary>
    /// Polling interval in ms for this reader's read loop.
    /// null = global default from settings.
    /// </summary>
    public int? RepeatTimeMs
    {
        get => _repeatTimeMs;
        set { _repeatTimeMs = value; OnPropertyChanged(nameof(RepeatTimeMs)); }
    }

    /// <summary>HID reader profile ID (e.g. "wCopy_NS122").</summary>
    public string HidProfile
    {
        get => _hidProfile;
        set { _hidProfile = value; OnPropertyChanged(nameof(HidProfile)); }
    }

    /// <summary>
    /// Service ID (service_type) for credit checking.
    /// Empty = no credit check (disabled).
    /// </summary>
    public string CreditServiceId
    {
        get => _creditServiceId;
        set { _creditServiceId = value; OnPropertyChanged(nameof(CreditServiceId)); }
    }

    /// <summary>Club ID for credit checking.</summary>
    public int CreditClubId
    {
        get => _creditClubId;
        set { _creditClubId = value; OnPropertyChanged(nameof(CreditClubId)); }
    }

    /// <summary>
    /// Auto-checkout after X minutes. 0 = disabled.
    /// </summary>
    public int AutoCheckoutMinutes
    {
        get => _autoCheckoutMinutes;
        set { _autoCheckoutMinutes = value; OnPropertyChanged(nameof(AutoCheckoutMinutes)); }
    }

    /// <summary>
    /// Reader debounce in seconds.
    /// null = global default from settings.
    /// </summary>
    public int? DuplicateTimeoutSeconds
    {
        get => _duplicateTimeoutSeconds;
        set { _duplicateTimeoutSeconds = value; OnPropertyChanged(nameof(DuplicateTimeoutSeconds)); }
    }

    /// <summary>
    /// Returns the effective debounce value (mapping value or global fallback).
    /// </summary>
    [JsonIgnore]
    public int EffectiveDuplicateTimeoutSeconds =>
        _duplicateTimeoutSeconds ?? GlobalDefaultDuplicateTimeoutSeconds;

    /// <summary>Jablotron Programmable Gate control enabled.</summary>
    public bool PgEnabled
    {
        get => _pgEnabled;
        set { _pgEnabled = value; OnPropertyChanged(nameof(PgEnabled)); OnPropertyChanged(nameof(PgGateDisplayName)); }
    }

    /// <summary>Cloud component ID of the Programmable Gate.</summary>
    public string PgGateComponentId
    {
        get => _pgGateComponentId;
        set { _pgGateComponentId = value; OnPropertyChanged(nameof(PgGateComponentId)); }
    }

    /// <summary>Service ID for the Programmable Gate (Jablotron Cloud).</summary>
    public string PgGateServiceId
    {
        get => _pgGateServiceId;
        set { _pgGateServiceId = value; OnPropertyChanged(nameof(PgGateServiceId)); }
    }

    /// <summary>Display name of the assigned Programmable Gate.</summary>
    public string PgGateName
    {
        get => _pgGateName;
        set { _pgGateName = value; OnPropertyChanged(nameof(PgGateName)); OnPropertyChanged(nameof(PgGateDisplayName)); }
    }

    /// <summary>Display name for the PG Gate in the DataGrid.</summary>
    [JsonIgnore]
    public string PgGateDisplayName => _pgEnabled ? _pgGateName : "";

    /// <summary>Trigger time in seconds for the Programmable Gate (ON→OFF).</summary>
    public double PgTriggerTimeSec
    {
        get => _pgTriggerTimeSec;
        set { _pgTriggerTimeSec = value; OnPropertyChanged(nameof(PgTriggerTimeSec)); }
    }

    /// <summary>Condition mode for gate triggering: "Always", "OnlyIfOn", "OnlyIfOff", "TimeRange".</summary>
    public string PgConditionMode
    {
        get => _pgConditionMode;
        set { _pgConditionMode = value; OnPropertyChanged(nameof(PgConditionMode)); }
    }

    /// <summary>Cloud component ID of the condition gate.</summary>
    public string PgConditionGateComponentId
    {
        get => _pgConditionGateComponentId;
        set { _pgConditionGateComponentId = value; OnPropertyChanged(nameof(PgConditionGateComponentId)); }
    }

    /// <summary>Service ID of the condition gate.</summary>
    public string PgConditionGateServiceId
    {
        get => _pgConditionGateServiceId;
        set { _pgConditionGateServiceId = value; OnPropertyChanged(nameof(PgConditionGateServiceId)); }
    }

    /// <summary>Display name of the condition gate.</summary>
    public string PgConditionGateName
    {
        get => _pgConditionGateName;
        set { _pgConditionGateName = value; OnPropertyChanged(nameof(PgConditionGateName)); }
    }

    /// <summary>Time range start (HH:mm). Only relevant for PgConditionMode=TimeRange.</summary>
    public string PgConditionTimeFrom
    {
        get => _pgConditionTimeFrom;
        set { _pgConditionTimeFrom = value; OnPropertyChanged(nameof(PgConditionTimeFrom)); }
    }

    /// <summary>Time range end (HH:mm). Only relevant for PgConditionMode=TimeRange.</summary>
    public string PgConditionTimeTo
    {
        get => _pgConditionTimeTo;
        set { _pgConditionTimeTo = value; OnPropertyChanged(nameof(PgConditionTimeTo)); }
    }

    /// <summary>
    /// Returns the effective device ID used as a stable identifier in the member cache.
    /// </summary>
    [JsonIgnore]
    public string EffectiveDeviceId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_checkinKey))
            {
                var numericId = VirtuagymApiBase.ExtractDeviceId(_checkinKey);
                if (!string.IsNullOrEmpty(numericId))
                    return numericId;
            }
            string slug = ToDeviceIdSlug(!string.IsNullOrWhiteSpace(_name) ? _name : _uuid);
            return !string.IsNullOrEmpty(slug) ? slug : _uuid;
        }
    }

    /// <summary>
    /// Creates a URL-safe device ID slug from a display name.
    /// </summary>
    public static string ToDeviceIdSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var sb = new StringBuilder(name.Length + 4);
        sb.Append('@');

        foreach (char c in name)
        {
            switch (c)
            {
                case 'ä': case 'Ä': sb.Append("ae"); break;
                case 'ö': case 'Ö': sb.Append("oe"); break;
                case 'ü': case 'Ü': sb.Append("ue"); break;
                case 'ß': sb.Append("ss"); break;
                case ' ': sb.Append('_'); break;
                default:
                    if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
                        sb.Append(c);
                    else if (c >= 'A' && c <= 'Z')
                        sb.Append((char)(c + 32));
                    break;
            }
        }

        string result = Regex.Replace(sb.ToString(), @"_+", "_");
        if (result.Length > 1 && result[1] == '_')
            result = "@" + result.Substring(2);
        if (result.Length > 1 && result[result.Length - 1] == '_')
            result = result.Substring(0, result.Length - 1);

        return result.Length > 1 ? result : null;
    }

    /// <summary>
    /// API version for this mapping. 0 = v0, 1 = v1, -1 = auto-detect.
    /// </summary>
    public double ApiVersion
    {
        get
        {
            if (_apiVersion < 0)
            {
                return !string.IsNullOrWhiteSpace(_checkinKey)
                    && !string.IsNullOrEmpty(VirtuagymApiBase.ExtractDeviceId(_checkinKey))
                    ? 0 : 1;
            }
            return _apiVersion;
        }
        set { _apiVersion = value; OnPropertyChanged(nameof(ApiVersion)); }
    }

    /// <summary>
    /// Card ID mode: 0 = Lower3Bytes, 1 = FullDecimal, 2 = FullHex.
    /// </summary>
    public int CardIdMode
    {
        get => _cardIdMode;
        set { _cardIdMode = value; OnPropertyChanged(nameof(CardIdMode)); }
    }

    /// <summary>
    /// Returns the <see cref="Hardware.Models.CardIdMode"/> enum value.
    /// </summary>
    [JsonIgnore]
    public Hardware.Models.CardIdMode CardIdModeEnum =>
        Enum.IsDefined(typeof(Hardware.Models.CardIdMode), _cardIdMode)
            ? (Hardware.Models.CardIdMode)_cardIdMode
            : Hardware.Models.CardIdMode.Lower3Bytes;

    /// <summary>Device description (populated at runtime, not serialized).</summary>
    [JsonIgnore]
    public string DeviceDescription
    {
        get => _deviceDescription;
        set { _deviceDescription = value; OnPropertyChanged(nameof(DeviceDescription)); }
    }

    /// <summary>Device manufacturer (populated at runtime, not serialized).</summary>
    [JsonIgnore]
    public string Manufacturer
    {
        get => _manufacturer;
        set { _manufacturer = value; OnPropertyChanged(nameof(Manufacturer)); }
    }

    /// <summary>Whether the assigned device is currently available.</summary>
    [JsonIgnore]
    public bool IsDeviceAvailable
    {
        get => _isDeviceAvailable;
        set { _isDeviceAvailable = value; OnPropertyChanged(nameof(IsDeviceAvailable)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
