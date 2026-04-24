namespace Jablotron.API
{
    public static class ApiConstants
    {
        public const string DefaultLoginSource = "MYJABLOTRON";
        public const string ContentTypeJson = "application/json";
        public const string DefaultAcceptLanguage = "en";

        public const string HeaderVendorId = "x-vendor-id";
        public const string HeaderClientVersion = "x-client-version";
        public const string HeaderAcceptLanguage = "Accept-Language";
        public const string HeaderAccept = "Accept";

        public const string HeaderVendorIdValue = "JABLOTRON:Jablotron";
        public const string HeaderClientVersionValue = "MYJ-PUB-ANDROID-21";

        public const string EndpointLogin = "userAuthorize.json";
        public const string EndpointServiceList = "serviceListGet.json";
        public const string EndpointServiceInformation = "serviceInformationGet.json";
        public const string EndpointServiceSettings = "getServiceSettings.json";
        public const string EndpointDeviceSchedule = "getDeviceSchedule.json";

        public const string EndpointSectionsFormat = "{0}/sectionsGet.json";
        public const string EndpointThermoDevicesFormat = "{0}/thermoDevicesGet.json";
        public const string EndpointKeyboardSegmentsFormat = "{0}/keyboardSegmentsGet.json";
        public const string EndpointProgrammableGatesFormat = "{0}/programmableGatesGet.json";
        public const string EndpointEventHistoryFormat = "{0}/eventHistoryGet.json";
        public const string EndpointControlComponentFormat = "{0}/controlComponent.json";
        public const string EndpointControlThermoDeviceFormat = "{0}/controlThermoDevice.json";

        public const string StateArm = "ARM";
        public const string StatePartialArm = "PARTIAL_ARM";
        public const string StateDisarm = "DISARM";
        public const string StateOn = "ON";
        public const string StateOff = "OFF";
        public const string StateScheduled = "SCHEDULED";

        public const string ControlActionSection = "CONTROL-SECTION";
        public const string ControlActionProgrammableGate = "CONTROL-PG";
        public const string ControlErrorWrongCode = "WRONG-CODE";
        public const string ErrorUserSessionExpired = "USER.SESSION.EXPIRED";
    }
}
