namespace Hardware.Models
{
    public class HidDeviceInfo
    {
        public string DevicePath { get; set; }
        public string DeviceId { get; set; }
        public string Manufacturer { get; set; }
        public string Product { get; set; }
        public int VendorId { get; init; }
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public string SerialNumber { get; init; } = string.Empty;

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Product)
                ? DevicePath
                : $"{Manufacturer} - {Product} ({DeviceId})";

        /// <summary>
        /// Alias for <see cref="ProductName"/>. Used for display purposes where a generic description is needed.
        /// </summary>
        public string Description => ProductName;
    }
}
